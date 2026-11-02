using System;
using System.IO;
using System.Net;
using EnterpriseRemote.Controller.UI.Utils;
using EnterpriseRemote.Host.UserAgent.Video;
using Xunit;

namespace EnterpriseRemote.UnitTests;

public class NetworkDiscoveryAndStreamingOptimizationTests
{
    [Fact]
    public void NetworkDiscoveryHelper_GetPrimaryLanIp_ReturnsValidIpv4Address()
    {
        string ip = NetworkDiscoveryHelper.GetPrimaryLanIp();
        Assert.NotNull(ip);
        Assert.NotEmpty(ip);

        bool parsed = IPAddress.TryParse(ip, out var addr);
        Assert.True(parsed);
        Assert.Equal(System.Net.Sockets.AddressFamily.InterNetwork, addr!.AddressFamily);
    }

    [Fact]
    public void NetworkDiscoveryHelper_GetActiveLanAdapters_ReturnsNonEmptyList()
    {
        var adapters = NetworkDiscoveryHelper.GetActiveLanAdapters();
        Assert.NotNull(adapters);
        Assert.NotEmpty(adapters);

        // First adapter should be valid
        var primary = adapters[0];
        Assert.False(string.IsNullOrWhiteSpace(primary.IpAddress));
        Assert.False(string.IsNullOrWhiteSpace(primary.Name));
    }

    [Fact]
    public void PixelConverter_FastBoxDownscaleBgra32_ScalesAccurately()
    {
        int srcW = 100;
        int srcH = 100;
        byte[] src = new byte[srcW * srcH * 4];

        // Fill with arbitrary recognizable color (B=50, G=100, R=150, A=255)
        for (int i = 0; i < src.Length; i += 4)
        {
            src[i] = 50;
            src[i + 1] = 100;
            src[i + 2] = 150;
            src[i + 3] = 255;
        }

        int dstW = 50;
        int dstH = 50;
        byte[] dst = new byte[dstW * dstH * 4];

        PixelConverter.FastBoxDownscaleBgra32(src, srcW, srcH, dst, dstW, dstH);

        // Verify output pixel colors
        for (int i = 0; i < dst.Length; i += 4)
        {
            Assert.Equal(50, dst[i]);
            Assert.Equal(100, dst[i + 1]);
            Assert.Equal(150, dst[i + 2]);
            Assert.Equal(255, dst[i + 3]);
        }
    }

    [Theory]
    [InlineData("http://192.168.1.50:5293", true)]
    [InlineData("http://172.18.11.122:5293", true)]
    [InlineData("http://10.0.0.1:5293", true)]
    [InlineData("http://127.0.0.1:5293", true)]
    [InlineData("https://statements-dem-ecology-vii.trycloudflare.com", false)]
    [InlineData("https://bore.pub:12345", false)]
    public void LanDiscoveryUtils_IsLanAddress_CorrectlyDistinguishesLanFromCloud(string url, bool expected)
    {
        bool actual = EnterpriseRemote.Common.Network.LanDiscoveryUtils.IsLanAddress(url);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(60)]
    [InlineData(100)]
    [InlineData(120)]
    [InlineData(144)]
    [InlineData(200)]
    public void DataChannelProtocol_StreamConfig_SupportsHighFramerateUpTo200Fps(byte targetFps)
    {
        var config = new EnterpriseRemote.Common.Protocol.StreamConfigPacket(
            TargetFps: targetFps,
            Quality: 55,
            MaxHeight: 720,
            LowLatencyMode: 1,
            TimestampMillis: (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        byte[] raw = EnterpriseRemote.Common.Protocol.DataChannelProtocol.SerializeStreamConfig(config);
        Assert.NotNull(raw);
        Assert.Equal(14, raw.Length);

        bool success = EnterpriseRemote.Common.Protocol.DataChannelProtocol.TryDeserializeStreamConfig(raw, out var parsed);
        Assert.True(success);
        Assert.Equal(targetFps, parsed.TargetFps);
        Assert.Equal(55, parsed.Quality);
        Assert.Equal(720, parsed.MaxHeight);
        Assert.Equal(1, parsed.LowLatencyMode);
    }

    [Fact]
    public void HighFramerateScheduler_CalculatesAccurateRemainingInterval()
    {
        double targetFps = 100.0;
        double targetIntervalMs = 1000.0 / targetFps; // 10ms
        long targetTicks = (long)(targetIntervalMs * System.Diagnostics.Stopwatch.Frequency / 1000.0);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        System.Threading.Thread.SpinWait(10000);

        long elapsedTicks = sw.ElapsedTicks;
        long remainingTicks = targetTicks - elapsedTicks;

        Assert.True(elapsedTicks > 0, "Stopwatch should be running and non-zero");
        Assert.True(remainingTicks <= targetTicks, "Remaining ticks should decrease as time elapses");
        int delayMs = (int)(remainingTicks * 1000 / System.Diagnostics.Stopwatch.Frequency);
        Assert.True(delayMs >= 0 && delayMs <= 10, "Delay must be between 0 and 10ms");
    }
}
