using System;

namespace EnterpriseRemote.Common.Models;

public enum UserRole
{
    Operator,
    Approver,
    Administrator,
    Auditor
}

[Flags]
public enum SessionCapabilities
{
    None = 0,
    View = 1,
    Input = 2,
    Clipboard = 4,
    FileTransfer = 8,
    AdminOperations = 16
}

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
}

public sealed class EnrollmentInvitation
{
    public string InvitationId { get; set; } = string.Empty;
    public string OrgId { get; set; } = string.Empty;
    public string GroupId { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public string CreatedByUserId { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public bool IsRedeemed { get; set; }
    public string? RedeemedByDeviceId { get; set; }
    public bool AttendedRequired { get; set; } = true;
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
