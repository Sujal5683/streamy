using System;
using System.Collections.Generic;
using EnterpriseRemote.Common.Models;
using EnterpriseRemote.Common.Protocol;
using EnterpriseRemote.Controller;
using EnterpriseRemote.Host.UserAgent.Input;
using EnterpriseRemote.Host.UserAgent.Video;
using Xunit;

namespace EnterpriseRemote.UnitTests;

public class MockInputSink : IInputSink
{
    public readonly List<(int X, int Y, MouseButton Button, InputEventType Type, short Wheel)> InjectedMouse = new();
    public readonly List<(ushort Vk, ushort Scan, bool IsDown, uint Unicode)> InjectedKeys = new();

    public void InjectMouse(int x, int y, MouseButton button, InputEventType type, short wheelDelta)
    {
        InjectedMouse.Add((x, y, button, type, wheelDelta));
    }

    public void InjectKeyboard(ushort virtualKey, ushort scanCode, bool isDown, uint unicodeChar)
    {
        InjectedKeys.Add((virtualKey, scanCode, isDown, unicodeChar));
    }
}

public class InputAndSessionTests
{
    [Fact]
    public void DataChannelProtocol_MousePacket_SerializesAndDeserializesAccurately()
    {
        var original = new MouseInputPacket(
            Type: InputEventType.MouseButtonDown,
            Button: MouseButton.Left | MouseButton.Right,
            DisplayIndex: 1,
            NormalizedX: 0.75f,
            NormalizedY: 0.25f,
            WheelDelta: -120,
            TimestampMillis: 1771482000123UL);

        byte[] raw = DataChannelProtocol.SerializeMouse(original);
        Assert.Equal(21, raw.Length);

        bool success = DataChannelProtocol.TryDeserializeMouse(raw, out var deserialized);
        Assert.True(success);
        Assert.Equal(original.Type, deserialized.Type);
        Assert.Equal(original.Button, deserialized.Button);
        Assert.Equal(original.DisplayIndex, deserialized.DisplayIndex);
        Assert.Equal(original.NormalizedX, deserialized.NormalizedX, 4);
        Assert.Equal(original.NormalizedY, deserialized.NormalizedY, 4);
        Assert.Equal(original.WheelDelta, deserialized.WheelDelta);
        Assert.Equal(original.TimestampMillis, deserialized.TimestampMillis);
    }

    [Fact]
    public void DataChannelProtocol_KeyboardPacket_SerializesAndDeserializesAccurately()
    {
        var original = new KeyboardInputPacket(
            Type: InputEventType.KeyDown,
            VirtualKeyCode: 0x41, // 'A'
            ScanCode: 0x1E,
            Modifiers: ModifierKeys.Control | ModifierKeys.Shift,
            UnicodeChar: 65,
            TimestampMillis: 1771482000456UL);

        byte[] raw = DataChannelProtocol.SerializeKeyboard(original);
        Assert.Equal(18, raw.Length);

        bool success = DataChannelProtocol.TryDeserializeKeyboard(raw, out var deserialized);
        Assert.True(success);
        Assert.Equal(original.Type, deserialized.Type);
        Assert.Equal(original.VirtualKeyCode, deserialized.VirtualKeyCode);
        Assert.Equal(original.ScanCode, deserialized.ScanCode);
        Assert.Equal(original.Modifiers, deserialized.Modifiers);
        Assert.Equal(original.UnicodeChar, deserialized.UnicodeChar);
        Assert.Equal(original.TimestampMillis, deserialized.TimestampMillis);
    }

    [Fact]
    public void DataChannelProtocol_RejectsTruncatedPacketsSafely()
    {
        byte[] truncated = new byte[10]; // Requires 21 for mouse, 18 for keyboard
        Assert.False(DataChannelProtocol.TryDeserializeMouse(truncated, out _));
        Assert.False(DataChannelProtocol.TryDeserializeKeyboard(truncated, out _));
        Assert.False(DataChannelProtocol.TryDeserializeMetrics(truncated, out _));
    }

