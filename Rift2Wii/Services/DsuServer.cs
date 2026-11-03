using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace OpenXRInputExplorer.Services;

/// <summary>Driver-free, loopback-only DSU server: left hand in slot 0, right in slot 1.</summary>
public sealed class DsuServer : IDisposable
{
    private readonly UdpClient _socket;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _worker;
    private readonly uint _id = (uint)Random.Shared.NextInt64(1, uint.MaxValue);
    private DsuPadState[] _states = [DsuPadState.Neutral, DsuPadState.Neutral];
    private float _deadzone = 0.15f;
    private long _publishedAt;
    private volatile int _clients;
    private volatile string? _error;
    private bool _disposed;
    public int Port { get; }
    public bool IsRunning => !_disposed && !_worker.IsCompleted;
    public string Status => _error is null ? $"DSU 127.0.0.1:{Port} — {_clients} subscriber(s). Slot 0 left, slot 1 right."
        : $"DSU stopped: {_error}";

    private sealed class Subscription(IPEndPoint endpoint)
    {
        public IPEndPoint Endpoint { get; } = endpoint;
        public long[] Expires { get; } = new long[2];
    }

    public DsuServer(int port = 26760)
    {
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        _socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
        Port = ((IPEndPoint)_socket.Client.LocalEndPoint!).Port;
        _worker = RunAsync(_stop.Token);
    }

    public void Publish(DsuPadState left, DsuPadState right, float deadzone)
    {
        Volatile.Write(ref _deadzone, Math.Clamp(deadzone, 0, 0.9f));
        Volatile.Write(ref _states, new[] { left, right });
        Interlocked.Exchange(ref _publishedAt, Environment.TickCount64);
    }

    private DsuPadState[] ReadStates() => Environment.TickCount64 - Interlocked.Read(ref _publishedAt) > 250
        ? new[] { DsuPadState.Neutral, DsuPadState.Neutral } : Volatile.Read(ref _states); // Neutralize stale input; keep virtual pads discoverable.

    private async Task RunAsync(CancellationToken cancellation)
    {
        var subscriptions = new Dictionary<IPEndPoint, Subscription>();
        uint counter = 0;
        try
        {
            Task<UdpReceiveResult> pending = _socket.ReceiveAsync(cancellation).AsTask();
            while (!cancellation.IsCancellationRequested)
            {
                await Task.WhenAny(pending, Task.Delay(8, cancellation));
                if (pending.IsCompleted)
                {
                    var received = await pending;
                    pending = _socket.ReceiveAsync(cancellation).AsTask();
                    byte[] request = received.Buffer;
                    var endpoint = received.RemoteEndPoint;
                    if (DsuProtocol.IsRequest(request))
                    {
                        uint type = BinaryPrimitives.ReadUInt32LittleEndian(request.AsSpan(16));
                        if (type == DsuProtocol.VersionMessage)
                        {
                            byte[] response = DsuProtocol.Message(type, 24, _id);
                            BinaryPrimitives.WriteUInt16LittleEndian(response.AsSpan(20), DsuProtocol.Version);
                            await Send(DsuProtocol.Finish(response), endpoint, cancellation);
                        }
                        else if (type == DsuProtocol.PortsMessage && request.Length >= 24)
                        {
                            uint count = BinaryPrimitives.ReadUInt32LittleEndian(request.AsSpan(20));
                            if (count <= 4 && request.Length >= 24 + count)
                            {
                                var states = ReadStates();
                                for (int i = 0; i < count; i++)
                                {
                                    byte slot = request[24 + i];
                                    if (slot > 3) continue;
                                    byte[] response = DsuProtocol.Message(type, 32, _id);
                                    DsuProtocol.WritePort(response, slot, slot < 2 && states[slot].Connected);
                                    await Send(DsuProtocol.Finish(response), endpoint, cancellation);
                                }
                            }
                        }
                        else if (type == DsuProtocol.DataMessage && request.Length >= 28 && request[20] <= 3)
                        {
                            if (!subscriptions.TryGetValue(endpoint, out var subscription) && subscriptions.Count < 16)
                                subscriptions[endpoint] = subscription = new Subscription(endpoint);
                            if (subscription is not null)
                                for (int slot = 0; slot < 2; slot++)
                                {
                                    bool matches = request[20] == 0 ||
                                        ((request[20] & 1) != 0 && request[21] == slot) ||
                                        ((request[20] & 2) != 0 && request.AsSpan(22, 6).SequenceEqual(
                                            new byte[] { 0x02, 0x52, 0x49, 0x46, 0x54, (byte)(slot + 1) }));
                                    if (matches) subscription.Expires[slot] = Environment.TickCount64 + 5000;
                                }
                        }
                    }
                }

                long now = Environment.TickCount64;
                var snapshot = ReadStates();
                counter++;
                foreach (var subscription in subscriptions.Values.ToArray())
                {
                    for (byte slot = 0; slot < 2; slot++)
                        if (subscription.Expires[slot] > now)
                            await Send(DsuProtocol.Pad(slot, snapshot[slot], counter, _id, Volatile.Read(ref _deadzone)),
                                subscription.Endpoint, cancellation);
                    if (subscription.Expires.All(expiry => expiry <= now)) subscriptions.Remove(subscription.Endpoint);
                }
                _clients = subscriptions.Count;
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (cancellation.IsCancellationRequested) { }
        catch (SocketException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error) { _error = error.Message; }
    }

    private async Task Send(byte[] packet, IPEndPoint endpoint, CancellationToken cancellation)
    {
        try { await _socket.SendAsync(packet, endpoint, cancellation); }
        catch (SocketException error) when (error.SocketErrorCode is SocketError.ConnectionReset or SocketError.ConnectionRefused) { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stop.Cancel();
        _socket.Dispose();
        _ = _worker.ContinueWith(_ => _stop.Dispose(), TaskScheduler.Default);
    }
}
