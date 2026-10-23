using System;
using System.Runtime.InteropServices;
using EnterpriseRemote.Common.Models;
using EnterpriseRemote.Common.Protocol;

namespace EnterpriseRemote.Host.UserAgent.Input;

public interface IInputSink
{
    void InjectMouse(int x, int y, MouseButton button, InputEventType type, short wheelDelta);
    void InjectMouse(int x, int y, MouseButton button, InputEventType type, short wheelDelta, float normalizedX, float normalizedY) =>
        InjectMouse(x, y, button, type, wheelDelta);
    void InjectKeyboard(ushort virtualKey, ushort scanCode, bool isDown, uint unicodeChar);
}

public sealed class WindowsSendInputSink : IInputSink
{
    private int _lastInjectedPhysX = -1;
    private int _lastInjectedPhysY = -1;

    public Action<byte[]>? ElevatedFallback { get; set; }

    public void InjectMouse(int x, int y, MouseButton button, InputEventType type, short wheelDelta)
    {
        InjectMouse(x, y, button, type, wheelDelta, -1f, -1f);
    }

    public void InjectMouse(int x, int y, MouseButton button, InputEventType type, short wheelDelta, float normalizedX, float normalizedY)
    {
        // 1. Query virtual desktop bounds to support single-monitor and multi-monitor setups with 100% precision
        int vLeft = GetSystemMetrics(SM_XVIRTUALSCREEN);
        int vTop = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int vWidth = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        int vHeight = GetSystemMetrics(SM_CYVIRTUALSCREEN);

        if (vWidth <= 0 || vHeight <= 0)
        {
            vLeft = 0;
            vTop = 0;
            vWidth = GetSystemMetrics(SM_CXSCREEN);
            vHeight = GetSystemMetrics(SM_CYSCREEN);
        }
        if (vWidth <= 0) vWidth = 1920;
        if (vHeight <= 0) vHeight = 1080;

        int physX = x;
        int physY = y;

        // Map pixel coordinates to virtual desktop normalized coordinates (0..65535) for SendInput
        int normX = (int)Math.Clamp(Math.Round(((double)(physX - vLeft) * 65536.0) / Math.Max(1, vWidth)), 0, 65535);
        int normY = (int)Math.Clamp(Math.Round(((double)(physY - vTop) * 65536.0) / Math.Max(1, vHeight)), 0, 65535);

        // 2. Synchronize cursor position without interfering with local physical mouse
        if (type == InputEventType.MouseMove)
        {
            if (physX == _lastInjectedPhysX && physY == _lastInjectedPhysY)
            {
                return;
            }

            _lastInjectedPhysX = physX;
            _lastInjectedPhysY = physY;
            SetCursorPos(physX, physY);
            return;
        }

        _lastInjectedPhysX = physX;
        _lastInjectedPhysY = physY;
        SetCursorPos(physX, physY);

        uint flags = MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK;

        switch (type)
        {
            case InputEventType.MouseButtonDown:
                if ((button & MouseButton.Left) != 0) flags |= MOUSEEVENTF_LEFTDOWN;
                if ((button & MouseButton.Right) != 0) flags |= MOUSEEVENTF_RIGHTDOWN;
                if ((button & MouseButton.Middle) != 0) flags |= MOUSEEVENTF_MIDDLEDOWN;
                break;
            case InputEventType.MouseButtonUp:
                if ((button & MouseButton.Left) != 0) flags |= MOUSEEVENTF_LEFTUP;
                if ((button & MouseButton.Right) != 0) flags |= MOUSEEVENTF_RIGHTUP;
                if ((button & MouseButton.Middle) != 0) flags |= MOUSEEVENTF_MIDDLEUP;
                break;
            case InputEventType.MouseWheel:
                flags |= MOUSEEVENTF_WHEEL;
                break;
        }

        var input = new INPUT
        {
            type = INPUT_MOUSE,
            u = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dx = normX,
                    dy = normY,
                    mouseData = (uint)wheelDelta,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };

        bool isCustomDesktop = Capture.DesktopSwitchManager.AttachCallingThreadToInputDesktop(out string desktopName) &&
                               !string.Equals(desktopName, "Default", StringComparison.OrdinalIgnoreCase);

        uint sent = SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
        if ((sent == 0 || isCustomDesktop) && ElevatedFallback != null)
        {
            var mousePacket = new MouseInputPacket(
                type,
                button,
                0,
                normalizedX >= 0f ? normalizedX : (float)normX / 65535f,
                normalizedY >= 0f ? normalizedY : (float)normY / 65535f,
                wheelDelta,
                (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            byte[] payload = DataChannelProtocol.SerializeMouse(in mousePacket);
            ElevatedFallback(payload);
        }
    }

    public void InjectKeyboard(ushort virtualKey, ushort scanCode, bool isDown, uint unicodeChar)
    {
        bool isCustomDesktop = Capture.DesktopSwitchManager.AttachCallingThreadToInputDesktop(out string desktopName) &&
                               !string.Equals(desktopName, "Default", StringComparison.OrdinalIgnoreCase);

        uint flags = 0;
        if (!isDown) flags |= KEYEVENTF_KEYUP;

        // Extended keys (arrows, home, end, ins, del, pgup, pgdn, numlock, divide)
        if ((virtualKey >= 33 && virtualKey <= 46) || virtualKey == 144 || virtualKey == 111)
        {
            flags |= KEYEVENTF_EXTENDEDKEY;
        }

        if (unicodeChar > 0 && virtualKey == 0)
        {
            flags |= KEYEVENTF_UNICODE;
            var input = new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = 0,
                        wScan = (ushort)unicodeChar,
                        dwFlags = flags,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };
            uint sent = SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
            if ((sent == 0 || isCustomDesktop) && ElevatedFallback != null)
            {
                var keyPacket = new KeyboardInputPacket(
                    isDown ? InputEventType.KeyDown : InputEventType.KeyUp,
                    virtualKey,
                    (ushort)unicodeChar,
                    ModifierKeys.None,
                    unicodeChar,
                    (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                byte[] payload = DataChannelProtocol.SerializeKeyboard(in keyPacket);
                ElevatedFallback(payload);
            }
        }
        else
        {
            ushort actualScan = scanCode > 0 ? scanCode : (ushort)MapVirtualKey(virtualKey, 0);
            var input = new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = virtualKey,
                        wScan = actualScan,
                        dwFlags = flags,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };
            uint sent = SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
            if ((sent == 0 || isCustomDesktop) && ElevatedFallback != null)
            {
                var keyPacket = new KeyboardInputPacket(
                    isDown ? InputEventType.KeyDown : InputEventType.KeyUp,
                    virtualKey,
                    actualScan,
                    ModifierKeys.None,
                    unicodeChar,
                    (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                byte[] payload = DataChannelProtocol.SerializeKeyboard(in keyPacket);
                ElevatedFallback(payload);
            }
        }
    }

    #region Win32 SendInput Declarations
    private const uint INPUT_MOUSE = 0;
    private const uint INPUT_KEYBOARD = 1;
    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;
    private const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;
    private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;

    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;
    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetCursorPos(int X, int Y);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, [In] INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MOUSEINPUT mi;
        [FieldOffset(0)]
        public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }
    #endregion
}

public sealed class InputInjectionEngine
{
    private readonly IInputSink _sink;
    private DisplayInfo _activeDisplay;
    private int _eventsInCurrentSecond;
    private long _currentSecondWindow;
    private const int MaxEventsPerSecond = 1000;

    // Disallowed system keys
    private const ushort VK_LWIN = 0x5B;
    private const ushort VK_RWIN = 0x5C;
    private const ushort VK_DELETE = 0x2E;

    public InputInjectionEngine(DisplayInfo initialDisplay, IInputSink? sink = null)
    {
        _activeDisplay = initialDisplay;
        _sink = sink ?? new WindowsSendInputSink();
    }

    public void UpdateDisplay(DisplayInfo display)
    {
        _activeDisplay = display;
    }

    public void SetElevatedFallback(Action<byte[]> fallback)
    {
        if (_sink is WindowsSendInputSink w)
        {
            w.ElevatedFallback = fallback;
        }
    }

    public bool ProcessMousePacket(in MouseInputPacket packet)
    {
        if (!CheckRateLimit()) return false;

        // 1. Strict Bounds Checking
        if (packet.NormalizedX < 0.0f || packet.NormalizedX > 1.0f ||
            packet.NormalizedY < 0.0f || packet.NormalizedY > 1.0f)
        {
            return false; // Out-of-bounds input packet dropped
        }

        // 2. Convert to Target Display Coordinates with bounds safety
        int dispW = Math.Max(1, _activeDisplay.Width);
        int dispH = Math.Max(1, _activeDisplay.Height);
        int pixelX = _activeDisplay.Left + (int)Math.Round(packet.NormalizedX * (dispW - 1));
        int pixelY = _activeDisplay.Top + (int)Math.Round(packet.NormalizedY * (dispH - 1));

        // 3. Inject with normalized coordinates for 100% DPI and resolution precision
        _sink.InjectMouse(pixelX, pixelY, packet.Button, packet.Type, packet.WheelDelta, packet.NormalizedX, packet.NormalizedY);
        return true;
    }

    public bool ProcessKeyboardPacket(in KeyboardInputPacket packet)
    {
        if (!CheckRateLimit()) return false;

        // 1. Block Windows Key injection and Ctrl+Alt+Del from raw packets
        if (packet.VirtualKeyCode == VK_LWIN || packet.VirtualKeyCode == VK_RWIN)
        {
            return false; // Block raw Windows key injection
        }

        if ((packet.Modifiers.HasFlag(ModifierKeys.Control) && packet.Modifiers.HasFlag(ModifierKeys.Alt) && packet.VirtualKeyCode == VK_DELETE))
        {
            return false; // Block raw Ctrl+Alt+Del injection
        }

        bool isDown = packet.Type == InputEventType.KeyDown;
        _sink.InjectKeyboard(packet.VirtualKeyCode, packet.ScanCode, isDown, packet.UnicodeChar);
        return true;
    }

    private bool CheckRateLimit()
    {
        long currentSec = DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond;
        if (currentSec != _currentSecondWindow)
        {
            _currentSecondWindow = currentSec;
            _eventsInCurrentSecond = 0;
        }

        _eventsInCurrentSecond++;
        return _eventsInCurrentSecond <= MaxEventsPerSecond;
    }
}
