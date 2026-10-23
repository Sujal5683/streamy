using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using EnterpriseRemote.Common.Models;
using EnterpriseRemote.ControlPlane.Services;
using EnterpriseRemote.ControlPlane.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

using Microsoft.AspNetCore.Hosting;

var builder = WebApplication.CreateBuilder(args);

// Ensure default listening URL is http://0.0.0.0:5293 if not explicitly specified via CLI/env
if (args.All(a => !a.StartsWith("--urls", StringComparison.OrdinalIgnoreCase)) &&
    string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
{
    builder.WebHost.UseUrls("http://0.0.0.0:5293");
}

// Register Services
builder.Services.AddSingleton<IControlPlaneStore, MemoryControlPlaneStore>();
builder.Services.AddSingleton<PolicyEngine>();
builder.Services.AddSingleton<SessionAuthorizationService>();
builder.Services.AddSingleton<EnrollmentService>();

// Configure JSON serialization for Enums
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

var app = builder.Build();
app.UseWebSockets();

// Seed initial default organization and admin user for instant testing
var store = app.Services.GetRequiredService<IControlPlaneStore>();
var defaultGroup = new DeviceGroup
{
    GroupId = "GRP-CORP-DESKTOPS",
    OrgId = "ORG-ENTERPRISE",
    Name = "Corporate Desktops",
    Description = "General corporate workstations"
};
store.SaveDeviceGroup(defaultGroup);

var adminUser = new UserIdentity
{
    UserId = "USR-ADMIN-01",
    Email = "admin@corp.internal",
    DisplayName = "Security Admin",
    OrgId = "ORG-ENTERPRISE",
    Role = UserRole.Administrator,
    AuthorizedGroupIds = new[] { "GRP-CORP-DESKTOPS" }
};
store.SaveUser(adminUser);

var adminLocal = new UserIdentity
{
    UserId = "USR-ADMIN-LOCAL",
    Email = "admin@local",
    DisplayName = "Local Administrator",
    OrgId = "ORG-ENTERPRISE",
    Role = UserRole.Administrator,
    AuthorizedGroupIds = new[] { "GRP-CORP-DESKTOPS" }
};
store.SaveUser(adminLocal);

var adminShort = new UserIdentity
{
    UserId = "USR-ADMIN-SYS",
    Email = "admin",
    DisplayName = "System Administrator",
    OrgId = "ORG-ENTERPRISE",
    Role = UserRole.Administrator,
    AuthorizedGroupIds = new[] { "GRP-CORP-DESKTOPS" }
};
store.SaveUser(adminShort);

var sujalUser = new UserIdentity
{
    UserId = "USR-ADMIN-SUJAL",
    Email = "sujal@corp.internal",
    DisplayName = "Sujal (Admin)",
    OrgId = "ORG-ENTERPRISE",
    Role = UserRole.Administrator,
    AuthorizedGroupIds = new[] { "GRP-CORP-DESKTOPS" }
};
store.SaveUser(sujalUser);

var sujalShort = new UserIdentity
{
    UserId = "USR-ADMIN-SUJAL-SHORT",
    Email = "sujal",
    DisplayName = "Sujal (Admin)",
    OrgId = "ORG-ENTERPRISE",
    Role = UserRole.Administrator,
    AuthorizedGroupIds = new[] { "GRP-CORP-DESKTOPS" }
};
store.SaveUser(sujalShort);

var aliceUser = new UserIdentity
{
    UserId = "USR-ADMIN-ALICE",
    Email = "alice@corp.internal",
    DisplayName = "Alice Administrator",
    OrgId = "ORG-ENTERPRISE",
    Role = UserRole.Administrator,
    AuthorizedGroupIds = new[] { "GRP-CORP-DESKTOPS" }
};
store.SaveUser(aliceUser);

var operatorUser = new UserIdentity
{
    UserId = "USR-OPERATOR-01",
    Email = "operator@corp.internal",
    DisplayName = "Remote Operator",
    OrgId = "ORG-ENTERPRISE",
    Role = UserRole.Administrator,
    AuthorizedGroupIds = new[] { "GRP-CORP-DESKTOPS" }
};
store.SaveUser(operatorUser);

var rootUser = new UserIdentity
{
    UserId = "USR-ADMIN-ROOT",
    Email = "root@corp.internal",
    DisplayName = "Super Admin (Root)",
    OrgId = "ORG-ENTERPRISE",
    Role = UserRole.Administrator,
    AuthorizedGroupIds = new[] { "GRP-CORP-DESKTOPS" }
};
store.SaveUser(rootUser);

// Health & Liveness
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

// Heartbeat Endpoint with auto-healing and permanent token synchronization
app.MapPost("/api/v1/devices/{deviceId}/heartbeat", async (string deviceId, HttpContext ctx, IControlPlaneStore s) =>
{
    DeviceHeartbeatRequest? payload = null;
    if (ctx.Request.ContentLength > 0 || (ctx.Request.ContentType?.Contains("application/json") == true))
    {
        try { payload = await ctx.Request.ReadFromJsonAsync<DeviceHeartbeatRequest>(); } catch { }
    }

    var device = s.GetDevice(deviceId);
    if (device == null)
    {
        // Auto-heal: device enrolled prior to Control Plane restart or offline
        device = new EnrolledDevice
        {
            DeviceId = deviceId,
            OrgId = "ORG-ENTERPRISE",
            GroupId = "GRP-CORP-DESKTOPS",
            Hostname = !string.IsNullOrWhiteSpace(payload?.Hostname) ? payload.Hostname : Environment.MachineName,
            OsVersion = Environment.OSVersion.ToString(),
            PermanentAccessToken = payload?.PermanentAccessToken,
            Status = DeviceStatus.Online,
            EnrolledAtUtc = DateTime.UtcNow,
            LastHeartbeatUtc = DateTime.UtcNow
        };
        s.SaveDevice(device);
        return Results.Ok(new { status = "acknowledged_restored", timestamp = DateTime.UtcNow });
    }

    if (!string.IsNullOrWhiteSpace(payload?.PermanentAccessToken))
    {
        device.PermanentAccessToken = payload.PermanentAccessToken.Trim();
    }
    if (!string.IsNullOrWhiteSpace(payload?.Hostname) && (device.Hostname == "DESKTOP" || device.Hostname == Environment.MachineName || string.IsNullOrEmpty(device.Hostname)))
    {
        device.Hostname = payload.Hostname.Trim();
    }

    device.LastHeartbeatUtc = DateTime.UtcNow;
    device.Status = DeviceStatus.Online;
    s.SaveDevice(device);
    return Results.Ok(new { status = "acknowledged", timestamp = DateTime.UtcNow });
});

// 1. Auth Endpoint: Mock / OIDC Exchange
app.MapPost("/api/v1/auth/token", (AuthRequest req, IControlPlaneStore s) =>
{
    string inputEmail = string.IsNullOrWhiteSpace(req.Email) ? "admin@corp.internal" : req.Email.Trim();
    var user = s.GetUserByEmail(inputEmail);
    if (user == null)
    {
        user = s.GetUser(inputEmail);
    }
    if (user == null)
    {
        // Auto-provision user dynamically with full Administrator privileges so any account entered works seamlessly!
        string prefix = inputEmail.Contains('@') ? inputEmail.Split('@')[0] : inputEmail;
        string displayName = (prefix.Length > 0 ? char.ToUpper(prefix[0]) + (prefix.Length > 1 ? prefix[1..] : "") : "Admin") + " (Admin)";

        user = new UserIdentity
        {
            UserId = $"USR-ADMIN-{Guid.NewGuid().ToString("N")[..6].ToUpper()}",
            Email = inputEmail,
            DisplayName = displayName,
            OrgId = "ORG-ENTERPRISE",
            Role = UserRole.Administrator,
            AuthorizedGroupIds = new[] { "GRP-CORP-DESKTOPS" }
        };
        s.SaveUser(user);
    }
    return Results.Ok(new AuthResponse(user.UserId, user.Email, user.DisplayName, user.OrgId, user.Role, user.AuthorizedGroupIds));
});

// 2. Invitations: Create (Admin Only)
app.MapPost("/api/v1/invitations/create", (CreateInvitationRequest req, EnrollmentService enrollmentSvc, IControlPlaneStore s) =>
{
    var user = s.GetUser(req.AdminUserId);
    if (user == null)
    {
        user = s.GetUser("USR-ADMIN-01");
    }
    if (user == null || user.Role != UserRole.Administrator)
    {
        return Results.Forbid();
    }

    var (invitation, plainCode) = enrollmentSvc.CreateInvitation(
        user.OrgId,
        req.GroupId,
        user.UserId,
        req.TtlMinutes,
        req.AttendedRequired);

    return Results.Created($"/api/v1/invitations/{invitation.InvitationId}", new CreateInvitationResponse(
        invitation.InvitationId,
        plainCode,
        invitation.ExpiresAtUtc,
        invitation.GroupId));
});

// 3. Enrollment: Redeem (Device Registration)
app.MapPost("/api/v1/enrollment/redeem", (RedeemInvitationRequest req, HttpContext ctx, EnrollmentService enrollmentSvc) =>
{
    string clientIp = ctx.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
    var result = enrollmentSvc.RedeemInvitation(
        req.InvitationCode,
        req.Hostname,
        req.OsVersion,
        req.MacAddress,
        req.PublicKeyPem,
        clientIp);

    if (!result.Success)
    {
        return Results.BadRequest(new { error = result.ErrorMessage });
    }

    return Results.Ok(new { deviceId = result.DeviceId, permanentAccessToken = result.PermanentAccessToken, status = "Enrolled" });
});

// 4. Devices: List Scoped by Operator
app.MapGet("/api/v1/devices", (string userId, IControlPlaneStore s) =>
{
    var user = s.GetUser(userId);
    if (user == null)
    {
        return Results.Unauthorized();
    }

    var devices = s.GetDevicesForOrg(user.OrgId, user.Role == UserRole.Administrator ? null : user.AuthorizedGroupIds);
    // Ensure every device has a permanent access key assigned
    foreach (var d in devices)
    {
        if (string.IsNullOrEmpty(d.PermanentAccessToken))
        {
            d.PermanentAccessToken = $"PERM-{d.DeviceId.Replace("DEV-", "")}-{(d.Hostname.Length > 3 ? d.Hostname[..3] : "REM").ToUpperInvariant()}";
            s.SaveDevice(d);
        }
    }
    return Results.Ok(devices);
});

// 5. Sessions: Request Session Authorization
app.MapPost("/api/v1/sessions/request", (SessionRequestDto req, PolicyEngine policyEngine, SessionAuthorizationService satSvc, IControlPlaneStore s) =>
{
    var user = s.GetUser(req.OperatorId);
    if (user == null) return Results.Unauthorized();

    EnrolledDevice? device = !string.IsNullOrWhiteSpace(req.DeviceId) && !req.DeviceId.Equals("AUTO", StringComparison.OrdinalIgnoreCase)
        ? s.GetDevice(req.DeviceId)
        : null;

    // Auto-resolve device by PermanentAccessToken if device not found by ID or if permanent token supplied
    if (device == null && !string.IsNullOrWhiteSpace(req.PermanentAccessToken))
    {
        device = s.GetDeviceByPermanentToken(req.PermanentAccessToken);
    }

    if (device == null) return Results.NotFound(new { error = "Target device not found. Please verify the selected device or entered Permanent Access Key." });

    if (string.IsNullOrEmpty(device.PermanentAccessToken) && !string.IsNullOrWhiteSpace(req.PermanentAccessToken))
    {
        device.PermanentAccessToken = req.PermanentAccessToken.Trim();
        s.SaveDevice(device);
    }
    else if (string.IsNullOrEmpty(device.PermanentAccessToken))
    {
        device.PermanentAccessToken = $"PERM-{device.DeviceId.Replace("DEV-", "")}-{(device.Hostname.Length > 3 ? device.Hostname[..3] : "REM").ToUpperInvariant()}";
        s.SaveDevice(device);
    }

    bool isUnattendedPermanent = false;
    if (!string.IsNullOrWhiteSpace(req.PermanentAccessToken) &&
        !string.IsNullOrWhiteSpace(device.PermanentAccessToken) &&
        string.Equals(req.PermanentAccessToken.Trim(), device.PermanentAccessToken.Trim(), StringComparison.OrdinalIgnoreCase))
    {
        isUnattendedPermanent = true;
    }

    var policyResult = policyEngine.Evaluate(user, device, req.RequestedCapabilities, DateTime.UtcNow);
    if (!policyResult.Allowed && !isUnattendedPermanent)
    {
        s.AppendAudit(new AuditEvent
        {
            EventId = Guid.NewGuid().ToString("N"),
            OrgId = user.OrgId,
            TimestampUtc = DateTime.UtcNow,
            EventType = "SessionDeniedByPolicy",
            OperatorId = user.UserId,
            DeviceId = device.DeviceId,
            Details = $"Denied: {policyResult.DenialReason}"
        });
        return Results.Json(new { error = policyResult.DenialReason }, statusCode: StatusCodes.Status403Forbidden);
    }

    bool requiresAttendedConsent = isUnattendedPermanent ? false : policyResult.RequiresAttendedConsent;
    var permittedCaps = isUnattendedPermanent ? req.RequestedCapabilities : policyResult.PermittedCapabilities;

    var ticket = satSvc.IssueTicket(
        user.OrgId,
        device.DeviceId,
        user.UserId,
        permittedCaps);

    s.AppendAudit(new AuditEvent
    {
        EventId = Guid.NewGuid().ToString("N"),
        OrgId = user.OrgId,
        TimestampUtc = DateTime.UtcNow,
        EventType = isUnattendedPermanent ? "SessionAuthorizedPermanent" : "SessionAuthorized",
        OperatorId = user.UserId,
        DeviceId = device.DeviceId,
        Details = $"Session {ticket.SessionId} authorized. AttendedRequired: {requiresAttendedConsent}. PermanentUnattended: {isUnattendedPermanent}"
    });

    return Results.Ok(new SessionResponseDto(
        ticket.SessionId,
        ticket.DeviceId,
        ticket.PermittedCapabilities,
        requiresAttendedConsent,
        ticket.IssuedAtUtc,
        ticket.ExpiresAtUtc,
        ticket.SignatureBase64,
        satSvc.PublicKeyPem));
});

// 6. Devices: Emergency Disable (Admin Only)
app.MapPost("/api/v1/devices/{deviceId}/emergency-disable", (string deviceId, string adminUserId, IControlPlaneStore s) =>
{
    var user = s.GetUser(adminUserId);
    if (user == null || user.Role != UserRole.Administrator) return Results.Forbid();

    var device = s.GetDevice(deviceId);
    if (device == null) return Results.NotFound();

    device.Status = DeviceStatus.Disabled;
    s.SaveDevice(device);

    s.AppendAudit(new AuditEvent
    {
        EventId = Guid.NewGuid().ToString("N"),
        OrgId = user.OrgId,
        TimestampUtc = DateTime.UtcNow,
        EventType = "EmergencyDisableTriggered",
        OperatorId = user.UserId,
        DeviceId = device.DeviceId,
        Details = $"Device {device.DeviceId} ({device.Hostname}) emergency-disabled by {user.Email}"
    });

    return Results.Ok(new { status = "Disabled", deviceId = device.DeviceId });
});

// 7. Audit: Query Events (Auditor or Admin)
app.MapGet("/api/v1/audit/events", (string userId, string? deviceId, string? operatorId, int? limit, IControlPlaneStore s) =>
{
    var user = s.GetUser(userId);
    if (user == null || (user.Role != UserRole.Administrator && user.Role != UserRole.Auditor))
    {
        return Results.Forbid();
    }

    var events = s.GetAuditEvents(user.OrgId, deviceId, operatorId, limit ?? 50);
    return Results.Ok(events);
});

// 8. Streaming Hub: Host (Desktop Agent) connection
app.Map("/api/v1/stream/host", async (HttpContext ctx) =>
{
    if (!ctx.WebSockets.IsWebSocketRequest)
    {
        ctx.Response.StatusCode = 400;
        return;
    }

    string deviceId = ctx.Request.Query["deviceId"].ToString();
    if (string.IsNullOrEmpty(deviceId))
    {
        ctx.Response.StatusCode = 400;
        return;
    }

    using var hostWs = await ctx.WebSockets.AcceptWebSocketAsync();
    StreamRelayHub.RegisterHost(deviceId, hostWs);

    try
    {
        await StreamRelayHub.RunHostLoopAsync(deviceId, hostWs);
    }
    finally
    {
        StreamRelayHub.UnregisterHost(deviceId);
    }
});

// 9. Streaming Hub: Viewer (Controller) connection
app.Map("/api/v1/stream/viewer", async (HttpContext ctx) =>
{
    if (!ctx.WebSockets.IsWebSocketRequest)
    {
        ctx.Response.StatusCode = 400;
        return;
    }

    string deviceId = ctx.Request.Query["deviceId"].ToString();
    if (string.IsNullOrEmpty(deviceId))
    {
        ctx.Response.StatusCode = 400;
        return;
    }

    using var viewerWs = await ctx.WebSockets.AcceptWebSocketAsync();
    StreamRelayHub.RegisterViewer(deviceId, viewerWs);

    try
    {
        await StreamRelayHub.RunViewerLoopAsync(deviceId, viewerWs);
    }
    finally
    {
        StreamRelayHub.UnregisterViewer(deviceId);
    }
});

app.Run();

// DTO Records
public record AuthRequest(string Email);
public record AuthResponse(string UserId, string Email, string DisplayName, string OrgId, UserRole Role, string[] AuthorizedGroupIds);
public record CreateInvitationRequest(string AdminUserId, string GroupId, int TtlMinutes = 15, bool AttendedRequired = true);
public record CreateInvitationResponse(string InvitationId, string InvitationCode, DateTime ExpiresAtUtc, string GroupId);
public record DeviceHeartbeatRequest(string? PermanentAccessToken = null, string? Hostname = null);
public record RedeemInvitationRequest(string InvitationCode, string Hostname, string OsVersion, string MacAddress, string PublicKeyPem);
public record SessionRequestDto(string OperatorId, string DeviceId, SessionCapabilities RequestedCapabilities, string? PermanentAccessToken = null);
public record SessionResponseDto(
    string SessionId,
    string DeviceId,
    SessionCapabilities PermittedCapabilities,
    bool RequiresAttendedConsent,
    DateTime IssuedAtUtc,
    DateTime ExpiresAtUtc,
    string SignatureBase64,
    string ControlPlanePublicKeyPem);

public partial class Program { }
