import os

def update_store():
    path = 'src/EnterpriseRemote.ControlPlane/Storage/IControlPlaneStore.cs'
    with open(path, 'r', encoding='utf-8') as f:
        content = f.read().replace('\r\n', '\n')

    interface_target = '''    void AppendAudit(AuditEvent auditEvent);
    IReadOnlyList<AuditEvent> GetAuditEvents(string orgId, string? deviceId = null, string? operatorId = null, int limit = 100);
}'''

    interface_repl = '''    void AppendAudit(AuditEvent auditEvent);
    IReadOnlyList<AuditEvent> GetAuditEvents(string orgId, string? deviceId = null, string? operatorId = null, int limit = 100);

    void SaveMasterKey(PermanentMasterKey key);
    PermanentMasterKey? GetMasterKey(string keyId);
    IReadOnlyList<PermanentMasterKey> GetMasterKeys(string orgId);
    bool DeleteMasterKey(string keyId);
    bool ValidateMasterKey(string secret, string? orgId = null);
}'''

    store_data_target = '''public sealed class PersistedStoreData
{
    public List<EnrolledDevice> Devices { get; set; } = new();
    public List<EnrollmentInvitation> Invitations { get; set; } = new();
}'''

    store_data_repl = '''public sealed class PersistedStoreData
{
    public List<EnrolledDevice> Devices { get; set; } = new();
    public List<EnrollmentInvitation> Invitations { get; set; } = new();
    public List<PermanentMasterKey> MasterKeys { get; set; } = new();
}'''

    persist_target = '''    private static string GetDefaultPersistPath()
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
    }'''

    persist_repl = '''    private static string GetDefaultPersistPath()
    {
        try
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string localPath = Path.Combine(baseDir, "controlplane_store.json");

            // If a local store already exists or directory is writable, keep store isolated here!
            if (File.Exists(localPath)) return localPath;

            try
            {
                string testFile = Path.Combine(baseDir, $".write_test_{Guid.NewGuid():N}");
                File.WriteAllText(testFile, "ok");
                File.Delete(testFile);
                return localPath;
            }
            catch { }

            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            string dir = Path.Combine(programData, "EnterpriseRemote");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "controlplane_store.json");
        }
        catch
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "controlplane_store.json");
        }
    }'''

    import_target = '''            string[] possibleIdentityFiles = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "device_identity.json"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "EnterpriseRemote", "device_identity.json"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "device_identity.json"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "device_identity.json"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "device_identity.json")
            };'''

    import_repl = '''            string[] possibleIdentityFiles = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "device_identity.json"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "device_identity.json"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "EnterpriseRemote", "device_identity.json")
            };'''

    members_target = '''    private readonly ConcurrentDictionary<string, DeviceGroup> _groups = new();'''
    members_repl = '''    private readonly ConcurrentDictionary<string, DeviceGroup> _groups = new();
    private readonly ConcurrentDictionary<string, PermanentMasterKey> _masterKeys = new();'''

    load_target = '''                        foreach (var i in data.Invitations) _invitations[i.InvitationId] = i;
                    }'''
    load_repl = '''                        foreach (var i in data.Invitations) _invitations[i.InvitationId] = i;
                    }
                    if (data?.MasterKeys != null)
                    {
                        foreach (var k in data.MasterKeys) _masterKeys[k.KeyId] = k;
                    }'''

    init_target = '''        TryImportLocalDeviceIdentity();
    }'''
    init_repl = '''        TryImportLocalDeviceIdentity();
        EnsureDefaultMasterKey();
    }'''

    save_target = '''                var snapshot = new PersistedStoreData
                {
                    Devices = _devices.Values.ToList(),
                    Invitations = _invitations.Values.ToList()
                };'''
    save_repl = '''                var snapshot = new PersistedStoreData
                {
                    Devices = _devices.Values.ToList(),
                    Invitations = _invitations.Values.ToList(),
                    MasterKeys = _masterKeys.Values.ToList()
                };'''

    methods_target = '''            return query.OrderByDescending(a => a.TimestampUtc).Take(limit).ToList();
        }
    }
}'''
    methods_repl = '''            return query.OrderByDescending(a => a.TimestampUtc).Take(limit).ToList();
        }
    }

    private void EnsureDefaultMasterKey()
    {
        if (_masterKeys.IsEmpty)
        {
            var defaultKey = new PermanentMasterKey
            {
                KeyId = "KEY-CORP-MASTER-01",
                OrgId = "ORG-ENTERPRISE",
                Name = "Primary Universal Master Key",
                SecretValue = "PERM-MASTER-CORP-2026",
                CreatedBy = "admin@corp.internal",
                CreatedAtUtc = DateTime.UtcNow,
                IsActive = true,
                Description = "Universal permanent access key for all enterprise workstations"
            };
            _masterKeys[defaultKey.KeyId] = defaultKey;
            SaveToDisk();
        }
    }

    public void SaveMasterKey(PermanentMasterKey key)
    {
        _masterKeys[key.KeyId] = key;
        SaveToDisk();
    }

    public PermanentMasterKey? GetMasterKey(string keyId)
    {
        _masterKeys.TryGetValue(keyId, out var key);
        return key;
    }

    public IReadOnlyList<PermanentMasterKey> GetMasterKeys(string orgId)
    {
        return _masterKeys.Values
            .Where(k => string.IsNullOrEmpty(orgId) || k.OrgId.Equals(orgId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(k => k.CreatedAtUtc)
            .ToList();
    }

    public bool DeleteMasterKey(string keyId)
    {
        bool removed = _masterKeys.TryRemove(keyId, out _);
        if (removed) SaveToDisk();
        return removed;
    }

    public bool ValidateMasterKey(string secret, string? orgId = null)
    {
        if (string.IsNullOrWhiteSpace(secret)) return false;
        string trimmed = secret.Trim();
        return _masterKeys.Values.Any(k =>
            k.IsActive &&
            (orgId == null || string.IsNullOrEmpty(k.OrgId) || k.OrgId.Equals(orgId, StringComparison.OrdinalIgnoreCase)) &&
            string.Equals(k.SecretValue.Trim(), trimmed, StringComparison.OrdinalIgnoreCase));
    }
}'''

    for t, r, name in [
        (interface_target, interface_repl, "interface"),
        (store_data_target, store_data_repl, "store_data"),
        (persist_target, persist_repl, "persist"),
        (import_target, import_repl, "import"),
        (members_target, members_repl, "members"),
        (load_target, load_repl, "load"),
        (init_target, init_repl, "init"),
        (save_target, save_repl, "save"),
        (methods_target, methods_repl, "methods")
    ]:
        if t in content:
            content = content.replace(t, r)
            print(f"[+] Replaced {name}")
        else:
            print(f"[-] Target not found: {name}")

    with open(path, 'w', encoding='utf-8') as f:
        f.write(content.replace('\n', '\r\n'))

def update_controller_usings():
    path = 'src/EnterpriseRemote.Controller/UI/RemoteViewerForm.cs'
    with open(path, 'r', encoding='utf-8') as f:
        content = f.read().replace('\r\n', '\n')

    if 'using EnterpriseRemote.Common.Network;' not in content:
        content = 'using EnterpriseRemote.Common.Network;\n' + content
        print('[+] Added using EnterpriseRemote.Common.Network to RemoteViewerForm.cs')

    with open(path, 'w', encoding='utf-8') as f:
        f.write(content.replace('\n', '\r\n'))

if __name__ == '__main__':
    update_store()
    update_controller_usings()

