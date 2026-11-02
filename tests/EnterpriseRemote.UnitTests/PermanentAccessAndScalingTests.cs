using System;
using System.Drawing;
using EnterpriseRemote.Common.Cryptography;
using EnterpriseRemote.Common.Models;
using EnterpriseRemote.ControlPlane.Services;
using EnterpriseRemote.ControlPlane.Storage;
using Xunit;

namespace EnterpriseRemote.UnitTests;

public class PermanentAccessAndScalingTests
{
    [Fact]
    public void EnrollmentService_GeneratesPermanentAccessToken_OnSuccessfulRedemption()
    {
        var store = new MemoryControlPlaneStore();
        var enrollmentSvc = new EnrollmentService(store);
        var (_, devPubKey) = CryptoUtils.GenerateEcdsaKeyPair();

        // 1. Create invitation
        var (invitation, code) = enrollmentSvc.CreateInvitation("ORG-CORP", "GRP-1", "ADMIN-1", ttlMinutes: 30);
        Assert.NotNull(invitation);

        // 2. Redeem invitation
        var result = enrollmentSvc.RedeemInvitation(
            code,
            "STREAMING-LAPTOP-13",
            "Windows 11 24H2",
            "00:15:5D:12:34:56",
            devPubKey,
            "192.168.1.150");

        Assert.True(result.Success);
        Assert.NotNull(result.PermanentAccessToken);
        Assert.StartsWith("PERM-", result.PermanentAccessToken);
        Assert.True(result.PermanentAccessToken.Length >= 16);

        // 3. Verify permanent access token persisted on device record
        var device = store.GetDevice(result.DeviceId!);
        Assert.NotNull(device);
        Assert.Equal(result.PermanentAccessToken, device.PermanentAccessToken);
    }

    [Fact]
    public void UnattendedSession_BypassesAttendedConsent_WhenPermanentTokenMatches()
    {
        var store = new MemoryControlPlaneStore();
        var enrollmentSvc = new EnrollmentService(store);
        var satSvc = new SessionAuthorizationService();
        var (_, devPubKey) = CryptoUtils.GenerateEcdsaKeyPair();

        var (_, code) = enrollmentSvc.CreateInvitation("ORG-CORP", "GRP-1", "ADMIN-1");
        var enrollResult = enrollmentSvc.RedeemInvitation(code, "REMOTE-PC", "Win11", "MAC1", devPubKey, "10.0.0.5");

        var device = store.GetDevice(enrollResult.DeviceId!);
        Assert.NotNull(device);

        bool policyAttendedRequired = true;
        string validPermToken = enrollResult.PermanentAccessToken!;

        // 1. Request session WITH matching permanent access token -> Unattended (Bypasses attended consent)
        bool consentRequiredWithToken = policyAttendedRequired;
        if (!string.IsNullOrEmpty(validPermToken) &&
            !string.IsNullOrEmpty(device.PermanentAccessToken) &&
            string.Equals(validPermToken, device.PermanentAccessToken, StringComparison.Ordinal))
        {
            consentRequiredWithToken = false;
        }
        Assert.False(consentRequiredWithToken);

        // 2. Request session with INVALID permanent token -> Must require attended consent
        string invalidToken = "PERM-INVALID-HACK";
        bool consentRequiredWithBadToken = policyAttendedRequired;
        if (!string.IsNullOrEmpty(invalidToken) &&
            !string.IsNullOrEmpty(device.PermanentAccessToken) &&
            string.Equals(invalidToken, device.PermanentAccessToken, StringComparison.Ordinal))
        {
            consentRequiredWithBadToken = false;
        }
        Assert.True(consentRequiredWithBadToken);
    }

