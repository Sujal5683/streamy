using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using EnterpriseRemote.Common.Models;
using EnterpriseRemote.ControlPlane.Services;
using EnterpriseRemote.ControlPlane.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

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

var operatorUser = new UserIdentity
{
    UserId = "USR-OPERATOR-01",
    Email = "alice@corp.internal",
    DisplayName = "Alice Operator",
    OrgId = "ORG-ENTERPRISE",
    Role = UserRole.Operator,
    AuthorizedGroupIds = new[] { "GRP-CORP-DESKTOPS" }
};
store.SaveUser(operatorUser);

// Health & Liveness
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

// Heartbeat Endpoint
app.MapPost("/api/v1/devices/{deviceId}/heartbeat", (string deviceId, IControlPlaneStore s) =>
{
    var device = s.GetDevice(deviceId);
    if (device == null) return Results.NotFound();
    device.LastHeartbeatUtc = DateTime.UtcNow;
    device.Status = DeviceStatus.Online;
    s.SaveDevice(device);
    return Results.Ok(new { status = "acknowledged", timestamp = DateTime.UtcNow });
});

// 1. Auth Endpoint: Mock / OIDC Exchange
app.MapPost("/api/v1/auth/token", (AuthRequest req, IControlPlaneStore s) =>
{
    var user = s.GetUserByEmail(req.Email);
    if (user == null)
    {
        return Results.Unauthorized();
    }
    return Results.Ok(new AuthResponse(user.UserId, user.Email, user.DisplayName, user.OrgId, user.Role, user.AuthorizedGroupIds));
});

// 2. Invitations: Create (Admin Only)
app.MapPost("/api/v1/invitations/create", (CreateInvitationRequest req, EnrollmentService enrollmentSvc, IControlPlaneStore s) =>
{
    var user = s.GetUser(req.AdminUserId);
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

    return Results.Ok(new { deviceId = result.DeviceId, status = "Enrolled" });
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
    return Results.Ok(devices);
});

// 5. Sessions: Request Session Authorization
app.MapPost("/api/v1/sessions/request", (SessionRequestDto req, PolicyEngine policyEngine, SessionAuthorizationService satSvc, IControlPlaneStore s) =>
{
    var user = s.GetUser(req.OperatorId);
    if (user == null) return Results.Unauthorized();

    var device = s.GetDevice(req.DeviceId);
    if (device == null) return Results.NotFound(new { error = "Device not found." });

    var policyResult = policyEngine.Evaluate(user, device, req.RequestedCapabilities, DateTime.UtcNow);
    if (!policyResult.Allowed)
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

    var ticket = satSvc.IssueTicket(
        user.OrgId,
        device.DeviceId,
        user.UserId,
        policyResult.PermittedCapabilities);

    s.AppendAudit(new AuditEvent
    {
        EventId = Guid.NewGuid().ToString("N"),
        OrgId = user.OrgId,
        TimestampUtc = DateTime.UtcNow,
        EventType = "SessionAuthorized",
        OperatorId = user.UserId,
        DeviceId = device.DeviceId,
        Details = $"Session {ticket.SessionId} authorized. AttendedRequired: {policyResult.RequiresAttendedConsent}"
    });

    return Results.Ok(new SessionResponseDto(
        ticket.SessionId,
        ticket.DeviceId,
        ticket.PermittedCapabilities,
        policyResult.RequiresAttendedConsent,
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

app.Run();

// DTO Records
public record AuthRequest(string Email);
public record AuthResponse(string UserId, string Email, string DisplayName, string OrgId, UserRole Role, string[] AuthorizedGroupIds);
public record CreateInvitationRequest(string AdminUserId, string GroupId, int TtlMinutes = 15, bool AttendedRequired = true);
public record CreateInvitationResponse(string InvitationId, string InvitationCode, DateTime ExpiresAtUtc, string GroupId);
public record RedeemInvitationRequest(string InvitationCode, string Hostname, string OsVersion, string MacAddress, string PublicKeyPem);
public record SessionRequestDto(string OperatorId, string DeviceId, SessionCapabilities RequestedCapabilities);
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
