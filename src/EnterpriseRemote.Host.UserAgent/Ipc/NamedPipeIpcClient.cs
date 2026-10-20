using System;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using EnterpriseRemote.Common.Protocol;

namespace EnterpriseRemote.Host.UserAgent.Ipc;

public sealed class NamedPipeIpcClient : IDisposable
{
    private readonly string _pipeName;
    private NamedPipeClientStream? _pipeClient;
    private byte[]? _sessionHmacKey;
    private CancellationTokenSource? _cts;
    private Task? _readTask;

    public bool IsConnected => _pipeClient?.IsConnected ?? false;
    public event Action<IpcFrame>? OnMessageReceived;

    public NamedPipeIpcClient(int sessionId)
    {
        _pipeName = $"EnterpriseRemote_IPC_{sessionId}";
    }

    public async Task<bool> ConnectAsync(int timeoutMilliseconds = 5000, CancellationToken ct = default)
    {
        try
        {
            _pipeClient = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await _pipeClient.ConnectAsync(timeoutMilliseconds, ct);
            _pipeClient.ReadMode = PipeTransmissionMode.Message;

            // Read initial handshake response to obtain session HMAC key
            byte[] handshakeBuffer = new byte[1024];
            int read = await _pipeClient.ReadAsync(handshakeBuffer.AsMemory(0, handshakeBuffer.Length), ct);
            if (read <= 0) return false;

            byte[] handshakeBytes = new byte[read];
            Buffer.BlockCopy(handshakeBuffer, 0, handshakeBytes, 0, read);

            if (!IpcFrame.TryDeserialize(handshakeBytes, out var frame) || frame == null)
            {
                return false;
            }

            if (frame.MessageType != IpcMessageType.HandshakeResponse || frame.Payload.Length != 32)
            {
                return false;
            }

            _sessionHmacKey = frame.Payload;

            // Start continuous reader loop
            _cts = new CancellationTokenSource();
            _readTask = Task.Run(() => ReadLoopAsync(_cts.Token));
            return true;
        }
        catch
        {
            Dispose();
            return false;
        }
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        byte[] buffer = new byte[65536];
        while (_pipeClient != null && _pipeClient.IsConnected && !ct.IsCancellationRequested)
        {
            try
            {
                int bytesRead = await _pipeClient.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
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
            catch
            {
                break;
            }
        }
    }

    public async Task SendFrameAsync(IpcFrame frame, CancellationToken ct = default)
    {
        if (_pipeClient == null || !_pipeClient.IsConnected || _sessionHmacKey == null) return;
        byte[] serialized = frame.Serialize(_sessionHmacKey);
        await _pipeClient.WriteAsync(serialized.AsMemory(0, serialized.Length), ct);
        await _pipeClient.FlushAsync(ct);
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _pipeClient?.Dispose();
        _cts?.Dispose();
    }
}