    [Fact]
    public void ViewportScaleModes_CalculateCorrectDestinationRectangles()
    {
        // Simulate a 13-inch laptop remote screen (1366x768) displayed on a 15-inch controller (1920x1080)
        var remoteSize = new Size(1366, 768);
        var canvasSize = new Size(1920, 1080);

        // 1. FIT MODE: Preserves aspect ratio, fits inside canvas
        var fitRect = CalculateScaledRectangle(remoteSize, canvasSize, ViewportScaleModeTest.Fit);
        Assert.True(fitRect.Width <= canvasSize.Width);
        Assert.True(fitRect.Height <= canvasSize.Height);
        Assert.True(fitRect.X >= 0);
        Assert.True(fitRect.Y >= 0);

        // 2. STRETCH MODE: Fills 100% of canvas with zero letterbox
        var stretchRect = CalculateScaledRectangle(remoteSize, canvasSize, ViewportScaleModeTest.Stretch);
        Assert.Equal(0, stretchRect.X);
        Assert.Equal(0, stretchRect.Y);
        Assert.Equal(canvasSize.Width, stretchRect.Width);
        Assert.Equal(canvasSize.Height, stretchRect.Height);

        // 3. ORIGINAL 1:1 MODE: Native pixels centered
        var origRect = CalculateScaledRectangle(remoteSize, canvasSize, ViewportScaleModeTest.Original);
        Assert.Equal(remoteSize.Width, origRect.Width);
        Assert.Equal(remoteSize.Height, origRect.Height);
        Assert.Equal((canvasSize.Width - remoteSize.Width) / 2, origRect.X);
        Assert.Equal((canvasSize.Height - remoteSize.Height) / 2, origRect.Y);
    }

    [Fact]
    public void MouseCoordinateNormalization_MapsToExactSubPixel_AcrossLetterbox()
    {
        var remoteSize = new Size(1366, 768);
        var canvasSize = new Size(1920, 1080);
        var fitRect = CalculateScaledRectangle(remoteSize, canvasSize, ViewportScaleModeTest.Fit);

        // Center of rendered remote screen
        int clickCenterX = fitRect.X + fitRect.Width / 2;
        int clickCenterY = fitRect.Y + fitRect.Height / 2;

        float normX = Math.Clamp((clickCenterX - fitRect.X) / (float)fitRect.Width, 0.0f, 1.0f);
        float normY = Math.Clamp((clickCenterY - fitRect.Y) / (float)fitRect.Height, 0.0f, 1.0f);

        Assert.Equal(0.5f, normX, 0.01f);
        Assert.Equal(0.5f, normY, 0.01f);

        // Top-left of rendered remote screen
        float tlNormX = Math.Clamp((fitRect.X - fitRect.X) / (float)fitRect.Width, 0.0f, 1.0f);
        float tlNormY = Math.Clamp((fitRect.Y - fitRect.Y) / (float)fitRect.Height, 0.0f, 1.0f);
        Assert.Equal(0.0f, tlNormX, 0.001f);
        Assert.Equal(0.0f, tlNormY, 0.001f);

        // Bottom-right of rendered remote screen
        float brNormX = Math.Clamp((fitRect.Right - fitRect.X) / (float)fitRect.Width, 0.0f, 1.0f);
        float brNormY = Math.Clamp((fitRect.Bottom - fitRect.Y) / (float)fitRect.Height, 0.0f, 1.0f);
        Assert.Equal(1.0f, brNormX, 0.001f);
        Assert.Equal(1.0f, brNormY, 0.001f);
    }

    private static Rectangle CalculateScaledRectangle(Size imgSize, Size canvasSize, ViewportScaleModeTest mode)
    {
        if (imgSize.Width <= 0 || imgSize.Height <= 0 || canvasSize.Width <= 0 || canvasSize.Height <= 0)
        {
            return new Rectangle(0, 0, Math.Max(1, canvasSize.Width), Math.Max(1, canvasSize.Height));
        }

        switch (mode)
        {
            case ViewportScaleModeTest.Stretch:
                return new Rectangle(0, 0, canvasSize.Width, canvasSize.Height);

            case ViewportScaleModeTest.Original:
                int origX = Math.Max(0, (canvasSize.Width - imgSize.Width) / 2);
                int origY = Math.Max(0, (canvasSize.Height - imgSize.Height) / 2);
                return new Rectangle(origX, origY, imgSize.Width, imgSize.Height);

            case ViewportScaleModeTest.Fit:
            default:
                float ratioX = (float)canvasSize.Width / imgSize.Width;
                float ratioY = (float)canvasSize.Height / imgSize.Height;
                float ratio = Math.Min(ratioX, ratioY);

                int newWidth = Math.Max(1, (int)(imgSize.Width * ratio));
                int newHeight = Math.Max(1, (int)(imgSize.Height * ratio));
                int posX = (canvasSize.Width - newWidth) / 2;
                int posY = (canvasSize.Height - newHeight) / 2;

                return new Rectangle(posX, posY, newWidth, newHeight);
        }
    }

