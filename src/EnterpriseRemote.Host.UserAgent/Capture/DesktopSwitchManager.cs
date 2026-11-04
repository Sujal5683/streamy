using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using EnterpriseRemote.Common.Protocol;

namespace EnterpriseRemote.Host.UserAgent.Capture;

/// <summary>
/// Proactive detector and synchronizer for Win32 input desktop transitions.
/// Secure exam browsers (such as Safe Exam Browser) isolate sessions by creating
/// dedicated desktops (e.g. WinSta0\SafeExamBrowser or WinSta0\SEBDesktop) and invoking SwitchDesktop.
/// This manager attaches capture and input worker threads directly to the active input desktop.
/// </summary>
public sealed class DesktopSwitchManager : IDisposable
{
    private readonly object _lock = new();
    private string _currentDesktopName = "Default";
    private System.Threading.Timer? _pollTimer;
    private bool _isDisposed;

    public string CurrentDesktopName
    {
        get
        {
            lock (_lock)
            {
                return _currentDesktopName;
            }
        }
    }

    public event Action<string>? OnDesktopChanged;

    public void StartMonitoring(int intervalMs = 250)
    {
        _pollTimer = new System.Threading.Timer(_ =>
        {
            try
            {
                CheckAndSyncDesktop(out string _);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Desktop switch check notice: {ex.Message}");
            }
        }, null, 100, intervalMs);
    }

    /// <summary>
    /// Checks if the active input desktop has changed from the current desktop.
    /// If changed, attaches the calling thread and raises OnDesktopChanged.
    /// </summary>
    public bool CheckAndSyncDesktop(out string activeName)
    {
        activeName = "Default";
        IntPtr hInputDesktop = OpenActiveInputDesktop();
        if (hInputDesktop == IntPtr.Zero)
        {
            lock (_lock)
            {
                activeName = _currentDesktopName;
            }
            return false;
        }

        try
        {
            string name = QueryDesktopName(hInputDesktop);
            if (string.IsNullOrEmpty(name)) name = "UnknownDesktop";

            bool changed = false;
            lock (_lock)
            {
                if (!string.Equals(_currentDesktopName, name, StringComparison.OrdinalIgnoreCase))
                {
                    Debug.WriteLine($"[+] Active Win32 desktop transition detected: '{_currentDesktopName}' -> '{name}'");
                    _currentDesktopName = name;
                    changed = true;
                }
                activeName = _currentDesktopName;
            }

            if (changed)
            {
                AttachThreadToDesktop(hInputDesktop);
                OnDesktopChanged?.Invoke(name);
                return true;
            }

            return false;
        }
        finally
        {
            CloseDesktop(hInputDesktop);
        }
    }

    /// <summary>
    /// Attaches the calling thread to the currently active input desktop.
    /// Note: The calling thread must not have any top-level windows or hooks created on it.
    /// </summary>
    public static bool AttachCallingThreadToInputDesktop(out string desktopName)
    {
        desktopName = "Default";
        IntPtr hDesktop = OpenActiveInputDesktop();
        if (hDesktop == IntPtr.Zero)
        {
            int err = Marshal.GetLastWin32Error();
            if (err == 5) // ERROR_ACCESS_DENIED implies restricted secure desktop (e.g., SEB, UAC)
            {
                desktopName = "ProtectedSecureDesktop";
                return false;
            }
            return false;
        }

        try
        {
            desktopName = QueryDesktopName(hDesktop);
            return AttachThreadToDesktop(hDesktop);
        }
        finally
        {
            CloseDesktop(hDesktop);
        }
    }

    /// <summary>
    /// Determines whether the active input desktop is a custom or protected secure desktop
    /// (e.g. SEBDesktop, SafeExamBrowser, Winlogon, UAC). Returns true if a non-Default desktop
    /// is active OR if access was denied by ACLs.
    /// </summary>
    public static bool IsCustomOrProtectedDesktopActive(out string desktopName)
    {
        desktopName = "Default";
        IntPtr hDesktop = OpenActiveInputDesktop();
        if (hDesktop == IntPtr.Zero)
        {
            int err = Marshal.GetLastWin32Error();
            if (err == 5) // ERROR_ACCESS_DENIED
            {
                desktopName = "ProtectedSecureDesktop";
                return true;
            }
            return false;
        }

        try
        {
            desktopName = QueryDesktopName(hDesktop);
            if (string.IsNullOrEmpty(desktopName)) desktopName = "UnknownDesktop";
            bool isCustom = !string.Equals(desktopName, "Default", StringComparison.OrdinalIgnoreCase);
            if (isCustom)
            {
                AttachThreadToDesktop(hDesktop);
            }
            return isCustom;
        }
        finally
        {
            CloseDesktop(hDesktop);
        }
    }

