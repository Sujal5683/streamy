using System;
using System.IO;

namespace EnterpriseRemote.Common.Protocol;

public enum InputEventType : byte
{
    MouseMove = 0x01,
    MouseButtonDown = 0x02,
    MouseButtonUp = 0x03,
    MouseWheel = 0x04,
    KeyDown = 0x10,
    KeyUp = 0x11,
    StreamConfig = 0x35,
    LatencyPing = 0x40,
    LatencyPong = 0x41
}

public enum MouseButton : byte
{
    None = 0,
    Left = 1,
    Right = 2,
    Middle = 4,
    X1 = 8,
    X2 = 16
}

[Flags]
public enum ModifierKeys : byte
{
    None = 0,
    Control = 1,
    Alt = 2,
    Shift = 4,
    Windows = 8
}

public readonly record struct MouseInputPacket(
    InputEventType Type,
    MouseButton Button,
    byte DisplayIndex,
    float NormalizedX,
    float NormalizedY,
    short WheelDelta,
    ulong TimestampMillis);

public readonly record struct KeyboardInputPacket(
    InputEventType Type,
    ushort VirtualKeyCode,
    ushort ScanCode,
    ModifierKeys Modifiers,
    uint UnicodeChar,
    ulong TimestampMillis);

public readonly record struct StreamConfigPacket(
    byte TargetFps,
    byte Quality,
    ushort MaxHeight,
    byte LowLatencyMode,
    ulong TimestampMillis);

public enum ControlAction : byte
{
    Ping = 0x01,
    Pong = 0x02,
    PauseSession = 0x10,
    ResumeSession = 0x11,
    TerminateSession = 0x12,
    SwitchDisplay = 0x20,
    SetQualityProfile = 0x30,
    SetStreamConfig = 0x35
}

public readonly record struct ControlPacket(
    ControlAction Action,
    uint Parameter,
    string? Payload);

public readonly record struct TelemetryMetricsPacket(
    float RoundTripTimeMs,
    float PacketLossPercentage,
    float FramesPerSecond,
    uint CurrentBitrateKbps,
    ulong TimestampMillis);

public static class DataChannelProtocol
{
    // Mouse packet: 1 + 1 + 1 + 4 + 4 + 2 + 8 = 21 bytes
    public static byte[] SerializeMouse(in MouseInputPacket packet)
    {
        byte[] buffer = new byte[21];
        buffer[0] = (byte)packet.Type;
        buffer[1] = (byte)packet.Button;
        buffer[2] = packet.DisplayIndex;
        BitConverter.TryWriteBytes(buffer.AsSpan(3, 4), packet.NormalizedX);
        BitConverter.TryWriteBytes(buffer.AsSpan(7, 4), packet.NormalizedY);
        BitConverter.TryWriteBytes(buffer.AsSpan(11, 2), packet.WheelDelta);
        BitConverter.TryWriteBytes(buffer.AsSpan(13, 8), packet.TimestampMillis);
        return buffer;
    }

    public static bool TryDeserializeMouse(ReadOnlySpan<byte> data, out MouseInputPacket packet)
    {
        if (data.Length < 21 || (data[0] != 0x01 && data[0] != 0x02 && data[0] != 0x03 && data[0] != 0x04))
        {
            packet = default;
            return false;
        }

        var type = (InputEventType)data[0];
        var button = (MouseButton)data[1];
        byte displayIndex = data[2];
        float normX = BitConverter.ToSingle(data.Slice(3, 4));
        float normY = BitConverter.ToSingle(data.Slice(7, 4));
        short wheel = BitConverter.ToInt16(data.Slice(11, 2));
        ulong ts = BitConverter.ToUInt64(data.Slice(13, 8));

        packet = new MouseInputPacket(type, button, displayIndex, normX, normY, wheel, ts);
        return true;
    }

    // Keyboard packet: 1 + 2 + 2 + 1 + 4 + 8 = 18 bytes
    public static byte[] SerializeKeyboard(in KeyboardInputPacket packet)
    {
        byte[] buffer = new byte[18];
        buffer[0] = (byte)packet.Type;
        BitConverter.TryWriteBytes(buffer.AsSpan(1, 2), packet.VirtualKeyCode);
        BitConverter.TryWriteBytes(buffer.AsSpan(3, 2), packet.ScanCode);
        buffer[5] = (byte)packet.Modifiers;
        BitConverter.TryWriteBytes(buffer.AsSpan(6, 4), packet.UnicodeChar);
        BitConverter.TryWriteBytes(buffer.AsSpan(10, 8), packet.TimestampMillis);
        return buffer;
    }

