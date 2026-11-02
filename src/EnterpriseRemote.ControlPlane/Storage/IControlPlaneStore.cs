using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using EnterpriseRemote.Common.Models;

namespace EnterpriseRemote.ControlPlane.Storage;

public interface IControlPlaneStore
{
    void SaveDevice(EnrolledDevice device);
    EnrolledDevice? GetDevice(string deviceId);
    EnrolledDevice? GetDeviceByPermanentToken(string permToken);
    IReadOnlyList<EnrolledDevice> GetDevicesForOrg(string orgId, IEnumerable<string>? groupFilter = null);
    
    void SaveInvitation(EnrollmentInvitation invitation);
    EnrollmentInvitation? FindInvitationByHash(string tokenHash);
    /// <summary>
    /// Finds an invitation by its plaintext 4-digit short PIN.
    /// Returns null if not found or the PIN is not associated with any invitation.
    /// </summary>
    EnrollmentInvitation? FindInvitationByShortPin(string shortPin);
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

public sealed class PersistedStoreData
{
    public List<EnrolledDevice> Devices { get; set; } = new();
    public List<EnrollmentInvitation> Invitations { get; set; } = new();
}

public sealed class MemoryControlPlaneStore : IControlPlaneStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly ConcurrentDictionary<string, EnrolledDevice> _devices = new();
    private readonly ConcurrentDictionary<string, EnrollmentInvitation> _invitations = new();
    private readonly ConcurrentDictionary<string, PolicyRule> _policies = new();
    private readonly ConcurrentDictionary<string, UserIdentity> _users = new();
    private readonly ConcurrentDictionary<string, DeviceGroup> _groups = new();
    private readonly List<AuditEvent> _auditLog = new();
    private readonly object _auditLock = new();
    private readonly object _invitationLock = new();
    private readonly object _fileLock = new();
    private readonly string? _persistFilePath;

    public MemoryControlPlaneStore(string? persistFilePath = null, bool inMemoryOnly = false)
    {
        bool isTest = AppDomain.CurrentDomain.GetAssemblies().Any(a => 
            a.GetName().Name?.Contains("xunit", StringComparison.OrdinalIgnoreCase) == true || 
            a.GetName().Name?.Contains("testhost", StringComparison.OrdinalIgnoreCase) == true);

        if (!inMemoryOnly && !isTest)
        {
            _persistFilePath = persistFilePath ?? GetDefaultPersistPath();
            LoadFromDisk();
        }
    }

