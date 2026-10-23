using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace EnterpriseRemote.Host.Service.Policy;

/// <summary>
/// Session 0 SYSTEM-level Window Affinity Neutralizer.
/// Operates with NT AUTHORITY\SYSTEM integrity and SeDebugPrivilege to strip
/// anti-capture masks (WDA_MONITOR / WDA_EXCLUDEFROMCAPTURE) across all processes,
/// supporting both x64 and x86/WoW64 binaries with permanent anti-reapplication patches.
/// </summary>
public static class WindowAffinityNeutralizer
{
    private const uint WDA_NONE = 0x00000000;

    private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
    private const uint TOKEN_QUERY = 0x0008;
    private const uint SE_PRIVILEGE_ENABLED = 0x00000002;

    private const uint PROCESS_CREATE_THREAD = 0x0002;
    private const uint PROCESS_VM_OPERATION = 0x0008;
    private const uint PROCESS_VM_WRITE = 0x0020;
    private const uint PROCESS_VM_READ = 0x0010;
    private const uint PROCESS_QUERY_INFORMATION = 0x0400;

    private const uint MEM_COMMIT = 0x1000;
    private const uint MEM_RESERVE = 0x2000;
    private const uint MEM_RELEASE = 0x8000;
    private const uint PAGE_EXECUTE_READWRITE = 0x40;

    private const uint TH32CS_SNAPMODULE = 0x00000008;
    private const uint TH32CS_SNAPMODULE32 = 0x00000010;

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

        if (pid == 0)
        {
            GetWindowThreadProcessId(hWnd, out pid);
        }
        if (pid == 0 || pid == (uint)Environment.ProcessId)
        {
            return false;
        }