    [Fact]
    public void InputInjectionEngine_TranslatesNormalizedCoordinates_ToBoundsCheckedPixels()
    {
        var display = new DisplayInfo
        {
            Left = 100,
            Top = 200,
            Width = 1920,
            Height = 1080
        };

        var mockSink = new MockInputSink();
        var engine = new InputInjectionEngine(display, mockSink);

        // Center screen: (0.5, 0.5)
        var centerPacket = new MouseInputPacket(
            Type: InputEventType.MouseMove,
            Button: MouseButton.None,
            DisplayIndex: 0,
            NormalizedX: 0.5f,
            NormalizedY: 0.5f,
            WheelDelta: 0,
            TimestampMillis: 1000);

        bool processed = engine.ProcessMousePacket(centerPacket);
        Assert.True(processed);
        Assert.Single(mockSink.InjectedMouse);

        var injected = mockSink.InjectedMouse[0];
        Assert.Equal(100 + (int)Math.Round(0.5f * 1919), injected.X);
        Assert.Equal(200 + (int)Math.Round(0.5f * 1079), injected.Y);
    }

    [Fact]
    public void InputInjectionEngine_RejectsOutOfBoundsMouseCoordinates()
    {
        var display = new DisplayInfo { Left = 0, Top = 0, Width = 1920, Height = 1080 };
        var mockSink = new MockInputSink();
        var engine = new InputInjectionEngine(display, mockSink);

        var oobNegative = new MouseInputPacket(InputEventType.MouseMove, MouseButton.None, 0, -0.1f, 0.5f, 0, 1000);
        var oobExcessive = new MouseInputPacket(InputEventType.MouseMove, MouseButton.None, 0, 0.5f, 1.2f, 0, 1000);

        Assert.False(engine.ProcessMousePacket(oobNegative));
        Assert.False(engine.ProcessMousePacket(oobExcessive));
        Assert.Empty(mockSink.InjectedMouse);
    }

    [Fact]
    public void InputInjectionEngine_BlocksRawWindowsKeyAndCtrlAltDel()
    {
        var display = new DisplayInfo { Left = 0, Top = 0, Width = 1920, Height = 1080 };
        var mockSink = new MockInputSink();
        var engine = new InputInjectionEngine(display, mockSink);

        // Windows key (0x5B)
        var winKey = new KeyboardInputPacket(InputEventType.KeyDown, 0x5B, 0, ModifierKeys.None, 0, 1000);
        Assert.False(engine.ProcessKeyboardPacket(winKey));

        // Ctrl+Alt+Del (VK_DELETE = 0x2E with Ctrl+Alt)
        var ctrlAltDel = new KeyboardInputPacket(InputEventType.KeyDown, 0x2E, 0, ModifierKeys.Control | ModifierKeys.Alt, 0, 1000);
        Assert.False(engine.ProcessKeyboardPacket(ctrlAltDel));

        // Ordinary key ('B' = 0x42) must be allowed
        var normalKey = new KeyboardInputPacket(InputEventType.KeyDown, 0x42, 0, ModifierKeys.None, 66, 1000);
        Assert.True(engine.ProcessKeyboardPacket(normalKey));
        Assert.Single(mockSink.InjectedKeys);
    }

    [Fact]
    public void AdaptiveQualityEngine_DynamicallyAdjustsProfileOnCongestion()
    {
        var engine = new AdaptiveQualityEngine();

        // 1. Initial state: UltraLowLatency (60 FPS)
        Assert.Equal(60, engine.CurrentParameters.TargetFps);
        Assert.Equal(QualityProfile.UltraLowLatency, engine.CurrentParameters.Profile);

        // 2. High loss & RTT spikes (sustained congestion)
        VideoEncodingParameters constrained = default;
        for (int i = 0; i < 8; i++)
        {
            constrained = engine.UpdateNetworkMetrics(sampleRttMs: 250.0f, sampleLossPercent: 12.0f);
        }
        Assert.Equal(15, constrained.TargetFps);
        Assert.Equal(QualityProfile.ConstrainedBandwidth, constrained.Profile);
        Assert.Equal(1200, constrained.TargetBitrateKbps);

        // 3. Recovery to favorable network
        for (int i = 0; i < 15; i++)
        {
            engine.UpdateNetworkMetrics(sampleRttMs: 15.0f, sampleLossPercent: 0.0f);
        }
        Assert.Equal(60, engine.CurrentParameters.TargetFps);
        Assert.Equal(QualityProfile.UltraLowLatency, engine.CurrentParameters.Profile);
    }

