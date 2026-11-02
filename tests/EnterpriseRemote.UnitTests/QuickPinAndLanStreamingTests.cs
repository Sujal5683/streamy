using System;
using System.Threading.Channels;
using System.Threading.Tasks;
using EnterpriseRemote.Common.Cryptography;
using EnterpriseRemote.Common.Models;
using EnterpriseRemote.ControlPlane.Services;
using EnterpriseRemote.ControlPlane.Storage;
using Xunit;

namespace EnterpriseRemote.UnitTests;

public class QuickPinAndLanStreamingTests
{
    [Fact]
    public void GenerateShortPin_ReturnsFourDigitNumericString()
    {
        for (int i = 0; i < 50; i++)
        {
            string pin = CryptoUtils.GenerateShortPin();
            Assert.NotNull(pin);
            Assert.Equal(4, pin.Length);
            Assert.True(int.TryParse(pin, out int num));
            Assert.InRange(num, 0, 9999);
        }
    }

    [Fact]
    public void EnrollmentService_CreatesInvitationWithShortPin_AndRedeemsWithPin()
    {
        var store = new MemoryControlPlaneStore();
        var enrollmentSvc = new EnrollmentService(store);
        var (_, devPubKey) = CryptoUtils.GenerateEcdsaKeyPair();

        var (invitation, plainCode) = enrollmentSvc.CreateInvitation(
            "ORG-1",
            "GRP-1",
            "ADMIN-1",
            ttlMinutes: 30,
            attendedRequired: true);

        Assert.NotNull(invitation.ShortPin);
        Assert.Equal(4, invitation.ShortPin.Length);

        // Can find invitation by Short PIN
        var foundByPin = store.FindInvitationByHash(invitation.ShortPin);
        Assert.NotNull(foundByPin);
        Assert.Equal(invitation.InvitationId, foundByPin.InvitationId);

        // Redeem using the 4-digit PIN directly instead of full token!
        var redemptionResult = enrollmentSvc.RedeemInvitation(
            invitationCode: invitation.ShortPin,
            hostname: "TEST-REMOTE-LAPTOP",
            osVersion: "Windows 11",
            macAddress: "AA:BB:CC:DD:EE:FF",
            publicKeyPem: devPubKey,
            clientIp: "172.18.11.122");

        Assert.True(redemptionResult.Success);
        Assert.NotNull(redemptionResult.DeviceId);
        Assert.Equal(invitation.ShortPin, redemptionResult.PermanentAccessToken);

        // Verify device saved in store has ShortPin
        var enrolledDevice = store.GetDevice(redemptionResult.DeviceId);
        Assert.NotNull(enrolledDevice);
        Assert.Equal(invitation.ShortPin, enrolledDevice.ShortPin);

        // Verify permanent token lookup works with ShortPin
        var deviceByPin = store.GetDeviceByPermanentToken(invitation.ShortPin);
        Assert.NotNull(deviceByPin);
        Assert.Equal(enrolledDevice.DeviceId, deviceByPin.DeviceId);
    }

    [Fact]
    public void EnrollmentService_RedeemsWithFlexibleCodeFormats()
    {
        var store = new MemoryControlPlaneStore();
        var enrollmentSvc = new EnrollmentService(store);
        var (_, devPubKey) = CryptoUtils.GenerateEcdsaKeyPair();

        // 1. Redeem without "INV-" prefix
        var (_, plainCode1) = enrollmentSvc.CreateInvitation("ORG-1", "GRP-1", "ADMIN-1");
        string withoutPrefix = plainCode1.StartsWith("INV-", StringComparison.OrdinalIgnoreCase)
            ? plainCode1.Substring(4)
            : plainCode1;
        var res1 = enrollmentSvc.RedeemInvitation(withoutPrefix, "HOST-1", "Win11", "00:11:22:33:44:55", devPubKey, "10.0.0.1");
        Assert.True(res1.Success, $"Failed to redeem without prefix: {res1.ErrorMessage}");

        // 2. Redeem with last 4 characters suffix (e.g. D335)
        var (_, plainCode2) = enrollmentSvc.CreateInvitation("ORG-1", "GRP-1", "ADMIN-1");
        string suffix = plainCode2.Substring(plainCode2.Length - 4);
        var res2 = enrollmentSvc.RedeemInvitation(suffix, "HOST-2", "Win11", "00:11:22:33:44:56", devPubKey, "10.0.0.2");
        Assert.True(res2.Success, $"Failed to redeem with suffix: {res2.ErrorMessage}");

        // 3. Redeem with lowercase full code
        var (_, plainCode3) = enrollmentSvc.CreateInvitation("ORG-1", "GRP-1", "ADMIN-1");
        var res3 = enrollmentSvc.RedeemInvitation(plainCode3.ToLowerInvariant(), "HOST-3", "Win11", "00:11:22:33:44:57", devPubKey, "10.0.0.3");
        Assert.True(res3.Success, $"Failed to redeem with lowercase: {res3.ErrorMessage}");
    }

    [Fact]
    public async Task ZeroBufferbloatChannel_DropsOldestFrames_PreservesZeroLag()
    {
        var channel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleWriter = true,
            SingleReader = true
        });

        // Simulate sender producing 10 frames rapidly while receiver is busy
        for (int i = 1; i <= 10; i++)
        {
            byte[] frameData = new byte[] { (byte)i };
            bool written = channel.Writer.TryWrite(frameData);
            Assert.True(written);
        }

        channel.Writer.Complete();

        // When receiver reads, it gets ONLY the latest frame (10), 1-9 were dropped!
        bool read = await channel.Reader.WaitToReadAsync();
        Assert.True(read);
        Assert.True(channel.Reader.TryRead(out var latestFrame));
        Assert.NotNull(latestFrame);
        Assert.Equal(10, latestFrame[0]);

        // Channel should now be empty
        Assert.False(channel.Reader.TryRead(out _));
    }

    [Theory]
    [InlineData("172.18.11.122", "http://172.18.11.122:5293")]
    [InlineData("172.18.11.122:5293", "http://172.18.11.122:5293")]
    [InlineData("http://172.18.11.122:5293", "http://172.18.11.122:5293")]
    [InlineData(" http://https://172.18.11.122:5293:5293 ", "http://172.18.11.122:5293")]
    [InlineData("http:// http://https://172.18.11.122:5293:5293", "http://172.18.11.122:5293")]
    [InlineData("https://tunnel-abc.trycloudflare.com", "https://tunnel-abc.trycloudflare.com")]
    [InlineData("tunnel-abc.trycloudflare.com:5293", "https://tunnel-abc.trycloudflare.com")]
    [InlineData("localhost", "http://localhost:5293")]
    [InlineData("", "http://127.0.0.1:5293")]
    public void UrlUtils_NormalizeServerUrl_ProducesCanonicalValidUri(string input, string expected)
    {
        string normalized = EnterpriseRemote.Common.Protocol.UrlUtils.NormalizeServerUrl(input);
        Assert.Equal(expected, normalized);

        // Guaranteed to parse cleanly without UriFormatException
        var uri = new Uri(normalized);
        Assert.True(uri.IsAbsoluteUri);
    }
}
