using System;
using System.Runtime.InteropServices;
using EnterpriseRemote.Common.Models;
using EnterpriseRemote.Common.Protocol;

namespace EnterpriseRemote.Host.Service.Input;

/// <summary>
/// Session 0 SYSTEM input injector.
/// Attaches to the active console input desktop (handling UAC/Consent, Winlogon, and UIPI)
/// and dispatches synthetic input packets with NT AUTHORITY\SYSTEM integrity.
/// Features driver-level companion injection (mouse_event / keybd_event) and direct HWND PostMessage routing.
/// </summary>
public static class SystemInputInjector
{
    private static long _lastMouseDownTicks = 0;
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
        int normX = (int)Math.Clamp(Math.Round(((double)(physX - vLeft) * 65535.0) / Math.Max(1, vWidth - 1)), 0, 65535);
        int normY = (int)Math.Clamp(Math.Round(((double)(physY - vTop) * 65535.0) / Math.Max(1, vHeight - 1)), 0, 65535);

        try { ClipCursor(IntPtr.Zero); } catch { }
        try { ReleaseCapture(); } catch { }

        // Route 1: SetCursorPos
        SetCursorPos(physX, physY);

        // Route 2: Driver-level mouse_event
        mouse_event(MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK | MOUSEEVENTF_MOVE, (uint)normX, (uint)normY, 0, UIntPtr.Zero);