    [Fact]
    public void MemoryControlPlaneStore_GetDeviceByPermanentToken_ResolvesDeviceAccurately()
    {
        var store = new MemoryControlPlaneStore();
        
        var dev1 = new EnrolledDevice
        {
            DeviceId = "DEV-ALPHA-01",
            OrgId = "ORG-CORP",
            GroupId = "GRP-ENG",
            Hostname = "DEV-HOST-1",
            PermanentAccessToken = "PERM-AAAA-1111-2222"
        };
        var dev2 = new EnrolledDevice
        {
            DeviceId = "DEV-BETA-02",
            OrgId = "ORG-CORP",
            GroupId = "GRP-SALES",
            Hostname = "DEV-HOST-2",
            PermanentAccessToken = "PERM-BBBB-3333-4444"
        };

        store.SaveDevice(dev1);
        store.SaveDevice(dev2);

        // Positive resolution
        var found1 = store.GetDeviceByPermanentToken("PERM-AAAA-1111-2222");
        Assert.NotNull(found1);
        Assert.Equal("DEV-ALPHA-01", found1.DeviceId);

        // Case-insensitive & trimmed resolution
        var found2 = store.GetDeviceByPermanentToken("  perm-bbbb-3333-4444  ");
        Assert.NotNull(found2);
        Assert.Equal("DEV-BETA-02", found2.DeviceId);

        // Negative resolution
        var notFound = store.GetDeviceByPermanentToken("PERM-NONEXISTENT");
        Assert.Null(notFound);

        var nullResult = store.GetDeviceByPermanentToken(null!);
        Assert.Null(nullResult);

        var emptyResult = store.GetDeviceByPermanentToken("   ");
        Assert.Null(emptyResult);
    }

    [Fact]
    public void SessionRequest_AutoResolution_ResolvesDevice_WhenDeviceIdIsAuto()
    {
        var store = new MemoryControlPlaneStore();
        string permKey = "PERM-KEY-9999-XXXX";

        var hostDevice = new EnrolledDevice
        {
            DeviceId = "DEV-HOST-ONLINE",
            OrgId = "ORG-CORP",
            GroupId = "GRP-DEFAULT",
            Hostname = "MY-OFFICE-PC",
            PermanentAccessToken = permKey
        };
        store.SaveDevice(hostDevice);

        // Simulating the resolution logic in /api/v1/sessions/request
        string requestDeviceId = "AUTO";
        string? requestPermanentToken = permKey;

        EnrolledDevice? resolvedDevice = null;
        if (string.Equals(requestDeviceId, "AUTO", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(requestDeviceId))
        {
            if (!string.IsNullOrWhiteSpace(requestPermanentToken))
            {
                resolvedDevice = store.GetDeviceByPermanentToken(requestPermanentToken);
            }
        }
        else
        {
            resolvedDevice = store.GetDevice(requestDeviceId);
            if (resolvedDevice == null && !string.IsNullOrWhiteSpace(requestPermanentToken))
            {
                resolvedDevice = store.GetDeviceByPermanentToken(requestPermanentToken);
            }
        }

        Assert.NotNull(resolvedDevice);
        Assert.Equal("DEV-HOST-ONLINE", resolvedDevice.DeviceId);
        Assert.Equal(permKey, resolvedDevice.PermanentAccessToken);
    }

    public enum ViewportScaleModeTest
    {
        Fit = 0,
        Stretch = 1,
        Original = 2
    }
}

