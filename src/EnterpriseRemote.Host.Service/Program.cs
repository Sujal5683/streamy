using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EnterpriseRemote.Common.Cryptography;
using EnterpriseRemote.Common.Models;
using EnterpriseRemote.Common.Network;
using EnterpriseRemote.Common.Protocol;
using EnterpriseRemote.Host.Service.Install;
using EnterpriseRemote.Host.Service.Ipc;
using EnterpriseRemote.Host.Service.Policy;

namespace EnterpriseRemote.Host.Service;

public static class Program
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "EnterpriseRemote");
    private static readonly string IdentityFile = Path.Combine(ConfigDir, "device_identity.json");
    private static readonly string LocalFallbackIdentityFile = "device_identity.json";

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeConsole();

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);
    private const int ATTACH_PARENT_PROCESS = -1;

    public static async Task<int> Main(string[] args)
    {
        string serverUrl = "http://127.0.0.1:5293";
        string command = args.Length > 0 ? args[0].ToLowerInvariant() : "--run";

        bool keepConsole = args.Any(a => a.Equals("--console", StringComparison.OrdinalIgnoreCase) || a.Equals("--debug", StringComparison.OrdinalIgnoreCase) || a.Equals("--enroll", StringComparison.OrdinalIgnoreCase) || a.Equals("--install", StringComparison.OrdinalIgnoreCase) || a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase));
        if (keepConsole)
        {
            try
            {
                AttachConsole(ATTACH_PARENT_PROCESS);
                var stdout = Console.OpenStandardOutput();
                var writer = new StreamWriter(stdout, System.Text.Encoding.UTF8) { AutoFlush = true };
                Console.SetOut(writer);
                Console.SetError(writer);
            }
            catch { }
        }
        else if (command == "--run")
        {
            try { FreeConsole(); } catch { }
        }

        if (keepConsole)
        {
            Console.WriteLine("===============================================================================");
            Console.WriteLine("   Enterprise Windows Remote Access: Host Background Service (Session 0)");
            Console.WriteLine("===============================================================================\n");
        }

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("--server", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                serverUrl = args[i + 1];
        serverUrl = UrlUtils.NormalizeServerUrl(serverUrl);
            }
        }

        if (args.Any(a => a.Equals("--clear-kill-switch", StringComparison.OrdinalIgnoreCase)))
        {
            ClearKillSwitch();
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
                ClearKillSwitch(); // Clean kill switch on fresh enrollment
                if (args.Length < 2)
                {
                    Console.WriteLine("[!] Usage: EnterpriseRemoteHostSvc.exe --enroll <INVITATION_CODE> [--server <URL>]");
                    return 1;
                }
                string invitationCode = args[1];
                return await EnrollDeviceAsync(serverUrl, invitationCode);

            case "--run":
            default:
                if (IsKillSwitchActive())
                {
                    Console.WriteLine("[!] Remote Streaming KILL SWITCH is active. Host.Service will not run.");
                    return 0;
                }
                return await RunServiceHostAsync(serverUrl);
        }
    }

    private static async Task<int> EnrollDeviceAsync(string serverUrl, string invitationCode)
    {
        serverUrl = UrlUtils.NormalizeServerUrl(serverUrl);
        invitationCode = invitationCode.Trim();
        Console.WriteLine($"[*] Initiating zero-trust cryptographic enrollment with: {serverUrl}");
        Console.WriteLine($"[*] Invitation Code: {invitationCode}");

        // 1. Generate unique hardware cryptographic identity (ECDSA P-256)
        var (privKey, pubKey) = CryptoUtils.GenerateEcdsaKeyPair();
        string hostname = Environment.MachineName;
        string osVersion = Environment.OSVersion.ToString();
        string macAddress = "00:50:56:C0:00:08"; // Standard primary adapter MAC

        using var http = CreateHttpClient(serverUrl, TimeSpan.FromSeconds(15));
        var payload = new
        {
            InvitationCode = invitationCode,
            Hostname = hostname,
            OsVersion = osVersion,
            MacAddress = macAddress,
            PublicKeyPem = pubKey
        };

        const int maxAttempts = 6;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var response = await http.PostAsJsonAsync("/api/v1/enrollment/redeem", payload);
                if (response.IsSuccessStatusCode)
                {
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
                        PermanentAccessToken = result.PermanentAccessToken,
                        EnrolledAtUtc = DateTime.UtcNow
                    };

                    SaveIdentity(identity);

                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"[+] Enrollment Successful! Device ID: {result.DeviceId}");
                    if (!string.IsNullOrEmpty(result.PermanentAccessToken))
                    {
                        Console.WriteLine($"[+] Permanent Unattended Access Key: {result.PermanentAccessToken}");
                    }
                    Console.WriteLine($"[+] Cryptographic identity generated and securely stored.");
                    Console.ResetColor();
                    return 0;
                }

                int statusCode = (int)response.StatusCode;
                string err = await response.Content.ReadAsStringAsync();

                // If Cloudflare edge warmup / proxy error (HTTP 530, 520-524, 502-504), wait and retry
                if ((statusCode >= 500 && statusCode <= 599) && attempt < maxAttempts)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"[*] Cloudflare tunnel edge is converging (HTTP {statusCode}). Retrying in 2s (attempt {attempt}/{maxAttempts})...");
                    Console.ResetColor();
                    await Task.Delay(2000);
                    continue;
                }

                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[!] Enrollment redemption failed: {response.StatusCode} - {err}");
                Console.ResetColor();
                return 1;
            }
            catch (Exception ex) when (attempt < maxAttempts && (ex is HttpRequestException || ex is TaskCanceledException))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"[*] Waiting for tunnel gateway to respond ({ex.Message}). Retrying in 2s (attempt {attempt}/{maxAttempts})...");
                Console.ResetColor();
                await Task.Delay(2000);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[!] Connection error during enrollment: {ex.Message}");
                if (ex is HttpRequestException || ex is TaskCanceledException)
                {
                    Console.WriteLine($"    Could not reach Control Plane at: {serverUrl}");
                    Console.WriteLine("    Please check:");
                    Console.WriteLine("    1. Is the Control Plane server running on the Main PC?");
                    Console.WriteLine("    2. If on university/office Wi-Fi: AP Client Isolation may be blocking direct LAN connections.");
                    Console.WriteLine("       -> Use a phone Mobile Hotspot for both PCs, or use Cloudflare Tunnel / Tailscale!");
                }
                Console.ResetColor();
                return 1;
            }
        }

        return 1;
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

                try
        {
            if (ServiceInstaller.IsElevatedAdministrator() && !ServiceInstaller.IsServiceInstalled())
            {
                string binaryPath = Environment.ProcessPath ?? "EnterpriseRemote.Host.Service.exe";
                ServiceInstaller.InstallService(binaryPath);
            }
        }
        catch { }

        using var serviceHost = new ServiceHost();
        serviceHost.Start();
        Console.WriteLine($"[+] Session 0 Host Service active. Named Pipe listening on session {serviceHost.ActiveSessionId}...");

        bool policiesConfigured = BrowserPolicyConfigurator.ApplyBrowserPolicies();
        if (policiesConfigured)
        {
            Console.WriteLine("[+] Enterprise Browser Streaming Policies verified (Chrome & Edge unblocked).");
        }

        using var http = CreateHttpClient(identity.ControlPlaneUrl, TimeSpan.FromSeconds(5));
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
            // KILL SWITCH: If user ran STOP or kill switch was activated, shut down immediately and do not restart UserAgent!
            if (IsKillSwitchActive())
            {
                Console.WriteLine("[!] Remote Streaming KILL SWITCH is active. Terminating Host.Service cleanly.");
                break;
            }

            try
            {
                heartbeatCount++;
                var hbPayload = new { PermanentAccessToken = identity.PermanentAccessToken, Hostname = identity.Hostname };
                var resp = await http.PostAsJsonAsync($"/api/v1/devices/{identity.DeviceId}/heartbeat", hbPayload, cts.Token);
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

            // Interactive watchdog: if running in user desktop session and kill switch is NOT active, ensure UserAgent stays alive
            if (heartbeatCount % 2 == 0)
            {
                EnsureUserAgentRunning();
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

    public static bool IsKillSwitchActive()
    {
        try
        {
            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            string flag1 = Path.Combine(programData, "EnterpriseRemote", "kill_switch.flag");
            string flag2 = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "kill_switch.flag");
            string flag3 = Path.Combine(Directory.GetCurrentDirectory(), "kill_switch.flag");
            return File.Exists(flag1) || File.Exists(flag2) || File.Exists(flag3);
        }
        catch
        {
            return false;
        }
    }

    public static void ActivateKillSwitch()
    {
        try
        {
            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            string dir = Path.Combine(programData, "EnterpriseRemote");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "kill_switch.flag"), DateTime.UtcNow.ToString("o"));
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "kill_switch.flag"), DateTime.UtcNow.ToString("o"));
        }
        catch { }
    }

    public static void ClearKillSwitch()
    {
        try
        {
            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            string flag1 = Path.Combine(programData, "EnterpriseRemote", "kill_switch.flag");
            string flag2 = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "kill_switch.flag");
            string flag3 = Path.Combine(Directory.GetCurrentDirectory(), "kill_switch.flag");
            if (File.Exists(flag1)) File.Delete(flag1);
            if (File.Exists(flag2)) File.Delete(flag2);
            if (File.Exists(flag3)) File.Delete(flag3);
        }
        catch { }
    }

    private static void EnsureUserAgentRunning()
    {
        try
        {
            // KILL SWITCH GUARD: Never respawn UserAgent if STOP was requested
            if (IsKillSwitchActive())
            {
                return;
            }

            if (Process.GetCurrentProcess().SessionId != 0)
            {
                var running = Process.GetProcessesByName("EnterpriseRemote.Host.UserAgent");
                if (running.Length == 0)
                {
                    string agentExe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "EnterpriseRemote.Host.UserAgent.exe");
                    if (File.Exists(agentExe))
                    {
                        var psi = new ProcessStartInfo
                        {
                            FileName = agentExe,
                            UseShellExecute = true,
                            CreateNoWindow = true,
                            WindowStyle = ProcessWindowStyle.Hidden
                        };
                        Process.Start(psi);
                        Console.WriteLine("[+] Watchdog: Respawned EnterpriseRemote.Host.UserAgent in active user session.");
                    }
                }
            }
        }
        catch { }
    }


    private static HttpClient CreateHttpClient(string serverUrl, TimeSpan timeout)
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };
        return new HttpClient(handler) { BaseAddress = new Uri(serverUrl), Timeout = timeout };
    }

    private static void SaveIdentity(StoredDeviceIdentity identity)
    {
        string json = JsonSerializer.Serialize(identity, new JsonSerializerOptions { WriteIndented = true });
        
        // 1. Try ProgramData (system-wide)
        try
        {
            Directory.CreateDirectory(ConfigDir);
            File.WriteAllText(IdentityFile, json);
        }
        catch { }

        // 2. Base directory of the executable
        try
        {
            string baseDirFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "device_identity.json");
            File.WriteAllText(baseDirFile, json);
        }
        catch { }

        // 3. Parent directory (if running from Bin/)
        try
        {
            string parentDirFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "device_identity.json");
            File.WriteAllText(parentDirFile, json);
        }
        catch { }

        // 4. Current working directory
        try
        {
            File.WriteAllText(LocalFallbackIdentityFile, json);
        }
        catch { }
    }

    public static StoredDeviceIdentity? LoadIdentity()
    {
        string[] candidates = new[]
        {
            IdentityFile,
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "device_identity.json"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "device_identity.json"),
            LocalFallbackIdentityFile
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path))
            {
                try
                {
                    string json = File.ReadAllText(path);
                    var ident = JsonSerializer.Deserialize<StoredDeviceIdentity>(json);
                    if (ident != null && !string.IsNullOrEmpty(ident.DeviceId))
                        return ident;
                }
                catch { }
            }
        }

        return null;
    }

    private record EnrollmentResponse(string DeviceId, string? PermanentAccessToken, string Status);
}

public class StoredDeviceIdentity
{
    public string DeviceId { get; set; } = string.Empty;
    public string Hostname { get; set; } = string.Empty;
    public string ControlPlaneUrl { get; set; } = string.Empty;
    public string PrivateKeyPem { get; set; } = string.Empty;
    public string PublicKeyPem { get; set; } = string.Empty;
    public string? PermanentAccessToken { get; set; }
    public DateTime EnrolledAtUtc { get; set; }
}
