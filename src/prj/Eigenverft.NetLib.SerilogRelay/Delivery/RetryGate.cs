using System;

namespace Eigenverft.NetLib.SerilogRelay
{
    internal sealed class RetryGate
    {
        private readonly object _sync = new object();
        private readonly EndpointRetryOptions _options;
        private readonly Func<double> _nextDouble;

        private int _consecutiveFailures;
        private DateTimeOffset? _nextAttemptAt;
        private bool _attemptInFlight;

        internal RetryGate(EndpointRetryOptions options, Func<double>? nextDouble = null)
        {
            ArgumentNullException.ThrowIfNull(options);

            _options = new EndpointRetryOptions
            {
                InitialDelay = options.InitialDelay,
                Multiplier = options.Multiplier,
                MaximumDelay = options.MaximumDelay,
                JitterRatio = options.JitterRatio,
                RespectRetryAfter = options.RespectRetryAfter,
            };

            if (_options.InitialDelay <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(options), "Retry initial delay must be greater than zero.");
            if (!double.IsFinite(_options.Multiplier) || _options.Multiplier < 1d)
                throw new ArgumentOutOfRangeException(nameof(options), "Retry multiplier must be finite and at least 1.");
            if (_options.MaximumDelay < _options.InitialDelay)
                throw new ArgumentOutOfRangeException(nameof(options), "Retry maximum delay must be greater than or equal to the initial delay.");
            if (!double.IsFinite(_options.JitterRatio) || _options.JitterRatio < 0d || _options.JitterRatio > 1d)
                throw new ArgumentOutOfRangeException(nameof(options), "Retry jitter ratio must be finite and between 0 and 1.");

            _nextDouble = nextDouble ?? Random.Shared.NextDouble;
        }

        internal TimeSpan MaximumDelay => _options.MaximumDelay;

        internal int ConsecutiveFailures
        {
            get
            {
                lock (_sync)
                {
                    return _consecutiveFailures;
                }
            }
        }

        internal DateTimeOffset? NextAttemptAt
        {
            get
            {
                lock (_sync)
                {
                    return _nextAttemptAt;
                }
            }
        }

        internal bool TryAcquire(DateTimeOffset now, bool ignoreBackoff = false)
        {
            lock (_sync)
            {
                if (_attemptInFlight || (!ignoreBackoff && _nextAttemptAt.HasValue && _nextAttemptAt.Value > now))
                    return false;

                _attemptInFlight = true;
                return true;
            }
        }

        internal TimeSpan GetDelay(DateTimeOffset now)
        {
            lock (_sync)
            {
                if (!_nextAttemptAt.HasValue || _nextAttemptAt.Value <= now)
                    return TimeSpan.Zero;

                return _nextAttemptAt.Value - now;
            }
        }

        internal void RecordSuccess()
        {
            lock (_sync)
            {
                _attemptInFlight = false;
                _consecutiveFailures = 0;
                _nextAttemptAt = null;
            }
        }

        internal void RecordFailure(DateTimeOffset now, DateTimeOffset? retryAfter)
        {
            lock (_sync)
            {
                _attemptInFlight = false;
                if (_consecutiveFailures < int.MaxValue)
                    _consecutiveFailures++;

                double exponent = Math.Pow(_options.Multiplier, _consecutiveFailures - 1);
                double baseMilliseconds = Math.Min(
                    _options.InitialDelay.TotalMilliseconds * exponent,
                    _options.MaximumDelay.TotalMilliseconds);

                double jitterFactor = 1d;
                if (_options.JitterRatio > 0d)
                {
                    double centered = (_nextDouble() * 2d) - 1d;
                    jitterFactor += centered * _options.JitterRatio;
                }

                double jitteredMilliseconds = Math.Min(
                    Math.Max(1d, baseMilliseconds * jitterFactor),
                    _options.MaximumDelay.TotalMilliseconds);

                DateTimeOffset nextAttempt = now + TimeSpan.FromMilliseconds(jitteredMilliseconds);
                if (_options.RespectRetryAfter
                    && retryAfter.HasValue
                    && retryAfter.Value > nextAttempt)
                {
                    DateTimeOffset latestAttempt = now + _options.MaximumDelay;
                    nextAttempt = retryAfter.Value > latestAttempt ? latestAttempt : retryAfter.Value;
                }

                _nextAttemptAt = nextAttempt;
            }
        }

        internal void CancelAttempt()
        {
            lock (_sync)
            {
                _attemptInFlight = false;
            }
        }
    }
}