    public static bool TryDeserializeKeyboard(ReadOnlySpan<byte> data, out KeyboardInputPacket packet)
    {
        if (data.Length < 18 || (data[0] != 0x10 && data[0] != 0x11))
        {
            packet = default;
            return false;
        }

        var type = (InputEventType)data[0];
        ushort vk = BitConverter.ToUInt16(data.Slice(1, 2));
        ushort scan = BitConverter.ToUInt16(data.Slice(3, 2));
        var mods = (ModifierKeys)data[5];
        uint unicode = BitConverter.ToUInt32(data.Slice(6, 4));
        ulong ts = BitConverter.ToUInt64(data.Slice(10, 8));

        packet = new KeyboardInputPacket(type, vk, scan, mods, unicode, ts);
        return true;
    }

    // Metrics packet: 4 + 4 + 4 + 4 + 8 = 24 bytes
    public static byte[] SerializeMetrics(in TelemetryMetricsPacket metrics)
    {
        byte[] buffer = new byte[24];
        BitConverter.TryWriteBytes(buffer.AsSpan(0, 4), metrics.RoundTripTimeMs);
        BitConverter.TryWriteBytes(buffer.AsSpan(4, 4), metrics.PacketLossPercentage);
        BitConverter.TryWriteBytes(buffer.AsSpan(8, 4), metrics.FramesPerSecond);
        BitConverter.TryWriteBytes(buffer.AsSpan(12, 4), metrics.CurrentBitrateKbps);
        BitConverter.TryWriteBytes(buffer.AsSpan(16, 8), metrics.TimestampMillis);
        return buffer;
    }

    public static bool TryDeserializeMetrics(ReadOnlySpan<byte> data, out TelemetryMetricsPacket metrics)
    {
        if (data.Length < 24)
        {
            metrics = default;
            return false;
        }

        float rtt = BitConverter.ToSingle(data.Slice(0, 4));
        float loss = BitConverter.ToSingle(data.Slice(4, 4));
        float fps = BitConverter.ToSingle(data.Slice(8, 4));
        uint bitrate = BitConverter.ToUInt32(data.Slice(12, 4));
        ulong ts = BitConverter.ToUInt64(data.Slice(16, 8));

        metrics = new TelemetryMetricsPacket(rtt, loss, fps, bitrate, ts);
        return true;
    }

    // StreamConfig packet: 1 + 1 + 1 + 2 + 1 + 8 = 14 bytes
    public static byte[] SerializeStreamConfig(in StreamConfigPacket packet)
    {
        byte[] buffer = new byte[14];
        buffer[0] = (byte)InputEventType.StreamConfig;
        buffer[1] = packet.TargetFps;
        buffer[2] = packet.Quality;
        BitConverter.TryWriteBytes(buffer.AsSpan(3, 2), packet.MaxHeight);
        buffer[5] = packet.LowLatencyMode;
        BitConverter.TryWriteBytes(buffer.AsSpan(6, 8), packet.TimestampMillis);
        return buffer;
    }

    public static bool TryDeserializeStreamConfig(ReadOnlySpan<byte> data, out StreamConfigPacket packet)
    {
        if (data.Length < 14 || data[0] != (byte)InputEventType.StreamConfig)
        {
            packet = default;
            return false;
        }

        byte targetFps = data[1];
        byte quality = data[2];
        ushort maxHeight = BitConverter.ToUInt16(data.Slice(3, 2));
        byte lowLatencyMode = data[5];
        ulong ts = BitConverter.ToUInt64(data.Slice(6, 8));

        packet = new StreamConfigPacket(targetFps, quality, maxHeight, lowLatencyMode, ts);
        return true;
    }

    // Latency Ping / Pong packets: 1 + 8 = 9 bytes
    public static byte[] SerializeLatencyPing(ulong timestampMillis)
    {
        byte[] buffer = new byte[9];
        buffer[0] = (byte)InputEventType.LatencyPing;
        BitConverter.TryWriteBytes(buffer.AsSpan(1, 8), timestampMillis);
        return buffer;
    }

    public static byte[] SerializeLatencyPong(ulong timestampMillis)
    {
        byte[] buffer = new byte[9];
        buffer[0] = (byte)InputEventType.LatencyPong;
        BitConverter.TryWriteBytes(buffer.AsSpan(1, 8), timestampMillis);
        return buffer;
    }

    public static bool TryDeserializeLatencyPingPong(ReadOnlySpan<byte> data, out bool isPing, out ulong timestampMillis)
    {
        if (data.Length < 9 || (data[0] != (byte)InputEventType.LatencyPing && data[0] != (byte)InputEventType.LatencyPong))
        {
            isPing = false;
            timestampMillis = 0;
            return false;
        }

        isPing = data[0] == (byte)InputEventType.LatencyPing;
        timestampMillis = BitConverter.ToUInt64(data.Slice(1, 8));
        return true;
    }
}

