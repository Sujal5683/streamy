using System;
using System.Runtime.InteropServices;
using EnterpriseRemote.Common.Models;
using EnterpriseRemote.Common.Protocol;

namespace EnterpriseRemote.Host.Service.Input;

/// <summary>
/// Session 0 SYSTEM input injector.
/// Attaches to the active console input desktop (handling UAC/Consent, Winlogon, and UIPI)
/// and dispatches synthetic input packets with NT AUTHORITY\SYSTEM integrity.
/// </summary>
public static class SystemInputInjector
{
    private const uint DESKTOP_ALL_ACCESS = 0x01FF;
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

    public static bool InjectInputPacket(byte[] payload)
    {
        if (payload == null || payload.Length < 18) return false;

        IntPtr hOriginalWinSta = GetProcessWindowStation();
        IntPtr hWinSta0 = IntPtr.Zero;
        try
        {
            hWinSta0 = OpenWindowStation("WinSta0", false, WINSTA_ALL_ACCESS);
            if (hWinSta0 != IntPtr.Zero)
            {
                SetProcessWindowStation(hWinSta0);
            }
        }
        catch { }

        IntPtr hDesktop = OpenInputDesktop(0, false, DESKTOP_ALL_ACCESS);
        if (hDesktop == IntPtr.Zero)
        {
            hDesktop = OpenInputDesktop(0, false, DESKTOP_READOBJECTS | DESKTOP_WRITEOBJECTS | DESKTOP_SWITCHDESKTOP);
        }

        if (hDesktop != IntPtr.Zero)
        {
            SetThreadDesktop(hDesktop);
        }

        try
        {
            var span = new ReadOnlySpan<byte>(payload);
            if (DataChannelProtocol.TryDeserializeMouse(span, out var mouse))
            {
                InjectMouse(mouse);
                return true;
            }
            if (DataChannelProtocol.TryDeserializeKeyboard(span, out var key))
            {
                InjectKeyboard(key);
                return true;
            }
            return false;
        }
        finally
        {
            if (hDesktop != IntPtr.Zero)
            {
                CloseDesktop(hDesktop);
            }
            if (hWinSta0 != IntPtr.Zero)
            {
                if (hOriginalWinSta != IntPtr.Zero) SetProcessWindowStation(hOriginalWinSta);
                CloseWindowStation(hWinSta0);
            }
        }
    }

    private static void InjectMouse(in MouseInputPacket packet)
    {
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

        int physX = vLeft + (int)Math.Clamp(Math.Round(packet.NormalizedX * (vWidth - 1)), 0, vWidth - 1);
        int physY = vTop + (int)Math.Clamp(Math.Round(packet.NormalizedY * (vHeight - 1)), 0, vHeight - 1);
        int normX = (int)Math.Clamp(Math.Round(((double)(physX - vLeft) * 65536.0) / Math.Max(1, vWidth)), 0, 65535);
        int normY = (int)Math.Clamp(Math.Round(((double)(physY - vTop) * 65536.0) / Math.Max(1, vHeight)), 0, 65535);

        SetCursorPos(physX, physY);

        if (packet.Type == InputEventType.MouseMove)
        {
            return;
        }

        uint flags = MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK;

        switch (packet.Type)
        {
            case InputEventType.MouseButtonDown:
                if ((packet.Button & MouseButton.Left) != 0) flags |= MOUSEEVENTF_LEFTDOWN;
                if ((packet.Button & MouseButton.Right) != 0) flags |= MOUSEEVENTF_RIGHTDOWN;
                if ((packet.Button & MouseButton.Middle) != 0) flags |= MOUSEEVENTF_MIDDLEDOWN;
                break;
            case InputEventType.MouseButtonUp:
                if ((packet.Button & MouseButton.Left) != 0) flags |= MOUSEEVENTF_LEFTUP;
                if ((packet.Button & MouseButton.Right) != 0) flags |= MOUSEEVENTF_RIGHTUP;
                if ((packet.Button & MouseButton.Middle) != 0) flags |= MOUSEEVENTF_MIDDLEUP;
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
                    mouseData = (uint)packet.WheelDelta,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };

        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    private static void InjectKeyboard(in KeyboardInputPacket packet)
    {
        uint flags = 0;
        if (packet.Type == InputEventType.KeyUp) flags |= KEYEVENTF_KEYUP;

        if ((packet.VirtualKeyCode >= 33 && packet.VirtualKeyCode <= 46) || packet.VirtualKeyCode == 144 || packet.VirtualKeyCode == 111)
        {
            flags |= KEYEVENTF_EXTENDEDKEY;
        }

        if (packet.UnicodeChar > 0 && packet.VirtualKeyCode == 0)
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
                        wScan = (ushort)packet.UnicodeChar,
                        dwFlags = flags,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };
            SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
        }
        else
        {
            ushort actualScan = packet.ScanCode > 0 ? packet.ScanCode : (ushort)MapVirtualKey(packet.VirtualKeyCode, 0);
            var input = new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = packet.VirtualKeyCode,
                        wScan = actualScan,
                        dwFlags = flags,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };
            SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
        }
    }

    #region Win32 P/Invoke
    private const uint WINSTA_ALL_ACCESS = 0x037F;
    private const uint DESKTOP_READOBJECTS = 0x0001;
    private const uint DESKTOP_WRITEOBJECTS = 0x0080;
    private const uint DESKTOP_SWITCHDESKTOP = 0x0100;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr OpenWindowStation(string lpszWinSta, bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetProcessWindowStation();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetProcessWindowStation(IntPtr hWinSta);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseWindowStation(IntPtr hWinSta);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr OpenInputDesktop(uint dwFlags, bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetThreadDesktop(IntPtr hDesktop);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseDesktop(IntPtr hDesktop);

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
