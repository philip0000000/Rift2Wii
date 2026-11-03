using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using OpenXRInputExplorer.Models;
using OpenXRInputExplorer.Services;

ConfigurationChecks.Run();
MotionChecks.Run();
PointerChecks.Run();
DolphinSetupChecks.Run();
if (args.Contains("--configuration-only")) return;

static void Check(bool value, string name) { if (!value) throw new Exception(name); }

// Every mapped control must press and release, including directions and analog thresholds.
foreach (ControllerControl control in Enum.GetValues<ControllerControl>())
{
    var sink = new Sink();
    using var output = new DesktopInputService(sink);
    output.Configure(true, false, false, new Dictionary<ControllerControl, ushort> { [control] = 66 }, true, 0.65f);
    var left = new ControllerState(); var right = new ControllerState();
    void Tick(bool leftAvailable = true, bool rightAvailable = true) =>
        output.Update(true, true, left, right, leftAvailable, rightAvailable, null, false);
    Tick();
    ControllerState hand = ControllerControls.IsLeft(control) ? left : right;
    switch (control)
    {
        case ControllerControl.LeftX: hand.X = true; break;
        case ControllerControl.LeftY: hand.Y = true; break;
        case ControllerControl.RightA: hand.A = true; break;
        case ControllerControl.RightB: hand.B = true; break;
        case ControllerControl.LeftStickClick or ControllerControl.RightStickClick: hand.ThumbstickClick = true; break;
        case ControllerControl.LeftTrigger or ControllerControl.RightTrigger: hand.Trigger = 0.7f; break;
        case ControllerControl.LeftGrip or ControllerControl.RightGrip: hand.Grip = 0.7f; break;
        case ControllerControl.LeftStickUp or ControllerControl.RightStickUp: hand.ThumbstickY = 0.8f; break;
        case ControllerControl.LeftStickDown or ControllerControl.RightStickDown: hand.ThumbstickY = -0.8f; break;
        case ControllerControl.LeftStickLeft or ControllerControl.RightStickLeft: hand.ThumbstickX = -0.8f; break;
        case ControllerControl.LeftStickRight or ControllerControl.RightStickRight: hand.ThumbstickX = 0.8f; break;
    }
    Tick(); Tick();
    Check(sink.Events.SequenceEqual(new[] { "66:True" }), $"{control}: exactly one key-down while held");
    Tick(false, false);
    Check(sink.Events.Last() == "66:False", $"{control}: release on tracking loss");
    int count = sink.Events.Count;
    Tick(); Check(sink.Events.Count == count, $"{control}: no held input after reconnect");
}

{
    var sink = new Sink(); using var output = new DesktopInputService(sink);
    var bindings = new Dictionary<ControllerControl, ushort> { [ControllerControl.LeftX] = 66, [ControllerControl.RightA] = 66 };
    output.Configure(true, true, false, bindings, false, 0.65f);
    var left = new ControllerState(); var right = new ControllerState();
    void Tick(bool focus = true, bool leftAvailable = true, bool rightAvailable = true) =>
        output.Update(true, focus, left, right, leftAvailable, rightAvailable, new DesktopPoint(5, 5), true);
    Tick(); left.X = true; right.A = true; Tick(); Tick(leftAvailable: false);
    Check(sink.Events.SequenceEqual(new[] { "66:True" }), "A shared key remains down when only one hand disconnects");
    right.A = false; Tick(); Check(sink.Events.Last() == "66:False", "Shared-key final release");
    right.Trigger = 1; Tick(); Check(!sink.Events.Any(item => item.StartsWith("Mouse:")), "Trigger can be keyboard-only without clicking mouse");
    output.Configure(true, true, true, new Dictionary<ControllerControl, ushort> { [ControllerControl.RightTrigger] = 67 }, false, 0.65f);
    right.Trigger = 0; Tick(); right.Trigger = 0.7f; Tick(); right.Trigger = 0.6f; Tick();
    Check(sink.Events.TakeLast(2).SequenceEqual(new[] { "67:True", "Mouse:True" }), "Trigger press and hysteresis");
    Tick(focus: false);
    Check(sink.Events.TakeLast(2).SequenceEqual(new[] { "67:False", "Mouse:False" }), "Focus loss releases trigger key and mouse");
    int count = sink.Events.Count; Tick(); Check(sink.Events.Count == count, "Refocus requires trigger release");
    output.Configure(false, false, false, bindings, false, 0.65f);
    Tick(); Check(sink.Events.Count == count, "Disabled output remains silent");
}
Console.WriteLine("PASS: all 18 controls, hold/release, shared keys, independent tracking, trigger/grip hysteresis and optional mouse clicks.");

// A separate CRC implementation acts as the UDP client's checksum checker.
uint PeerCrc(byte[] bytes)
{
    var copy = (byte[])bytes.Clone(); Array.Clear(copy, 8, 4);
    var table = new uint[256];
    for (uint n = 0; n < 256; n++)
    { uint value = n; for (int bit = 0; bit < 8; bit++) value = (value & 1) == 0 ? value >> 1 : (value >> 1) ^ 0xEDB88320; table[n] = value; }
    uint crc = uint.MaxValue;
    foreach (byte value in copy) crc = table[(crc ^ value) & 255] ^ (crc >> 8);
    return ~crc;
}
byte[] Request(uint type, int length)
{
    var packet = DsuProtocol.Message(type, length, 123);
    packet[3] = (byte)'C';
    return packet;
}
async Task Send(UdpClient client, int port, byte[] packet)
{
    BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(8), PeerCrc(packet));
    await client.SendAsync(packet, new IPEndPoint(IPAddress.Loopback, port));
}
async Task<byte[]> Receive(UdpClient client, Func<byte[], bool> match)
{
    using var timeout = new CancellationTokenSource(3000);
    while (true)
    {
        byte[] packet = (await client.ReceiveAsync(timeout.Token)).Buffer;
        Check(packet.AsSpan(0, 4).SequenceEqual("DSUS"u8), "Response magic");
        Check(BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(6)) + 16 == packet.Length, "Response length");
        Check(BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(8)) == PeerCrc(packet), "Response CRC");
        if (match(packet)) return packet;
    }
}

