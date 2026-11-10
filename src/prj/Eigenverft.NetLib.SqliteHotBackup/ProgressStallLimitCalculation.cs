using System;

namespace Eigenverft.NetLib.SqliteHotBackup
{
    /// <summary>A configuration recipe bounding inactivity and observed rewind work, independently per source attempt.</summary>
    public sealed class ProgressStallLimitCalculation
    {
        /// <summary>Gets or sets the maximum interval without copied-prefix advance. Defaults to five minutes.</summary>
        public TimeSpan NoProgressTimeout { get; set; } = TimeSpan.FromMinutes(5);
        /// <summary>Gets or sets the allowed accumulated counter loss divided by the largest observed page count. Defaults to two.</summary>
        public double MaxRewindWorkFactor { get; set; } = 2.0;

        internal ProgressStallLimitCalculation Copy() => new()
        {
            NoProgressTimeout = NoProgressTimeout,
            MaxRewindWorkFactor = MaxRewindWorkFactor
        };

        internal void Validate()
        {
            if (NoProgressTimeout <= TimeSpan.Zero || NoProgressTimeout.TotalSeconds > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(NoProgressTimeout));
            }
            if (!double.IsFinite(MaxRewindWorkFactor) || MaxRewindWorkFactor <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(MaxRewindWorkFactor));
            }
        }
    }

    internal sealed class ProgressStallState
    {
        private readonly ProgressStallLimitCalculation _settings;
        private readonly TimeProvider _time;
        private long _lastAdvance;
        private long _previousCopied;
        private TimeSpan _preparationTime;

        internal ProgressStallState(ProgressStallLimitCalculation settings, TimeProvider time, TimeSpan preparationTime = default)
        {
            settings.Validate();
            _settings = settings.Copy();
            _time = time;
            _lastAdvance = time.GetTimestamp();
            _preparationTime = preparationTime;
        }

        internal long TotalPages { get; private set; }
        internal long RemainingPages { get; private set; }
        internal long LargestPageCount { get; private set; }
        internal long RewoundPages { get; private set; }

        internal void Observe(long total, long remaining)
        {
            TotalPages = total;
            RemainingPages = remaining;
            LargestPageCount = Math.Max(LargestPageCount, total);
            long copied = total - remaining;
            if (copied > _previousCopied)
            {
                _lastAdvance = _time.GetTimestamp();
                _preparationTime = TimeSpan.Zero;
            }
            else if (copied < _previousCopied)
            {
                RewoundPages = checked(RewoundPages + _previousCopied - copied);
            }
            _previousCopied = copied;
        }

        internal string? ExceededReason()
        {
            if (_time.GetElapsedTime(_lastAdvance) + _preparationTime >= _settings.NoProgressTimeout)
            {
                return "The source made no copied-prefix advance within its execution limit.";
            }
            if (RewoundPages > _settings.MaxRewindWorkFactor * LargestPageCount)
            {
                return "Observed rewind work exceeded the source's execution limit.";
            }
            return null;
        }
    }
}
