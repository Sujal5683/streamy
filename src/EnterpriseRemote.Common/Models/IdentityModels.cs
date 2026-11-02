using System;
using System.Text.Json.Serialization;

namespace EnterpriseRemote.Common.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum UserRole
{
    Operator,
    Approver,
    Administrator,
    Auditor
}

[Flags]
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SessionCapabilities
{
    None = 0,
    View = 1,
    Input = 2,
    Clipboard = 4,
    FileTransfer = 8,
    AdminOperations = 16
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DeviceStatus
{
    Offline,
    Online,
    InSession,
    Disabled,
    Revoked
}

public sealed class UserIdentity
{
    public string UserId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string OrgId { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.Operator;
    public string[] AuthorizedGroupIds { get; set; } = Array.Empty<string>();
}

public sealed class DeviceGroup
{
    public string GroupId { get; set; } = string.Empty;
    public string OrgId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public sealed class EnrolledDevice
{
    public string DeviceId { get; set; } = string.Empty;
    public string OrgId { get; set; } = string.Empty;
    public string GroupId { get; set; } = string.Empty;
    public string Hostname { get; set; } = string.Empty;
    public string OsVersion { get; set; } = string.Empty;
    public string MacAddress { get; set; } = string.Empty;
    public string PublicKeyPem { get; set; } = string.Empty;
    public DateTime EnrolledAtUtc { get; set; }
    public DateTime LastHeartbeatUtc { get; set; }
    public DeviceStatus Status { get; set; } = DeviceStatus.Offline;
    public string? PermanentAccessToken { get; set; }
    /// <summary>
    /// 4-digit short PIN assigned at enrollment for fast attended re-connection.
    /// Null when the device was enrolled via full invitation code only.
    /// </summary>
    public string? ShortPin { get; set; }
}

public sealed class EnrollmentInvitation
{
    public string InvitationId { get; set; } = string.Empty;
    public string OrgId { get; set; } = string.Empty;
    public string GroupId { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public string? PlaintextCode { get; set; }
    public string CreatedByUserId { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public bool IsRedeemed { get; set; }
    public string? RedeemedByDeviceId { get; set; }
    public bool AttendedRequired { get; set; } = true;
    /// <summary>
    /// Optional 4-digit numeric short PIN stored as plaintext for display/lookup.
    /// Used so operators can verbally share a PIN rather than the full token.
    /// </summary>
    public string? ShortPin { get; set; }
}

public sealed class PolicyRule
{
    public string RuleId { get; set; } = string.Empty;
    public string OrgId { get; set; } = string.Empty;
    public string GroupId { get; set; } = string.Empty;
    public UserRole MinimumRole { get; set; } = UserRole.Operator;
    public bool AttendedRequired { get; set; } = true;
    public TimeSpan AllowedTimeStart { get; set; } = TimeSpan.Zero;
    public TimeSpan AllowedTimeEnd { get; set; } = TimeSpan.FromHours(24);
    public SessionCapabilities PermittedCapabilities { get; set; } = SessionCapabilities.View | SessionCapabilities.Input;
}

public sealed class SessionAuthorizationTicket
{
    public string SessionId { get; set; } = string.Empty;
    public string OrgId { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public string OperatorId { get; set; } = string.Empty;
    public SessionCapabilities PermittedCapabilities { get; set; }
    public DateTime IssuedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public string SignatureBase64 { get; set; } = string.Empty;

    public string ComputeSignaturePayload() =>
        $"{SessionId}:{OrgId}:{DeviceId}:{OperatorId}:{(int)PermittedCapabilities}:{IssuedAtUtc.Ticks}:{ExpiresAtUtc.Ticks}";
}

public sealed class AuditEvent
{
    public string EventId { get; set; } = string.Empty;
    public string OrgId { get; set; } = string.Empty;
    public DateTime TimestampUtc { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string? OperatorId { get; set; }
    public string? DeviceId { get; set; }
    public string Details { get; set; } = string.Empty;
    public string ClientIp { get; set; } = string.Empty;
}
