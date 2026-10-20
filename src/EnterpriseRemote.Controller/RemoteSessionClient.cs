using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using EnterpriseRemote.Common.Models;
using EnterpriseRemote.Common.Protocol;

namespace EnterpriseRemote.Controller;

public sealed class RemoteSessionClient : IDisposable
{
    private readonly string _operatorId;
    private readonly string _deviceId;
    private bool _isPaused;
    private bool _isActive;
    private float _lastRttMs;
    private float _lastLoss;
    private float _lastFps;
    private uint _lastBitrate;

    public string? SessionId { get; private set; }
    public SessionCapabilities GrantedCapabilities { get; private set; }
    public bool RequiresAttendedConsent { get; private set; }
    public bool IsActive => _isActive;
    public bool IsPaused => _isPaused;

    // Telemetry properties
    public float RoundTripTimeMs => _lastRttMs;
    public float PacketLossPercentage => _lastLoss;
    public float FramesPerSecond => _lastFps;
    public uint BitrateKbps => _lastBitrate;

    // Simulated / real transport callbacks for testing
    public Action<byte[]>? OnInputPacketReady { get; set; }
    public Action<ControlPacket>? OnControlPacketReady { get; set; }

    public RemoteSessionClient(string operatorId, string deviceId)
    {
        _operatorId = operatorId;
        _deviceId = deviceId;
    }

    public void InitializeSession(string sessionId, SessionCapabilities capabilities, bool requiresAttended)
    {
        SessionId = sessionId;
        GrantedCapabilities = capabilities;
        RequiresAttendedConsent = requiresAttended;
        _isActive = true;
        _isPaused = false;
    }

    public void SendMouseMove(float normalizedX, float normalizedY, byte displayIndex = 0)
    {
        if (!_isActive || _isPaused) return;
        if (!GrantedCapabilities.HasFlag(SessionCapabilities.Input)) return;

        var packet = new MouseInputPacket(
            Type: InputEventType.MouseMove,
            Button: MouseButton.None,
            DisplayIndex: displayIndex,
            NormalizedX: Math.Clamp(normalizedX, 0.0f, 1.0f),
            NormalizedY: Math.Clamp(normalizedY, 0.0f, 1.0f),
            WheelDelta: 0,
            TimestampMillis: (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        byte[] raw = DataChannelProtocol.SerializeMouse(packet);
        OnInputPacketReady?.Invoke(raw);
    }

    public void SendMouseButton(MouseButton button, bool isDown, float normalizedX, float normalizedY, byte displayIndex = 0)
    {
        if (!_isActive || _isPaused) return;
        if (!GrantedCapabilities.HasFlag(SessionCapabilities.Input)) return;

        var packet = new MouseInputPacket(
            Type: isDown ? InputEventType.MouseButtonDown : InputEventType.MouseButtonUp,
            Button: button,
            DisplayIndex: displayIndex,
            NormalizedX: Math.Clamp(normalizedX, 0.0f, 1.0f),
            NormalizedY: Math.Clamp(normalizedY, 0.0f, 1.0f),
            WheelDelta: 0,
            TimestampMillis: (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        byte[] raw = DataChannelProtocol.SerializeMouse(packet);
        OnInputPacketReady?.Invoke(raw);
    }

    public void SendKeyboardKey(ushort virtualKey, bool isDown, ModifierKeys modifiers = ModifierKeys.None, uint unicodeChar = 0)
    {
        if (!_isActive || _isPaused) return;
        if (!GrantedCapabilities.HasFlag(SessionCapabilities.Input)) return;

        var packet = new KeyboardInputPacket(
            Type: isDown ? InputEventType.KeyDown : InputEventType.KeyUp,
            VirtualKeyCode: virtualKey,
            ScanCode: 0,
            Modifiers: modifiers,
            UnicodeChar: unicodeChar,
            TimestampMillis: (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        byte[] raw = DataChannelProtocol.SerializeKeyboard(packet);
        OnInputPacketReady?.Invoke(raw);
    }

    public void PauseStreaming()
    {
        if (!_isActive) return;
        _isPaused = true;
        OnControlPacketReady?.Invoke(new ControlPacket(ControlAction.PauseSession, 0, null));
    }

    public void ResumeStreaming()
    {
        if (!_isActive) return;
        _isPaused = false;
        OnControlPacketReady?.Invoke(new ControlPacket(ControlAction.ResumeSession, 0, null));
    }

    public void TerminateSession()
    {
        if (!_isActive) return;
        _isActive = false;
        OnControlPacketReady?.Invoke(new ControlPacket(ControlAction.TerminateSession, 0, null));
    }

    public void ProcessIncomingMetrics(ReadOnlySpan<byte> metricsData)
    {
        if (DataChannelProtocol.TryDeserializeMetrics(metricsData, out var metrics))
        {
            _lastRttMs = metrics.RoundTripTimeMs;
            _lastLoss = metrics.PacketLossPercentage;
            _lastFps = metrics.FramesPerSecond;
            _lastBitrate = metrics.CurrentBitrateKbps;
        }
    }

    public void Dispose()
    {
        TerminateSession();
    }
}
