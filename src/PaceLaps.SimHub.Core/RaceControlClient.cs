using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PaceLaps.SimHub.Core
{
    /// <summary>
    /// The driver's connection to race control, as the web views do it (synchronized display protocol): measure the
    /// clock offset, load the state, follow the device's channels, show each call when the corrected clock reaches its
    /// showAt, report when it was shown, warn at once when offline and resync with the cursor on return. Display only:
    /// the server decides what this device may see.
    /// </summary>
    public sealed class RaceControlClient : IDisposable
    {
        /// <summary>A full clock measurement this often (the web views: 5 minutes).</summary>
        public const long ClockResyncMs = 300_000;

        /// <summary>One ping this often in between: the heartbeat that keeps the device counted as connected.</summary>
        public const long HeartbeatMs = 60_000;

        public const long RetryMs = 5_000;
        public const long ClockBurstGapMs = 20;

        private readonly IDeviceApi _api;
        private readonly IRealtime _realtime;
        private readonly ISystemClock _clock;
        private readonly ITimers _timers;
        private readonly Uri _site;
        private readonly string _clientVersion;
        private readonly Action<string>? _log;
        private readonly ClockSync _clockSync;
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();
        private readonly SemaphoreSlim _loadGate = new SemaphoreSlim(1, 1);
        private readonly object _gate = new object();
        private readonly Dictionary<string, IDisposable> _pending = new Dictionary<string, IDisposable>();
        private readonly HashSet<string> _seen = new HashSet<string>();
        private readonly HashSet<string> _joined = new HashSet<string>();

        private DriverState _state = new DriverState();
        private bool _connected;
        private bool _everConnected;
        private bool _signedOut;
        private string? _socketId;
        private string? _cursor;
        private int _oneOffSeconds = 8;
        private DeviceChannels _channels = DeviceChannels.None;
        private string? _controlChannel;

        public RaceControlClient(IDeviceApi api, IRealtime realtime, ISystemClock clock, ITimers timers, Uri site,
            string clientVersion, Action<string>? log = null)
        {
            _api = api;
            _realtime = realtime;
            _clock = clock;
            _timers = timers;
            _site = site;
            _clientVersion = clientVersion;
            _log = log;
            _clockSync = new ClockSync(clock, timers, api.Clock);
            _realtime.Connected += OnConnected;
            _realtime.Disconnected += OnDisconnected;
            _realtime.Received += OnReceived;
        }

        /// <summary>The latest state; replaced, never changed.</summary>
        public DriverState State => Volatile.Read(ref _state);

        /// <summary>Server time now, by the device clock corrected with the measured offset.</summary>
        public long ServerNowMs => _clockSync.ServerNowMs;

        /// <summary>A new state (on any thread).</summary>
        public event Action<DriverState>? Changed;

        /// <summary>A call was shown now (for its sound and SimHub's events).</summary>
        public event Action<RaceMessage>? CallShown;

        /// <summary>Connects and keeps the state current until disposed. Returns once the background work runs.</summary>
        public async Task Start()
        {
            var stop = _stop.Token;
            Publish();

            // One ping is enough to start; more refine the offset while it connects.
            await _clockSync.Burst(1, ClockBurstGapMs, stop).ConfigureAwait(false);
            Run(() => _clockSync.Burst(7, ClockBurstGapMs, stop));

            await Load(null).ConfigureAwait(false);
            if (Volatile.Read(ref _signedOut))
            {
                return;
            }

            Run(ConnectRealtime);
            Run(ClockLoop);
            Run(ExpireLoop);
        }

        public void Dispose()
        {
            _stop.Cancel();
            lock (_gate)
            {
                foreach (var timer in _pending.Values)
                {
                    timer.Dispose();
                }

                _pending.Clear();
            }
        }

        private async Task ConnectRealtime()
        {
            var stop = _stop.Token;
            string? key = null;
            while (key == null && !stop.IsCancellationRequested)
            {
                try
                {
                    key = await _api.RealtimeKey(stop).ConfigureAwait(false);
                }
                catch (DeviceApiException e)
                {
                    if (Problem(e))
                    {
                        return;
                    }

                    await _timers.Delay(RetryMs, stop).ConfigureAwait(false);
                }
            }

            if (key != null)
            {
                await _realtime.Run(PusherProtocol.SocketUri(_site, key, _clientVersion), stop).ConfigureAwait(false);
            }
        }

        /// <summary>A ping a minute (the heartbeat), a full measurement every 5 minutes.</summary>
        private async Task ClockLoop()
        {
            var stop = _stop.Token;
            var sinceFull = 0L;
            while (!stop.IsCancellationRequested)
            {
                await _timers.Delay(HeartbeatMs, stop).ConfigureAwait(false);
                sinceFull += HeartbeatMs;
                if (sinceFull >= ClockResyncMs)
                {
                    sinceFull = 0;
                    await _clockSync.Burst(4, ClockBurstGapMs, stop).ConfigureAwait(false);
                }
                else
                {
                    await _clockSync.Ping(stop).ConfigureAwait(false);
                }

                Publish();
            }
        }

        /// <summary>Each one-off has its own timer (Present); this is the safety net, once a second.</summary>
        private async Task ExpireLoop()
        {
            var stop = _stop.Token;
            while (!stop.IsCancellationRequested)
            {
                await _timers.Delay(1_000, stop).ConfigureAwait(false);
                Expire();
            }
        }

        /// <summary>The state snapshot; with a cursor only what was missed (resync).</summary>
        private async Task Load(string? cursor)
        {
            var stop = _stop.Token;
            await _loadGate.WaitAsync(stop).ConfigureAwait(false);
            try
            {
                StateSnapshot snapshot;
                try
                {
                    snapshot = await _api.State(cursor, stop).ConfigureAwait(false);
                }
                catch (DeviceApiException e)
                {
                    if (!Problem(e))
                    {
                        _timers.After(RetryMs, () => Run(() => Load(cursor)));
                    }

                    return;
                }

                Apply(snapshot, cursor);
            }
            finally
            {
                _loadGate.Release();
            }

            await SubscribeWanted().ConfigureAwait(false);
        }

        private void Apply(StateSnapshot snapshot, string? cursor)
        {
            var due = new List<RaceMessage>();
            lock (_gate)
            {
                var sameSession = _state.SessionId != null && snapshot.SessionId == _state.SessionId;
                var next = _state.Copy();
                next.Problem = null;
                next.SessionId = snapshot.SessionId;
                next.SessionName = snapshot.SessionName;
                next.Device = snapshot.Device;
                next.Phase = snapshot.Phase;
                next.Procedure = snapshot.Procedure;
                next.Target = snapshot.Target;
                if (snapshot.HasRace)
                {
                    next.Race = snapshot.Race;
                }

                _oneOffSeconds = snapshot.OneOffSeconds;
                _channels = snapshot.Channels;
                _controlChannel = snapshot.Device.League == null ? null : "league-devices." + snapshot.Device.League;

                if (cursor != null && sameSession)
                {
                    // Missed while offline: shown now and reported late.
                    due.AddRange(snapshot.RecentMessages);
                    next.Display = next.Display.WithBanners(snapshot.Banners);
                }
                else
                {
                    // Connect, new session or reload: restore the state; only calls still to come are scheduled.
                    _seen.Clear();
                    foreach (var m in snapshot.RecentMessages)
                    {
                        _seen.Add(m.Id);
                    }

                    next.Display = CallDisplay.FromSnapshot(snapshot, _clockSync.ServerNowMs, out var later);
                    foreach (var m in later)
                    {
                        _seen.Remove(m.Id);
                        due.Add(m);
                    }
                }

                _cursor = Cursors.Later(_cursor, snapshot.Cursor);
                _state = next;
            }

            Publish();
            foreach (var m in due)
            {
                Receive(m);
            }
        }

        /// <summary>Reloads the target and the procedure only (the car's sorting target changed).</summary>
        private async Task RefreshTarget()
        {
            try
            {
                var snapshot = await _api.State(_cursor, _stop.Token).ConfigureAwait(false);
                lock (_gate)
                {
                    var next = _state.Copy();
                    next.Target = snapshot.Target;
                    next.Phase = snapshot.Phase ?? next.Phase;
                    next.Procedure = snapshot.Procedure;
                    _state = next;
                }

                Publish();
            }
            catch (DeviceApiException)
            {
                // The next change or a resync brings it.
            }
        }

        /// <summary>Joins the device's channels on the current socket and leaves the others.</summary>
        private async Task SubscribeWanted()
        {
            string? socketId;
            List<string> join;
            List<string> leave;
            lock (_gate)
            {
                socketId = _socketId;
                if (!_connected || socketId == null)
                {
                    return;
                }

                var wanted = _channels.Banner.Concat(_channels.Live).ToList();
                if (_controlChannel != null)
                {
                    wanted.Add(_controlChannel);
                }

                join = wanted.Where(c => !_joined.Contains(c)).ToList();
                leave = _joined.Where(c => !wanted.Contains(c)).ToList();
                foreach (var c in join)
                {
                    _joined.Add(c);
                }

                foreach (var c in leave)
                {
                    _joined.Remove(c);
                }
            }

            var stop = _stop.Token;
            foreach (var c in leave)
            {
                await _realtime.Send(PusherProtocol.Unsubscribe("private-" + c), stop).ConfigureAwait(false);
            }

            foreach (var c in join)
            {
                try
                {
                    var auth = await _api.Authorize(socketId, "private-" + c, stop).ConfigureAwait(false);
                    await _realtime.Send(PusherProtocol.Subscribe("private-" + c, auth), stop).ConfigureAwait(false);
                }
                catch (DeviceApiException e)
                {
                    _log?.Invoke("channel " + c + ": " + e.Message);
                    lock (_gate)
                    {
                        _joined.Remove(c);
                    }

                    if (!Problem(e))
                    {
                        _timers.After(RetryMs, () => Run(SubscribeWanted));
                    }
                }
            }
        }

        private void OnConnected(string socketId)
        {
            bool wasOffline;
            string? cursor;
            lock (_gate)
            {
                wasOffline = _everConnected && !_connected;
                _connected = true;
                _everConnected = true;
                _socketId = socketId;
                // A new socket has no channels yet.
                _joined.Clear();
                cursor = _cursor;
            }

            Publish();
            // Back after a drop: what was missed (and the channels); otherwise just the channels.
            Run(wasOffline ? () => Load(cursor) : (Func<Task>)SubscribeWanted);
        }

        private void OnDisconnected()
        {
            lock (_gate)
            {
                _connected = false;
                _socketId = null;
                _joined.Clear();
            }

            Publish();
        }

        private void OnReceived(PusherFrame frame)
        {
            if (frame.Channel == null || !frame.Channel.StartsWith("private-", StringComparison.Ordinal))
            {
                return;
            }

            var channel = frame.Channel.Substring("private-".Length);
            DeviceChannels channels;
            string? control;
            lock (_gate)
            {
                channels = _channels;
                control = _controlChannel;
            }

            if (channel == control)
            {
                // A new session, or the audit visibility changed: load what this device may see now.
                if (frame.Event == "session.changed" || frame.Event == "settings.changed")
                {
                    Run(() => Load(null));
                }
            }
            else if (channels.Live.Contains(channel))
            {
                if (frame.Event == "race.state" && frame.Data != null)
                {
                    var race = RaceState.From(Json.Object(frame.Data));
                    if (race != null)
                    {
                        Update(s => s.Race = race);
                    }
                }
            }
            else if (channels.Banner.Contains(channel))
            {
                if (frame.Event == "message" && frame.Data != null)
                {
                    var message = RaceMessage.FromJson(frame.Data);
                    if (message != null)
                    {
                        Receive(message);
                    }
                }
                else if (frame.Event == "target.changed")
                {
                    Run(RefreshTarget);
                }
            }
        }

        /// <summary>A call for this device: shown at its showAt, or at once if that has passed.</summary>
        private void Receive(RaceMessage message)
        {
            var receivedAt = _clock.NowMs;
            long delay;
            lock (_gate)
            {
                if (!_seen.Add(message.Id))
                {
                    return;
                }

                _cursor = Cursors.Later(_cursor, message.Cursor);
                delay = Schedule.DelayUntil(message.ShowAtMs, receivedAt, _clockSync.OffsetMs);
                if (delay > 0)
                {
                    _pending[message.Id] = _timers.After(delay, () => Present(message, receivedAt, false));
                    return;
                }
            }

            Present(message, receivedAt, true);
        }

        private void Present(RaceMessage message, long receivedAtMs, bool late)
        {
            var best = _clockSync.Best;
            var offset = best?.OffsetMs ?? 0;
            var shownAt = _clockSync.ServerNowMs;
            int oneOffSeconds;
            lock (_gate)
            {
                _pending.Remove(message.Id);
                oneOffSeconds = _oneOffSeconds;
                var next = _state.Copy();
                next.Display = next.Display.Show(message, shownAt, oneOffSeconds);
                _state = next;
            }

            if (!message.Sticky)
            {
                _timers.After(oneOffSeconds * 1000L, Expire);
            }

            Publish();
            try
            {
                CallShown?.Invoke(message);
            }
            catch (Exception e)
            {
                _log?.Invoke("call shown handler: " + e.Message);
            }

            var report = new ShownReport(message.Id, shownAt, receivedAtMs + (long)Math.Round(offset), offset, best?.RttMs ?? 0, late);
            Run(async () =>
            {
                try
                {
                    await _api.Shown(report, _stop.Token).ConfigureAwait(false);
                }
                catch (DeviceApiException)
                {
                    // Lost reports only thin out the statistics.
                }
            });
        }

        private void Expire()
        {
            bool changed;
            lock (_gate)
            {
                var display = _state.Display.Expire(_clockSync.ServerNowMs);
                changed = !ReferenceEquals(display, _state.Display);
                if (changed)
                {
                    var next = _state.Copy();
                    next.Display = display;
                    _state = next;
                }
            }

            if (changed)
            {
                Publish();
            }
        }

        /// <summary>Records a failed request; true when the overlay URL no longer works (everything stops).</summary>
        private bool Problem(DeviceApiException e)
        {
            _log?.Invoke(e.Message);
            if (e.SignedOut)
            {
                Volatile.Write(ref _signedOut, true);
                _stop.Cancel();
                Update(s => s.Problem = "This overlay URL no longer works. Paste a current one from your driver page.");
                return true;
            }

            Update(s => s.Problem = "Cannot reach race control.");
            return false;
        }

        private void Update(Action<DriverState> change)
        {
            lock (_gate)
            {
                var next = _state.Copy();
                change(next);
                _state = next;
            }

            Publish();
        }

        /// <summary>Sets the status and clock from the connection, then tells the listeners.</summary>
        private void Publish()
        {
            DriverState state;
            lock (_gate)
            {
                var next = _state.Copy();
                next.Status = Volatile.Read(ref _signedOut) ? ConnectionStatus.SignedOut
                    : _connected ? ConnectionStatus.Connected
                    : _everConnected ? ConnectionStatus.Offline
                    : ConnectionStatus.Connecting;
                next.Clock = _clockSync.Best;
                _state = next;
                state = next;
            }

            try
            {
                Changed?.Invoke(state);
            }
            catch (Exception e)
            {
                _log?.Invoke("state handler: " + e.Message);
            }
        }

        /// <summary>
        /// Background work, started on the calling thread (it runs until its first wait), whose failures are logged
        /// instead of lost. Callers are timer, socket or SimHub threads; tests with fakes run it to the end inline.
        /// </summary>
        private void Run(Func<Task> work)
        {
            Task task;
            try
            {
                task = work();
            }
            catch (Exception e)
            {
                _log?.Invoke(e.GetType().Name + ": " + e.Message);
                return;
            }

            task.ContinueWith(t =>
            {
                var e = t.Exception?.GetBaseException();
                if (e != null && !(e is OperationCanceledException))
                {
                    _log?.Invoke(e.GetType().Name + ": " + e.Message);
                }
            }, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
        }
    }
}