        // 2. In-process execution via remote thread inside the target process with SYSTEM rights
        IntPtr hProcess = IntPtr.Zero;
        try
        {
            hProcess = OpenProcess(
                PROCESS_CREATE_THREAD | PROCESS_VM_OPERATION | PROCESS_VM_WRITE | PROCESS_VM_READ | PROCESS_QUERY_INFORMATION,
                false,
                pid);

            if (hProcess == IntPtr.Zero)
            {
                return false;
            }

            bool isWow64 = false;
            if (Environment.Is64BitOperatingSystem)
            {
                IsWow64Process(hProcess, out isWow64);
            }

            if (isWow64)
            {
                return NeutralizeWow64(hProcess, hWnd, pid);
            }
            else
            {
                return NeutralizeX64(hProcess, hWnd);
            }
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

    private static bool NeutralizeX64(IntPtr hProcess, IntPtr hWnd)
    {
        IntPtr hUser32 = GetModuleHandle("user32.dll");
        if (hUser32 == IntPtr.Zero) hUser32 = LoadLibrary("user32.dll");
        if (hUser32 == IntPtr.Zero) return false;

        IntPtr pFnSetAffinity = GetProcAddress(hUser32, "SetWindowDisplayAffinity");
        if (pFnSetAffinity == IntPtr.Zero) return false;

        IntPtr pRemoteStub = IntPtr.Zero;
        IntPtr hThread = IntPtr.Zero;

        try
        {
            // x64 fastcall stub:
            // 48 83 EC 28             sub rsp, 40
            // 48 B9 <8-byte hWnd>     mov rcx, hWnd
            // 31 D2                   xor edx, edx (WDA_NONE)
            // 48 B8 <8-byte pFn>      mov rax, pFnSetAffinity
            // FF D0                   call rax
            // 48 83 C4 28             add rsp, 40
            // C3                      ret
            byte[] stub = new byte[]
            {
                0x48, 0x83, 0xEC, 0x28,                         // sub rsp, 40
                0x48, 0xB9, 0, 0, 0, 0, 0, 0, 0, 0,             // mov rcx, hWnd
                0x31, 0xD2,                                     // xor edx, edx
                0x48, 0xB8, 0, 0, 0, 0, 0, 0, 0, 0,             // mov rax, pFn
                0xFF, 0xD0,                                     // call rax
                0x48, 0x83, 0xC4, 0x28,                         // add rsp, 40
                0xC3                                            // ret
            };

            byte[] hwndBytes = BitConverter.GetBytes(hWnd.ToInt64());
            Array.Copy(hwndBytes, 0, stub, 6, 8);

            byte[] pfnBytes = BitConverter.GetBytes(pFnSetAffinity.ToInt64());
            Array.Copy(pfnBytes, 0, stub, 18, 8);

            pRemoteStub = VirtualAllocEx(hProcess, IntPtr.Zero, (uint)stub.Length, MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE);
            if (pRemoteStub != IntPtr.Zero && WriteProcessMemory(hProcess, pRemoteStub, stub, (uint)stub.Length, out _))
            {
                hThread = CreateRemoteThread(hProcess, IntPtr.Zero, 0, pRemoteStub, IntPtr.Zero, 0, out _);
                if (hThread != IntPtr.Zero)
                {
                    WaitForSingleObject(hThread, 500);
                }
            }

            // Apply permanent inline anti-reapplication patch
            ApplyInlinePatch(hProcess, pFnSetAffinity, new byte[] { 0xB8, 0x01, 0x00, 0x00, 0x00, 0xC3 });
            return true;
        }
        finally
        {
            if (hThread != IntPtr.Zero) CloseHandle(hThread);
            if (pRemoteStub != IntPtr.Zero) VirtualFreeEx(hProcess, pRemoteStub, 0, MEM_RELEASE);
        }
    }

    private static bool NeutralizeWow64(IntPtr hProcess, IntPtr hWnd, uint pid)
    {
        try
        {
            IntPtr pFn32 = GetWow64FunctionAddress(pid, "user32.dll", "SetWindowDisplayAffinity");
            if (pFn32 == IntPtr.Zero)
            {
                IntPtr hUser32 = GetModuleHandle("user32.dll");
                if (hUser32 != IntPtr.Zero) pFn32 = GetProcAddress(hUser32, "SetWindowDisplayAffinity");
            }

            if (pFn32 != IntPtr.Zero)
            {
                byte[] stub32 = new byte[]
                {
                    0x55,
                    0x89, 0xE5,
                    0x6A, 0x00,
                    0x68, 0, 0, 0, 0,
                    0xB8, 0, 0, 0, 0,
                    0xFF, 0xD0,
                    0x89, 0xEC,
                    0x5D,
                    0xC2, 0x04, 0x00
                };

                byte[] hwndBytes = BitConverter.GetBytes((int)hWnd.ToInt64());
                Array.Copy(hwndBytes, 0, stub32, 5, 4);

                byte[] pfnBytes = BitConverter.GetBytes((int)pFn32.ToInt64());
                Array.Copy(pfnBytes, 0, stub32, 10, 4);

                IntPtr pRemoteStub = VirtualAllocEx(hProcess, IntPtr.Zero, (uint)stub32.Length, MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE);
                if (pRemoteStub != IntPtr.Zero && WriteProcessMemory(hProcess, pRemoteStub, stub32, (uint)stub32.Length, out _))
                {
                    IntPtr hThread = CreateRemoteThread(hProcess, IntPtr.Zero, 0, pRemoteStub, IntPtr.Zero, 0, out _);
                    if (hThread != IntPtr.Zero)
                    {
                        WaitForSingleObject(hThread, 500);
                        CloseHandle(hThread);
                    }
                    VirtualFreeEx(hProcess, pRemoteStub, 0, MEM_RELEASE);
                }

                // 32-bit inline patch: mov eax, 1; ret 8
                ApplyInlinePatch(hProcess, pFn32, new byte[] { 0xB8, 0x01, 0x00, 0x00, 0x00, 0xC2, 0x08, 0x00 });
                return true;
            }
        }
        catch { }

        return false;
    }

    private static void ApplyInlinePatch(IntPtr hProcess, IntPtr pTargetAddress, byte[] patchBytes)
    {
        try
        {
            if (VirtualProtectEx(hProcess, pTargetAddress, (uint)patchBytes.Length, PAGE_EXECUTE_READWRITE, out uint oldProtect))
            {
                WriteProcessMemory(hProcess, pTargetAddress, patchBytes, (uint)patchBytes.Length, out _);
                VirtualProtectEx(hProcess, pTargetAddress, (uint)patchBytes.Length, oldProtect, out _);
                FlushInstructionCache(hProcess, pTargetAddress, (uint)patchBytes.Length);
            }
        }
        catch { }
    }

    private static IntPtr GetWow64FunctionAddress(uint pid, string moduleName, string functionName)
    {
        try
        {
            string sysWow64Dir = Environment.GetFolderPath(Environment.SpecialFolder.SystemX86);
            string dllPath = Path.Combine(sysWow64Dir, moduleName);
            if (!File.Exists(dllPath)) return IntPtr.Zero;

            IntPtr hLocal = LoadLibraryEx(dllPath, IntPtr.Zero, 0x00000001);
            if (hLocal == IntPtr.Zero) return IntPtr.Zero;

            try
            {
                IntPtr pLocalFn = GetProcAddress(hLocal, functionName);
                if (pLocalFn == IntPtr.Zero) return IntPtr.Zero;
                long rva = pLocalFn.ToInt64() - hLocal.ToInt64();

                IntPtr hSnapshot = CreateToolhelp32Snapshot(TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, pid);
                if (hSnapshot == IntPtr.Zero || hSnapshot == new IntPtr(-1)) return IntPtr.Zero;

                try
                {
                    var entry = new MODULEENTRY32 { dwSize = (uint)Marshal.SizeOf<MODULEENTRY32>() };
                    if (Module32First(hSnapshot, ref entry))
                    {
                        do
                        {
                            if (string.Equals(entry.szModule, moduleName, StringComparison.OrdinalIgnoreCase))
                            {
                                return IntPtr.Add(entry.modBaseAddr, (int)rva);
                            }
                        } while (Module32Next(hSnapshot, ref entry));
                    }
                }
                finally
                {
                    CloseHandle(hSnapshot);
                }
            }
            finally
            {
                FreeLibrary(hLocal);
            }
        }
        catch { }

        return IntPtr.Zero;
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

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool IsWow64Process(IntPtr hProcess, out bool wow64Process);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint flAllocationType, uint flProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint dwFreeType);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualProtectEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint flNewProtect, out uint lpflOldProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, uint nSize, out IntPtr lpNumberOfBytesWritten);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FlushInstructionCache(IntPtr hProcess, IntPtr lpBaseAddress, uint dwSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateRemoteThread(IntPtr hProcess, IntPtr lpThreadAttributes, uint dwStackSize, IntPtr lpStartAddress, IntPtr lpParameter, uint dwCreationFlags, out uint lpThreadId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LoadLibrary(string lpLibFileName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LoadLibraryEx(string lpLibFileName, IntPtr hFile, uint dwFlags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeLibrary(IntPtr hModule);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern bool Module32First(IntPtr hSnapshot, ref MODULEENTRY32 lpme);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern bool Module32Next(IntPtr hSnapshot, ref MODULEENTRY32 lpme);

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

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct MODULEENTRY32
    {
        public uint dwSize;
        public uint th32ModuleID;
        public uint th32ProcessID;
        public uint GlblcntUsage;
        public uint ProccntUsage;
        public IntPtr modBaseAddr;
        public uint modBaseSize;
        public IntPtr hModule;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szModule;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExePath;
    }
    #endregion
}
