using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using EnterpriseRemote.Common.Models;

namespace EnterpriseRemote.ControlPlane.Storage;

public interface IControlPlaneStore
{
    void SaveDevice(EnrolledDevice device);
    EnrolledDevice? GetDevice(string deviceId);
    IReadOnlyList<EnrolledDevice> GetDevicesForOrg(string orgId, IEnumerable<string>? groupFilter = null);
    
    void SaveInvitation(EnrollmentInvitation invitation);
    EnrollmentInvitation? FindInvitationByHash(string tokenHash);
    bool TryRedeemInvitation(string tokenHash, string deviceId);

    void SavePolicy(PolicyRule policy);
    PolicyRule? GetPolicyForGroup(string orgId, string groupId);

    void SaveUser(UserIdentity user);
    UserIdentity? GetUser(string userId);
    UserIdentity? GetUserByEmail(string email);

    void SaveDeviceGroup(DeviceGroup group);
    IReadOnlyList<DeviceGroup> GetGroupsForOrg(string orgId);

    void AppendAudit(AuditEvent auditEvent);
    IReadOnlyList<AuditEvent> GetAuditEvents(string orgId, string? deviceId = null, string? operatorId = null, int limit = 100);
}

public sealed class MemoryControlPlaneStore : IControlPlaneStore
{
    private readonly ConcurrentDictionary<string, EnrolledDevice> _devices = new();
    private readonly ConcurrentDictionary<string, EnrollmentInvitation> _invitations = new();
    private readonly ConcurrentDictionary<string, PolicyRule> _policies = new();
    private readonly ConcurrentDictionary<string, UserIdentity> _users = new();
    private readonly ConcurrentDictionary<string, DeviceGroup> _groups = new();
    private readonly List<AuditEvent> _auditLog = new();
    private readonly object _auditLock = new();
    private readonly object _invitationLock = new();

    public void SaveDevice(EnrolledDevice device)
    {
        _devices[device.DeviceId] = device;
    }

    public EnrolledDevice? GetDevice(string deviceId)
    {
        _devices.TryGetValue(deviceId, out var device);
        return device;
    }

    public IReadOnlyList<EnrolledDevice> GetDevicesForOrg(string orgId, IEnumerable<string>? groupFilter = null)
    {
        var query = _devices.Values.Where(d => d.OrgId.Equals(orgId, StringComparison.OrdinalIgnoreCase));
        if (groupFilter != null && groupFilter.Any())
        {
            var allowedGroups = new HashSet<string>(groupFilter, StringComparer.OrdinalIgnoreCase);
            query = query.Where(d => allowedGroups.Contains(d.GroupId));
        }
        return query.OrderBy(d => d.Hostname).ToList();
    }

    public void SaveInvitation(EnrollmentInvitation invitation)
    {
        _invitations[invitation.InvitationId] = invitation;
    }

    public EnrollmentInvitation? FindInvitationByHash(string tokenHash)
    {
        return _invitations.Values.FirstOrDefault(i => i.TokenHash.Equals(tokenHash, StringComparison.OrdinalIgnoreCase));
    }

    public bool TryRedeemInvitation(string tokenHash, string deviceId)
    {
        lock (_invitationLock)
        {
            var invitation = FindInvitationByHash(tokenHash);
            if (invitation == null || invitation.IsRedeemed || invitation.ExpiresAtUtc < DateTime.UtcNow)
            {
                return false;
            }

            invitation.IsRedeemed = true;
            invitation.RedeemedByDeviceId = deviceId;
            return true;
        }
    }

    public void SavePolicy(PolicyRule policy)
    {
        _policies[policy.GroupId] = policy;
    }

    public PolicyRule? GetPolicyForGroup(string orgId, string groupId)
    {
        _policies.TryGetValue(groupId, out var policy);
        return policy;
    }

    public void SaveUser(UserIdentity user)
    {
        _users[user.UserId] = user;
    }

    public UserIdentity? GetUser(string userId)
    {
        _users.TryGetValue(userId, out var user);
        return user;
    }

    public UserIdentity? GetUserByEmail(string email)
    {
        return _users.Values.FirstOrDefault(u => u.Email.Equals(email, StringComparison.OrdinalIgnoreCase));
    }

    public void SaveDeviceGroup(DeviceGroup group)
    {
        _groups[group.GroupId] = group;
    }

    public IReadOnlyList<DeviceGroup> GetGroupsForOrg(string orgId)
    {
        return _groups.Values.Where(g => g.OrgId.Equals(orgId, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    public void AppendAudit(AuditEvent auditEvent)
    {
        lock (_auditLock)
        {
            _auditLog.Add(auditEvent);
        }
    }

    public IReadOnlyList<AuditEvent> GetAuditEvents(string orgId, string? deviceId = null, string? operatorId = null, int limit = 100)
    {
        lock (_auditLock)
        {
            var query = _auditLog.Where(a => a.OrgId.Equals(orgId, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(deviceId))
            {
                query = query.Where(a => a.DeviceId != null && a.DeviceId.Equals(deviceId, StringComparison.OrdinalIgnoreCase));
            }
            if (!string.IsNullOrEmpty(operatorId))
            {
                query = query.Where(a => a.OperatorId != null && a.OperatorId.Equals(operatorId, StringComparison.OrdinalIgnoreCase));
            }
            return query.OrderByDescending(a => a.TimestampUtc).Take(limit).ToList();
        }
    }
}