    [Fact]
    public void RemoteSessionClient_SuppressesInput_WhenCapabilityNotGrantedOrPaused()
    {
        var client = new RemoteSessionClient("OP-1", "DEV-1");
        int dispatchedCount = 0;
        client.OnInputPacketReady = _ => dispatchedCount++;

        // Initialize session with View ONLY (No Input capability)
        client.InitializeSession("SES-1", SessionCapabilities.View, requiresAttended: false);

        client.SendMouseMove(0.5f, 0.5f);
        client.SendKeyboardKey(0x41, true);
        Assert.Equal(0, dispatchedCount); // Suppressed

        // Now grant Input capability
        client.InitializeSession("SES-1", SessionCapabilities.View | SessionCapabilities.Input, requiresAttended: false);
        client.SendMouseMove(0.5f, 0.5f);
        Assert.Equal(1, dispatchedCount); // Allowed

        // Pause session
        client.PauseStreaming();
        client.SendMouseMove(0.6f, 0.6f);
        Assert.Equal(1, dispatchedCount); // Suppressed while paused
    }

    [Fact]
    public void WindowsSendInputSink_ExecutesSafely()
    {
        var sink = new EnterpriseRemote.Host.UserAgent.Input.WindowsSendInputSink();
        sink.InjectMouse(500, 500, MouseButton.None, InputEventType.MouseMove, 0);
        sink.InjectMouse(500, 500, MouseButton.Left, InputEventType.MouseButtonDown, 0);
        sink.InjectMouse(500, 500, MouseButton.Left, InputEventType.MouseButtonUp, 0);
    }

    [Fact]
    public void DataChannelProtocol_CursorUpdate_SerializesAndDeserializesAllShapes()
    {
        var shapes = new[]
        {
            RemoteCursorShape.Default,
            RemoteCursorShape.Hand,
            RemoteCursorShape.IBeam,
            RemoteCursorShape.SizeAll,
            RemoteCursorShape.SizeWE,
            RemoteCursorShape.SizeNS,
            RemoteCursorShape.Wait,
            RemoteCursorShape.Cross
        };

        foreach (var shape in shapes)
        {
            byte[] bytes = DataChannelProtocol.SerializeCursorUpdate(shape);
            Assert.Equal(2, bytes.Length);
            Assert.Equal((byte)InputEventType.CursorUpdate, bytes[0]);

            bool success = DataChannelProtocol.TryDeserializeCursorUpdate(bytes, out var deserialized);
            Assert.True(success);
            Assert.Equal(shape, deserialized);
        }
    }

    [Fact]
    public void InputPacket_CoalescedBuffer_DeserializesBothMoveAndClick()
    {
        // Simulate TCP/WebSocket buffer containing both MouseMove and MouseButtonDown
        var move = new MouseInputPacket(InputEventType.MouseMove, MouseButton.None, 0, 0.4f, 0.4f, 0, 1000);
        var click = new MouseInputPacket(InputEventType.MouseButtonDown, MouseButton.Left, 0, 0.4f, 0.4f, 0, 1005);

        byte[] moveBytes = DataChannelProtocol.SerializeMouse(in move);
        byte[] clickBytes = DataChannelProtocol.SerializeMouse(in click);

        byte[] coalesced = new byte[moveBytes.Length + clickBytes.Length];
        Array.Copy(moveBytes, 0, coalesced, 0, moveBytes.Length);
        Array.Copy(clickBytes, 0, coalesced, moveBytes.Length, clickBytes.Length);

        Assert.Equal(42, coalesced.Length);

        // Verify first packet
        bool ok1 = DataChannelProtocol.TryDeserializeMouse(coalesced.AsSpan(0, 21), out var p1);
        Assert.True(ok1);
        Assert.Equal(InputEventType.MouseMove, p1.Type);

        // Verify second packet
        bool ok2 = DataChannelProtocol.TryDeserializeMouse(coalesced.AsSpan(21, 21), out var p2);
        Assert.True(ok2);
        Assert.Equal(InputEventType.MouseButtonDown, p2.Type);
        Assert.Equal(MouseButton.Left, p2.Button);
    }
}
