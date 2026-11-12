using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace PaceLaps.SimHub.Core
{
    /// <summary>The device clock in Unix milliseconds. Tests use a fake one.</summary>
    public interface ISystemClock
    {
        long NowMs { get; }
    }

    public sealed class SystemClock : ISystemClock
    {
        public long NowMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    /// <summary>One-shot timers and delays on the device clock. Tests advance a fake one by hand.</summary>
    public interface ITimers
    {
        /// <summary>Runs <paramref name="callback"/> once after <paramref name="delayMs"/>; dispose to cancel.</summary>
        IDisposable After(long delayMs, Action callback);

        Task Delay(long delayMs, CancellationToken cancellation);
    }

    /// <summary>Thread pool timers (System.Threading.Timer: about 15 ms resolution on Windows).</summary>
    public sealed class ThreadTimers : ITimers
    {
        public IDisposable After(long delayMs, Action callback)
        {
            var timer = new OneShot(callback);
            timer.Start(Math.Max(0, delayMs));
            return timer;
        }

        public Task Delay(long delayMs, CancellationToken cancellation) =>
            Task.Delay(TimeSpan.FromMilliseconds(Math.Max(0, delayMs)), cancellation);

        private sealed class OneShot : IDisposable
        {
            private readonly Action _callback;
            private Timer? _timer;

            public OneShot(Action callback) => _callback = callback;

            public void Start(long delayMs)
            {
                _timer = new Timer(_ => Fire(), null, Timeout.Infinite, Timeout.Infinite);
                _timer.Change(delayMs, Timeout.Infinite);
            }

            private void Fire()
            {
                Dispose();
                _callback();
            }

            public void Dispose() => Interlocked.Exchange(ref _timer, null)?.Dispose();
        }
    }

    /// <summary>ISO 8601 timestamps as the contracts write them (UTC, milliseconds, Z).</summary>
    public static class Iso
    {
        public static long ToMs(string iso) =>
            DateTimeOffset.Parse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal)
                .ToUnixTimeMilliseconds();

        public static bool TryToMs(string? iso, out long ms)
        {
            ms = 0;
            if (iso == null || !DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var at))
            {
                return false;
            }

            ms = at.ToUnixTimeMilliseconds();
            return true;
        }

        public static string FromMs(long ms) =>
            DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
    }
}
