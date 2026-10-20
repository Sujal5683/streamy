using System;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using EnterpriseRemote.Common.Protocol;

namespace EnterpriseRemote.Host.Service.Ipc;

public sealed class NamedPipeIpcServer : IDisposable
{
    private readonly string _pipeName;
    private readonly byte[] _sessionHmacKey;
    private NamedPipeServerStream? _pipeServer;
    private CancellationTokenSource? _cts;
    private Task? _listenerTask;
    private bool _isConnected;

    public byte[] SessionHmacKey => _sessionHmacKey;
    public bool IsConnected => _isConnected;

    public event Action<IpcFrame>? OnMessageReceived;

    public NamedPipeIpcServer(int targetSessionId)
    {
        _pipeName = $"EnterpriseRemote_IPC_{targetSessionId}";
        _sessionHmacKey = RandomNumberGenerator.GetBytes(32); // 256-bit ephemeral key
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _listenerTask = Task.Run(() => ListenLoopAsync(_cts.Token));
    }

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // Create Named Pipe with restrictive security descriptor
                var pipeSecurity = new PipeSecurity();

                // Grant LocalSystem Full Access
                var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
                pipeSecurity.AddAccessRule(new PipeAccessRule(systemSid, PipeAccessRights.FullControl, AccessControlType.Allow));

                // Grant Administrators Full Access
                var adminSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
                pipeSecurity.AddAccessRule(new PipeAccessRule(adminSid, PipeAccessRights.FullControl, AccessControlType.Allow));

                // Grant Interactive Users Read/Write access (restricted by session ID naming)
                var interactiveSid = new SecurityIdentifier(WellKnownSidType.InteractiveSid, null);
                pipeSecurity.AddAccessRule(new PipeAccessRule(interactiveSid, PipeAccessRights.ReadWrite, AccessControlType.Allow));

                _pipeServer = NamedPipeServerStreamAcl.Create(
                    _pipeName,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Message,
                    PipeOptions.Asynchronous,
                    inBufferSize: 65536,
                    outBufferSize: 65536,
                    pipeSecurity);

                await _pipeServer.WaitForConnectionAsync(ct);
                _isConnected = true;

                // Send Handshake Response with session HMAC key
                var handshakeResp = new IpcFrame
                {
                    MessageType = IpcMessageType.HandshakeResponse,
                    SequenceNumber = 1,
                    Payload = _sessionHmacKey
                };
                await SendFrameAsync(handshakeResp, ct);

                // Read loop
                byte[] buffer = new byte[65536];
                while (_pipeServer.IsConnected && !ct.IsCancellationRequested)
                {
                    int bytesRead = await _pipeServer.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
                    if (bytesRead <= 0) break;

                    byte[] messageBytes = new byte[bytesRead];
                    Buffer.BlockCopy(buffer, 0, messageBytes, 0, bytesRead);

                    if (IpcFrame.TryDeserialize(messageBytes, out var frame, _sessionHmacKey))
                    {
                        if (frame != null)
                        {
                            OnMessageReceived?.Invoke(frame);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Pipe server exception: {ex.Message}");
            }
            finally
            {
                _isConnected = false;
                _pipeServer?.Dispose();
                _pipeServer = null;
            }
        }
    }

    public async Task SendFrameAsync(IpcFrame frame, CancellationToken ct = default)
    {
        if (_pipeServer == null || !_pipeServer.IsConnected) return;
        byte[] serialized = frame.Serialize(_sessionHmacKey);
        await _pipeServer.WriteAsync(serialized.AsMemory(0, serialized.Length), ct);
        await _pipeServer.FlushAsync(ct);
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _pipeServer?.Dispose();
        _cts?.Dispose();
    }
}