var diagonal = DsuProtocol.ApplyDeadzone(new Vector2(1, 1), 0.15f);
Check(Math.Abs(diagonal.Length() - 1) < 0.0001f && Math.Abs(diagonal.X - diagonal.Y) < 0.0001f, "Radial dead zone preserves diagonals and clamps magnitude");
Check(DsuProtocol.ApplyDeadzone(new Vector2(0.1f, 0), 0.15f) == Vector2.Zero, "Noise removed by dead zone");
Check(DsuProtocol.ApplyDeadzone(new Vector2(float.NaN, 0), 0.15f) == Vector2.Zero, "Invalid axes neutralized");

int port;
using (var reserve = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0))) port = ((IPEndPoint)reserve.Client.LocalEndPoint!).Port;
using (var server = new DsuServer(port))
using (var first = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
using (var second = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
{
    await Send(first, port, Request(DsuProtocol.VersionMessage, 20));
    var version = await Receive(first, packet => packet.Length == 24);
    Check(BinaryPrimitives.ReadUInt16LittleEndian(version.AsSpan(20)) == 1001, "Protocol negotiation");
    var bad = Request(DsuProtocol.VersionMessage, 20); bad[8] = 12;
    await first.SendAsync(bad, new IPEndPoint(IPAddress.Loopback, port));
    using (var shortWait = new CancellationTokenSource(80))
    {
        try { await first.ReceiveAsync(shortWait.Token); throw new Exception("Malformed CRC was accepted"); }
        catch (OperationCanceledException) { }
    }
    var ports = Request(DsuProtocol.PortsMessage, 28);
    BinaryPrimitives.WriteUInt32LittleEndian(ports.AsSpan(20), 4);
    ports[24] = 0; ports[25] = 1; ports[26] = 2; ports[27] = 3;
    await Send(first, port, ports);
    var portInfo = new Dictionary<byte, byte[]>();
    for (int i = 0; i < 4; i++) { var info = await Receive(first, p => p.Length == 32); portInfo[info[20]] = info; }
    Check(portInfo[0][21] == 2 && portInfo[1][21] == 2 && portInfo[2][21] == 0 && portInfo[3][21] == 0, "Two stable virtual pads");
    Check(portInfo[0][29] != portInfo[1][29], "Distinct stable controller identities");

    var left = new DsuPadState(true, new Vector2(0.25f, 0.5f), 0.7f, 0.2f, true, false, true);
    var right = new DsuPadState(true, new Vector2(-1, -1), 1, 0, false, true, false);
    server.Publish(left, right, 0.15f);
    var register = Request(DsuProtocol.DataMessage, 28); register[20] = 1; register[21] = 0;
    await Send(first, port, register);
    var data = await Receive(first, p => p.Length == 100);
    Check(data[20] == 0 && data[31] == 1 && data[40] > 128 && data[40] < 255 && data[41] < 128, "Fractional left stick and correct up-axis sign");
    Check(data[49] == 255 && data[50] == 0 && (data[36] & 2) != 0 && data[55] is >= 178 and <= 179 && data[53] == 51, "Buttons, analog trigger/grip and stick click");
    Check(BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(68)) > 0 &&
        BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(80)) == -1, "Stationary sensor includes gravity and timestamp");
    register[21] = 1; await Send(second, port, register);
    var other = await Receive(second, p => p.Length == 100);
    Check(other[20] == 1 && other[50] == 255 && other[49] == 0 && other[55] == 255, "Independent second subscriber and right hand");

    // Change the first client's registration to MAC-only for the other hand.
    register[20] = 2; portInfo[1].AsSpan(24, 6).CopyTo(register.AsSpan(22));
    await Send(first, port, register);
    Check((await Receive(first, p => p.Length == 100 && p[20] == 1))[50] == 255, "MAC-based subscription");
    await Task.Delay(300);
    var neutral = await Receive(second, p => p.Length == 100 && p[40] == 128 && p[41] == 128 && p[55] == 0);
    Check(neutral[21] == 2 && neutral[49] == 0 && neutral[50] == 0, "Stale input neutralized without removing virtual pads");
}
using (var rebound = new UdpClient(new IPEndPoint(IPAddress.Loopback, port))) { }
Console.WriteLine("PASS: DSU version/discovery, independent ID/MAC subscriptions, checksums, analog axes, buttons, watchdog and socket cleanup.");

sealed class Sink : IDesktopInputSink
{
    public List<string> Events { get; } = [];
    public void Key(ushort key, bool down) => Events.Add($"{key}:{down}");
    public void MoveMouse(DesktopPoint point) { }
    public void LeftMouseButton(bool down) => Events.Add($"Mouse:{down}");
}
