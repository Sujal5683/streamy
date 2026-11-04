using System;
using System.IO;
using System.Text;

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
    LatencyPong = 0x41,
    /// <summary>Viewer-to-host frame acknowledgment for end-to-end zero-bufferbloat flow control.</summary>
    FrameAck = 0x42,
    /// <summary>Requests the host to switch to a named Win32 desktop.</summary>
    SwitchDesktop = 0x50,
    /// <summary>Host-to-controller notification of available desktops and displays.</summary>
    DesktopListUpdate = 0x51,
    /// <summary>Host-to-controller notification of the current remote cursor shape.</summary>
    CursorUpdate = 0x52,
    /// <summary>Host-to-controller notification of real-time remote cursor position and visibility.</summary>
    CursorPosition = 0x53
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

/// <summary>Standard cursor shapes that can be remoted to the controller side.</summary>
public enum RemoteCursorShape : byte
{
    Default = 0,
    Hand = 1,
    IBeam = 2,
    SizeAll = 3,
    SizeWE = 4,
    SizeNS = 5,
    Wait = 6,
    Cross = 7,
    No = 8,
    SizeNWSE = 9,
    SizeNESW = 10,
    UpArrow = 11,
    AppStarting = 12,
    Help = 13
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

/// <summary>
/// Request from controller to host to switch to a named Win32 desktop and/or physical display.
/// Wire: [0x50][DisplayIndex:1][AutoFollow:1][NameLen:2LE][Name:UTF8][Timestamp:8LE]
/// </summary>
public readonly record struct SwitchDesktopPacket(
    byte DisplayIndex,
    bool AutoFollow,
    string DesktopName,
    ulong TimestampMillis);

/// <summary>
/// Describes one physical display/monitor available on the host.
/// </summary>
public readonly record struct DisplaySummary(
    byte Index,
    string Name,
    int Width,
    int Height,
    bool IsPrimary);

/// <summary>
/// Host-to-controller notification listing available Win32 desktops and physical displays.
/// Wire: [0x51][ActiveNameLen:2LE][ActiveName:UTF8][DesktopCount:1][...names...][DisplayCount:1][...displays...][Timestamp:8LE]
/// </summary>
public readonly record struct DesktopListPacket(
    string ActiveDesktop,
    string[] Desktops,
    DisplaySummary[] Displays,
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

    // ─── SwitchDesktop packet ────────────────────────────────────────────────────
    // Wire layout: [0x50:1][DisplayIndex:1][AutoFollow:1][NameLen:2LE][Name:UTF8][Timestamp:8LE]
    // Minimum length without name: 1+1+1+2+8 = 13 bytes

    /// <summary>Serializes a <see cref="SwitchDesktopPacket"/> into the wire format.</summary>
    public static byte[] SerializeSwitchDesktop(in SwitchDesktopPacket packet)
    {
        byte[] nameBytes = Encoding.UTF8.GetBytes(packet.DesktopName ?? string.Empty);
        int totalLen = 1 + 1 + 1 + 2 + nameBytes.Length + 8;
        byte[] buffer = new byte[totalLen];
        int pos = 0;
        buffer[pos++] = (byte)InputEventType.SwitchDesktop;
        buffer[pos++] = packet.DisplayIndex;
        buffer[pos++] = packet.AutoFollow ? (byte)1 : (byte)0;
        BitConverter.TryWriteBytes(buffer.AsSpan(pos, 2), (ushort)nameBytes.Length);
        pos += 2;
        nameBytes.CopyTo(buffer, pos);
        pos += nameBytes.Length;
        BitConverter.TryWriteBytes(buffer.AsSpan(pos, 8), packet.TimestampMillis);
        return buffer;
    }

    /// <summary>Tries to deserialize a <see cref="SwitchDesktopPacket"/> from raw bytes.</summary>
    public static bool TryDeserializeSwitchDesktop(ReadOnlySpan<byte> data, out SwitchDesktopPacket packet)
    {
        // Minimum: type(1) + displayIndex(1) + autoFollow(1) + nameLen(2) + timestamp(8) = 13
        if (data.Length < 13 || data[0] != (byte)InputEventType.SwitchDesktop)
        {
            packet = default;
            return false;
        }

        byte displayIndex = data[1];
        bool autoFollow = data[2] != 0;
        ushort nameLen = BitConverter.ToUInt16(data.Slice(3, 2));
        int nameStart = 5;

        if (data.Length < nameStart + nameLen + 8)
        {
            packet = default;
            return false;
        }

        string desktopName = nameLen > 0
            ? Encoding.UTF8.GetString(data.Slice(nameStart, nameLen))
            : string.Empty;
        ulong ts = BitConverter.ToUInt64(data.Slice(nameStart + nameLen, 8));

        packet = new SwitchDesktopPacket(displayIndex, autoFollow, desktopName, ts);
        return true;
    }

    // ─── DesktopList packet ──────────────────────────────────────────────────────
    // Wire layout:
    //   [0x51:1]
    //   [ActiveNameLen:2LE][ActiveName:UTF8]
    //   [DesktopCount:1]  → for each: [NameLen:2LE][Name:UTF8]
    //   [DisplayCount:1]  → for each: [Index:1][NameLen:2LE][Name:UTF8][Width:4LE][Height:4LE][IsPrimary:1]
    //   [Timestamp:8LE]

    /// <summary>Serializes a <see cref="DesktopListPacket"/> into the wire format.</summary>
    public static byte[] SerializeDesktopList(in DesktopListPacket packet)
    {
        using var ms = new MemoryStream(256);
        using var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);

        bw.Write((byte)InputEventType.DesktopListUpdate);

        byte[] activeBytes = Encoding.UTF8.GetBytes(packet.ActiveDesktop ?? string.Empty);
        bw.Write((ushort)activeBytes.Length);
        bw.Write(activeBytes);

        bw.Write((byte)(packet.Desktops?.Length ?? 0));
        if (packet.Desktops != null)
        {
            foreach (var name in packet.Desktops)
            {
                byte[] nb = Encoding.UTF8.GetBytes(name ?? string.Empty);
                bw.Write((ushort)nb.Length);
                bw.Write(nb);
            }
        }

        bw.Write((byte)(packet.Displays?.Length ?? 0));
        if (packet.Displays != null)
        {
            foreach (var d in packet.Displays)
            {
                bw.Write(d.Index);
                byte[] nb = Encoding.UTF8.GetBytes(d.Name ?? string.Empty);
                bw.Write((ushort)nb.Length);
                bw.Write(nb);
                bw.Write(d.Width);
                bw.Write(d.Height);
                bw.Write(d.IsPrimary ? (byte)1 : (byte)0);
            }
        }

        bw.Write(packet.TimestampMillis);
        bw.Flush();
        return ms.ToArray();
    }

