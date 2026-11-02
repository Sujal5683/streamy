using System;
using System.Runtime.InteropServices;

namespace EnterpriseRemote.Common.Security;

/// <summary>
/// Enables high-privilege Windows security tokens (SeDebugPrivilege, SeTcbPrivilege, etc.)
/// allowing the process to attach to secure desktops, access cross-session WindowStations,
/// and bypass low-level restrictions.
/// </summary>
public static class SecurityPrivilegeManager
{
    private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
    private const uint TOKEN_QUERY = 0x0008;
    private const uint SE_PRIVILEGE_ENABLED = 0x00000002;

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

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool LookupPrivilegeValue(string? lpSystemName, string lpName, out LUID lpLuid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(IntPtr TokenHandle, bool DisableAllPrivileges, ref TOKEN_PRIVILEGES NewState, uint BufferLength, IntPtr PreviousState, IntPtr ReturnLength);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    public static void EnableAllProcessPrivileges()
    {
        EnablePrivilege("SeDebugPrivilege");
        EnablePrivilege("SeIncreaseQuotaPrivilege");
        EnablePrivilege("SeSecurityPrivilege");
        EnablePrivilege("SeTakeOwnershipPrivilege");
        EnablePrivilege("SeTcbPrivilege");
        EnablePrivilege("SeImpersonatePrivilege");
    }

    public static bool EnablePrivilege(string privilegeName)
    {
        try
        {
            if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out IntPtr hToken))
            {
                return false;
            }

            try
            {
                if (!LookupPrivilegeValue(null, privilegeName, out LUID luid))
                {
                    return false;
                }

                var tp = new TOKEN_PRIVILEGES
                {
                    PrivilegeCount = 1,
                    Luid = luid,
                    Attributes = SE_PRIVILEGE_ENABLED
                };

                return AdjustTokenPrivileges(hToken, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
            }
            finally
            {
                CloseHandle(hToken);
            }
        }
        catch
        {
            return false;
        }
    }
}

