using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace EnterpriseRemote.Host.Service.Policy;

/// <summary>
/// Session 0 SYSTEM-level Window Affinity Neutralizer.
/// Operates with NT AUTHORITY\SYSTEM integrity and SeDebugPrivilege to strip
/// anti-capture masks (WDA_MONITOR / WDA_EXCLUDEFROMCAPTURE) safely across desktop windows.
/// Non-intrusive enterprise implementation avoiding heuristic AV triggers.
/// </summary>
public static class WindowAffinityNeutralizer
{
    private const uint WDA_NONE = 0x00000000;

    private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
    private const uint TOKEN_QUERY = 0x0008;
    private const uint SE_PRIVILEGE_ENABLED = 0x00000002;

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const uint PROCESS_QUERY_INFORMATION = 0x0400;

    private static bool _debugPrivilegeAcquired = false;

    public static bool Neutralize(IntPtr hWnd, uint pid)
    {
        if (hWnd == IntPtr.Zero || !IsWindow(hWnd)) return false;

        EnsureDebugPrivilege();

        // 1. Direct call with SYSTEM token privileges
        try
        {
            if (SetWindowDisplayAffinity(hWnd, WDA_NONE))
            {
                Debug.WriteLine($"[+] System WindowAffinityNeutralizer: direct unmasking succeeded on hWnd=0x{hWnd:X8}");
                return true;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"System WindowAffinityNeutralizer direct exception: {ex.Message}");
        }

        // 2. Verification check: if already WDA_NONE, report success
        try
        {
            if (GetWindowDisplayAffinity(hWnd, out uint currentAffinity) && currentAffinity == WDA_NONE)
            {
                return true;
            }
        }
        catch { }

        if (pid == 0)
        {
            GetWindowThreadProcessId(hWnd, out pid);
        }
        if (pid == 0 || pid == (uint)Environment.ProcessId)
        {
            return false;
        }

        // 3. Process accessibility check
        IntPtr hProcess = IntPtr.Zero;
        try
        {
            hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (hProcess == IntPtr.Zero)
            {
                hProcess = OpenProcess(PROCESS_QUERY_INFORMATION, false, pid);
            }

            if (hProcess == IntPtr.Zero)
            {
                return false;
            }

            // Retry SetWindowDisplayAffinity after privilege elevation and process handle confirmation
            if (SetWindowDisplayAffinity(hWnd, WDA_NONE))
            {
                return true;
            }

            if (GetWindowDisplayAffinity(hWnd, out uint affAfter) && affAfter == WDA_NONE)
            {
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"System WindowAffinityNeutralizer error: {ex.Message}");
            return false;
        }
        finally
        {
            if (hProcess != IntPtr.Zero) CloseHandle(hProcess);
        }
    }

    private static void EnsureDebugPrivilege()
    {
        if (_debugPrivilegeAcquired) return;
        try
        {
            if (OpenProcessToken(Process.GetCurrentProcess().Handle, TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out IntPtr hToken))
            {
                try
                {
                    if (LookupPrivilegeValue(null, "SeDebugPrivilege", out LUID luid))
                    {
                        var tp = new TOKEN_PRIVILEGES
                        {
                            PrivilegeCount = 1,
                            Luid = luid,
                            Attributes = SE_PRIVILEGE_ENABLED
                        };
                        AdjustTokenPrivileges(hToken, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
                        _debugPrivilegeAcquired = true;
                    }
                }
                finally
                {
                    CloseHandle(hToken);
                }
            }
        }
        catch { }
    }

    #region Win32 P/Invoke
    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowDisplayAffinity(IntPtr hWnd, out uint pdwAffinity);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool LookupPrivilegeValue(string? lpSystemName, string lpName, out LUID lpLuid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(IntPtr TokenHandle, bool DisableAllPrivileges, ref TOKEN_PRIVILEGES NewState, uint BufferLength, IntPtr PreviousState, IntPtr ReturnLength);

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_PRIVILEGES
    {
        public uint PrivilegeCount;
        public LUID Luid;
        public uint Attributes;
    }
    #endregion
}
