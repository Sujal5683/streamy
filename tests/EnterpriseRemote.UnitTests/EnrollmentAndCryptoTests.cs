using System;
using System.Threading;
using EnterpriseRemote.Common.Cryptography;
using EnterpriseRemote.Common.Models;
using EnterpriseRemote.ControlPlane.Services;
using EnterpriseRemote.ControlPlane.Storage;
using Xunit;

namespace EnterpriseRemote.UnitTests;

public class EnrollmentAndCryptoTests
{
    [Fact]
    public void CryptoUtils_EcdsaP256_SignsAndVerifiesCorrectly()
    {
        var (privKey, pubKey) = CryptoUtils.GenerateEcdsaKeyPair();
        string message = "EnterpriseRemote:SessionPayload:12345";

        string signature = CryptoUtils.SignData(message, privKey);
        Assert.NotEmpty(signature);

        bool valid = CryptoUtils.VerifySignature(message, signature, pubKey);
        Assert.True(valid);

        // Tampered message should fail
        bool tampered = CryptoUtils.VerifySignature(message + "_tampered", signature, pubKey);
        Assert.False(tampered);

        // Different keypair should fail
        var (_, otherPubKey) = CryptoUtils.GenerateEcdsaKeyPair();
        bool wrongKey = CryptoUtils.VerifySignature(message, signature, otherPubKey);
        Assert.False(wrongKey);
    }

    [Fact]
    public void EnrollmentService_CreatesAndRedeemsInvitation_EnforcingSingleUse()
    {
        var store = new MemoryControlPlaneStore();
        var enrollmentSvc = new EnrollmentService(store);
        var (_, devPubKey) = CryptoUtils.GenerateEcdsaKeyPair();

        // 1. Admin creates invitation
        var (invitation, plainCode) = enrollmentSvc.CreateInvitation(
            "ORG-1",
            "GRP-1",
            "ADMIN-1",
            ttlMinutes: 15,
            attendedRequired: true);

        Assert.NotNull(invitation);
        Assert.StartsWith("INV-", plainCode);
        Assert.False(invitation.IsRedeemed);

        // 2. Device redeems invitation
        var result = enrollmentSvc.RedeemInvitation(
            plainCode,
            "DESKTOP-FIN-01",
            "Windows 11 Enterprise 24H2",
            "00:1A:2B:3C:4D:5E",
            devPubKey,
            "192.168.1.100");

        Assert.True(result.Success);
        Assert.NotNull(result.DeviceId);
        Assert.StartsWith("DEV-", result.DeviceId);

        // Verify device state in store
        var device = store.GetDevice(result.DeviceId);
        Assert.NotNull(device);
        Assert.Equal("DESKTOP-FIN-01", device.Hostname);
        Assert.Equal(DeviceStatus.Online, device.Status);
        Assert.Equal(devPubKey, device.PublicKeyPem);

        // 3. Second redemption of same token MUST fail (Single-use enforcement)
        var secondResult = enrollmentSvc.RedeemInvitation(
            plainCode,
            "DESKTOP-ATTACKER",
            "Linux Rogue",
            "FF:FF:FF:FF:FF:FF",
            devPubKey,
            "192.168.1.101");

        Assert.False(secondResult.Success);
        Assert.Contains("already been redeemed", secondResult.ErrorMessage);
    }

    [Fact]
    public void EnrollmentService_RejectsExpiredInvitation()
    {
        var store = new MemoryControlPlaneStore();
        var enrollmentSvc = new EnrollmentService(store);
        var (_, devPubKey) = CryptoUtils.GenerateEcdsaKeyPair();

        // Create an invitation with negative TTL (already expired)
        var (invitation, plainCode) = enrollmentSvc.CreateInvitation(
            "ORG-1",
            "GRP-1",
            "ADMIN-1",
            ttlMinutes: -1);

        var result = enrollmentSvc.RedeemInvitation(
            plainCode,
            "DESKTOP-1",
            "Windows 11",
            "00:11:22:33:44:55",
            devPubKey,
            "192.168.1.100");

        Assert.False(result.Success);
        Assert.Contains("expired", result.ErrorMessage);
    }

    [Fact]
    public void EnrollmentService_EnforcesRateLimiting_OnExcessiveAttempts()
    {
        var store = new MemoryControlPlaneStore();
        var enrollmentSvc = new EnrollmentService(store);
        var (_, devPubKey) = CryptoUtils.GenerateEcdsaKeyPair();
        string attackerIp = "10.0.0.99";

        // Attempt 5 invalid redemptions
        for (int i = 0; i < 5; i++)
        {
            var res = enrollmentSvc.RedeemInvitation(
                $"INV-FAKE-0000-{i:D4}",
                "HACKER-PC",
                "OS",
                "MAC",
                devPubKey,
                attackerIp);
            Assert.False(res.Success);
        }

        // 6th attempt should be blocked by rate-limiter
        var blockedRes = enrollmentSvc.RedeemInvitation(
            "INV-FAKE-0000-0006",
            "HACKER-PC",
            "OS",
            "MAC",
            devPubKey,
            attackerIp);

        Assert.False(blockedRes.Success);
        Assert.Contains("Rate limit exceeded", blockedRes.ErrorMessage);
    }

