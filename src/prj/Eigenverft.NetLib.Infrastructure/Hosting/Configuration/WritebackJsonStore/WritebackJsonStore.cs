using System;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace Eigenverft.NetLib.Infrastructure.Hosting.Configuration.WritebackJsonStore
{
    /// <summary>
    /// Manages a typed JSON document with two deliberately separate mutable branches: the file-backed
    /// <see cref="Current"/> state and a non-persisted <see cref="RuntimeWorkingCopy"/>.
    /// </summary>
    /// <typeparam name="T">Document type. Must be a reference type with a public parameterless constructor.</typeparam>
    /// <remarks>
    /// <para>
    /// The store captures <see cref="InitialSnapshot"/> once when it is created. <see cref="Current"/> represents
    /// the document that was most recently loaded from or written to the backing file. <see cref="RuntimeWorkingCopy"/>
    /// is an intentionally detached in-memory branch for runtime-only work that must not implicitly modify the file.
    /// </para>
    /// <para>
    /// Use <see cref="MutateCurrentAndSave"/> when a change is intended to become persisted configuration. Use
    /// <see cref="MutateRuntimeWorkingCopy"/> when code needs to work with the same typed model without persisting
    /// those changes. Changes to <see cref="Current"/> do not automatically overwrite <see cref="RuntimeWorkingCopy"/>;
    /// call <see cref="RestoreRuntimeWorkingCopyFromCurrent"/> explicitly when the runtime branch should discard its
    /// own changes and start again from the current file-backed state.
    /// </para>
    /// <para>
    /// The store is not an <c>IConfiguration</c> provider and does not trigger configuration reloads itself. It can
    /// be used beside normal JSON configuration or SwitchableJson with reload-on-change enabled: a successful write
    /// changes the file, and the configuration provider remains responsible for observing and publishing that change.
    /// </para>
    /// </remarks>
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
        private T _runtimeWorkingCopy;

        /// <summary>
        /// Raised after <see cref="Current"/> is changed by a successful store operation or by a successful reload
        /// of an externally changed backing file.
        /// </summary>
        /// <remarks>
        /// The first argument is a deep snapshot of the previous <see cref="Current"/> value and the second argument
        /// is a deep snapshot of the new value. The event is not raised when an operation is called with
        /// <c>notify: false</c>.
        /// </remarks>
        public event Action<T, T>? CurrentChanged;

        /// <summary>
        /// Raised after <see cref="RuntimeWorkingCopy"/> is explicitly mutated or restored.
        /// </summary>
        /// <remarks>
        /// The first argument is a deep snapshot of the previous runtime working copy and the second argument is a
        /// deep snapshot of the new value. Runtime-working-copy changes never write the backing file. The event is
        /// not raised when an operation is called with <c>notify: false</c>.
        /// </remarks>
        public event Action<T, T>? RuntimeWorkingCopyChanged;

        /// <summary>Raised when the store catches and handles an internal file-system, reload, or serialization error.</summary>
        /// <remarks>
        /// This event reports handled background errors, such as watcher-driven reload failures. Operations that
        /// cannot complete may still throw after the error has been reported.
        /// </remarks>
        public event Action<Exception>? ErrorOccurred;

        /// <summary>Gets the absolute path of the JSON document managed by this store.</summary>
        public string FilePath => _filePath;

        /// <summary>
        /// Gets the current file-backed branch of the typed document.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This value is updated by <see cref="MutateCurrentAndSave"/>, by the restore methods that target
        /// <see cref="Current"/>, and by <see cref="ReloadCurrentFromFile"/>. When file watching is enabled,
        /// successful external file changes are also reloaded into this branch.
        /// </para>
        /// <para>
        /// Treat the returned instance as read-only. Direct property mutation bypasses persistence and change
        /// notifications. Use <see cref="MutateCurrentAndSave"/> for controlled persisted changes.
        /// </para>
        /// </remarks>
        public T Current
        { get { lock (_syncRoot) { ThrowIfDisposed(); return _current; } } }

        /// <summary>
        /// Gets a deep copy of the document state captured when this store was created, before any store operation,
        /// external reload, persisted mutation, or runtime-only mutation.
        /// </summary>
        /// <remarks>
        /// The snapshot is the stable rollback baseline for the lifetime of the store. It is never replaced when
        /// <see cref="Current"/> reloads or changes. A deep copy is returned so callers cannot mutate the stored
        /// baseline.
        /// </remarks>
        public T InitialSnapshot
        { get { lock (_syncRoot) { ThrowIfDisposed(); return Clone(_initialSnapshot); } } }

        /// <summary>
        /// Gets the detached in-memory working branch intended for runtime-only use.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The runtime working copy starts as a deep copy of <see cref="InitialSnapshot"/>. Mutating it does not
        /// modify <see cref="Current"/> and never writes the backing JSON file. Likewise, persisted changes and file
        /// reloads do not automatically overwrite this branch.
        /// </para>
        /// <para>
        /// Use <see cref="MutateRuntimeWorkingCopy"/> for controlled changes,
        /// <see cref="RestoreRuntimeWorkingCopyFromCurrent"/> to discard runtime-only changes and resynchronize from
        /// the current file-backed state, or <see cref="RestoreRuntimeWorkingCopyFromInitialSnapshot"/> to return the
        /// runtime branch to the store's original startup state.
        /// </para>
        /// <para>
        /// Treat the returned instance as read-only outside the store mutation methods so that
        /// <see cref="RuntimeWorkingCopyChanged"/> remains a reliable notification boundary.
        /// </para>
        /// </remarks>
        public T RuntimeWorkingCopy
        { get { lock (_syncRoot) { ThrowIfDisposed(); return _runtimeWorkingCopy; } } }

        /// <summary>
        /// Initializes a typed file-backed JSON store and creates independent current and runtime working branches
        /// from the document state observed at construction time.
        /// </summary>
        /// <param name="filePath">Absolute or relative path of the backing JSON document.</param>
        /// <param name="watchForExternalChanges">
        /// When <see langword="true"/>, watches the backing file and reloads successful external changes into
        /// <see cref="Current"/>. The runtime working copy remains intentionally detached.
        /// </param>
        /// <param name="options">Optional JSON serializer options. When <see langword="null"/>, store defaults are used.</param>
        /// <remarks>
        /// If the file does not exist, a new <typeparamref name="T"/> is used and persisted. The state captured after
        /// construction becomes <see cref="InitialSnapshot"/> for the lifetime of this instance.
        /// </remarks>
        public WritebackJsonStore(string filePath = "dynamicsettings.json", bool watchForExternalChanges = true, JsonSerializerOptions? options = null)
        {
            _filePath = Path.GetFullPath(filePath);
            _options = options ?? CreateDefaultOptions();

            var dir = Path.GetDirectoryName(_filePath)!;
            Directory.CreateDirectory(dir);

            _current = LoadFromDisk() ?? new T();
            _initialSnapshot = Clone(_current);
            _runtimeWorkingCopy = Clone(_initialSnapshot);

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

        /// <summary>
        /// Mutates <see cref="Current"/> and writes the resulting document to the backing JSON file.
        /// </summary>
        /// <param name="mutate">Mutation applied to the current file-backed branch.</param>
        /// <param name="notify">When <see langword="true"/>, raises <see cref="CurrentChanged"/> after the save completes.</param>
        /// <remarks>
        /// This is the explicit writeback path. It does not change <see cref="RuntimeWorkingCopy"/>; use
        /// <see cref="RestoreRuntimeWorkingCopyFromCurrent"/> separately when the runtime branch should adopt the
        /// newly persisted state.
        /// </remarks>
        public void MutateCurrentAndSave(Action<T> mutate, bool notify = true)
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
                    handler = CurrentChanged;
                    if (handler != null) oldSnapshot = Clone(_current);
                }

                mutate(_current);
                SaveToDisk(_current);

                if (handler != null) newSnapshot = Clone(_current);
            }

            handler?.Invoke(oldSnapshot!, newSnapshot!);
        }

        /// <summary>
        /// Mutates <see cref="RuntimeWorkingCopy"/> without modifying <see cref="Current"/> or writing the backing file.
        /// </summary>
        /// <param name="mutate">Mutation applied only to the detached runtime working branch.</param>
        /// <param name="notify">
        /// When <see langword="true"/>, raises <see cref="RuntimeWorkingCopyChanged"/> after the mutation completes.
        /// </param>
        /// <remarks>
        /// Use this path for transient runtime work with the typed settings model when the persisted configuration must
        /// remain unchanged. The runtime branch stays detached until an explicit restore method is called.
        /// </remarks>
        public void MutateRuntimeWorkingCopy(Action<T> mutate, bool notify = true)
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
                    handler = RuntimeWorkingCopyChanged;
                    if (handler != null) oldSnapshot = Clone(_runtimeWorkingCopy);
                }

                mutate(_runtimeWorkingCopy);

                if (handler != null) newSnapshot = Clone(_runtimeWorkingCopy);
            }

            handler?.Invoke(oldSnapshot!, newSnapshot!);
        }

        /// <summary>
        /// Restores <see cref="Current"/> from <see cref="InitialSnapshot"/> and writes that original state back to
        /// the backing JSON file.
        /// </summary>
        /// <param name="notify">When <see langword="true"/>, raises <see cref="CurrentChanged"/> after the save completes.</param>
        /// <remarks>
        /// Only the persisted/current branch is restored. <see cref="RuntimeWorkingCopy"/> is deliberately left
        /// untouched. Use <see cref="RestoreAllFromInitialSnapshotAndSave"/> when both branches must roll back together.
        /// </remarks>
        public void RestoreCurrentFromInitialSnapshotAndSave(bool notify = true)
        {
            Action<T, T>? handler = null;
            T? oldSnapshot = null;
            T? newSnapshot = null;

            lock (_syncRoot)
            {
                ThrowIfDisposed();

                if (notify)
                {
                    handler = CurrentChanged;
                    if (handler != null) oldSnapshot = Clone(_current);
                }

                _current = Clone(_initialSnapshot);
                SaveToDisk(_current);

                if (handler != null) newSnapshot = Clone(_current);
            }

            handler?.Invoke(oldSnapshot!, newSnapshot!);
        }

        /// <summary>
        /// Replaces <see cref="RuntimeWorkingCopy"/> with a deep copy of the current file-backed state.
        /// </summary>
        /// <param name="notify">
        /// When <see langword="true"/>, raises <see cref="RuntimeWorkingCopyChanged"/> after the restore completes.
        /// </param>
        /// <remarks>
        /// This discards all runtime-only changes and makes the runtime branch start again from <see cref="Current"/>.
        /// No disk I/O occurs. This method is useful after a persisted mutation or external reload when runtime code
        /// explicitly chooses to adopt the current configuration state.
        /// </remarks>
        public void RestoreRuntimeWorkingCopyFromCurrent(bool notify = true)
        {
            Action<T, T>? handler = null;
            T? oldSnapshot = null;
            T? newSnapshot = null;

            lock (_syncRoot)
            {
                ThrowIfDisposed();

                if (notify)
                {
                    handler = RuntimeWorkingCopyChanged;
                    if (handler != null) oldSnapshot = Clone(_runtimeWorkingCopy);
                }

                _runtimeWorkingCopy = Clone(_current);

                if (handler != null) newSnapshot = Clone(_runtimeWorkingCopy);
            }

            handler?.Invoke(oldSnapshot!, newSnapshot!);
        }

        /// <summary>
        /// Replaces <see cref="RuntimeWorkingCopy"/> with a deep copy of <see cref="InitialSnapshot"/>.
        /// </summary>
        /// <param name="notify">
        /// When <see langword="true"/>, raises <see cref="RuntimeWorkingCopyChanged"/> after the restore completes.
        /// </param>
        /// <remarks>
        /// This discards all runtime-only changes and returns the runtime branch to the state observed when the store
        /// was created. <see cref="Current"/> and the backing JSON file are not modified.
        /// </remarks>
        public void RestoreRuntimeWorkingCopyFromInitialSnapshot(bool notify = true)
        {
            Action<T, T>? handler = null;
            T? oldSnapshot = null;
            T? newSnapshot = null;

            lock (_syncRoot)
            {
                ThrowIfDisposed();

                if (notify)
                {
                    handler = RuntimeWorkingCopyChanged;
                    if (handler != null) oldSnapshot = Clone(_runtimeWorkingCopy);
                }

                _runtimeWorkingCopy = Clone(_initialSnapshot);

                if (handler != null) newSnapshot = Clone(_runtimeWorkingCopy);
            }

            handler?.Invoke(oldSnapshot!, newSnapshot!);
        }

        /// <summary>
        /// Performs a full store rollback by restoring both <see cref="Current"/> and
        /// <see cref="RuntimeWorkingCopy"/> from <see cref="InitialSnapshot"/> and persisting the restored current state.
        /// </summary>
        /// <param name="notify">
        /// When <see langword="true"/>, raises both <see cref="CurrentChanged"/> and
        /// <see cref="RuntimeWorkingCopyChanged"/> after the rollback completes.
        /// </param>
        /// <remarks>
        /// Use this operation when the complete store should return to the state observed at construction time. The
        /// backing file is rewritten from <see cref="InitialSnapshot"/> and both mutable branches are reset to deep
        /// copies of that same baseline.
        /// </remarks>
        public void RestoreAllFromInitialSnapshotAndSave(bool notify = true)
        {
            Action<T, T>? currentHandler = null;
            Action<T, T>? runtimeHandler = null;
            T? oldCurrent = null;
            T? newCurrent = null;
            T? oldRuntime = null;
            T? newRuntime = null;

            lock (_syncRoot)
            {
                ThrowIfDisposed();

                if (notify)
                {
                    currentHandler = CurrentChanged;
                    runtimeHandler = RuntimeWorkingCopyChanged;
                    if (currentHandler != null) oldCurrent = Clone(_current);
                    if (runtimeHandler != null) oldRuntime = Clone(_runtimeWorkingCopy);
                }

                _current = Clone(_initialSnapshot);
                SaveToDisk(_current);
                _runtimeWorkingCopy = Clone(_initialSnapshot);

                if (currentHandler != null) newCurrent = Clone(_current);
                if (runtimeHandler != null) newRuntime = Clone(_runtimeWorkingCopy);
            }

            currentHandler?.Invoke(oldCurrent!, newCurrent!);
            runtimeHandler?.Invoke(oldRuntime!, newRuntime!);
        }

        /// <summary>
        /// Reloads the backing JSON file and replaces <see cref="Current"/> with the successfully loaded document.
        /// </summary>
        /// <param name="notify">When <see langword="true"/>, raises <see cref="CurrentChanged"/> after a successful reload.</param>
        /// <returns><see langword="true"/> when a document was loaded and applied; otherwise <see langword="false"/>.</returns>
        /// <remarks>
        /// The runtime working copy remains untouched. When file watching is enabled this method is also used internally
        /// after debouncing external file-system notifications.
        /// </remarks>
        public bool ReloadCurrentFromFile(bool notify = true)
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
                    handler = CurrentChanged;
                    if (handler != null) oldSnapshot = Clone(_current);
                }

                _current = reloaded;

                if (handler != null) newSnapshot = Clone(_current);
            }

            handler?.Invoke(oldSnapshot!, newSnapshot!);
            return true;
        }

        /// <summary>
        /// Creates and returns a deep snapshot of <see cref="Current"/>.
        /// </summary>
        /// <remarks>
        /// Use this method when a caller needs an independently mutable copy of the current file-backed state without
        /// changing the store. To work with the store's dedicated runtime branch, use <see cref="RuntimeWorkingCopy"/>
        /// and <see cref="MutateRuntimeWorkingCopy"/> instead.
        /// </remarks>
        public T GetCurrentSnapshot()
        { lock (_syncRoot) { ThrowIfDisposed(); return Clone(_current); } }

        /// <summary>Stops file watching and releases timer resources owned by this store.</summary>
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
            try { ReloadCurrentFromFile(notify: true); }
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
