using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using EnterpriseRemote.Common.Cryptography;
using EnterpriseRemote.Common.Models;
using EnterpriseRemote.ControlPlane.Storage;

namespace EnterpriseRemote.ControlPlane.Services;

public readonly record struct EnrollmentRedemptionResult(
    bool Success,
    string? DeviceId,
    string? ErrorMessage);

public sealed class EnrollmentService
{
    private readonly IControlPlaneStore _store;
    private readonly ConcurrentDictionary<string, List<DateTime>> _ipAttempts = new();
    private readonly object _rateLimitLock = new();
    private const int MaxAttemptsPerMinute = 5;

    public EnrollmentService(IControlPlaneStore store)
    {
        _store = store;
    }

    public (EnrollmentInvitation Invitation, string PlaintextCode) CreateInvitation(
        string orgId,
        string groupId,
        string createdByUserId,
        int ttlMinutes = 15,
        bool attendedRequired = true)
    {
        string rawCode = CryptoUtils.GenerateInvitationCode();
        string tokenHash = CryptoUtils.ComputeSha256(rawCode);

        var now = DateTime.UtcNow;
        var invitation = new EnrollmentInvitation
        {
            InvitationId = Guid.NewGuid().ToString("N"),
            OrgId = orgId,
            GroupId = groupId,
            TokenHash = tokenHash,
            CreatedByUserId = createdByUserId,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddMinutes(ttlMinutes),
            IsRedeemed = false,
            AttendedRequired = attendedRequired
        };

        _store.SaveInvitation(invitation);

        _store.AppendAudit(new AuditEvent
        {
            EventId = Guid.NewGuid().ToString("N"),
            OrgId = orgId,
            TimestampUtc = now,
            EventType = "InvitationCreated",
            OperatorId = createdByUserId,
            Details = $"Invitation {invitation.InvitationId} created for group {groupId} (TTL: {ttlMinutes}m)",
            ClientIp = "internal"
        });

        return (invitation, rawCode);
    }

    public EnrollmentRedemptionResult RedeemInvitation(
        string invitationCode,
        string hostname,
        string osVersion,
        string macAddress,
        string publicKeyPem,
        string clientIp)
    {
        // 1. Enforce Rate Limiting per Client IP
        if (IsRateLimited(clientIp))
        {
            return new EnrollmentRedemptionResult(false, null, "Rate limit exceeded. Too many enrollment attempts.");
        }

        // 2. Validate Public Key Format
        if (string.IsNullOrWhiteSpace(publicKeyPem) || !publicKeyPem.Contains("PUBLIC KEY"))
        {
            return new EnrollmentRedemptionResult(false, null, "Invalid device cryptographic public key.");
        }

        // 3. Hash Invitation Token and Check Expiry / Redemption
        string tokenHash = CryptoUtils.ComputeSha256(invitationCode.Trim());
        var invitation = _store.FindInvitationByHash(tokenHash);

        if (invitation == null)
        {
            RecordAttempt(clientIp);
            return new EnrollmentRedemptionResult(false, null, "Invalid enrollment invitation code.");
        }

        if (invitation.IsRedeemed)
        {
            RecordAttempt(clientIp);
            return new EnrollmentRedemptionResult(false, null, "Enrollment invitation has already been redeemed.");
        }

        if (invitation.ExpiresAtUtc < DateTime.UtcNow)
        {
            RecordAttempt(clientIp);
            return new EnrollmentRedemptionResult(false, null, "Enrollment invitation has expired.");
        }

        // 4. Generate Unique Device ID & Cryptographic Registration
        string deviceId = $"DEV-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
        bool redeemed = _store.TryRedeemInvitation(tokenHash, deviceId);
        if (!redeemed)
        {
            return new EnrollmentRedemptionResult(false, null, "Concurrent redemption conflict. Try again.");
        }

        var device = new EnrolledDevice
        {
            DeviceId = deviceId,
            OrgId = invitation.OrgId,
            GroupId = invitation.GroupId,
            Hostname = hostname,
            OsVersion = osVersion,
            MacAddress = macAddress,
            PublicKeyPem = publicKeyPem,
            EnrolledAtUtc = DateTime.UtcNow,
            LastHeartbeatUtc = DateTime.UtcNow,
            Status = DeviceStatus.Online
        };

        _store.SaveDevice(device);

        _store.AppendAudit(new AuditEvent
        {
            EventId = Guid.NewGuid().ToString("N"),
            OrgId = invitation.OrgId,
            TimestampUtc = DateTime.UtcNow,
            EventType = "DeviceEnrolled",
            DeviceId = deviceId,
            Details = $"Device {hostname} successfully enrolled into group {invitation.GroupId}",
            ClientIp = clientIp
        });

        return new EnrollmentRedemptionResult(true, deviceId, null);
    }

    private bool IsRateLimited(string clientIp)
    {
        lock (_rateLimitLock)
        {
            var now = DateTime.UtcNow;
            if (!_ipAttempts.TryGetValue(clientIp, out var list))
            {
                return false;
            }

            list.RemoveAll(t => (now - t).TotalMinutes > 1.0);
            return list.Count >= MaxAttemptsPerMinute;
        }
    }

    private void RecordAttempt(string clientIp)
    {
        lock (_rateLimitLock)
        {
            var now = DateTime.UtcNow;
            var list = _ipAttempts.GetOrAdd(clientIp, _ => new List<DateTime>());
            list.RemoveAll(t => (now - t).TotalMinutes > 1.0);
            list.Add(now);
        }
    }
}
