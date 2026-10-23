using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using EnterpriseRemote.Common.Models;
using EnterpriseRemote.Common.Protocol;

using System.Text.Json.Serialization;

namespace EnterpriseRemote.Controller;

public static class Program
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [STAThread]
    public static int Main(string[] args)
    {
        bool runGui = args.Length == 0 || Array.Exists(args, a => a.Equals("--gui", StringComparison.OrdinalIgnoreCase));
        if (runGui)
        {
            try
            {
                Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += (s, e) =>
                {
                    try
                    {
                        string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "thread_crash.log");
                        File.WriteAllText(logPath, e.Exception.ToString());
                    }
                    catch { }
                };
                AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                {
                    try
                    {
                        string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "domain_crash.log");
                        File.WriteAllText(logPath, e.ExceptionObject?.ToString() ?? "Unknown domain exception");
                    }
                    catch { }
                };
                Application.Run(new UI.RemoteViewerForm());
                return 0;
            }
            catch (Exception ex)
            {
                try
                {
                    string crashPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log");
                    File.WriteAllText(crashPath, ex.ToString());
                }
                catch { }
                return 1;
            }
        }

        return RunCliAsync(args).GetAwaiter().GetResult();
    }

    private static async Task<int> RunCliAsync(string[] args)
    {
        Console.WriteLine("===============================================================================");
        Console.WriteLine("   Enterprise Windows Remote Access: Operator Controller Application");
        Console.WriteLine("===============================================================================\n");

        string serverUrl = "http://127.0.0.1:5293";
        string email = "alice@corp.internal";
        string? targetDeviceId = null;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("--server", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                serverUrl = args[i + 1];
            }
            else if (args[i].Equals("--email", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                email = args[i + 1];
            }
            else if (args[i].Equals("--device", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                targetDeviceId = args[i + 1];
            }
        }

        using var http = new HttpClient { BaseAddress = new Uri(serverUrl), Timeout = TimeSpan.FromSeconds(10) };

        // 1. Authenticate with Identity Provider via Control Plane
        Console.WriteLine($"[*] Authenticating Operator '{email}' with {serverUrl}...");
        var authResp = await http.PostAsJsonAsync("/api/v1/auth/token", new { Email = email });
        if (!authResp.IsSuccessStatusCode)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[!] Authentication failed: {authResp.StatusCode}");
            Console.ResetColor();
            return 1;
        }

        var authUser = await authResp.Content.ReadFromJsonAsync<AuthResult>(JsonOpts);
        if (authUser == null) return 1;

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[+] Authenticated as: {authUser.DisplayName} ({authUser.Role})");
        Console.WriteLine($"[+] Organization:     {authUser.OrgId}");
        Console.WriteLine($"[+] Authorized Groups: {string.Join(", ", authUser.AuthorizedGroupIds)}");
        Console.ResetColor();

        // 2. Discover Authorized Devices
        Console.WriteLine("\n[*] Discovering authorized enterprise devices...");
        var devicesResp = await http.GetAsync($"/api/v1/devices?userId={authUser.UserId}");
        if (!devicesResp.IsSuccessStatusCode)
        {
            Console.WriteLine($"[!] Failed to query device inventory: {devicesResp.StatusCode}");
            return 1;
        }

        var devices = await devicesResp.Content.ReadFromJsonAsync<List<EnrolledDevice>>(JsonOpts);
        if (devices == null || devices.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("[!] No enrolled devices found for your authorized groups.");
            Console.WriteLine("    Please enroll an endpoint first via EnterpriseRemoteHostSvc.exe --enroll <INVITATION_CODE>");
            Console.ResetColor();
            return 0;
        }

        Console.WriteLine($"[+] Found {devices.Count} authorized device(s):");
        foreach (var d in devices)
        {
            Console.WriteLine($"    -> [{d.DeviceId}] {d.Hostname} | OS: {d.OsVersion} | Status: {d.Status} | Group: {d.GroupId}");
        }

        // 3. Select Target Device
        string selectedId = targetDeviceId ?? devices[0].DeviceId;
        Console.WriteLine($"\n[*] Requesting remote session for device: {selectedId}...");

        var sessionRequest = new
        {
            OperatorId = authUser.UserId,
            DeviceId = selectedId,
            RequestedCapabilities = SessionCapabilities.View | SessionCapabilities.Input | SessionCapabilities.Clipboard
        };

        var sessionResp = await http.PostAsJsonAsync("/api/v1/sessions/request", sessionRequest);
        if (!sessionResp.IsSuccessStatusCode)
        {
            string err = await sessionResp.Content.ReadAsStringAsync();
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[!] Session authorization denied by policy: {sessionResp.StatusCode} - {err}");
            Console.ResetColor();
            return 1;
        }

        var sessionResult = await sessionResp.Content.ReadFromJsonAsync<SessionResult>(JsonOpts);
        if (sessionResult == null) return 1;

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[+] Session Authorized! Session ID: {sessionResult.SessionId}");
        Console.WriteLine($"[+] Capabilities Granted: {sessionResult.PermittedCapabilities}");
        Console.WriteLine($"[+] Attended Consent Required: {sessionResult.RequiresAttendedConsent}");
        Console.WriteLine($"[+] Signed SAT Token: {sessionResult.SignatureBase64[..16]}... (Valid for 60s)");
        Console.ResetColor();

        // 4. Initialize Client & Remote Control Session
        var client = new RemoteSessionClient(authUser.UserId, selectedId);
        client.InitializeSession(sessionResult.SessionId, sessionResult.PermittedCapabilities, sessionResult.RequiresAttendedConsent);

        Console.WriteLine("\n[+] Remote Desktop Stream Active. Dispatching test input events...");
        client.SendMouseMove(0.5f, 0.5f);
        client.SendMouseButton(MouseButton.Left, isDown: true, 0.5f, 0.5f);
        client.SendMouseButton(MouseButton.Left, isDown: false, 0.5f, 0.5f);
        Console.WriteLine("[+] Input events bounds-checked and transmitted.");

        // 5. Clean Teardown
        client.TerminateSession();
        Console.WriteLine("[+] Session terminated cleanly.");

        return 0;
    }

    private record AuthResult(string UserId, string Email, string DisplayName, string OrgId, UserRole Role, string[] AuthorizedGroupIds);
    private record SessionResult(
        string SessionId,
        string DeviceId,
        SessionCapabilities PermittedCapabilities,
        bool RequiresAttendedConsent,
        DateTime IssuedAtUtc,
        DateTime ExpiresAtUtc,
        string SignatureBase64,
        string ControlPlanePublicKeyPem);
}
