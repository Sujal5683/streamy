using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using EnterpriseRemote.Common.Models;
using EnterpriseRemote.Common.Network;
using EnterpriseRemote.ControlPlane.Services;
using EnterpriseRemote.ControlPlane.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Hosting;

// Discover the most authoritative wwwroot directory.
// Prioritize workspace source tree (src/EnterpriseRemote.ControlPlane/wwwroot) if present so UI changes are instantly live across both Browser and Windows Desktop App!
string[] candidateRoots =
[
    Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "src", "EnterpriseRemote.ControlPlane", "wwwroot"),
    Path.Combine(AppContext.BaseDirectory, "..", "..", "src", "EnterpriseRemote.ControlPlane", "wwwroot"),
    Path.Combine(AppContext.BaseDirectory, "..", "src", "EnterpriseRemote.ControlPlane", "wwwroot"),
    Path.Combine(Directory.GetCurrentDirectory(), "src", "EnterpriseRemote.ControlPlane", "wwwroot"),
    Path.Combine(AppContext.BaseDirectory, "wwwroot"),
    Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"),
    Path.Combine(Directory.GetCurrentDirectory(), "EnterpriseRemote_Apps", "1_MAIN_PC_CONTROLLER", "Bin", "wwwroot")
];

string initialWebRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
string initialContentRoot = AppContext.BaseDirectory;

foreach (var r in candidateRoots)
{
    string full = Path.GetFullPath(r);
    if (Directory.Exists(full) && File.Exists(Path.Combine(full, "index.html")))
    {
        initialWebRoot = full;
        initialContentRoot = Directory.GetParent(full)?.FullName ?? AppContext.BaseDirectory;
        break;
    }
}
var options = new WebApplicationOptions
{
    Args = args,
    ContentRootPath = initialContentRoot,
    WebRootPath = initialWebRoot
};
var builder = WebApplication.CreateBuilder(options);

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
builder.Services.AddSingleton<MasterKeyStore>();

// Configure JSON serialization for Enums
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();
app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.Headers.Append("Cache-Control", "no-cache, no-store, must-revalidate");
        ctx.Context.Response.Headers.Append("Pragma", "no-cache");
        ctx.Context.Response.Headers.Append("Expires", "0");
    }
});
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

// Auth: Roles list (used by WebView2 controller health check and UI role introspection)
app.MapGet("/api/v1/auth/roles", () => Results.Ok(new[]
{
    new { role = "Operator",      description = "Can request attended sessions on authorized devices." },
    new { role = "Approver",      description = "Can approve attended session requests." },
    new { role = "Administrator", description = "Full access: enroll, audit, emergency control, manage keys." },
    new { role = "Auditor",       description = "Read-only access to audit logs and session history." }
}));

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
        invitation.GroupId,
        invitation.ShortPin));
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

// 8. Gateway: LAN adapter discovery — returns the host's active LAN adapters and primary Wi-Fi/LAN IP.
// The web UI reads this to populate the "Wi-Fi (LAN IP)" chip so users click to switch instantly.
app.MapGet("/api/v1/gateway/lan", () =>
{
    var adapters = LanDiscoveryUtils.GetActiveLanAdapters();
    string primaryIp = adapters.Count > 0 ? adapters[0].IpAddress : "127.0.0.1";
    string? wirelessIp = adapters.FirstOrDefault(a => a.IsWireless).IpAddress;
    string wifiIp = !string.IsNullOrEmpty(wirelessIp) ? wirelessIp : primaryIp;
    string? tailscaleIp = LanDiscoveryUtils.GetTailscaleIp();
    return Results.Ok(new
    {
        primaryIp,
        wirelessIp,
        wifiIp,
        tailscaleIp,
        primaryUrl = $"http://{primaryIp}:5293",
        wifiUrl = $"http://{wifiIp}:5293",
        adapters = adapters.Select(a => new
        {
            a.IpAddress,
            a.Name,
            a.Description,
            a.HasGateway,
            a.IsWireless,
            url = $"http://{a.IpAddress}:5293"
        }).ToArray()
    });
});

// 9. Gateway: Tunnel status — checks for an active Cloudflare/bore.pub tunnel and Tailscale P2P WireGuard
app.MapGet("/api/v1/gateway/tunnel", () =>
{
    string? cfTunnelUrl = LanDiscoveryUtils.TryReadPublicTunnelUrl();
    bool cfActive = !string.IsNullOrEmpty(cfTunnelUrl);

    string? tsIp = LanDiscoveryUtils.GetTailscaleIp();
    bool tsActive = !string.IsNullOrEmpty(tsIp);
    string? tsUrl = tsActive ? $"http://{tsIp}:5293" : null;

    return Results.Ok(new
    {
        active = cfActive || tsActive,
        url = cfTunnelUrl ?? tsUrl,
        mode = cfActive ? "cloudflare" : (tsActive ? "tailscale" : "none"),
        cloudflare = new
        {
            active = cfActive,
            url = cfTunnelUrl,
            message = cfActive ? $"Active Cloudflare Edge: {cfTunnelUrl}" : "Cloudflare tunnel offline."
        },
        tailscale = new
        {
            active = tsActive,
            ip = tsIp,
            url = tsUrl,
            message = tsActive ? $"Tailscale P2P Active: {tsUrl}" : "Tailscale not detected or offline."
        },
        tailscaleIp = tsIp,
        tailscaleUrl = tsUrl,
        message = cfActive
            ? $"Cloudflare Tunnel Active: {cfTunnelUrl}"
            : (tsActive ? $"Tailscale WireGuard Active: {tsUrl}" : "No remote tunnels active.")
    });
});