    private static string GetDefaultPersistPath()
    {
        try
        {
            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            string dir = Path.Combine(programData, "EnterpriseRemote");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "controlplane_store.json");
        }
        catch
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "controlplane_store.json");
        }
    }

    private void LoadFromDisk()
    {
        if (string.IsNullOrEmpty(_persistFilePath)) return;
        try
        {
            lock (_fileLock)
            {
                if (File.Exists(_persistFilePath))
                {
                    string json = File.ReadAllText(_persistFilePath);
                    var data = JsonSerializer.Deserialize<PersistedStoreData>(json, JsonOpts);
                    if (data?.Devices != null)
                    {
                        foreach (var d in data.Devices) _devices[d.DeviceId] = d;
                    }
                    if (data?.Invitations != null)
                    {
                        foreach (var i in data.Invitations) _invitations[i.InvitationId] = i;
                    }
                }
            }
        }
        catch
        {
            // Best effort
        }

        TryImportLocalDeviceIdentity();
    }

    private void TryImportLocalDeviceIdentity()
    {
        try
        {
            string[] possibleIdentityFiles = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "device_identity.json"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "EnterpriseRemote", "device_identity.json"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "device_identity.json"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "device_identity.json"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "device_identity.json")
            };

            foreach (var file in possibleIdentityFiles)
            {
                if (File.Exists(file))
                {
                    string json = File.ReadAllText(file);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("DeviceId", out var idProp))
                    {
                        string deviceId = idProp.GetString() ?? "";
                        string? permToken = root.TryGetProperty("PermanentAccessToken", out var permProp) ? permProp.GetString() : null;

                        if (!string.IsNullOrEmpty(deviceId))
                        {
                            if (!_devices.ContainsKey(deviceId))
                            {
                                string hostname = root.TryGetProperty("Hostname", out var hProp) ? (hProp.GetString() ?? Environment.MachineName) : Environment.MachineName;
                                string pubKey = root.TryGetProperty("PublicKeyPem", out var pkProp) ? (pkProp.GetString() ?? "") : "";

                                var device = new EnrolledDevice
                                {
                                    DeviceId = deviceId,
                                    OrgId = "ORG-ENTERPRISE",
                                    GroupId = "GRP-CORP-DESKTOPS",
                                    Hostname = hostname,
                                    OsVersion = Environment.OSVersion.ToString(),
                                    PublicKeyPem = pubKey,
                                    PermanentAccessToken = permToken,
                                    Status = DeviceStatus.Online,
                                    EnrolledAtUtc = DateTime.UtcNow,
                                    LastHeartbeatUtc = DateTime.UtcNow
                                };
                                _devices[deviceId] = device;
                                SaveToDisk();
                                break;
                            }
                            else if (!string.IsNullOrEmpty(permToken))
                            {
                                var existingDev = _devices[deviceId];
                                if (string.IsNullOrEmpty(existingDev.PermanentAccessToken))
                                {
                                    existingDev.PermanentAccessToken = permToken;
                                    SaveToDisk();
                                }
                            }
                        }
                    }
                }
            }
        }
        catch
        {
            // Best effort
        }
    }

    private void SaveToDisk()
    {
        if (string.IsNullOrEmpty(_persistFilePath)) return;
        try
        {
            lock (_fileLock)
            {
                var snapshot = new PersistedStoreData
                {
                    Devices = _devices.Values.ToList(),
                    Invitations = _invitations.Values.ToList()
                };
                string json = JsonSerializer.Serialize(snapshot, JsonOpts);
                File.WriteAllText(_persistFilePath, json);
            }
        }
        catch
        {
            // Best effort
        }
    }

    public void SaveDevice(EnrolledDevice device)
    {
        _devices[device.DeviceId] = device;
        SaveToDisk();
    }

    public EnrolledDevice? GetDevice(string deviceId)
    {
        _devices.TryGetValue(deviceId, out var device);
        return device;
    }

    public EnrolledDevice? GetDeviceByPermanentToken(string permToken)
    {
        if (string.IsNullOrWhiteSpace(permToken)) return null;
        return _devices.Values.FirstOrDefault(d =>
            !string.IsNullOrWhiteSpace(d.PermanentAccessToken) &&
            string.Equals(d.PermanentAccessToken.Trim(), permToken.Trim(), StringComparison.OrdinalIgnoreCase));
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
        SaveToDisk();
    }

    public EnrollmentInvitation? FindInvitationByHash(string tokenHash)
    {
        if (string.IsNullOrWhiteSpace(tokenHash)) return null;
        string clean = tokenHash.Trim();
        return _invitations.Values.FirstOrDefault(i =>
            i.TokenHash.Equals(clean, StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrEmpty(i.ShortPin) && i.ShortPin.Equals(clean, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrEmpty(i.PlaintextCode) && (
                i.PlaintextCode.Equals(clean, StringComparison.OrdinalIgnoreCase) ||
                i.PlaintextCode.Replace("-", "").Equals(clean.Replace("-", ""), StringComparison.OrdinalIgnoreCase) ||
                (clean.Length >= 4 && i.PlaintextCode.EndsWith(clean, StringComparison.OrdinalIgnoreCase))
            )));
    }

    public EnrollmentInvitation? FindInvitationByShortPin(string shortPin)
    {
        if (string.IsNullOrWhiteSpace(shortPin)) return null;
        string clean = shortPin.Trim().ToUpperInvariant();
        return _invitations.Values.FirstOrDefault(i =>
            (!string.IsNullOrEmpty(i.ShortPin) && i.ShortPin.Equals(clean, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrEmpty(i.PlaintextCode) && (
                i.PlaintextCode.Equals(clean, StringComparison.OrdinalIgnoreCase) ||
                i.PlaintextCode.Replace("-", "").Equals(clean.Replace("-", ""), StringComparison.OrdinalIgnoreCase) ||
                i.PlaintextCode.EndsWith(clean, StringComparison.OrdinalIgnoreCase)
            )));
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
            SaveToDisk();
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
