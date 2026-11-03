using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
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

        // Map pixel coordinates to virtual desktop normalized coordinates (0..65535) for SendInput & mouse_event
        int normX = (int)Math.Clamp(Math.Round(((double)(physX - vLeft) * 65535.0) / Math.Max(1, vWidth - 1)), 0, 65535);
        int normY = (int)Math.Clamp(Math.Round(((double)(physY - vTop) * 65535.0) / Math.Max(1, vHeight - 1)), 0, 65535);

        // 2. High-speed, unblocked MouseMove routing (Atomic SetCursorPos + hardware mouse_event)
        if (type == InputEventType.MouseMove)
        {
            if (physX == _lastInjectedPhysX && physY == _lastInjectedPhysY)
            {
                return;
            }

            _lastInjectedPhysX = physX;
            _lastInjectedPhysY = physY;

            // Route 1: Direct Win32 Cursor warp (0ms cursor positioning)
            SetCursorPos(physX, physY);

            // Route 2: Driver-level hardware mouse motion in win32k.sys
            mouse_event(MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK | MOUSEEVENTF_MOVE, (uint)normX, (uint)normY, 0, UIntPtr.Zero);
            return;
        }

        // Check if calling thread is attached to a custom secure desktop (e.g. SEBDesktop, Winlogon)
        bool isCustomDesktop = Capture.DesktopSwitchManager.IsCustomOrProtectedDesktopActive(out string desktopName) &&
                               !string.Equals(desktopName, "Default", StringComparison.OrdinalIgnoreCase);

        // Resolve target and foreground windows only for physical button clicks
        POINT pt = new POINT { x = physX, y = physY };
        IntPtr targetHwnd = WindowFromPoint(pt);
        bool isElevatedTarget = IsWindowElevated(targetHwnd);

        // 3. Button Click & Wheel Injection (Triple-Drive with Trap Neutralization)
        // Neutralize cursor containment traps (ClipCursor) and exclusive mouse captures (ReleaseCapture)
        try { ClipCursor(IntPtr.Zero); } catch { }
        try { ReleaseCapture(); } catch { }

        _lastInjectedPhysX = physX;
        _lastInjectedPhysY = physY;
        SetCursorPos(physX, physY);

        IntPtr fgHwnd = GetForegroundWindow();

        // Break foreground focus monopoly if target window differs from foreground window
        if (type == InputEventType.MouseButtonDown && targetHwnd != IntPtr.Zero && fgHwnd != IntPtr.Zero && targetHwnd != fgHwnd)
        {
            BreakForegroundLock(targetHwnd, fgHwnd);
        }

        uint flags = MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK | MOUSEEVENTF_MOVE;
        uint mouseEventFlag = 0;

        switch (type)
        {
            case InputEventType.MouseButtonDown:
                if ((button & MouseButton.Left) != 0) { flags |= MOUSEEVENTF_LEFTDOWN; mouseEventFlag |= MOUSEEVENTF_LEFTDOWN; }
                if ((button & MouseButton.Right) != 0) { flags |= MOUSEEVENTF_RIGHTDOWN; mouseEventFlag |= MOUSEEVENTF_RIGHTDOWN; }
                if ((button & MouseButton.Middle) != 0) { flags |= MOUSEEVENTF_MIDDLEDOWN; mouseEventFlag |= MOUSEEVENTF_MIDDLEDOWN; }
                break;
            case InputEventType.MouseButtonUp:
                if ((button & MouseButton.Left) != 0) { flags |= MOUSEEVENTF_LEFTUP; mouseEventFlag |= MOUSEEVENTF_LEFTUP; }
                if ((button & MouseButton.Right) != 0) { flags |= MOUSEEVENTF_RIGHTUP; mouseEventFlag |= MOUSEEVENTF_RIGHTUP; }
                if ((button & MouseButton.Middle) != 0) { flags |= MOUSEEVENTF_MIDDLEUP; mouseEventFlag |= MOUSEEVENTF_MIDDLEUP; }
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
                    mouseData = (uint)wheelDelta,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };

        // Dispatch 1: Win32 SendInput (Atomic movement + button actuation)
        uint sent = SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());

        // Only invoke secondary driver or message fallbacks if SendInput failed (sent == 0)
        if (sent == 0)
        {
            // Dispatch 2: Driver-level mouse_event (ControlOS method, hardware driver level in win32k.sys)
            if (mouseEventFlag != 0)
            {
                mouse_event(mouseEventFlag, (uint)normX, (uint)normY, (uint)wheelDelta, UIntPtr.Zero);
            }

            // Dispatch 3: Direct PostMessage to target HWND (bypasses WH_MOUSE_LL hooks installed by exam/security apps)
            if (type == InputEventType.MouseButtonDown || type == InputEventType.MouseButtonUp)
            {
                PostDirectMouseMessage(targetHwnd, physX, physY, button, type);
            }
        }

        // Dispatch 4: Session 0 SYSTEM IPC (bypasses UIPI and custom secure desktops)
        if ((sent == 0 || isCustomDesktop || isElevatedTarget) && ElevatedFallback != null)
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
            ElevatedFallback?.Invoke(payload);
        }
    }

    public void InjectKeyboard(ushort virtualKey, ushort scanCode, bool isDown, uint unicodeChar)
    {
        bool isCustomDesktop = Capture.DesktopSwitchManager.IsCustomOrProtectedDesktopActive(out string desktopName) &&
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
                ElevatedFallback?.Invoke(payload);
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

            // Route 1: Win32 SendInput (Atomic primary injection - exactly 1 keystroke)
            uint sent = SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());

            // Route 2: Driver-level keybd_event ONLY as fallback when SendInput fails (sent == 0)
            if (sent == 0)
            {
                uint keybdFlags = 0;
                if (!isDown) keybdFlags |= 0x0002; // KEYEVENTF_KEYUP
                if ((flags & KEYEVENTF_EXTENDEDKEY) != 0) keybdFlags |= 0x0001; // KEYEVENTF_EXTENDEDKEY
                keybd_event((byte)virtualKey, (byte)actualScan, keybdFlags, UIntPtr.Zero);
            }

            // Route 3: Session 0 SYSTEM IPC bridge fallback ONLY if SendInput was blocked or custom desktop
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
                ElevatedFallback?.Invoke(payload);
            }
        }
    }

    private static void BreakForegroundLock(IntPtr targetHwnd, IntPtr fgHwnd)
    {
        try
        {
            uint fgThread = GetWindowThreadProcessId(fgHwnd, out _);
            uint curThread = GetCurrentThreadId();
            if (fgThread != curThread && fgThread != 0)
            {
                AttachThreadInput(curThread, fgThread, true);
                SetForegroundWindow(targetHwnd);
                BringWindowToTop(targetHwnd);
                AttachThreadInput(curThread, fgThread, false);
            }
            else
            {
                SetForegroundWindow(targetHwnd);
                BringWindowToTop(targetHwnd);
            }
        }
        catch { }
    }

    private static bool IsWindowElevated(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return false;
        try
        {
            GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == 0) return false;

            IntPtr hProc = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (hProc == IntPtr.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                return err == 5; // ERROR_ACCESS_DENIED implies higher integrity level
            }

            try
            {
                if (OpenProcessToken(hProc, TOKEN_QUERY, out IntPtr hToken))
                {
                    try
                    {
                        if (GetTokenInformation(hToken, TokenElevation, out TOKEN_ELEVATION elevation, (uint)Marshal.SizeOf<TOKEN_ELEVATION>(), out _))
                        {
                            return elevation.TokenIsElevated != 0;
                        }
                    }
                    finally
                    {
                        CloseHandle(hToken);
                    }
                }
            }
            finally
            {
                CloseHandle(hProc);
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static void PostDirectMouseMessage(IntPtr hWnd, int screenX, int screenY, MouseButton button, InputEventType type)
    {
        if (hWnd == IntPtr.Zero) return;
        try
        {
            POINT pt = new POINT { x = screenX, y = screenY };
            ScreenToClient(hWnd, ref pt);
            IntPtr lParam = (IntPtr)((pt.y << 16) | (pt.x & 0xFFFF));

            uint msg = 0;
            IntPtr wParam = IntPtr.Zero;

            if (type == InputEventType.MouseButtonDown)
            {
                if ((button & MouseButton.Left) != 0) { msg = WM_LBUTTONDOWN; wParam = (IntPtr)MK_LBUTTON; }
                else if ((button & MouseButton.Right) != 0) { msg = WM_RBUTTONDOWN; wParam = (IntPtr)MK_RBUTTON; }
                else if ((button & MouseButton.Middle) != 0) { msg = WM_MBUTTONDOWN; wParam = (IntPtr)MK_MBUTTON; }
            }
            else if (type == InputEventType.MouseButtonUp)
            {
                if ((button & MouseButton.Left) != 0) { msg = WM_LBUTTONUP; wParam = IntPtr.Zero; }
                else if ((button & MouseButton.Right) != 0) { msg = WM_RBUTTONUP; wParam = IntPtr.Zero; }
                else if ((button & MouseButton.Middle) != 0) { msg = WM_MBUTTONUP; wParam = IntPtr.Zero; }
            }

            if (msg != 0)
            {
                PostMessage(hWnd, msg, wParam, lParam);
            }
        }
        catch { }
    }

    #region Win32 Declarations
    private const uint GA_ROOT = 2;
    private const uint SPI_SETFOREGROUNDLOCKTIMEOUT = 0x2001;
    private const uint SPIF_SENDCHANGE = 0x0002;
    private const uint WM_CHAR = 0x0102;
    private const uint WM_MOUSEMOVE = 0x0200;

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SendNotifyMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr SetActiveWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hWnd);

    private const uint WM_LBUTTONDOWN = 0x0201;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_RBUTTONDOWN = 0x0204;
    private const uint WM_RBUTTONUP = 0x0205;
    private const uint WM_MBUTTONDOWN = 0x0207;
    private const uint WM_MBUTTONUP = 0x0208;
    private const uint MK_LBUTTON = 0x0001;
    private const uint MK_RBUTTON = 0x0002;
    private const uint MK_MBUTTON = 0x0010;
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const uint TOKEN_QUERY = 0x0008;
    private const int TokenElevation = 20;

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

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_ELEVATION
    {
        public int TokenIsElevated;
    }

    [DllImport("user32.dll")]
    private static extern bool ClipCursor(IntPtr lpRect);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(POINT Point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    private static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(IntPtr TokenHandle, int TokenInformationClass, out TOKEN_ELEVATION TokenInformation, uint TokenInformationLength, out uint ReturnLength);

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

public sealed class InputInjectionEngine : IDisposable
{
    private volatile int _lastInjectedScreenX = -1;
    private volatile int _lastInjectedScreenY = -1;
    private long _lastInjectedTimeMs = 0;

    public int LastInjectedScreenX => _lastInjectedScreenX;
    public int LastInjectedScreenY => _lastInjectedScreenY;
    public long LastInjectedTimeMs => Interlocked.Read(ref _lastInjectedTimeMs);
    public event Action? OnInputInjected;
    private readonly IInputSink _sink;
    private DisplayInfo _activeDisplay;
    private int _eventsInCurrentSecond;
    private long _currentSecondWindow;
    private const int MaxEventsPerSecond = 1000;

    // Disallowed system keys
    private const ushort VK_LWIN = 0x5B;
    private const ushort VK_RWIN = 0x5C;
    private const ushort VK_DELETE = 0x2E;

    // Dedicated single-threaded input dispatcher bound directly to the active Win32 input desktop
    private readonly BlockingCollection<Action> _inputQueue = new(new ConcurrentQueue<Action>());
    private readonly Thread _dispatcherThread;
    private readonly CancellationTokenSource _cts = new();
    private bool _isDisposed;

    public InputInjectionEngine(DisplayInfo initialDisplay, IInputSink? sink = null)
    {
        _activeDisplay = initialDisplay;
        _sink = sink ?? new WindowsSendInputSink();

        _dispatcherThread = new Thread(InputDispatcherLoop)
        {
            Name = "EnterpriseRemote.InputDispatcher",
            IsBackground = true,
            Priority = ThreadPriority.Highest
        };
        _dispatcherThread.Start();
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

        _lastInjectedScreenX = pixelX;
        _lastInjectedScreenY = pixelY;
        Interlocked.Exchange(ref _lastInjectedTimeMs, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        var copy = packet;
        // Enqueue to dedicated desktop-bound dispatcher thread for jitter-free, attached execution
        if (_sink is WindowsSendInputSink && !_isDisposed && !_inputQueue.IsAddingCompleted)
        {
            try
            {
                _inputQueue.Add(() =>
                {
                    _sink.InjectMouse(pixelX, pixelY, copy.Button, copy.Type, copy.WheelDelta, copy.NormalizedX, copy.NormalizedY);
                });
                return true;
            }
            catch { }
        }

        if (copy.Type is InputEventType.MouseButtonDown or InputEventType.MouseButtonUp)
        {
            OnInputInjected?.Invoke();
        }

        // Fallback to direct synchronous execution
        _sink.InjectMouse(pixelX, pixelY, packet.Button, packet.Type, packet.WheelDelta, packet.NormalizedX, packet.NormalizedY);
        return true;
    }

    public bool ProcessKeyboardPacket(in KeyboardInputPacket packet)
    {
        bool isDown = packet.Type == InputEventType.KeyDown;

        // Never rate-limit or drop KeyUp packets to ensure keys never get stuck
        if (isDown && !CheckRateLimit()) return false;

        // 1. Block Windows Key injection and Ctrl+Alt+Del from raw packets
        if (packet.VirtualKeyCode == VK_LWIN || packet.VirtualKeyCode == VK_RWIN)
        {
            return false;
        }

        if ((packet.Modifiers.HasFlag(ModifierKeys.Control) && packet.Modifiers.HasFlag(ModifierKeys.Alt) && packet.VirtualKeyCode == VK_DELETE))
        {
            return false;
        }

        // Direct high-priority execution with 0ms queue latency
        _sink.InjectKeyboard(packet.VirtualKeyCode, packet.ScanCode, isDown, packet.UnicodeChar);
        OnInputInjected?.Invoke();
        return true;
    }

    private void InputDispatcherLoop()
    {
        // Dedicated thread continuously maintains attachment to the active Win32 input desktop
        // (Owns no windows or hooks, guaranteeing SetThreadDesktop never fails with ERROR_BUSY 170)
        try
        {
            Capture.DesktopSwitchManager.AttachCallingThreadToInputDesktop(out _);

            foreach (var action in _inputQueue.GetConsumingEnumerable(_cts.Token))
            {
                try
                {
                    action();
                }
                catch { }
            }
        }
        catch (OperationCanceledException) { }
        catch { }
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

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _cts.Cancel();
        _inputQueue.CompleteAdding();
        try
        {
            _inputQueue.Dispose();
            _cts.Dispose();
        }
        catch { }
    }
}

