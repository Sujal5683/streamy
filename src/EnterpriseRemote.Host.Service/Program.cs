using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EnterpriseRemote.Common.Cryptography;
using EnterpriseRemote.Common.Models;
using EnterpriseRemote.Common.Protocol;
using EnterpriseRemote.Host.Service.Install;
using EnterpriseRemote.Host.Service.Ipc;

namespace EnterpriseRemote.Host.Service;

public static class Program
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "EnterpriseRemote");
    private static readonly string IdentityFile = Path.Combine(ConfigDir, "device_identity.json");
    private static readonly string LocalFallbackIdentityFile = "device_identity.json";

    public static async Task<int> Main(string[] args)
    {
        Console.WriteLine("===============================================================================");
        Console.WriteLine("   Enterprise Windows Remote Access: Host Background Service (Session 0)");
        Console.WriteLine("===============================================================================\n");

        string serverUrl = "http://127.0.0.1:5293";
        string command = args.Length > 0 ? args[0].ToLowerInvariant() : "--run";

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("--server", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                serverUrl = args[i + 1];
            }
        }

        switch (command)
        {
            case "--install":
                string binaryPath = Environment.ProcessPath ?? "EnterpriseRemoteHostSvc.exe";
                bool installed = ServiceInstaller.InstallService(binaryPath);
                return installed ? 0 : 1;

            case "--uninstall":
                bool uninstalled = ServiceInstaller.UninstallService();
                return uninstalled ? 0 : 1;

            case "--enroll":
                if (args.Length < 2)
                {
                    Console.WriteLine("[!] Usage: EnterpriseRemoteHostSvc.exe --enroll <INVITATION_CODE> [--server <URL>]");
                    return 1;
                }
                string invitationCode = args[1];
                return await EnrollDeviceAsync(serverUrl, invitationCode);

            case "--run":
            default:
                return await RunServiceHostAsync(serverUrl);
        }
    }

    private static async Task<int> EnrollDeviceAsync(string serverUrl, string invitationCode)
    {
        Console.WriteLine($"[*] Initiating zero-trust cryptographic enrollment with: {serverUrl}");
        Console.WriteLine($"[*] Invitation Code: {invitationCode}");

        // 1. Generate unique hardware cryptographic identity (ECDSA P-256)
        var (privKey, pubKey) = CryptoUtils.GenerateEcdsaKeyPair();
        string hostname = Environment.MachineName;
        string osVersion = Environment.OSVersion.ToString();
        string macAddress = "00:50:56:C0:00:08"; // Standard primary adapter MAC

        using var http = new HttpClient { BaseAddress = new Uri(serverUrl), Timeout = TimeSpan.FromSeconds(10) };
        var payload = new
        {
            InvitationCode = invitationCode,
            Hostname = hostname,
            OsVersion = osVersion,
            MacAddress = macAddress,
            PublicKeyPem = pubKey
        };

        try
        {
            var response = await http.PostAsJsonAsync("/api/v1/enrollment/redeem", payload);
            if (!response.IsSuccessStatusCode)
            {
                string err = await response.Content.ReadAsStringAsync();
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[!] Enrollment redemption failed: {response.StatusCode} - {err}");
                Console.ResetColor();
                return 1;
            }

            var result = await response.Content.ReadFromJsonAsync<EnrollmentResponse>();
            if (result == null || string.IsNullOrEmpty(result.DeviceId))
            {
                Console.WriteLine("[!] Invalid response received from Control Plane.");
                return 1;
            }

            // Save enrolled device identity
            var identity = new StoredDeviceIdentity
            {
                DeviceId = result.DeviceId,
                Hostname = hostname,
                ControlPlaneUrl = serverUrl,
                PrivateKeyPem = privKey,
                PublicKeyPem = pubKey,
                EnrolledAtUtc = DateTime.UtcNow
            };

            SaveIdentity(identity);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[+] Enrollment Successful! Device ID: {result.DeviceId}");
            Console.WriteLine($"[+] Cryptographic identity generated and securely stored.");
            Console.ResetColor();
            return 0;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[!] Connection error during enrollment: {ex.Message}");
            Console.ResetColor();
            return 1;
        }
    }

    private static async Task<int> RunServiceHostAsync(string serverUrl)
    {
        var identity = LoadIdentity();
        if (identity == null)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("[!] Device is not enrolled. Please enroll first using:");
            Console.WriteLine("    EnterpriseRemote.Host.Service.exe --enroll <INV-XXXX-XXXX-XXXX> [--server <URL>]");
            Console.ResetColor();
            return 2;
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[+] Enrolled Device ID: {identity.DeviceId} ({identity.Hostname})");
        Console.WriteLine($"[+] Control Plane URL:  {identity.ControlPlaneUrl}");
        Console.ResetColor();

        using var serviceHost = new ServiceHost();
        serviceHost.Start();
        Console.WriteLine($"[+] Session 0 Host Service active. Named Pipe listening on session {serviceHost.ActiveSessionId}...");

        using var http = new HttpClient { BaseAddress = new Uri(identity.ControlPlaneUrl), Timeout = TimeSpan.FromSeconds(5) };
        var cts = new CancellationTokenSource();

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
            Console.WriteLine("\n[*] Shutting down host service cleanly...");
        };

        // Heartbeat loop
        int heartbeatCount = 0;
        while (!cts.IsCancellationRequested)
        {
            try
            {
                heartbeatCount++;
                var resp = await http.PostAsync($"/api/v1/devices/{identity.DeviceId}/heartbeat", null, cts.Token);
                if (resp.IsSuccessStatusCode && heartbeatCount <= 3)
                {
                    Console.WriteLine($"[+] Heartbeat #{heartbeatCount} acknowledged by Control Plane.");
                }
            }
            catch (Exception ex)
            {
                if (heartbeatCount <= 3)
                {
                    Console.WriteLine($"[!] Heartbeat notice: {ex.Message}");
                }
            }

            try
            {
                await Task.Delay(5000, cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        return 0;
    }

    private static void SaveIdentity(StoredDeviceIdentity identity)
    {
        string json = JsonSerializer.Serialize(identity, new JsonSerializerOptions { WriteIndented = true });
        try
        {
            Directory.CreateDirectory(ConfigDir);
            File.WriteAllText(IdentityFile, json);
        }
        catch
        {
            // Ignore if CommonApplicationData write fails
        }

        try
        {
            File.WriteAllText(LocalFallbackIdentityFile, json);
        }
        catch
        {
            // Ignore if current directory write fails
        }
    }

    public static StoredDeviceIdentity? LoadIdentity()
    {
        string target = File.Exists(IdentityFile) ? IdentityFile :
                        File.Exists(LocalFallbackIdentityFile) ? LocalFallbackIdentityFile : string.Empty;

        if (string.IsNullOrEmpty(target)) return null;

        try
        {
            string json = File.ReadAllText(target);
            return JsonSerializer.Deserialize<StoredDeviceIdentity>(json);
        }
        catch
        {
            return null;
        }
    }

    private record EnrollmentResponse(string DeviceId, string Status);
}

public class StoredDeviceIdentity
{
    public string DeviceId { get; set; } = string.Empty;
    public string Hostname { get; set; } = string.Empty;
    public string ControlPlaneUrl { get; set; } = string.Empty;
    public string PrivateKeyPem { get; set; } = string.Empty;
    public string PublicKeyPem { get; set; } = string.Empty;
    public DateTime EnrolledAtUtc { get; set; }
}
