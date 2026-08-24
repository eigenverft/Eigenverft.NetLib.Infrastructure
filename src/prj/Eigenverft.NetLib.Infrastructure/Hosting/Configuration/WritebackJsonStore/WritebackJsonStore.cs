using System;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace Eigenverft.NetLib.Infrastructure.Hosting.Configuration.WritebackJsonStore
{
    /// <summary>File-backed JSON document manager with optional reload-on-change and a non-persisted working copy.</summary>
    /// <typeparam name="T">Document type. Must be a reference type with a public parameterless constructor.</typeparam>
    public sealed class WritebackJsonStore<T> : IDisposable where T : class, new()
    {
        private readonly object _syncRoot = new();
        private readonly string _filePath;
        private readonly JsonSerializerOptions _options;

        private readonly FileSystemWatcher? _watcher;
        private readonly TimeSpan _debounce = TimeSpan.FromMilliseconds(250);
        private Timer? _debounceTimer;
        private DateTime _ignoreFileChangesUntilUtc;

        private bool _disposed;

        private T _current;
        private readonly T _initialSnapshot;
        private T _workingCopy;

        /// <summary>Raised after the current document has been reloaded from disk or saved.</summary>
        /// <remarks>The first argument is the previous snapshot, the second is the new snapshot.</remarks>
        public event Action<T, T>? DocumentChanged;

        /// <summary>Raised after the in-memory working copy has been mutated via <see cref="MutateWorkingCopy"/>.</summary>
        /// <remarks>The first argument is the previous snapshot, the second is the new snapshot.</remarks>
        public event Action<T, T>? WorkingCopyChanged;

        /// <summary>Raised when an internal exception is caught and handled.</summary>
        public event Action<Exception>? ErrorOccurred;

        /// <summary>Gets the absolute file path used by this instance.</summary>
        public string FilePath => _filePath;

        /// <summary>
        /// Gets the current in-memory instance.
        /// Treat it as read-only; use <see cref="MutateAndSave"/> to change it safely.
        /// </summary>
        public T Current
        { get { lock (_syncRoot) { ThrowIfDisposed(); return _current; } } }

        /// <summary>Gets a deep copy of the initial document as it was loaded or created during construction.</summary>
        public T InitialSnapshot
        { get { lock (_syncRoot) { ThrowIfDisposed(); return Clone(_initialSnapshot); } } }

        /// <summary>
        /// Gets the in-memory working copy initialized from <see cref="InitialSnapshot"/> and never written to disk.
        /// Prefer <see cref="MutateWorkingCopy"/> for thread-safe updates.
        /// </summary>
        public T WorkingCopy
        { get { lock (_syncRoot) { ThrowIfDisposed(); return _workingCopy; } } }

        /// <summary>
        /// Initializes a new file-backed JSON store.
        /// If the file does not exist, it is created using a new <typeparamref name="T"/> instance.
        /// </summary>
        /// <param name="filePath">Path of the JSON file.</param>
        /// <param name="watchForExternalChanges">When true, external file edits trigger reload.</param>
        /// <param name="options">Optional serializer options. If null, defaults are used.</param>
        public WritebackJsonStore(string filePath = "dynamicsettings.json", bool watchForExternalChanges = true, JsonSerializerOptions? options = null)
        {
            _filePath = Path.GetFullPath(filePath);
            _options = options ?? CreateDefaultOptions();

            var dir = Path.GetDirectoryName(_filePath)!;
            Directory.CreateDirectory(dir);

            _current = LoadFromDisk() ?? new T();
            _initialSnapshot = Clone(_current);
            _workingCopy = Clone(_initialSnapshot);

            SaveToDisk(_current, isInitialization: true);

            if (watchForExternalChanges)
            {
                _watcher = new FileSystemWatcher(dir)
                {
                    Filter = Path.GetFileName(_filePath),
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName
                };

                _watcher.Changed += HandleFileChanged;
                _watcher.Created += HandleFileChanged;
                _watcher.Renamed += HandleFileRenamed;
                _watcher.EnableRaisingEvents = true;
            }
        }

        /// <summary>Applies changes to the current document and writes the result to disk.</summary>
        public void MutateAndSave(Action<T> mutate, bool notify = true)
        {
            ArgumentNullException.ThrowIfNull(mutate);

            Action<T, T>? handler = null;
            T? oldSnapshot = null;
            T? newSnapshot = null;

            lock (_syncRoot)
            {
                ThrowIfDisposed();

                if (notify)
                {
                    handler = DocumentChanged;
                    if (handler != null) oldSnapshot = Clone(_current);
                }

                mutate(_current);
                SaveToDisk(_current);

                if (handler != null) newSnapshot = Clone(_current);
            }

            handler?.Invoke(oldSnapshot!, newSnapshot!);
        }

        /// <summary>Applies changes to the in-memory working copy only. The working copy is never written to disk.</summary>
        public void MutateWorkingCopy(Action<T> mutate, bool notify = true)
        {
            ArgumentNullException.ThrowIfNull(mutate);

            Action<T, T>? handler = null;
            T? oldSnapshot = null;
            T? newSnapshot = null;

            lock (_syncRoot)
            {
                ThrowIfDisposed();

                if (notify)
                {
                    handler = WorkingCopyChanged;
                    if (handler != null) oldSnapshot = Clone(_workingCopy);
                }

                mutate(_workingCopy);

                if (handler != null) newSnapshot = Clone(_workingCopy);
            }

            handler?.Invoke(oldSnapshot!, newSnapshot!);
        }

        /// <summary>Resets the current document to the initial snapshot and persists it to disk.</summary>
        public void ResetToInitialAndSave(bool notify = true)
        {
            Action<T, T>? handler = null;
            T? oldSnapshot = null;
            T? newSnapshot = null;

            lock (_syncRoot)
            {
                ThrowIfDisposed();

                if (notify)
                {
                    handler = DocumentChanged;
                    if (handler != null) oldSnapshot = Clone(_current);
                }

                _current = Clone(_initialSnapshot);
                SaveToDisk(_current);

                if (handler != null) newSnapshot = Clone(_current);
            }

            handler?.Invoke(oldSnapshot!, newSnapshot!);
        }

        /// <summary>Resets the working copy back to the initial snapshot. No disk I/O occurs.</summary>
        public void ResetWorkingCopy(bool notify = true)
        {
            Action<T, T>? handler = null;
            T? oldSnapshot = null;
            T? newSnapshot = null;

            lock (_syncRoot)
            {
                ThrowIfDisposed();

                if (notify)
                {
                    handler = WorkingCopyChanged;
                    if (handler != null) oldSnapshot = Clone(_workingCopy);
                }

                _workingCopy = Clone(_initialSnapshot);

                if (handler != null) newSnapshot = Clone(_workingCopy);
            }

            handler?.Invoke(oldSnapshot!, newSnapshot!);
        }

        /// <summary>Reloads the document from disk and replaces the current in-memory instance.</summary>
        public bool ReloadFromFile(bool notify = true)
        {
            Action<T, T>? handler = null;
            T? oldSnapshot = null;
            T? newSnapshot = null;

            lock (_syncRoot)
            {
                ThrowIfDisposed();

                var reloaded = LoadFromDisk();
                if (reloaded is null) return false;

                if (notify)
                {
                    handler = DocumentChanged;
                    if (handler != null) oldSnapshot = Clone(_current);
                }

                _current = reloaded;

                if (handler != null) newSnapshot = Clone(_current);
            }

            handler?.Invoke(oldSnapshot!, newSnapshot!);
            return true;
        }

        /// <summary>Creates a deep snapshot of the current document by serializing and deserializing it.</summary>
        public T GetSnapshot()
        { lock (_syncRoot) { ThrowIfDisposed(); return Clone(_current); } }

        /// <summary>Disposes resources associated with this instance.</summary>
        public void Dispose()
        {
            lock (_syncRoot)
            {
                if (_disposed) return;
                _disposed = true;

                _watcher?.Dispose();
                _debounceTimer?.Dispose();
            }
        }

        private void HandleFileRenamed(object sender, RenamedEventArgs e) => HandleFileChanged(sender, e);

        private void HandleFileChanged(object sender, FileSystemEventArgs e)
        {
            lock (_syncRoot)
            {
                if (_disposed) return;
                if (DateTime.UtcNow <= _ignoreFileChangesUntilUtc) return;

                if (_debounceTimer is null) _debounceTimer = new Timer(_ => ProcessExternalFileChange(), null, _debounce, Timeout.InfiniteTimeSpan);
                else _debounceTimer.Change(_debounce, Timeout.InfiniteTimeSpan);
            }
        }

        private void ProcessExternalFileChange()
        {
            try { ReloadFromFile(notify: true); }
            catch (Exception ex) { ErrorOccurred?.Invoke(ex); }
        }

        private T? LoadFromDisk()
        {
            const int maxAttempts = 3;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    if (!File.Exists(_filePath)) return new T();

                    using var fs = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    if (fs.Length == 0) return new T();

                    return JsonSerializer.Deserialize<T>(fs, _options) ?? new T();
                }
                catch (IOException ex) when (attempt < maxAttempts)
                {
                    ErrorOccurred?.Invoke(ex);
                    Thread.Sleep(100);
                }
                catch (JsonException ex)
                {
                    ErrorOccurred?.Invoke(ex);
                    return new T();
                }
            }

            ErrorOccurred?.Invoke(new IOException($"Failed to read JSON file '{_filePath}' after multiple attempts."));
            return new T();
        }

        private void SaveToDisk(T value, bool isInitialization = false)
        {
            const int maxAttempts = 3;
            var json = JsonSerializer.Serialize(value, _options);

            _ignoreFileChangesUntilUtc = DateTime.UtcNow.AddMilliseconds(500);

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    File.WriteAllText(_filePath, json);
                    return;
                }
                catch (IOException ex) when (attempt < maxAttempts)
                {
                    ErrorOccurred?.Invoke(ex);
                    Thread.Sleep(100);
                }
            }

            var finalException = new IOException($"Failed to write JSON file '{_filePath}' after multiple attempts.");
            ErrorOccurred?.Invoke(finalException);
            if (!isInitialization) throw finalException;
        }

        private T Clone(T source)
        {
            var json = JsonSerializer.Serialize(source, _options);
            return JsonSerializer.Deserialize<T>(json, _options) ?? new T();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(WritebackJsonStore<T>));
        }

        private static JsonSerializerOptions CreateDefaultOptions()
        {
            return new JsonSerializerOptions
            {
                WriteIndented = true,
                AllowTrailingCommas = true,
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            };
        }
    }
}