    /// <summary>Tries to deserialize a <see cref="DesktopListPacket"/> from raw bytes.</summary>
    public static bool TryDeserializeDesktopList(ReadOnlySpan<byte> data, out DesktopListPacket packet)
    {
        packet = default;
        if (data.Length < 1 || data[0] != (byte)InputEventType.DesktopListUpdate)
            return false;

        try
        {
            // Use a MemoryStream/BinaryReader on a copy to handle variable-length data safely
            byte[] copy = data.ToArray();
            using var ms = new MemoryStream(copy);
            using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: false);

            br.ReadByte(); // type byte already validated

            ushort activeLen = br.ReadUInt16();
            string activeDesktop = activeLen > 0 ? Encoding.UTF8.GetString(br.ReadBytes(activeLen)) : string.Empty;

            byte desktopCount = br.ReadByte();
            string[] desktops = new string[desktopCount];
            for (int i = 0; i < desktopCount; i++)
            {
                ushort nl = br.ReadUInt16();
                desktops[i] = nl > 0 ? Encoding.UTF8.GetString(br.ReadBytes(nl)) : string.Empty;
            }

            byte displayCount = br.ReadByte();
            var displays = new DisplaySummary[displayCount];
            for (int i = 0; i < displayCount; i++)
            {
                byte idx = br.ReadByte();
                ushort nl = br.ReadUInt16();
                string name = nl > 0 ? Encoding.UTF8.GetString(br.ReadBytes(nl)) : string.Empty;
                int width = br.ReadInt32();
                int height = br.ReadInt32();
                bool isPrimary = br.ReadByte() != 0;
                displays[i] = new DisplaySummary(idx, name, width, height, isPrimary);
            }

            ulong ts = br.ReadUInt64();
            packet = new DesktopListPacket(activeDesktop, desktops, displays, ts);
            return true;
        }
        catch
        {
            packet = default;
            return false;
        }
    }

    // ─── CursorUpdate packet ─────────────────────────────────────────────────────
    // Wire layout: [0x52:1][CursorShape:1]  →  exactly 2 bytes

    /// <summary>Serializes a cursor shape update into a 2-byte wire packet.</summary>
    public static byte[] SerializeCursorUpdate(RemoteCursorShape shape)
    {
        return new byte[] { (byte)InputEventType.CursorUpdate, (byte)shape };
    }

    /// <summary>Tries to deserialize a cursor shape update from raw bytes.</summary>
    public static bool TryDeserializeCursorUpdate(ReadOnlySpan<byte> data, out RemoteCursorShape shape)
    {
        if (data.Length < 2 || data[0] != (byte)InputEventType.CursorUpdate)
        {
            shape = default;
            return false;
        }

        shape = (RemoteCursorShape)data[1];
        return true;
    }

    // ─── CursorPosition packet ───────────────────────────────────────────────────
    // Wire layout: [0x53:1][X:2 LE][Y:2 LE][DisplayWidth:2 LE][DisplayHeight:2 LE][CursorShape:1][Visible:1] → exactly 11 bytes

    /// <summary>Serializes real-time remote cursor position and visibility into an 11-byte wire packet.</summary>
    public static byte[] SerializeCursorPosition(in CursorPositionPacket packet)
    {
        byte[] buffer = new byte[11];
        buffer[0] = (byte)InputEventType.CursorPosition;
        BitConverter.TryWriteBytes(buffer.AsSpan(1, 2), packet.X);
        BitConverter.TryWriteBytes(buffer.AsSpan(3, 2), packet.Y);
        BitConverter.TryWriteBytes(buffer.AsSpan(5, 2), packet.DisplayWidth);
        BitConverter.TryWriteBytes(buffer.AsSpan(7, 2), packet.DisplayHeight);
        buffer[9] = (byte)packet.Shape;
        buffer[10] = (byte)(packet.Visible ? 1 : 0);
        return buffer;
    }

    /// <summary>Tries to deserialize real-time cursor position from raw bytes.</summary>
    public static bool TryDeserializeCursorPosition(ReadOnlySpan<byte> data, out CursorPositionPacket packet)
    {
        if (data.Length < 11 || data[0] != (byte)InputEventType.CursorPosition)
        {
            packet = default;
            return false;
        }

        ushort x = BitConverter.ToUInt16(data.Slice(1, 2));
        ushort y = BitConverter.ToUInt16(data.Slice(3, 2));
        ushort w = BitConverter.ToUInt16(data.Slice(5, 2));
        ushort h = BitConverter.ToUInt16(data.Slice(7, 2));
        RemoteCursorShape shape = (RemoteCursorShape)data[9];
        bool visible = data[10] != 0;

        packet = new CursorPositionPacket(x, y, w, h, shape, visible);
        return true;
    }

    /// <summary>Serializes a 5-byte FrameAck packet for zero-bufferbloat flow control.</summary>
    public static byte[] SerializeFrameAck(uint sequence = 0)
    {
        byte[] buffer = new byte[5];
        buffer[0] = (byte)InputEventType.FrameAck;
        BitConverter.TryWriteBytes(buffer.AsSpan(1, 4), sequence);
        return buffer;
    }

    /// <summary>Tries to deserialize a FrameAck packet from raw bytes.</summary>
    public static bool TryDeserializeFrameAck(ReadOnlySpan<byte> data, out uint sequence)
    {
        if (data.Length >= 5 && data[0] == (byte)InputEventType.FrameAck)
        {
            sequence = BitConverter.ToUInt32(data.Slice(1, 4));
            return true;
        }
        else if (data.Length >= 2 && data[0] == (byte)InputEventType.FrameAck)
        {
            sequence = 0;
            return true;
        }
        sequence = 0;
        return false;
    }
}

public readonly record struct CursorPositionPacket(
    ushort X,
    ushort Y,
    ushort DisplayWidth,
    ushort DisplayHeight,
    RemoteCursorShape Shape,
    bool Visible);

