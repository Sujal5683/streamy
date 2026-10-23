using System;
using System.Threading.Channels;
using System.Threading.Tasks;
using EnterpriseRemote.Common.Protocol;
using Xunit;

namespace EnterpriseRemote.UnitTests;

public class ZeroBufferbloatAndStreamConfigTests
{
    [Fact]
    public void DataChannelProtocol_StreamConfigPacket_SerializesAndDeserializesAccurately()
    {
        var original = new StreamConfigPacket(
            TargetFps: 60,
            Quality: 75,
            MaxHeight: 1080,
            LowLatencyMode: 1,
            TimestampMillis: 1771482000555UL);

        byte[] raw = DataChannelProtocol.SerializeStreamConfig(original);
        Assert.Equal(14, raw.Length);
        Assert.Equal((byte)InputEventType.StreamConfig, raw[0]);

        bool success = DataChannelProtocol.TryDeserializeStreamConfig(raw, out var deserialized);
        Assert.True(success);
        Assert.Equal(original.TargetFps, deserialized.TargetFps);
        Assert.Equal(original.Quality, deserialized.Quality);
        Assert.Equal(original.MaxHeight, deserialized.MaxHeight);
        Assert.Equal(original.LowLatencyMode, deserialized.LowLatencyMode);
        Assert.Equal(original.TimestampMillis, deserialized.TimestampMillis);
    }

    [Theory]
    [InlineData(15, 50, 720)]
    [InlineData(30, 70, 1080)]
    [InlineData(45, 72, 0)]
    [InlineData(60, 85, 0)]
    public void DataChannelProtocol_StreamConfigPacket_SupportsVariousProfiles(byte fps, byte quality, ushort maxHeight)
    {
        var original = new StreamConfigPacket(
            TargetFps: fps,
            Quality: quality,
            MaxHeight: maxHeight,
            LowLatencyMode: 1,
            TimestampMillis: (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        byte[] raw = DataChannelProtocol.SerializeStreamConfig(original);
        bool success = DataChannelProtocol.TryDeserializeStreamConfig(raw, out var deserialized);

        Assert.True(success);
        Assert.Equal(fps, deserialized.TargetFps);
        Assert.Equal(quality, deserialized.Quality);
        Assert.Equal(maxHeight, deserialized.MaxHeight);
    }

    [Fact]
    public void DataChannelProtocol_StreamConfigPacket_RejectsInvalidPayloads()
    {
        byte[] shortBuffer = new byte[10];
        Assert.False(DataChannelProtocol.TryDeserializeStreamConfig(shortBuffer, out _));

        byte[] invalidHeader = new byte[14];
        invalidHeader[0] = 0x99; // invalid event type
        Assert.False(DataChannelProtocol.TryDeserializeStreamConfig(invalidHeader, out _));
    }

    [Fact]
    public void DataChannelProtocol_LatencyPingPong_AccuratelyDifferentiates()
    {
        ulong timestamp = 1771483000999UL;

        // Ping
        byte[] pingRaw = DataChannelProtocol.SerializeLatencyPing(timestamp);
        Assert.Equal(9, pingRaw.Length);
        Assert.Equal((byte)InputEventType.LatencyPing, pingRaw[0]);

        bool pingOk = DataChannelProtocol.TryDeserializeLatencyPingPong(pingRaw, out bool isPing, out ulong pingTs);
        Assert.True(pingOk);
        Assert.True(isPing);
        Assert.Equal(timestamp, pingTs);

        // Pong
        byte[] pongRaw = DataChannelProtocol.SerializeLatencyPong(timestamp);
        Assert.Equal(9, pongRaw.Length);
        Assert.Equal((byte)InputEventType.LatencyPong, pongRaw[0]);

        bool pongOk = DataChannelProtocol.TryDeserializeLatencyPingPong(pongRaw, out bool isPing2, out ulong pongTs);
        Assert.True(pongOk);
        Assert.False(isPing2);
        Assert.Equal(timestamp, pongTs);
    }

    [Fact]
    public async Task RelayChannel_DropOldest_GuaranteesOnlyFreshestFrameDelivered()
    {
        // Simulates ControlPlane StreamRelayHub 1-capacity DropOldest Channel
        var channel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleWriter = true,
            SingleReader = true
        });

        byte[] frame1 = new byte[] { 1, 1, 1 };
        byte[] frame2 = new byte[] { 2, 2, 2 };
        byte[] frame3 = new byte[] { 3, 3, 3 };

        // Host pushes 3 consecutive frames in rapid succession while Viewer is busy
        channel.Writer.TryWrite(frame1);
        channel.Writer.TryWrite(frame2);
        channel.Writer.TryWrite(frame3);

        // Viewer wakes up and reads
        Assert.True(channel.Reader.TryRead(out var readFrame));
        // Stale frame1 and frame2 were dropped; viewer gets the freshest frame3
        Assert.Same(frame3, readFrame);
        // Channel is now empty; no stale backlog
        Assert.False(channel.Reader.TryRead(out _));

        await Task.CompletedTask;
    }
}
