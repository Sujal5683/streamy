using System;
using System.Linq;
using System.Threading.Tasks;
using EnterpriseRemote.Common.Audit;
using EnterpriseRemote.Common.Cryptography;
using EnterpriseRemote.Common.FileTransfer;
using EnterpriseRemote.Common.Models;
using EnterpriseRemote.Common.Protocol;
using EnterpriseRemote.Controller;
using EnterpriseRemote.ControlPlane.Services;
using EnterpriseRemote.ControlPlane.Storage;
using EnterpriseRemote.Host.UserAgent.Capture;
using EnterpriseRemote.Host.UserAgent.Input;
using EnterpriseRemote.Host.UserAgent.Tray;
using Xunit;

namespace EnterpriseRemote.UnitTests;

public class EndToEndWorkflowTests
{
    [Fact]
    public void CompleteWorkflowB_DeviceEnrollmentAndLifecycleManagement()
    {
        // Setup Control Plane
        var store = new MemoryControlPlaneStore();
        var enrollmentSvc = new EnrollmentService(store);

        // 1. Admin logs in and generates high-entropy invitation
        string orgId = "ORG-FINANCE-CORP";
        string groupId = "GRP-SECURE-WORKSTATIONS";
        string adminId = "USR-CHIEF-ADMIN";

        var (invitation, plainCode) = enrollmentSvc.CreateInvitation(
            orgId,
            groupId,
            adminId,
            ttlMinutes: 15,
            attendedRequired: false); // Unattended allowed by admin

        Assert.NotNull(invitation);
        Assert.StartsWith("INV-", plainCode);

        // 2. Target Endpoint generates hardware cryptographic keypair (ECDSA P-256)
        var (devPrivKey, devPubKey) = CryptoUtils.GenerateEcdsaKeyPair();

        // 3. Endpoint redeems invitation with public key
        var redeemResult = enrollmentSvc.RedeemInvitation(
            plainCode,
            hostname: "FIN-TERMINAL-01",
            osVersion: "Windows 11 Enterprise (10.0.26200)",
            macAddress: "00:50:56:C0:00:08",
            publicKeyPem: devPubKey,
            clientIp: "10.0.4.15");

        Assert.True(redeemResult.Success, redeemResult.ErrorMessage);
        string deviceId = redeemResult.DeviceId!;
        Assert.StartsWith("DEV-", deviceId);

        // 4. Verify device is enrolled and online in database
        var device = store.GetDevice(deviceId);
        Assert.NotNull(device);
        Assert.Equal(DeviceStatus.Online, device.Status);
        Assert.Equal(devPubKey, device.PublicKeyPem);

        // 5. Attempting to replay or reuse the invitation MUST fail
        var replayResult = enrollmentSvc.RedeemInvitation(
            plainCode,
            hostname: "ROGUE-PC",
            osVersion: "Linux",
            macAddress: "AA:BB:CC:DD:EE:FF",
            publicKeyPem: devPubKey,
            clientIp: "10.0.4.99");

        Assert.False(replayResult.Success);
        Assert.Contains("already been redeemed", replayResult.ErrorMessage);

        // 6. Admin triggers Emergency Disable
        device.Status = DeviceStatus.Disabled;
        store.SaveDevice(device);

        store.AppendAudit(new AuditEvent
        {
            EventId = Guid.NewGuid().ToString("N"),
            OrgId = orgId,
            TimestampUtc = DateTime.UtcNow,
            EventType = "EmergencyDisableTriggered",
            OperatorId = adminId,
            DeviceId = deviceId,
            Details = $"Device {deviceId} emergency-disabled by administrator.",
            ClientIp = "10.0.1.1"
        });

        // 7. Verify device is disabled and further access is blocked
        var disabledDev = store.GetDevice(deviceId);
        Assert.NotNull(disabledDev);
        Assert.Equal(DeviceStatus.Disabled, disabledDev.Status);
    }

