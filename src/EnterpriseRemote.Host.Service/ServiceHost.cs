using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using EnterpriseRemote.Common.Models;
using EnterpriseRemote.Common.Protocol;
using EnterpriseRemote.Host.Service.Input;
using EnterpriseRemote.Host.Service.Ipc;

namespace EnterpriseRemote.Host.Service;

public sealed class ServiceHost : IDisposable
{
    private NamedPipeIpcServer? _ipcServer;
    private int _activeSessionId = -1;
    private bool _isRunning;
    private CancellationTokenSource? _cts;

    public int ActiveSessionId => _activeSessionId;
    public bool IsRunning => _isRunning;

    public void Start()
    {
        _isRunning = true;
        _cts = new CancellationTokenSource();

        // 1. Detect current active console session
        _activeSessionId = (int)WTSGetActiveConsoleSessionId();
        if (_activeSessionId <= 0) _activeSessionId = 1;

        // 2. Start Named Pipe Server for this session
        _ipcServer = new NamedPipeIpcServer(_activeSessionId);
        _ipcServer.OnMessageReceived += HandleIpcMessage;
        _ipcServer.Start();
    }

    private void HandleIpcMessage(IpcFrame frame)
    {
        switch (frame.MessageType)
        {
            case IpcMessageType.ConsentResult:
                // User approved / denied session
                break;
            case IpcMessageType.SessionStateUpdate:
                // Telemetry / frame metrics
                break;
            case IpcMessageType.TerminateSession:
                // User clicked Emergency Stop in local tray
                break;
            case IpcMessageType.InjectInputElevated:
                SystemInputInjector.InjectInputPacket(frame.Payload);
                break;
            case IpcMessageType.NeutralizeAffinity:
                if (frame.Payload != null && frame.Payload.Length >= 12)
                {
                    long rawHwnd = BitConverter.ToInt64(frame.Payload, 0);
                    uint targetPid = BitConverter.ToUInt32(frame.Payload, 8);
                    Policy.WindowAffinityNeutralizer.Neutralize((IntPtr)rawHwnd, targetPid);
                }
                break;
            case IpcMessageType.SwitchDesktopElevated:
                if (frame.Payload != null && frame.Payload.Length > 0)
                {
                    string targetDesktop = System.Text.Encoding.UTF8.GetString(frame.Payload);
                    SwitchDesktopElevated(targetDesktop);
                }
                break;
            case IpcMessageType.GrantDesktopAccess:
                GrantDesktopAccessToCurrentSession();
                break;
        }
    }

    private void SwitchDesktopElevated(string desktopName)
    {
        if (string.IsNullOrWhiteSpace(desktopName) || desktopName.Equals("Auto", StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            IntPtr hWinSta = OpenWindowStation("WinSta0", false, WINSTA_ALL_ACCESS);
            if (hWinSta != IntPtr.Zero)
            {
                SetProcessWindowStation(hWinSta);
                CloseWindowStation(hWinSta);
            }

            IntPtr hDesk = OpenDesktop(desktopName, 0, false, DESKTOP_ALL_ACCESS | DESKTOP_SWITCHDESKTOP);
            if (hDesk == IntPtr.Zero)
            {
                hDesk = OpenDesktop(desktopName, 0, false, DESKTOP_READOBJECTS | DESKTOP_SWITCHDESKTOP);
            }

            if (hDesk != IntPtr.Zero)
            {
                try
                {
                    SwitchDesktop(hDesk);
                }
                finally
                {
                    CloseDesktop(hDesk);
                }
            }

            // Immediately grant DACL access so user agent can read and capture it
            GrantDesktopAccessToCurrentSession();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ServiceHost] SwitchDesktopElevated notice: {ex.Message}");
        }
    }

    private void GrantDesktopAccessToCurrentSession()
    {
        try
        {
            IntPtr hDesk = OpenInputDesktop(0, false, WRITE_DAC | READ_CONTROL);
            if (hDesk != IntPtr.Zero)
            {
                try
                {
                    // A NULL DACL grants full unrestricted access to interactive processes
                    SetSecurityInfo(hDesk, SE_WINDOW_OBJECT, DACL_SECURITY_INFORMATION, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                }
                finally
                {
                    CloseDesktop(hDesk);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ServiceHost] GrantDesktopAccess notice: {ex.Message}");
        }
    }

    public async Task TriggerEmergencyDisableAsync()
    {
        if (_ipcServer != null && _ipcServer.IsConnected)
        {
            var terminateFrame = new IpcFrame
            {
                MessageType = IpcMessageType.TerminateSession,
                SequenceNumber = 9999,
                Payload = System.Text.Encoding.UTF8.GetBytes("EmergencyDisable")
            };
            await _ipcServer.SendFrameAsync(terminateFrame);
        }

        Dispose();
    }

    public void Dispose()
    {
        _isRunning = false;
        _cts?.Cancel();
        _ipcServer?.Dispose();
        _cts?.Dispose();
    }

    #region Win32 Declarations
    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();

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
    private static extern bool CloseDesktop(IntPtr hDesktop);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern uint SetSecurityInfo(
        IntPtr handle,
        int ObjectType,
        uint SecurityInfo,
        IntPtr psidOwner,
        IntPtr psidGroup,
        IntPtr pDacl,
        IntPtr pSacl);

    private const uint WINSTA_ALL_ACCESS = 0x037F;
    private const uint DESKTOP_ALL_ACCESS = 0x01FF;
    private const uint DESKTOP_SWITCHDESKTOP = 0x0100;
    private const uint DESKTOP_READOBJECTS = 0x0001;
    private const int SE_WINDOW_OBJECT = 9;
    private const uint DACL_SECURITY_INFORMATION = 4;
    private const uint WRITE_DAC = 0x00040000;
    private const uint READ_CONTROL = 0x00020000;
    #endregion
}
