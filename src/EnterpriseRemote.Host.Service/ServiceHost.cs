using System;
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
        if (_activeSessionId < 0) _activeSessionId = 1;

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

    #region Win32 WTS Declarations
    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();
    #endregion
}