        if (packet.Type == InputEventType.MouseMove)
        {
            // Route 3: SendInput move
            var moveInput = new INPUT
            {
                type = INPUT_MOUSE,
                u = new InputUnion
                {
                    mi = new MOUSEINPUT
                    {
                        dx = normX,
                        dy = normY,
                        mouseData = 0,
                        dwFlags = MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK | MOUSEEVENTF_MOVE,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };
            SendInput(1, new[] { moveInput }, Marshal.SizeOf<INPUT>());
            return;
        }

        uint flags = MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK | MOUSEEVENTF_MOVE;
        uint mouseEventFlag = 0;

        switch (packet.Type)
        {
            case InputEventType.MouseButtonDown:
                if ((packet.Button & MouseButton.Left) != 0) { flags |= MOUSEEVENTF_LEFTDOWN; mouseEventFlag |= MOUSEEVENTF_LEFTDOWN; }
                if ((packet.Button & MouseButton.Right) != 0) { flags |= MOUSEEVENTF_RIGHTDOWN; mouseEventFlag |= MOUSEEVENTF_RIGHTDOWN; }
                if ((packet.Button & MouseButton.Middle) != 0) { flags |= MOUSEEVENTF_MIDDLEDOWN; mouseEventFlag |= MOUSEEVENTF_MIDDLEDOWN; }
                break;
            case InputEventType.MouseButtonUp:
                if ((packet.Button & MouseButton.Left) != 0) { flags |= MOUSEEVENTF_LEFTUP; mouseEventFlag |= MOUSEEVENTF_LEFTUP; }
                if ((packet.Button & MouseButton.Right) != 0) { flags |= MOUSEEVENTF_RIGHTUP; mouseEventFlag |= MOUSEEVENTF_RIGHTUP; }
                if ((packet.Button & MouseButton.Middle) != 0) { flags |= MOUSEEVENTF_MIDDLEUP; mouseEventFlag |= MOUSEEVENTF_MIDDLEUP; }
                break;
            case InputEventType.MouseWheel:
                flags |= MOUSEEVENTF_WHEEL;
                mouseEventFlag |= MOUSEEVENTF_WHEEL;
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

        // Dispatch 1: Win32 SendInput
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());

        // Dispatch 2: Driver-level mouse_event
        if (mouseEventFlag != 0)
        {
            mouse_event(mouseEventFlag, (uint)normX, (uint)normY, (uint)packet.WheelDelta, UIntPtr.Zero);
        }

        // Physical Actuation Hold Window: Record Down and enforce 35ms hold window before Up
        if (packet.Type == InputEventType.MouseButtonDown)
        {
            _lastMouseDownTicks = System.Diagnostics.Stopwatch.GetTimestamp();
        }
        else if (packet.Type == InputEventType.MouseButtonUp)
        {
            if (_lastMouseDownTicks > 0)
            {
                long elapsedMs = (System.Diagnostics.Stopwatch.GetTimestamp() - _lastMouseDownTicks) * 1000 / System.Diagnostics.Stopwatch.Frequency;
                if (elapsedMs < 35)
                {
                    Thread.Sleep((int)(35 - elapsedMs));
                }
            }
        }

        // Dispatch 3: Direct PostMessage / SendNotifyMessage to target HWND and Root Window (bypasses WH_MOUSE_LL)
        if (packet.Type == InputEventType.MouseButtonDown || packet.Type == InputEventType.MouseButtonUp)
        {
            try
            {
                POINT pt = new POINT { x = physX, y = physY };
                IntPtr targetHwnd = WindowFromPoint(pt);
                if (targetHwnd != IntPtr.Zero)
                {
                    IntPtr rootHwnd = GetAncestor(targetHwnd, GA_ROOT);
                    if (rootHwnd == IntPtr.Zero) rootHwnd = targetHwnd;

                    if (packet.Type == InputEventType.MouseButtonDown)
                    {
                        try
                        {
                            SetForegroundWindow(rootHwnd);
                            BringWindowToTop(rootHwnd);
                        }
                        catch { }
                    }

                    ScreenToClient(targetHwnd, ref pt);
                    IntPtr lParam = (IntPtr)((pt.y << 16) | (pt.x & 0xFFFF));
                    uint msg = 0;
                    IntPtr wParam = IntPtr.Zero;

                    if (packet.Type == InputEventType.MouseButtonDown)
                    {
                        if ((packet.Button & MouseButton.Left) != 0) { msg = WM_LBUTTONDOWN; wParam = (IntPtr)MK_LBUTTON; }
                        else if ((packet.Button & MouseButton.Right) != 0) { msg = WM_RBUTTONDOWN; wParam = (IntPtr)MK_RBUTTON; }
                        else if ((packet.Button & MouseButton.Middle) != 0) { msg = WM_MBUTTONDOWN; wParam = (IntPtr)MK_MBUTTON; }
                    }
                    else if (packet.Type == InputEventType.MouseButtonUp)
                    {
                        if ((packet.Button & MouseButton.Left) != 0) { msg = WM_LBUTTONUP; wParam = IntPtr.Zero; }
                        else if ((packet.Button & MouseButton.Right) != 0) { msg = WM_RBUTTONUP; wParam = IntPtr.Zero; }
                        else if ((packet.Button & MouseButton.Middle) != 0) { msg = WM_MBUTTONUP; wParam = IntPtr.Zero; }
                    }

                    if (msg != 0)
                    {
                        PostMessage(targetHwnd, msg, wParam, lParam);
                        SendNotifyMessage(targetHwnd, msg, wParam, lParam);

                        if (rootHwnd != targetHwnd && rootHwnd != IntPtr.Zero)
                        {
                            POINT rootPt = new POINT { x = physX, y = physY };
                            ScreenToClient(rootHwnd, ref rootPt);
                            IntPtr rootLParam = (IntPtr)((rootPt.y << 16) | (rootPt.x & 0xFFFF));
                            PostMessage(rootHwnd, msg, wParam, rootLParam);
                        }
                    }
                }
            }
            catch { }
        }
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

            // Route 1: Win32 SendInput
            SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());

            // Route 2: Driver-level keybd_event
            uint keybdFlags = 0;
            if (packet.Type == InputEventType.KeyUp) keybdFlags |= 0x0002;
            if ((flags & KEYEVENTF_EXTENDEDKEY) != 0) keybdFlags |= 0x0001;
            keybd_event((byte)packet.VirtualKeyCode, (byte)actualScan, keybdFlags, UIntPtr.Zero);
        }
    }

    #region Win32 P/Invoke
    private const uint GA_ROOT = 2;
    private const uint WM_CHAR = 0x0102;

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SendNotifyMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    private const uint WM_LBUTTONDOWN = 0x0201;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_RBUTTONDOWN = 0x0204;
    private const uint WM_RBUTTONUP = 0x0205;
    private const uint WM_MBUTTONDOWN = 0x0207;
    private const uint WM_MBUTTONUP = 0x0208;
    private const uint MK_LBUTTON = 0x0001;
    private const uint MK_RBUTTON = 0x0002;
    private const uint MK_MBUTTON = 0x0010;

    private const uint WINSTA_ALL_ACCESS = 0x037F;
    private const uint DESKTOP_READOBJECTS = 0x0001;
    private const uint DESKTOP_WRITEOBJECTS = 0x0080;
    private const uint DESKTOP_SWITCHDESKTOP = 0x0100;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [DllImport("user32.dll")]
    private static extern bool ClipCursor(IntPtr lpRect);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(POINT Point);

    [DllImport("user32.dll")]
    private static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

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
    private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

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