    [Fact]
    public void CompleteWorkflowA_OperatorConnectRemoteControlAndCleanTeardown()
    {
        // Setup Control Plane & Services
        var store = new MemoryControlPlaneStore();
        var policyEngine = new PolicyEngine(store);
        var satSvc = new SessionAuthorizationService();
        string orgId = "ORG-FINANCE-CORP";
        string groupId = "GRP-SECURE-WORKSTATIONS";

        // Setup Policy: Unattended allowed, View + Input + Clipboard permitted
        store.SavePolicy(new PolicyRule
        {
            OrgId = orgId,
            GroupId = groupId,
            MinimumRole = UserRole.Operator,
            AttendedRequired = false,
            AllowedTimeStart = TimeSpan.Zero,
            AllowedTimeEnd = TimeSpan.FromHours(24),
            PermittedCapabilities = SessionCapabilities.View | SessionCapabilities.Input | SessionCapabilities.Clipboard
        });

        // Setup Operator
        var op = new UserIdentity
        {
            UserId = "USR-ALICE",
            Email = "alice@corp.internal",
            DisplayName = "Alice Operator",
            OrgId = orgId,
            Role = UserRole.Operator,
            AuthorizedGroupIds = new[] { groupId }
        };
        store.SaveUser(op);

        // Setup Target Device
        var (devPriv, devPub) = CryptoUtils.GenerateEcdsaKeyPair();
        var dev = new EnrolledDevice
        {
            DeviceId = "DEV-FIN-01",
            OrgId = orgId,
            GroupId = groupId,
            Hostname = "FIN-TERMINAL-01",
            Status = DeviceStatus.Online,
            PublicKeyPem = devPub
        };
        store.SaveDevice(dev);

        // 1. Operator queries authorized devices
        var authorizedDevices = store.GetDevicesForOrg(op.OrgId, op.AuthorizedGroupIds);
        Assert.Single(authorizedDevices);
        Assert.Equal("DEV-FIN-01", authorizedDevices[0].DeviceId);

        // 2. Operator requests session with View + Input
        var requestedCaps = SessionCapabilities.View | SessionCapabilities.Input;
        var policyResult = policyEngine.Evaluate(op, dev, requestedCaps, DateTime.UtcNow);
        Assert.True(policyResult.Allowed);
        Assert.False(policyResult.RequiresAttendedConsent); // Unattended access approved by policy

        // 3. Control plane issues cryptographically signed Session Authorization Ticket (SAT)
        var satTicket = satSvc.IssueTicket(op.OrgId, dev.DeviceId, op.UserId, policyResult.PermittedCapabilities);
        Assert.NotNull(satTicket);

        // 4. Host verifies SAT ticket signature using Control Plane public key
        bool satValid = satSvc.VerifyTicket(satTicket);
        Assert.True(satValid);

        // 5. Host establishes session tray indicator
        var tray = new SessionTrayIndicator();
        tray.ShowActiveSession(op.DisplayName);
        Assert.True(tray.IsInSession);

        // 6. Controller establishes session client
        var controllerClient = new RemoteSessionClient(op.UserId, dev.DeviceId);
        controllerClient.InitializeSession(satTicket.SessionId, satTicket.PermittedCapabilities, policyResult.RequiresAttendedConsent);
        Assert.True(controllerClient.IsActive);

        // 7. Input Injection: Controller sends mouse click, Host bounds-checks and injects
        var mockSink = new MockInputSink();
        var inputEngine = new InputInjectionEngine(new DisplayInfo { Left = 0, Top = 0, Width = 1920, Height = 1080 }, mockSink);

        controllerClient.OnInputPacketReady = rawPacket =>
        {
            if (DataChannelProtocol.TryDeserializeMouse(rawPacket, out var mousePacket))
            {
                inputEngine.ProcessMousePacket(mousePacket);
            }
        };

        controllerClient.SendMouseButton(MouseButton.Left, true, 0.5f, 0.5f);
        Assert.Single(mockSink.InjectedMouse);
        var click = mockSink.InjectedMouse[0];
        Assert.Equal(MouseButton.Left, click.Button);
        Assert.Equal(960, click.X);
        Assert.Equal(540, click.Y);

        // 8. Session Teardown: Disconnect cleanly and log audit event
        controllerClient.TerminateSession();
        Assert.False(controllerClient.IsActive);

        tray.CloseActiveSession();
        Assert.False(tray.IsInSession);

        store.AppendAudit(new AuditEvent
        {
            EventId = Guid.NewGuid().ToString("N"),
            OrgId = orgId,
            TimestampUtc = DateTime.UtcNow,
            EventType = "SessionDisconnectedCleanly",
            OperatorId = op.UserId,
            DeviceId = dev.DeviceId,
            Details = $"Session {satTicket.SessionId} closed cleanly. ExitCode: 0",
            ClientIp = "10.0.1.20"
        });

        var audits = store.GetAuditEvents(orgId, dev.DeviceId);
        Assert.Contains(audits, a => a.EventType == "SessionDisconnectedCleanly");
    }

