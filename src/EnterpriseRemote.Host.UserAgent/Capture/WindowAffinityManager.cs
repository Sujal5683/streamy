using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace EnterpriseRemote.Host.UserAgent.Capture;

/// <summary>
/// Proactive detector, monitor, and neutralizer for windows protected with SetWindowDisplayAffinity
/// (WDA_MONITOR = 0x1, WDA_EXCLUDEFROMCAPTURE = 0x11).
/// Supports multi-architecture injection (x64 and x86/WoW64), child window tree inspection,
/// event-driven win-event hooking, and inline anti-reapplication patching.
/// Unmasks Safe Exam Browser, Chrome, Edge, WebView2, and custom organization secure browsers.
/// </summary>
public sealed class WindowAffinityManager : IDisposable
{
    private const uint WDA_NONE = 0x00000000;
    private const uint WDA_MONITOR = 0x00000001;
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

    private readonly List<IntPtr> _activeProtectedWindows = new();
    private readonly HashSet<uint> _permanentlyNeutralizedPids = new();
    private readonly object _lock = new();
    private System.Threading.Timer? _scanTimer;
    private IntPtr _hWinEventHook = IntPtr.Zero;
    private WinEventDelegate? _winEventProc;
    private bool _isDisposed;

    public IReadOnlyList<IntPtr> ActiveProtectedWindows
    {
        get
        {
            lock (_lock)
            {
                return _activeProtectedWindows.ToArray();
            }
        }
    }

    public event Action<IntPtr, string, string, uint>? OnWindowProtectedDetected;
    public event Action<IntPtr, string, bool>? OnAffinityNeutralized;
    public Action<IntPtr, uint>? RequestElevatedNeutralization { get; set; }

    public void StartMonitoring(int intervalMs = 400)
    {
        // 1. High-frequency scan timer (sub-second reactivity)
        _scanTimer = new System.Threading.Timer(_ =>
        {
            try
            {
                ScanAndNeutralize();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Affinity scanner notice: {ex.Message}");
            }
        }, null, 100, intervalMs);

        // 2. Install WinEvent hook for instant reactivity upon window creation/show
        try
        {
            _winEventProc = new WinEventDelegate(WinEventProcCallback);
            _hWinEventHook = SetWinEventHook(
                EVENT_OBJECT_SHOW,
                EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero,
                _winEventProc,
                0,
                0,
                WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WinEventHook setup notice: {ex.Message}");
        }
    }

    private void WinEventProcCallback(IntPtr hWinEventHook, uint eventType, IntPtr hWnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (hWnd == IntPtr.Zero || idObject != 0 || idChild != 0) return;
        try
        {
            if (GetWindowDisplayAffinity(hWnd, out uint affinity) && affinity != WDA_NONE)
            {
                InspectAndNeutralizeWindow(hWnd, affinity, new List<IntPtr>());
            }
        }
        catch { }
    }

    /// <summary>
    /// Scans all top-level windows and their child window hierarchies for display affinity masks,
    /// neutralizing them immediately.
    /// </summary>
    public List<IntPtr> ScanAndNeutralize()
    {
        var foundProtected = new List<IntPtr>();

        EnumWindows((hWnd, lParam) =>
        {
            if (!IsWindow(hWnd) || !IsWindowVisible(hWnd) || IsIconic(hWnd))
            {
                return true;
            }

            // Inspect top-level window
            if (GetWindowDisplayAffinity(hWnd, out uint affinity) && affinity != WDA_NONE)
            {
                InspectAndNeutralizeWindow(hWnd, affinity, foundProtected);
            }

            // Inspect nested child windows (e.g. CEF, Chromium, WebView2, and WPF render hosts)
            EnumChildWindows(hWnd, (hChild, childParam) =>
            {
                if (IsWindow(hChild) && IsWindowVisible(hChild))
                {
                    if (GetWindowDisplayAffinity(hChild, out uint childAffinity) && childAffinity != WDA_NONE)
                    {
                        InspectAndNeutralizeWindow(hChild, childAffinity, foundProtected);
                    }
                }
                return true;
            }, IntPtr.Zero);

            return true;
        }, IntPtr.Zero);

        lock (_lock)
        {
            _activeProtectedWindows.Clear();
            _activeProtectedWindows.AddRange(foundProtected);
        }

        return foundProtected;
    }

    private void InspectAndNeutralizeWindow(IntPtr hWnd, uint affinity, List<IntPtr> accumulator)
    {
        accumulator.Add(hWnd);
        string title = GetWindowTitle(hWnd);
        string procName = GetProcessName(hWnd, out uint pid);

        OnWindowProtectedDetected?.Invoke(hWnd, title, procName, affinity);
        Console.WriteLine($"[!] Detected anti-streaming window protection on '{procName}' (PID={pid}): hWnd=0x{hWnd:X8}, Title='{title}', Affinity=0x{affinity:X8}");

        bool neutralized = NeutralizeWindowAffinity(hWnd, pid);
        if (!neutralized && RequestElevatedNeutralization != null)
        {
            RequestElevatedNeutralization.Invoke(hWnd, pid);
        }
        OnAffinityNeutralized?.Invoke(hWnd, procName, neutralized);

        if (neutralized)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[+] Successfully neutralized screen capture blocking on '{procName}' (hWnd=0x{hWnd:X8}). Visuals unmasked.");
            Console.ResetColor();
        }
    }

