using System.Buffers.Binary;
using System.Numerics;

namespace OpenXRInputExplorer.Services;

public readonly record struct DsuPadState(bool Connected, Vector2 Stick, float Trigger, float Grip,
    bool Primary, bool Secondary, bool StickClick)
{
    public ControllerMotion Motion { get; init; } = ControllerMotion.Rest;
    public static DsuPadState Neutral => new(true, Vector2.Zero, 0, 0, false, false, false);
}

/// <summary>Cemuhook/DSU v1001 wire format, shared with Dolphin's DSUClient backend.</summary>
public static class DsuProtocol
{
    public const ushort Version = 1001;
    public const uint VersionMessage = 0x100000, PortsMessage = 0x100001, DataMessage = 0x100002;

    public static uint Crc(ReadOnlySpan<byte> packet)
    {
        uint crc = uint.MaxValue;
        for (int index = 0; index < packet.Length; index++)
        {
            crc ^= index is >= 8 and < 12 ? 0u : packet[index];
            for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xEDB88320u : 0u);
        }
        return ~crc;
    }

    public static bool IsRequest(ReadOnlySpan<byte> packet) => packet.Length is >= 20 and <= 1024 &&
        packet[..4].SequenceEqual("DSUC"u8) && BinaryPrimitives.ReadUInt16LittleEndian(packet[4..]) <= Version &&
        BinaryPrimitives.ReadUInt16LittleEndian(packet[6..]) + 16 == packet.Length &&
        BinaryPrimitives.ReadUInt32LittleEndian(packet[8..]) == Crc(packet);

    public static byte[] Message(uint type, int length, uint serverId)
    {
        var packet = new byte[length];
        "DSUS"u8.CopyTo(packet);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4), Version);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(6), (ushort)(length - 16));
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(12), serverId);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(16), type);
        return packet;
    }

    public static byte[] Finish(byte[] packet)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(8), Crc(packet));
        return packet;
    }

    public static void WritePort(Span<byte> packet, byte slot, bool connected)
    {
        packet[20] = slot;
        packet[21] = connected ? (byte)2 : (byte)0;
        packet[22] = 2; // Full gyro virtual controller.
        packet[23] = 1; // USB-like local virtual source.
        new byte[] { 0x02, 0x52, 0x49, 0x46, 0x54, (byte)(slot + 1) }.CopyTo(packet[24..30]);
        packet[30] = 5;
    }

    public static Vector2 ApplyDeadzone(Vector2 value, float deadzone)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y)) return Vector2.Zero;
        deadzone = Math.Clamp(deadzone, 0, 0.9f);
        float magnitude = value.Length();
        if (magnitude <= deadzone || magnitude < 0.00001f) return Vector2.Zero;
        return value / magnitude * ((Math.Min(magnitude, 1) - deadzone) / (1 - deadzone));
    }

    private static byte Axis(float value)
    {
        value = Math.Clamp(value, -1, 1);
        return (byte)MathF.Round(128 + value * (value < 0 ? 128 : 127));
    }

    private static byte Analog(float value) => float.IsFinite(value)
        ? (byte)MathF.Round(Math.Clamp(value, 0, 1) * 255) : (byte)0;

    public static byte[] Pad(byte slot, DsuPadState state, uint counter, uint serverId, float deadzone)
    {
        if (!state.Connected) state = default;
        byte[] packet = Message(DataMessage, 100, serverId);
        WritePort(packet, slot, state.Connected);
        packet[31] = state.Connected ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(32), counter);
        if (state.StickClick) packet[36] |= 2; // L3
        if (state.Primary) { packet[37] |= 0x40; packet[49] = 255; } // Cross
        if (state.Secondary) { packet[37] |= 0x20; packet[50] = 255; } // Circle
        byte trigger = Analog(state.Trigger), grip = Analog(state.Grip);
        if (grip > 127) packet[37] |= 4; // L1
        if (trigger > 127) packet[37] |= 1; // L2
        packet[53] = grip;
        packet[55] = trigger;
        Vector2 stick = ApplyDeadzone(state.Stick, deadzone);
        packet[40] = Axis(stick.X);
        packet[41] = Axis(-stick.Y); // Dolphin's "Left Y-" is up.
        packet[42] = packet[43] = 128;
        var motion = state.Motion;
        // Keep one monotonic wire clock, including neutral/watchdog samples and XR restarts.
        ulong timestamp = (ulong)(System.Diagnostics.Stopwatch.GetTimestamp() * (1_000_000d / System.Diagnostics.Stopwatch.Frequency));
        BinaryPrimitives.WriteUInt64LittleEndian(packet.AsSpan(68), timestamp);
        float[] sensors = [motion.Acceleration.X, motion.Acceleration.Y, motion.Acceleration.Z,
            motion.Gyroscope.X, motion.Gyroscope.Y, motion.Gyroscope.Z];
        for (int i = 0; i < sensors.Length; i++)
            BinaryPrimitives.WriteSingleLittleEndian(packet.AsSpan(76 + i * 4), sensors[i]);
        return Finish(packet);
    }
}