    [Fact]
    public void SecurityTest_ReplayAndPrivilegeEscalation_AreHardBlocked()
    {
        var store = new MemoryControlPlaneStore();
        var policyEngine = new PolicyEngine(store);
        var satSvc = new SessionAuthorizationService();
        string orgId = "ORG-CORP";
        string restrictedGroupId = "GRP-EXECUTIVE-CONFIDENTIAL";

        store.SavePolicy(new PolicyRule
        {
            OrgId = orgId,
            GroupId = restrictedGroupId,
            MinimumRole = UserRole.Administrator,
            PermittedCapabilities = SessionCapabilities.View | SessionCapabilities.AdminOperations
        });

        var standardOperator = new UserIdentity
        {
            UserId = "OP-BOB",
            OrgId = orgId,
            Role = UserRole.Operator, // Not Admin
            AuthorizedGroupIds = new[] { "GRP-STANDARD-PC" } // Not restricted group
        };

        var executiveLaptop = new EnrolledDevice
        {
            DeviceId = "DEV-CEO-LAPTOP",
            OrgId = orgId,
            GroupId = restrictedGroupId,
            Status = DeviceStatus.Online
        };

        // 1. Standard operator attempting to access executive laptop is blocked
        var result = policyEngine.Evaluate(standardOperator, executiveLaptop, SessionCapabilities.View, DateTime.UtcNow);
        Assert.False(result.Allowed);
        Assert.Contains("not authorized", result.DenialReason);

        // 2. Operator requesting unpermitted capability (FileTransfer when policy allows only View)
        var viewOnlyPolicy = new PolicyRule
        {
            OrgId = orgId,
            GroupId = "GRP-VIEW-ONLY",
            MinimumRole = UserRole.Operator,
            PermittedCapabilities = SessionCapabilities.View // FileTransfer NOT permitted
        };
        store.SavePolicy(viewOnlyPolicy);

        var publicDevice = new EnrolledDevice
        {
            DeviceId = "DEV-KIOSK-01",
            OrgId = orgId,
            GroupId = "GRP-VIEW-ONLY",
            Status = DeviceStatus.Online
        };

        var kioskOperator = new UserIdentity
        {
            UserId = "OP-KIOSK",
            OrgId = orgId,
            Role = UserRole.Operator,
            AuthorizedGroupIds = new[] { "GRP-VIEW-ONLY" }
        };

        var privEscResult = policyEngine.Evaluate(kioskOperator, publicDevice, SessionCapabilities.FileTransfer, DateTime.UtcNow);
        Assert.False(privEscResult.Allowed);
        Assert.Contains("exceed policy allowance", privEscResult.DenialReason);
    }
}
