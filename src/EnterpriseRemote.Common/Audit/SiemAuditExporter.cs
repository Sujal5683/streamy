using System;
using System.Text.Json;
using EnterpriseRemote.Common.Models;
using EnterpriseRemote.Common.Redaction;

namespace EnterpriseRemote.Common.Audit;

public static class SiemAuditExporter
{
    /// <summary>
    /// Formats an audit event as structured JSON with sensitive data redacted.
    /// </summary>
    public static string ToJson(AuditEvent auditEvent)
    {
        var sanitized = new
        {
            auditEvent.EventId,
            auditEvent.OrgId,
            Timestamp = auditEvent.TimestampUtc.ToString("o"),
            auditEvent.EventType,
            auditEvent.OperatorId,
            auditEvent.DeviceId,
            Details = SensitiveDataRedactor.Redact(auditEvent.Details),
            auditEvent.ClientIp
        };

        return JsonSerializer.Serialize(sanitized);
    }

    /// <summary>
    /// Formats an audit event into Common Event Format (CEF) standard.
    /// Format: CEF:Version|Device Vendor|Device Product|Device Version|Device Event Class ID|Name|Severity|[Extension]
    /// </summary>
    public static string ToCef(AuditEvent auditEvent)
    {
        string sanitizedDetails = SensitiveDataRedactor.Redact(auditEvent.Details);
        string cefExtension = $"dvc={auditEvent.DeviceId ?? "none"} " +
                              $"suser={auditEvent.OperatorId ?? "system"} " +
                              $"src={auditEvent.ClientIp} " +
                              $"msg={sanitizedDetails.Replace("=", "\\=")} " +
                              $"cs1={auditEvent.OrgId} cs1Label=OrgId";

        return $"CEF:0|EnterpriseRemote|RemoteAccessPlatform|1.0|{auditEvent.EventType}|{auditEvent.EventType}|5|{cefExtension}";
    }

    /// <summary>
    /// Formats an audit event into RFC 5424 Syslog standard.
    /// Format: <PRI>VERSION TIMESTAMP HOSTNAME APP-NAME PROCID MSGID [STRUCTURED-DATA] MSG
    /// </summary>
    public static string ToRfc5424Syslog(AuditEvent auditEvent, string hostname = "control-plane")
    {
        string timestamp = auditEvent.TimestampUtc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");
        string sanitizedDetails = SensitiveDataRedactor.Redact(auditEvent.Details);
        string structuredData = $"[remoteAccess@48201 eventId=\"{auditEvent.EventId}\" orgId=\"{auditEvent.OrgId}\" op=\"{auditEvent.OperatorId ?? "none"}\" dev=\"{auditEvent.DeviceId ?? "none"}\"]";

        // PRI 134 = Facility local0 (16*8=128) + Severity Informational (6)
        return $"<134>1 {timestamp} {hostname} EnterpriseRemote 1 {auditEvent.EventType} {structuredData} {sanitizedDetails}";
    }
}
