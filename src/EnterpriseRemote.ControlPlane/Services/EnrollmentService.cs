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
    string? PermanentAccessToken,
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
        string shortPin = CryptoUtils.GenerateShortPin();

        var now = DateTime.UtcNow;
        var invitation = new EnrollmentInvitation
        {
            InvitationId = Guid.NewGuid().ToString("N"),
            OrgId = orgId,
            GroupId = groupId,
            TokenHash = tokenHash,
            PlaintextCode = rawCode,
            CreatedByUserId = createdByUserId,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddMinutes(ttlMinutes),
            IsRedeemed = false,
            AttendedRequired = attendedRequired,
            ShortPin = shortPin
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
            return new EnrollmentRedemptionResult(false, null, null, "Rate limit exceeded. Too many enrollment attempts.");
        }

        // 2. Validate Public Key Format
        if (string.IsNullOrWhiteSpace(publicKeyPem) || !publicKeyPem.Contains("PUBLIC KEY"))
        {
            return new EnrollmentRedemptionResult(false, null, null, "Invalid device cryptographic public key.");
        }

        // 3. Resolve invitation — match by full code hash, prefix-added hash, short PIN, plaintext code, or suffix
        string trimmedCode = invitationCode.Trim();
        string upperCode = trimmedCode.ToUpperInvariant();
        string withPrefix = upperCode.StartsWith("INV-", StringComparison.OrdinalIgnoreCase)
            ? upperCode
            : $"INV-{upperCode}";

        EnrollmentInvitation? invitation = null;
        bool redeemedByShortPin = false;

        // Attempt 1: standard full-token hash lookup
        invitation = _store.FindInvitationByHash(CryptoUtils.ComputeSha256(trimmedCode));

        // Attempt 1b: uppercase hash lookup
        if (invitation == null && trimmedCode != upperCode)
        {
            invitation = _store.FindInvitationByHash(CryptoUtils.ComputeSha256(upperCode));
        }

        // Attempt 1c: hash with INV- prepended (if user entered without INV-)
        if (invitation == null)
        {
            invitation = _store.FindInvitationByHash(CryptoUtils.ComputeSha256(withPrefix));
        }

        // Attempt 2: short PIN or suffix lookup (numeric or alphanumeric, 4+ chars)
        if (invitation == null)
        {
            invitation = _store.FindInvitationByShortPin(trimmedCode);
        }
        if (invitation == null && trimmedCode != upperCode)
        {
            invitation = _store.FindInvitationByShortPin(upperCode);
        }

        // Attempt 3: direct store hash / plaintext code lookup
        if (invitation == null)
        {
            invitation = _store.FindInvitationByHash(trimmedCode);
        }

        if (invitation == null)
        {
            RecordAttempt(clientIp);
            return new EnrollmentRedemptionResult(false, null, null, "Invalid enrollment invitation code.");
        }

        if (invitation.IsRedeemed)
        {
            RecordAttempt(clientIp);
            return new EnrollmentRedemptionResult(false, null, null, "Enrollment invitation has already been redeemed.");
        }

        if (invitation.ExpiresAtUtc < DateTime.UtcNow)
        {
            RecordAttempt(clientIp);
            return new EnrollmentRedemptionResult(false, null, null, "Enrollment invitation has expired.");
        }

        if (!string.IsNullOrEmpty(invitation.ShortPin) &&
            (string.Equals(trimmedCode, invitation.ShortPin, StringComparison.OrdinalIgnoreCase) || trimmedCode.Length == 4))
        {
            redeemedByShortPin = true;
        }

        // Use the invitation's canonical TokenHash for TryRedeemInvitation
        string tokenHash = invitation.TokenHash;

        // 4. Generate Unique Device ID & Cryptographic Registration with Permanent Access Token
        string deviceId = $"DEV-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";

        // When redeemed by ShortPin the permanent token IS the ShortPin (easy to type for attended sessions)
        string permToken = redeemedByShortPin
            ? invitation.ShortPin!
            : $"PERM-{Guid.NewGuid().ToString("N")[..12].ToUpperInvariant()}";

        bool redeemed = _store.TryRedeemInvitation(tokenHash, deviceId);
        if (!redeemed)
        {
            return new EnrollmentRedemptionResult(false, null, null, "Concurrent redemption conflict. Try again.");
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
            Status = DeviceStatus.Online,
            PermanentAccessToken = permToken,
            ShortPin = invitation.ShortPin
        };

        _store.SaveDevice(device);

        _store.AppendAudit(new AuditEvent
        {
            EventId = Guid.NewGuid().ToString("N"),
            OrgId = invitation.OrgId,
            TimestampUtc = DateTime.UtcNow,
            EventType = "DeviceEnrolled",
            DeviceId = deviceId,
            Details = $"Device {hostname} successfully enrolled with permanent unattended access key into group {invitation.GroupId}",
            ClientIp = clientIp
        });

        return new EnrollmentRedemptionResult(true, deviceId, permToken, null);
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
