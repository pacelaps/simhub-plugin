using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PaceLaps.SimHub.Core
{
    /// <summary>The realtime connection (Reverb): connects, reconnects, and passes on channel events.</summary>
    public interface IRealtime
    {
        /// <summary>A connection is up; the argument is its socket id (channels must be authorized for it).</summary>
        event Action<string>? Connected;

        /// <summary>The connection is gone; it reconnects by itself.</summary>
        event Action? Disconnected;

        /// <summary>An event on a channel (never a pusher: protocol frame).</summary>
        event Action<PusherFrame>? Received;

        /// <summary>Connects and keeps reconnecting until cancelled.</summary>
        Task Run(Uri socketUri, CancellationToken cancellation);

        /// <summary>Sends a frame; false when not connected.</summary>
        Task<bool> Send(string frame, CancellationToken cancellation);
    }

    /// <summary>
    /// <see cref="IRealtime"/> over ClientWebSocket, as pusher-js does it: after the activity timeout without traffic it
    /// sends pusher:ping and drops the connection when no answer comes within 30 s; reconnects after 1, 2, 4, 8, then
    /// every 10 s.
    /// </summary>
    public sealed class PusherSocket : IRealtime
    {
        public const int PongTimeoutMs = 30_000;
        private static readonly int[] BackoffMs = { 1_000, 2_000, 4_000, 8_000, 10_000 };

        private readonly ITimers _timers;
        private readonly Action<string>? _log;
        private readonly SemaphoreSlim _sendGate = new SemaphoreSlim(1, 1);
        private ClientWebSocket? _socket;

        public PusherSocket(ITimers timers, Action<string>? log = null)
        {
            _timers = timers;
            _log = log;
        }

        public event Action<string>? Connected;
        public event Action? Disconnected;
        public event Action<PusherFrame>? Received;

        public async Task Run(Uri socketUri, CancellationToken cancellation)
        {
            var attempt = 0;
            while (!cancellation.IsCancellationRequested)
            {
                var wasUp = false;
                using (var socket = new ClientWebSocket())
                {
                    socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
                    try
                    {
                        await socket.ConnectAsync(socketUri, cancellation).ConfigureAwait(false);
                        _socket = socket;
                        wasUp = await Receive(socket, cancellation).ConfigureAwait(false);
                    }
                    catch (Exception e) when (!cancellation.IsCancellationRequested)
                    {
                        _log?.Invoke("realtime: " + e.GetType().Name + ": " + e.Message);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    finally
                    {
                        _socket = null;
                    }
                }

                if (wasUp)
                {
                    attempt = 0;
                    Disconnected?.Invoke();
                }

                if (cancellation.IsCancellationRequested)
                {
                    break;
                }

                try
                {
                    await _timers.Delay(BackoffMs[Math.Min(attempt++, BackoffMs.Length - 1)], cancellation).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        public async Task<bool> Send(string frame, CancellationToken cancellation)
        {
            var socket = _socket;
            if (socket == null || socket.State != WebSocketState.Open)
            {
                return false;
            }

            await _sendGate.WaitAsync(cancellation).ConfigureAwait(false);
            try
            {
                var bytes = Encoding.UTF8.GetBytes(frame);
                await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellation).ConfigureAwait(false);
                return true;
            }
            catch (WebSocketException)
            {
                return false;
            }
            finally
            {
                _sendGate.Release();
            }
        }

        /// <summary>Reads until the connection ends; true if it was established (connection_established came).</summary>
        private async Task<bool> Receive(ClientWebSocket socket, CancellationToken cancellation)
        {
            var established = false;
            var activityMs = 120_000L;
            var buffer = new byte[16 * 1024];
            var pingSent = false;
            // One read stays pending while the silence check runs beside it: cancelling a pending read would abort
            // the socket.
            Task<string?>? read = null;
            while (socket.State == WebSocketState.Open && !cancellation.IsCancellationRequested)
            {
                read ??= ReadMessage(socket, buffer, cancellation);

                // Silence for the activity timeout: ping. Silence for 30 s after a ping: the connection is dead.
                using (var quiet = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
                {
                    var silence = _timers.Delay(pingSent ? PongTimeoutMs : activityMs, quiet.Token);
                    var first = await Task.WhenAny(read, silence).ConfigureAwait(false);
                    quiet.Cancel();
                    if (first != read)
                    {
                        if (cancellation.IsCancellationRequested)
                        {
                            break;
                        }

                        if (pingSent)
                        {
                            _log?.Invoke("realtime: no answer to a ping, reconnecting");
                            socket.Abort();
                            return established;
                        }

                        pingSent = await Send(PusherProtocol.Ping(), cancellation).ConfigureAwait(false);
                        continue;
                    }
                }

                var text = await read.ConfigureAwait(false);
                read = null;
                if (text == null)
                {
                    return established;
                }

                pingSent = false;
                var frame = PusherProtocol.Parse(text);
                if (frame == null)
                {
                    continue;
                }

                if (PusherProtocol.TryConnectionEstablished(frame, out var socketId, out var activitySec))
                {
                    established = true;
                    // A little less than the server's own timeout, as pusher-js does.
                    activityMs = Math.Max(5, activitySec - 5) * 1000L;
                    Connected?.Invoke(socketId);
                }
                else if (frame.Event == "pusher:ping")
                {
                    await Send(PusherProtocol.Pong(), cancellation).ConfigureAwait(false);
                }
                else if (frame.Event == "pusher:error")
                {
                    _log?.Invoke("realtime: server error " + frame.Data);
                }
                else if (!frame.Event.StartsWith("pusher:", StringComparison.Ordinal) && !frame.Event.StartsWith("pusher_internal:", StringComparison.Ordinal))
                {
                    Received?.Invoke(frame);
                }
            }

            return established;
        }

        /// <summary>One whole text message, or null when the socket closed.</summary>
        private static async Task<string?> ReadMessage(ClientWebSocket socket, byte[] buffer, CancellationToken cancellation)
        {
            using var message = new MemoryStream();
            while (true)
            {
                var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellation).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return null;
                }

                message.Write(buffer, 0, result.Count);
                if (result.EndOfMessage)
                {
                    return Encoding.UTF8.GetString(message.ToArray());
                }
            }
        }
    }
}