    /// <summary>
    /// Attaches the calling thread to the active input desktop, updating <paramref name="currentDesktopName"/>
    /// if the active desktop differs from it.
    /// Returns true when a switch was performed (i.e., the desktop changed).
    /// </summary>
    public static bool SyncCallingThreadToInputDesktop(ref string currentDesktopName, out string activeName)
    {
        bool switched = AttachCallingThreadToInputDesktop(out activeName);
        if (string.IsNullOrEmpty(activeName))
            activeName = "Default";
        currentDesktopName = activeName;
        return switched;
    }

    public static bool AttachThreadToDesktop(IntPtr hDesktop)
    {
        if (hDesktop == IntPtr.Zero) return false;
        bool result = SetThreadDesktop(hDesktop);
        if (!result)
        {
            int err = Marshal.GetLastWin32Error();
            Debug.WriteLine($"SetThreadDesktop warning: error code {err} (170=ERROR_BUSY if thread owns windows)");
        }
        return result;
    }

    public static bool SwitchToDesktopByName(string desktopName)
    {
        if (string.IsNullOrWhiteSpace(desktopName) || desktopName.Equals("Auto", StringComparison.OrdinalIgnoreCase))
            return true;

        try
        {
            uint desired = DESKTOP_ALL_ACCESS | DESKTOP_SWITCHDESKTOP;
            IntPtr hDesk = OpenDesktop(desktopName, 0, false, desired);
            if (hDesk == IntPtr.Zero)
            {
                hDesk = OpenDesktop(desktopName, 0, false, DESKTOP_READOBJECTS | DESKTOP_SWITCHDESKTOP);
            }

            if (hDesk != IntPtr.Zero)
            {
                try
                {
                    bool switched = SwitchDesktop(hDesk);
                    AttachThreadToDesktop(hDesk);
                    return switched;
                }
                finally
                {
                    CloseDesktop(hDesk);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"SwitchToDesktopByName exception: {ex.Message}");
        }
        return false;
    }

    public static IntPtr OpenActiveInputDesktop()
    {
        try
        {
            IntPtr hWinSta = OpenWindowStation("WinSta0", false, WINSTA_ALL_ACCESS);
            if (hWinSta != IntPtr.Zero)
            {
                SetProcessWindowStation(hWinSta);
                CloseWindowStation(hWinSta);
            }
        }
        catch { }

        // Tier 1: Request full control
        IntPtr hDesktop = OpenInputDesktop(0, false, DESKTOP_ALL_ACCESS);
        if (hDesktop != IntPtr.Zero) return hDesktop;

        // Tier 2: Read / Switch / Execute permissions
        uint desired = DESKTOP_READOBJECTS | DESKTOP_SWITCHDESKTOP | DESKTOP_WRITEOBJECTS | GENERIC_READ | GENERIC_EXECUTE;
        hDesktop = OpenInputDesktop(0, false, desired);
        if (hDesktop != IntPtr.Zero) return hDesktop;

        // Tier 3: Minimal read access
        return OpenInputDesktop(0, false, DESKTOP_READOBJECTS | DESKTOP_SWITCHDESKTOP);
    }

    public static string QueryDesktopName(IntPtr hDesktop)
    {
        if (hDesktop == IntPtr.Zero) return string.Empty;

        uint needed = 0;
        GetUserObjectInformation(hDesktop, UOI_NAME, IntPtr.Zero, 0, out needed);
        if (needed == 0) return string.Empty;

        IntPtr buffer = Marshal.AllocHGlobal((int)needed);
        try
        {
            if (GetUserObjectInformation(hDesktop, UOI_NAME, buffer, needed, out _))
            {
                string raw = Marshal.PtrToStringUni(buffer) ?? string.Empty;
                return raw.TrimEnd('\0');
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return string.Empty;
    }

    /// <summary>
    /// Returns the names of all Win32 desktops visible in the current window station (WinSta0).
    /// Always includes "Default". Safe to call from any thread.
    /// </summary>
    public static string[] GetAvailableDesktops()
    {
        var names = new List<string>();

        try
        {
            EnumDesktopsDelegate callback = (name, _) =>
            {
                if (!string.IsNullOrEmpty(name))
                    names.Add(name);
                return true;
            };

            IntPtr hWinSta = OpenWindowStation("WinSta0", false, WINSTA_ENUMERATE | WINSTA_ENUMDESKTOPS);
            if (hWinSta != IntPtr.Zero)
            {
                try
                {
                    EnumDesktops(hWinSta, callback, IntPtr.Zero);
                }
                finally
                {
                    CloseWindowStation(hWinSta);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"GetAvailableDesktops notice: {ex.Message}");
        }

        // Always guarantee at least "Default"
        if (names.Count == 0)
            names.Add("Default");

        return names.ToArray();
    }

    /// <summary>
    /// Returns a <see cref="DisplaySummary"/> for every physical monitor attached to the system.
    /// Uses <see cref="Screen.AllScreens"/> which wraps the Win32 EnumDisplayMonitors API.
    /// </summary>
    public static DisplaySummary[] GetPhysicalDisplays()
    {
        var screens = Screen.AllScreens;
        var result = new DisplaySummary[screens.Length];
        for (int i = 0; i < screens.Length; i++)
        {
            var s = screens[i];
            result[i] = new DisplaySummary(
                Index: (byte)i,
                Name: s.DeviceName ?? $"\\\\.\\DISPLAY{i + 1}",
                Width: s.Bounds.Width,
                Height: s.Bounds.Height,
                IsPrimary: s.Primary);
        }
        return result;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _pollTimer?.Dispose();
    }

    #region Win32 P/Invoke
    private delegate bool EnumDesktopsDelegate([MarshalAs(UnmanagedType.LPWStr)] string lpszDesktop, IntPtr lParam);

    private const uint UOI_NAME = 2;
    private const uint DESKTOP_READOBJECTS = 0x0001;
    private const uint DESKTOP_CREATEWINDOW = 0x0002;
    private const uint DESKTOP_CREATEMENU = 0x0004;
    private const uint DESKTOP_HOOKCONTROL = 0x0008;
    private const uint DESKTOP_JOURNALRECORD = 0x0010;
    private const uint DESKTOP_JOURNALPLAYBACK = 0x0020;
    private const uint DESKTOP_ENUMERATE = 0x0040;
    private const uint DESKTOP_WRITEOBJECTS = 0x0080;
    private const uint DESKTOP_SWITCHDESKTOP = 0x0100;
    private const uint DESKTOP_ALL_ACCESS = 0x01FF;
    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_EXECUTE = 0x20000000;
    private const uint WINSTA_ALL_ACCESS = 0x037F;
    private const uint WINSTA_ENUMERATE = 0x0100;
    private const uint WINSTA_ENUMDESKTOPS = 0x0001;

    [DllImport("user32.dll", EntryPoint = "OpenWindowStationW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenWindowStation([MarshalAs(UnmanagedType.LPWStr)] string lpszWinSta, bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetProcessWindowStation(IntPtr hWinSta);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseWindowStation(IntPtr hWinSta);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr OpenInputDesktop(uint dwFlags, bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll", EntryPoint = "OpenDesktopW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenDesktop([MarshalAs(UnmanagedType.LPWStr)] string lpszDesktop, uint dwFlags, bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SwitchDesktop(IntPtr hDesktop);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetThreadDesktop(IntPtr hDesktop);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseDesktop(IntPtr hDesktop);

    [DllImport("user32.dll", EntryPoint = "GetUserObjectInformationW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool GetUserObjectInformation(IntPtr hObj, uint nIndex, IntPtr pvInfo, uint nLength, out uint lpnLengthNeeded);

    [DllImport("user32.dll", EntryPoint = "EnumDesktopsW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool EnumDesktops(IntPtr hwinsta, EnumDesktopsDelegate lpEnumFunc, IntPtr lParam);
    #endregion
}