    /// <summary>
    /// Neutralizes the display affinity of a specific window.
    /// Employs direct call, multi-arch remote thread execution (x64 and x86),
    /// and permanent anti-reapplication in-memory patching.
    /// </summary>
    public static bool NeutralizeWindowAffinity(IntPtr hWnd, uint pid = 0)
    {
        if (hWnd == IntPtr.Zero || !IsWindow(hWnd)) return false;

        // 1. Direct call first (valid if running in same process or supported)
        if (SetWindowDisplayAffinity(hWnd, WDA_NONE))
        {
            return true;
        }

        if (pid == 0)
        {
            GetWindowThreadProcessId(hWnd, out pid);
        }
        if (pid == 0 || pid == (uint)Environment.ProcessId)
        {
            return false;
        }

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

            bool stripped = false;
            if (isWow64)
            {
                // Target is 32-bit (x86) process on 64-bit OS
                stripped = NeutralizeWow64Process(hProcess, hWnd, pid);
            }
            else
            {
                // Target is 64-bit (x64) process
                stripped = NeutralizeX64Process(hProcess, hWnd);
            }

            // Verify if affinity was neutralized
            if (GetWindowDisplayAffinity(hWnd, out uint newAffinity) && newAffinity == WDA_NONE)
            {
                return true;
            }

            return stripped;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"NeutralizeWindowAffinity error: {ex.Message}");
            return false;
        }
        finally
        {
            if (hProcess != IntPtr.Zero) CloseHandle(hProcess);
        }
    }

    private static bool NeutralizeX64Process(IntPtr hProcess, IntPtr hWnd)
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

            // Anti-reapplication patch: Patch user32!SetWindowDisplayAffinity in target process
            // so subsequent calls from the browser's watchdog timers return TRUE without applying masks:
            // B8 01 00 00 00  (mov eax, 1)
            // C3              (ret)
            ApplyInlineNoOpPatch(hProcess, pFnSetAffinity, new byte[] { 0xB8, 0x01, 0x00, 0x00, 0x00, 0xC3 });

            return true;
        }
        finally
        {
            if (hThread != IntPtr.Zero) CloseHandle(hThread);
            if (pRemoteStub != IntPtr.Zero) VirtualFreeEx(hProcess, pRemoteStub, 0, MEM_RELEASE);
        }
    }

    private static bool NeutralizeWow64Process(IntPtr hProcess, IntPtr hWnd, uint pid)
    {
        // For 32-bit processes, retrieve 32-bit user32.dll export offset and patch
        try
        {
            IntPtr pFn32 = GetWow64FunctionAddress(pid, "user32.dll", "SetWindowDisplayAffinity");
            if (pFn32 == IntPtr.Zero)
            {
                // Fallback: standard user32 resolution
                IntPtr hUser32 = GetModuleHandle("user32.dll");
                if (hUser32 != IntPtr.Zero) pFn32 = GetProcAddress(hUser32, "SetWindowDisplayAffinity");
            }

            if (pFn32 != IntPtr.Zero)
            {
                // 32-bit stdcall stub:
                // 55                push ebp
                // 89 E5             mov ebp, esp
                // 6A 00             push 0 (WDA_NONE)
                // 68 <4-byte hWnd>  push hWnd
                // B8 <4-byte pFn>   mov eax, pFn
                // FF D0             call eax
                // 89 EC             mov esp, ebp
                // 5D                pop ebp
                // C2 04 00          ret 4
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

                // Inline patch for 32-bit: mov eax, 1; ret 8 (stdcall clean 2 params)
                ApplyInlineNoOpPatch(hProcess, pFn32, new byte[] { 0xB8, 0x01, 0x00, 0x00, 0x00, 0xC2, 0x08, 0x00 });
                return true;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"NeutralizeWow64Process notice: {ex.Message}");
        }

        return false;
    }

    private static void ApplyInlineNoOpPatch(IntPtr hProcess, IntPtr pTargetAddress, byte[] patchBytes)
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
            // Calculate export RVA from SysWOW64 DLL on disk
            string sysWow64Dir = Environment.GetFolderPath(Environment.SpecialFolder.SystemX86);
            string dllPath = Path.Combine(sysWow64Dir, moduleName);
            if (!File.Exists(dllPath)) return IntPtr.Zero;

            IntPtr hLocal = LoadLibraryEx(dllPath, IntPtr.Zero, 0x00000001); // DONT_RESOLVE_DLL_REFERENCES
            if (hLocal == IntPtr.Zero) return IntPtr.Zero;

            try
            {
                IntPtr pLocalFn = GetProcAddress(hLocal, functionName);
                if (pLocalFn == IntPtr.Zero) return IntPtr.Zero;
                long rva = pLocalFn.ToInt64() - hLocal.ToInt64();

                // Find module base in target process
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

    private static string GetWindowTitle(IntPtr hWnd)
    {
        var sb = new StringBuilder(256);
        GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private static string GetProcessName(IntPtr hWnd, out uint pid)
    {
        GetWindowThreadProcessId(hWnd, out pid);
        if (pid == 0) return "Unknown";
        try
        {
            using var proc = Process.GetProcessById((int)pid);
            return proc.ProcessName;
        }
        catch
        {
            return $"PID_{pid}";
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _scanTimer?.Dispose();
        if (_hWinEventHook != IntPtr.Zero)
        {
            UnhookWinEvent(_hWinEventHook);
            _hWinEventHook = IntPtr.Zero;
        }
    }

    #region Win32 P/Invoke
    private const uint MEM_COMMIT = 0x00001000;
    private const uint MEM_RESERVE = 0x00002000;
    private const uint MEM_RELEASE = 0x00008000;
    private const uint PAGE_EXECUTE_READWRITE = 0x40;

    private const uint PROCESS_VM_OPERATION = 0x0008;
    private const uint PROCESS_VM_READ = 0x0010;
    private const uint PROCESS_VM_WRITE = 0x0020;
    private const uint PROCESS_CREATE_THREAD = 0x0002;
    private const uint PROCESS_QUERY_INFORMATION = 0x0400;

    private const uint EVENT_OBJECT_SHOW = 0x8002;
    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

    private const uint TH32CS_SNAPMODULE = 0x00000008;
    private const uint TH32CS_SNAPMODULE32 = 0x00000010;

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hWnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowDisplayAffinity(IntPtr hWnd, out uint pdwAffinity);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

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

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr LoadLibrary(string lpLibFileName);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr LoadLibraryEx(string lpLibFileName, IntPtr hFile, uint dwFlags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeLibrary(IntPtr hModule);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern bool Module32First(IntPtr hSnapshot, ref MODULEENTRY32 lpme);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern bool Module32Next(IntPtr hSnapshot, ref MODULEENTRY32 lpme);

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