// 10. Gateway: Start Cloudflare Tunnel — launches cloudflared in a new visible terminal window
app.MapPost("/api/v1/gateway/start-tunnel", () =>
{
    try
    {
        // Locate the start_tunnel script relative to the server binary or project root
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string[] candidates =
        [
            Path.Combine(baseDir, "start_tunnel.ps1"),
            Path.Combine(baseDir, "..", "start_tunnel.ps1"),
            Path.Combine(baseDir, "scripts", "start_tunnel.ps1"),
            Path.Combine(baseDir, "..", "scripts", "start_tunnel.ps1"),
            Path.Combine(baseDir, "..", "..", "scripts", "start_tunnel.ps1"),
            Path.Combine(baseDir, "..", "..", "..", "scripts", "start_tunnel.ps1"),
            Path.Combine(baseDir, "..", "..", "..", "..", "scripts", "start_tunnel.ps1"),
        ];

        string? scriptPath = candidates.Select(Path.GetFullPath).FirstOrDefault(File.Exists);

        if (scriptPath != null)
        {
            // Launch cloudflare tunnel script in a new visible PowerShell terminal window
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-ExecutionPolicy Bypass -File \"{scriptPath}\"",
                UseShellExecute = true,
                CreateNoWindow = false,
                WindowStyle = ProcessWindowStyle.Normal
            });

            return Results.Ok(new { success = true, message = "Cloudflare tunnel terminal launched! Public URL will appear in public_tunnel_url.txt within ~5 seconds." });
        }

        // Fallback: launch cloudflared directly if script not found
        Process.Start(new ProcessStartInfo
        {
            FileName = "cloudflared",
            Arguments = "tunnel --url http://localhost:5293",
            UseShellExecute = true,
            CreateNoWindow = false,
            WindowStyle = ProcessWindowStyle.Normal
        });
        return Results.Ok(new { success = true, message = "Launched cloudflared tunnel directly. Monitoring for public URL..." });
    }
    catch (Exception ex)
    {
        return Results.Ok(new { success = false, message = $"Could not launch tunnel terminal: {ex.Message}. Please run scripts/start_tunnel.ps1 manually." });
    }
});

// 10B. Gateway: Start Tailscale Setup & Status Helper
app.MapPost("/api/v1/gateway/start-tailscale", () =>
{
    try
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string[] candidates =
        [
            Path.Combine(baseDir, "start_tailscale_tunnel.ps1"),
            Path.Combine(baseDir, "..", "start_tailscale_tunnel.ps1"),
            Path.Combine(baseDir, "scripts", "start_tailscale_tunnel.ps1"),
            Path.Combine(baseDir, "..", "scripts", "start_tailscale_tunnel.ps1"),
            Path.Combine(baseDir, "..", "..", "scripts", "start_tailscale_tunnel.ps1"),
            Path.Combine(baseDir, "..", "..", "..", "scripts", "start_tailscale_tunnel.ps1"),
            Path.Combine(baseDir, "..", "..", "..", "..", "scripts", "start_tailscale_tunnel.ps1"),
        ];

        string? scriptPath = candidates.Select(Path.GetFullPath).FirstOrDefault(File.Exists);
        if (scriptPath != null)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-ExecutionPolicy Bypass -File \"{scriptPath}\"",
                UseShellExecute = true,
                CreateNoWindow = false,
                WindowStyle = ProcessWindowStyle.Normal
            });

            return Results.Ok(new { success = true, message = "Tailscale helper terminal launched! Verifying WireGuard connection..." });
        }

        string[] batCandidates =
        [
            Path.Combine(baseDir, "START_TAILSCALE_MODE.bat"),
            Path.Combine(baseDir, "..", "START_TAILSCALE_MODE.bat"),
            Path.Combine(baseDir, "scripts", "Start_Tailscale_Mode.bat"),
            Path.Combine(baseDir, "..", "scripts", "Start_Tailscale_Mode.bat")
        ];
        string? batPath = batCandidates.Select(Path.GetFullPath).FirstOrDefault(File.Exists);
        if (batPath != null)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = batPath,
                UseShellExecute = true,
                CreateNoWindow = false,
                WindowStyle = ProcessWindowStyle.Normal
            });
            return Results.Ok(new { success = true, message = "Tailscale mode launcher started!" });
        }

        return Results.Ok(new { success = false, message = "Tailscale helper script not found. Please make sure Tailscale is installed on this PC." });
    }
    catch (Exception ex)
    {
        return Results.Ok(new { success = false, message = $"Could not launch Tailscale helper: {ex.Message}" });
    }
});

