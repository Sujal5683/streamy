using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EnterpriseRemote.Common.Protocol;
using EnterpriseRemote.Host.Service.Ipc;
using EnterpriseRemote.Host.UserAgent.Ipc;
using EnterpriseRemote.Host.UserAgent.Tray;
using Xunit;

namespace EnterpriseRemote.UnitTests;

public class IpcAndServiceTests
{
    [Fact]
    public void IpcFrame_SerializesAndDeserializes_WithHmacVerification()
    {
        byte[] hmacKey = RandomNumberGenerator.GetBytes(32);
        var frame = new IpcFrame
        {
            MessageType = IpcMessageType.PromptConsent,
            SequenceNumber = 42,
            Payload = Encoding.UTF8.GetBytes("Operator Alice requests access.")
        };

        byte[] raw = frame.Serialize(hmacKey);
        Assert.NotNull(raw);
        Assert.True(raw.Length >= 16 + 32);

        // Deserialization with correct HMAC key must succeed
        bool success = IpcFrame.TryDeserialize(raw, out var deserialized, hmacKey);
        Assert.True(success);
        Assert.NotNull(deserialized);
        Assert.Equal(frame.MessageType, deserialized.MessageType);
        Assert.Equal(frame.SequenceNumber, deserialized.SequenceNumber);
        Assert.Equal("Operator Alice requests access.", Encoding.UTF8.GetString(deserialized.Payload));

        // Deserialization with wrong HMAC key must fail (Tamper detection)
        byte[] wrongKey = RandomNumberGenerator.GetBytes(32);
        bool failedWrongKey = IpcFrame.TryDeserialize(raw, out _, wrongKey);
        Assert.False(failedWrongKey);
    }

    [Fact]
    public async Task NamedPipeIpc_ServerAndClient_ExchangeHandshakeAndFrames()
    {
        int testSessionId = 9988;
        using var server = new NamedPipeIpcServer(testSessionId);
        server.Start();

        var serverReceivedSource = new TaskCompletionSource<IpcFrame>();
        server.OnMessageReceived += frame => serverReceivedSource.TrySetResult(frame);

        using var client = new NamedPipeIpcClient(testSessionId);
        bool connected = await client.ConnectAsync(timeoutMilliseconds: 5000);
        Assert.True(connected);
        Assert.True(client.IsConnected);

        // Send message from Client to Server
        var clientFrame = new IpcFrame
        {
            MessageType = IpcMessageType.ConsentResult,
            SequenceNumber = 101,
            Payload = Encoding.UTF8.GetBytes("ApprovedByLocalUser")
        };

        await client.SendFrameAsync(clientFrame);

        var completedTask = await Task.WhenAny(serverReceivedSource.Task, Task.Delay(3000));
        Assert.Equal(serverReceivedSource.Task, completedTask);

        var receivedFrame = await serverReceivedSource.Task;
        Assert.Equal(IpcMessageType.ConsentResult, receivedFrame.MessageType);
        Assert.Equal(101u, receivedFrame.SequenceNumber);
        Assert.Equal("ApprovedByLocalUser", Encoding.UTF8.GetString(receivedFrame.Payload));
    }

    [Fact]
    public void SessionTrayIndicator_ManagesActiveStateAndEmergencyStop()
    {
        var tray = new SessionTrayIndicator();
        bool emergencyFired = false;
        tray.OnLocalEmergencyDisconnectRequested += () => emergencyFired = true;

        Assert.False(tray.IsInSession);
        Assert.Null(tray.OperatorName);

        // Start session
        tray.ShowActiveSession("Alice Smith");
        Assert.True(tray.IsInSession);
        Assert.Equal("Alice Smith", tray.OperatorName);

        // Prompt consent
        bool consent = tray.PromptLocalConsent("Alice Smith", "Disk Cleanup");
        Assert.True(consent);

        // Local Emergency Stop
        tray.TriggerEmergencyStop();
        Assert.True(emergencyFired);
        Assert.False(tray.IsInSession);
        Assert.Null(tray.OperatorName);
    }
}