    [Fact]
    public void SessionAuthorizationService_IssuesAndVerifiesSignedSatTokens()
    {
        var satSvc = new SessionAuthorizationService();

        var ticket = satSvc.IssueTicket(
            orgId: "ORG-CORP",
            deviceId: "DEV-101",
            operatorId: "USR-ALICE",
            capabilities: SessionCapabilities.View | SessionCapabilities.Input,
            ttlSeconds: 60);

        Assert.NotNull(ticket);
        Assert.NotEmpty(ticket.SessionId);
        Assert.NotEmpty(ticket.SignatureBase64);

        // Verification must pass
        bool isValid = satSvc.VerifyTicket(ticket);
        Assert.True(isValid);

        // Tampering with capabilities must invalidate signature
        ticket.PermittedCapabilities = SessionCapabilities.AdminOperations;
        bool isTamperedValid = satSvc.VerifyTicket(ticket);
        Assert.False(isTamperedValid);
    }

    [Fact]
    public void EnrollmentService_RedeemsWithMasterPasskey_SucceedsImmediately()
    {
        var store = new MemoryControlPlaneStore();
        var enrollmentSvc = new EnrollmentService(store);
        var (_, devPubKey) = CryptoUtils.GenerateEcdsaKeyPair();

        // Enrolling with permanent master key passkey
        var res = enrollmentSvc.RedeemInvitation(
            "PERM-MASTER-UNIVERSAL-2026",
            "WORKSTATION-REMOTE",
            "Windows 11 Enterprise",
            "00:11:22:33:44:55",
            devPubKey,
            "192.168.1.55");

        Assert.True(res.Success);
        Assert.NotNull(res.DeviceId);
        Assert.StartsWith("DEV-", res.DeviceId);
        Assert.Equal("PERM-MASTER-UNIVERSAL-2026", res.PermanentAccessToken);

        var dev = store.GetDevice(res.DeviceId);
        Assert.NotNull(dev);
        Assert.Equal("WORKSTATION-REMOTE", dev.Hostname);
    }

    [Fact]
    public void EnrollmentService_RedeemsWithUniversalPin1234_Succeeds()
    {
        var store = new MemoryControlPlaneStore();
        var enrollmentSvc = new EnrollmentService(store);
        var (_, devPubKey) = CryptoUtils.GenerateEcdsaKeyPair();

        var res = enrollmentSvc.RedeemInvitation(
            "1234",
            "OFFICE-LAPTOP",
            "Windows 11 Pro",
            "00:22:33:44:55:66",
            devPubKey,
            "192.168.1.56");

        Assert.True(res.Success);
        Assert.NotNull(res.DeviceId);
        Assert.StartsWith("DEV-", res.DeviceId);
        Assert.Equal("1234", res.PermanentAccessToken);
    }

    [Fact]
    public void EnrollmentService_ReEnrollmentSameHostname_ReusesDeviceIdAndUpdatesCredentials()
    {
        var store = new MemoryControlPlaneStore();
        var enrollmentSvc = new EnrollmentService(store);
        var (_, devPubKey1) = CryptoUtils.GenerateEcdsaKeyPair();
        var (_, devPubKey2) = CryptoUtils.GenerateEcdsaKeyPair();

        // 1. Initial enrollment via PIN 1234 on same Wi-Fi
        var res1 = enrollmentSvc.RedeemInvitation(
            "1234",
            "LAPTOP-REMOTE-DEV",
            "Windows 11 Pro",
            "00:11:22:33:44:55",
            devPubKey1,
            "192.168.1.100");

        Assert.True(res1.Success);
        string deviceId1 = res1.DeviceId!;

        // 2. Switched to Cloudflare Tunnel and re-enrolled with Master Key
        var res2 = enrollmentSvc.RedeemInvitation(
            "PERM-MASTER-SWITCH-TEST",
            "laptop-remote-dev", // case insensitive match
            "Windows 11 Pro",
            "00:11:22:33:44:55",
            devPubKey2,
            "192.168.1.100");

        Assert.True(res2.Success);
        string deviceId2 = res2.DeviceId!;

        // Must reuse the exact same canonical DeviceId
        Assert.Equal(deviceId1, deviceId2);

        // Store must contain exactly 1 device, NOT 2 duplicates
        var devices = store.GetDevicesForOrg("ORG-ENTERPRISE");
        Assert.Single(devices);

        var device = store.GetDevice(deviceId1);
        Assert.NotNull(device);
        Assert.Equal("PERM-MASTER-SWITCH-TEST", device.PermanentAccessToken);
        Assert.Equal(devPubKey2, device.PublicKeyPem);

        // Lookup by hostname must also succeed
        var byHost = store.GetDeviceByHostname("LAPTOP-REMOTE-DEV");
        Assert.NotNull(byHost);
        Assert.Equal(deviceId1, byHost.DeviceId);
    }
}
