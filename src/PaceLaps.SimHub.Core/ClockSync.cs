using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PaceLaps.SimHub.Core
{
    /// <summary>One ping's result: server clock minus device clock, and the network round trip.</summary>
    public readonly struct ClockSample
    {
        public ClockSample(double offsetMs, double rttMs)
        {
            OffsetMs = offsetMs;
            RttMs = rttMs;
        }

        public double OffsetMs { get; }
        public double RttMs { get; }
    }

    /// <summary>The server's answer to a clock ping (contract clock_pong), in Unix milliseconds.</summary>
    public readonly struct ClockPong
    {
        public ClockPong(long serverTimeMs, long? serverReceivedMs)
        {
            ServerTimeMs = serverTimeMs;
            ServerReceivedMs = serverReceivedMs;
        }

        public long ServerTimeMs { get; }
        public long? ServerReceivedMs { get; }
    }

    /// <summary>
    /// Clock offset to the server, as the web views measure it. A sample is one ping: the device times t0 (sent) and
    /// t3 (answered), the server t1 (ping arrived) and t2 (pong left). As in NTP:
    ///   offset = ((t1 - t0) + (t2 - t3)) / 2, rtt = (t3 - t0) - (t2 - t1),
    /// accurate to rtt / 2. The sample with the shortest round trip among the last few wins.
    /// </summary>
    public sealed class ClockSync
    {
        public const int Keep = 8;

        private readonly ISystemClock _clock;
        private readonly ITimers _timers;
        private readonly Func<long, CancellationToken, Task<ClockPong>> _ping;
        private readonly object _gate = new object();
        private List<ClockSample> _samples = new List<ClockSample>();

        /// <param name="ping">Sends a clock ping with the given device time and returns the pong.</param>
        public ClockSync(ISystemClock clock, ITimers timers, Func<long, CancellationToken, Task<ClockPong>> ping)
        {
            _clock = clock;
            _timers = timers;
            _ping = ping;
        }

        public static ClockSample SampleFrom(long sentAtMs, long receivedAtMs, long serverTimeMs, long? serverReceivedMs = null)
        {
            var serverReceived = serverReceivedMs ?? serverTimeMs;
            var processing = Math.Max(0, serverTimeMs - serverReceived);
            var rtt = Math.Max(0, receivedAtMs - sentAtMs - processing);
            var offset = (serverReceived - sentAtMs + (serverTimeMs - receivedAtMs)) / 2.0;
            return new ClockSample(offset, rtt);
        }

        public static ClockSample? BestOf(IEnumerable<ClockSample> samples)
        {
            ClockSample? best = null;
            foreach (var s in samples)
            {
                if (best == null || s.RttMs < best.Value.RttMs)
                {
                    best = s;
                }
            }

            return best;
        }

        public ClockSample? Best
        {
            get
            {
                lock (_gate)
                {
                    return BestOf(_samples);
                }
            }
        }

        public double OffsetMs => Best?.OffsetMs ?? 0;

        /// <summary>Server time now: the device clock corrected by the best offset (0 before any sample).</summary>
        public long ServerNowMs => _clock.NowMs + (long)Math.Round(OffsetMs);

        public void Add(ClockSample sample)
        {
            lock (_gate)
            {
                // Older samples go: the device clock drifts, and a PC that slept may have jumped.
                _samples = _samples.Concat(new[] { sample }).Skip(Math.Max(0, _samples.Count + 1 - Keep)).ToList();
            }
        }

        /// <summary>One ping. A failed ping gives no sample; the connection state shows the rest.</summary>
        public async Task<bool> Ping(CancellationToken cancellation)
        {
            var sentAt = _clock.NowMs;
            ClockPong pong;
            try
            {
                pong = await _ping(sentAt, cancellation).ConfigureAwait(false);
            }
            catch (Exception) when (!cancellation.IsCancellationRequested)
            {
                return false;
            }

            Add(SampleFrom(sentAt, _clock.NowMs, pong.ServerTimeMs, pong.ServerReceivedMs));
            return true;
        }

        /// <summary><paramref name="count"/> pings close together (a dozing network is slow on its first packet).</summary>
        public async Task<ClockSample?> Burst(int count, long gapMs, CancellationToken cancellation)
        {
            for (var i = 0; i < count; i++)
            {
                await Ping(cancellation).ConfigureAwait(false);
                if (i < count - 1)
                {
                    await _timers.Delay(gapMs, cancellation).ConfigureAwait(false);
                }
            }

            return Best;
        }
    }
}
