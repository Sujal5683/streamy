using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace EnterpriseRemote.ControlPlane.Services;

public class MasterKeyRecord
{
    public string KeyId { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string SecretValue { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string TargetScope { get; set; } = "ALL";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string? CreatedByUserId { get; set; }
}

public record CreateMasterKeyRequest(string Name, string SecretValue, string? Description, string? TargetScope, string? UserId);

public static class MasterKeysService
{
    private static readonly object LockObj = new();
    private static readonly string StorageFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EnterpriseRemote",
        "master_keys.json");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    static MasterKeysService()
    {
        EnsureInitialized();
    }

    private static void EnsureInitialized()
    {
        lock (LockObj)
        {
            try
            {
                string dir = Path.GetDirectoryName(StorageFilePath)!;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                if (!File.Exists(StorageFilePath))
                {
                    var defaults = new List<MasterKeyRecord>
                    {
                        new()
                        {
                            KeyId = "perm-master-001",
                            Name = "Default IT Master Unattended Key",
                            SecretValue = "PERM-MASTER-UNIVERSAL-2026",
                            Description = "Universal permanent access key across all company endpoints. Never expires.",
                            TargetScope = "ALL",
                            IsActive = true,
                            CreatedAtUtc = DateTime.UtcNow,
                            CreatedByUserId = "USR-ADMIN-01"
                        },
                        new()
                        {
                            KeyId = "perm-master-002",
                            Name = "Universal Admin Quick Key",
                            SecretValue = "PERM-ADMIN-PAIR-ALL",
                            Description = "Quick pairing permanent key for field laptops and engineering workstations.",
                            TargetScope = "ALL",
                            IsActive = true,
                            CreatedAtUtc = DateTime.UtcNow,
                            CreatedByUserId = "USR-ADMIN-01"
                        }
                    };
                    File.WriteAllText(StorageFilePath, JsonSerializer.Serialize(defaults, JsonOpts));
                }
            }
            catch { }
        }
    }

    public static List<MasterKeyRecord> GetKeys()
    {
        lock (LockObj)
        {
            try
            {
                if (File.Exists(StorageFilePath))
                {
                    string json = File.ReadAllText(StorageFilePath);
                    var list = JsonSerializer.Deserialize<List<MasterKeyRecord>>(json);
                    if (list != null) return list;
                }
            }
            catch { }
            return new List<MasterKeyRecord>();
        }
    }

    public static MasterKeyRecord AddOrUpdate(CreateMasterKeyRequest req)
    {
        lock (LockObj)
        {
            var keys = GetKeys();
            string trimmedSecret = req.SecretValue.Trim();

            var existing = keys.FirstOrDefault(k => string.Equals(k.SecretValue.Trim(), trimmedSecret, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                existing.Name = req.Name;
                existing.Description = req.Description;
                existing.IsActive = true;
                Save(keys);
                return existing;
            }

            var newKey = new MasterKeyRecord
            {
                KeyId = Guid.NewGuid().ToString("N"),
                Name = req.Name,
                SecretValue = trimmedSecret,
                Description = req.Description,
                TargetScope = req.TargetScope ?? "ALL",
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow,
                CreatedByUserId = req.UserId ?? "USR-ADMIN-01"
            };
            keys.Insert(0, newKey);
            Save(keys);
            return newKey;
        }
    }

    public static bool Toggle(string keyId)
    {
        lock (LockObj)
        {
            var keys = GetKeys();
            var target = keys.FirstOrDefault(k => string.Equals(k.KeyId, keyId, StringComparison.OrdinalIgnoreCase));
            if (target == null) return false;

            target.IsActive = !target.IsActive;
            Save(keys);
            return true;
        }
    }

    public static bool Delete(string keyId)
    {
        lock (LockObj)
        {
            var keys = GetKeys();
            int removed = keys.RemoveAll(k => string.Equals(k.KeyId, keyId, StringComparison.OrdinalIgnoreCase));
            if (removed > 0)
            {
                Save(keys);
                return true;
            }
            return false;
        }
    }

    public static bool IsValidMasterKey(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        string trimmed = token.Trim();
        var keys = GetKeys();
        return keys.Any(k => k.IsActive && string.Equals(k.SecretValue.Trim(), trimmed, StringComparison.OrdinalIgnoreCase));
    }

    private static void Save(List<MasterKeyRecord> keys)
    {
        try
        {
            string json = JsonSerializer.Serialize(keys, JsonOpts);
            File.WriteAllText(StorageFilePath, json);
        }
        catch { }
    }
}