// 11. Master Keys: List all keys for the user's org
app.MapGet("/api/v1/master-keys", (string? userId, MasterKeyStore keyStore, IControlPlaneStore s) =>
{
    string orgId = "ORG-ENTERPRISE";
    if (!string.IsNullOrEmpty(userId))
    {
        var user = s.GetUser(userId);
        if (user != null) orgId = user.OrgId;
    }
    var keys = keyStore.GetKeysForOrg(orgId);
    return Results.Ok(keys);
});

// 12. Master Keys: Create a new permanent master key
app.MapPost("/api/v1/master-keys", async (HttpContext ctx, MasterKeyStore keyStore, IControlPlaneStore s) =>
{
    MasterKeyCreateRequest? req = null;
    try { req = await ctx.Request.ReadFromJsonAsync<MasterKeyCreateRequest>(); } catch { }

    if (req == null || string.IsNullOrWhiteSpace(req.SecretValue))
    {
        return Results.BadRequest(new { error = "secretValue is required." });
    }

    string orgId = req.OrgId ?? "ORG-ENTERPRISE";

    var key = new MasterKeyEntry
    {
        KeyId = $"MK-{Guid.NewGuid().ToString("N")[..10].ToUpperInvariant()}",
        OrgId = orgId,
        Name = string.IsNullOrWhiteSpace(req.Name) ? "Universal Master Key" : req.Name,
        SecretValue = req.SecretValue.Trim(),
        Description = req.Description ?? string.Empty,
        CreatedBy = req.CreatedBy ?? "admin",
        CreatedAtUtc = DateTime.UtcNow,
        IsActive = req.IsActive ?? true
    };

    keyStore.AddKey(key);
    return Results.Created($"/api/v1/master-keys/{key.KeyId}", key);
});

// 13. Master Keys: Toggle enabled/disabled state
app.MapMethods("/api/v1/master-keys/{keyId}/toggle", ["PATCH"], (string keyId, MasterKeyStore keyStore) =>
{
    var key = keyStore.GetKey(keyId);
    if (key == null) return Results.NotFound(new { error = "Master key not found." });

    key.IsActive = !key.IsActive;
    return Results.Ok(key);
});

// 14. Master Keys: Delete / revoke a key
app.MapDelete("/api/v1/master-keys/{keyId}", (string keyId, MasterKeyStore keyStore) =>
{
    bool removed = keyStore.RemoveKey(keyId);
    if (!removed) return Results.NotFound(new { error = "Master key not found." });
    return Results.Ok(new { status = "deleted", keyId });
});

// 15. Streaming Hub: Host (Desktop Agent) connection
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

// 16. Streaming Hub: Viewer (Controller) connection
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
        StreamRelayHub.UnregisterViewer(deviceId, viewerWs);
    }
});

app.MapFallbackToFile("index.html", new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.Headers.Append("Cache-Control", "no-cache, no-store, must-revalidate");
        ctx.Context.Response.Headers.Append("Pragma", "no-cache");
        ctx.Context.Response.Headers.Append("Expires", "0");
    }
});

app.Run();

// ─── DTO Records ──────────────────────────────────────────────────────────────
public record AuthRequest(string Email);
public record AuthResponse(string UserId, string Email, string DisplayName, string OrgId, UserRole Role, string[] AuthorizedGroupIds);
public record CreateInvitationRequest(string AdminUserId, string GroupId, int TtlMinutes = 15, bool AttendedRequired = true);
public record CreateInvitationResponse(string InvitationId, string InvitationCode, DateTime ExpiresAtUtc, string GroupId, string? ShortPin = null);
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

public record MasterKeyCreateRequest(
    string? Name,
    string SecretValue,
    string? Description,
    string? CreatedBy,
    string? OrgId,
    bool? IsActive);

// ─── Master Key Store ─────────────────────────────────────────────────────────
/// <summary>
/// Lightweight in-memory store for permanent master access keys.
/// A master key works across ALL enrolled devices in the org — no per-device pairing needed.
/// </summary>
public sealed class MasterKeyEntry
{
    public string KeyId { get; set; } = string.Empty;
    public string OrgId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string SecretValue { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class MasterKeyStore
{
    private readonly ConcurrentDictionary<string, MasterKeyEntry> _keys = new();

    public void AddKey(MasterKeyEntry key) => _keys[key.KeyId] = key;

    public MasterKeyEntry? GetKey(string keyId)
    {
        _keys.TryGetValue(keyId, out var key);
        return key;
    }

    public List<MasterKeyEntry> GetKeysForOrg(string orgId) =>
        _keys.Values
             .Where(k => k.OrgId.Equals(orgId, StringComparison.OrdinalIgnoreCase))
             .OrderByDescending(k => k.CreatedAtUtc)
             .ToList();

    public bool RemoveKey(string keyId) => _keys.TryRemove(keyId, out _);
}

public partial class Program { }
