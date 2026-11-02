using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using EnterpriseRemote.Common.Models;
using EnterpriseRemote.Common.Protocol;
using EnterpriseRemote.Host.UserAgent.Capture;
using EnterpriseRemote.Host.UserAgent.Input;
using EnterpriseRemote.Host.UserAgent.Ipc;
using EnterpriseRemote.Host.UserAgent.Tray;
namespace EnterpriseRemote.Host.UserAgent;
public static class Program
{
    private static bool IsAdministrator()
    {
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);
    private static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new(-4);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeConsole();
    [STAThread]
    public static async Task<int> Main(string[] args)
    {
        // Auto-elevate if not running as Administrator to ensure UIPI bypass across elevated and lockdown apps
        if (!IsAdministrator() && !args.Any(a => a.Equals("--no-elevate", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = Environment.ProcessPath ?? "EnterpriseRemote.Host.UserAgent.exe",
                    Arguments = string.Join(" ", args),
                    Verb = "runas",
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(psi);
                return 0;
            }
            catch
            {
                // User declined UAC prompt; continue un-elevated with SYSTEM service fallback
            }
        }
        await Task.Yield();
        try
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) =>
            {
                try { System.IO.File.WriteAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "useragent_thread_crash.log"), e.Exception.ToString()); } catch { }
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                try { System.IO.File.WriteAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "useragent_domain_crash.log"), e.ExceptionObject?.ToString() ?? "Domain error"); } catch { }
            };
            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                e.SetObserved();
            };
        }
        catch { }
        try
        {
            SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        }
        catch { }
        // Detach from console window completely unless explicit debug flag is passed (prevents taskbar icon)
        bool keepConsole = false;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("--console", StringComparison.OrdinalIgnoreCase) || args[i].Equals("--debug", StringComparison.OrdinalIgnoreCase))
            {
                keepConsole = true;
                break;
            }
        }
        if (!keepConsole)
        {
            try { FreeConsole(); } catch { }
        }
        string? serverUrl = null;
        string? deviceId = null;
        int sessionId = System.Diagnostics.Process.GetCurrentProcess().SessionId;
        if (sessionId <= 0) sessionId = 1;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("--session", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                int.TryParse(args[i + 1], out sessionId);
            }
            else if (args[i].Equals("--server", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                serverUrl = args[i + 1];
            }
            else if (args[i].Equals("--device", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                deviceId = args[i + 1];
            }
        }
        // Auto-discover identity if not provided via command line
        if (string.IsNullOrEmpty(deviceId) || string.IsNullOrEmpty(serverUrl))
        {
            var ident = LoadLocalIdentity();
            if (ident != null)
            {
                deviceId ??= ident.DeviceId;
                serverUrl ??= ident.ControlPlaneUrl;
            }
        }
        Console.WriteLine($"[*] Initializing User Session Agent for Session ID: {sessionId}...");
        // 1. Initialize Screen Capture Engine
        using var capturer = new DxgiDesktopDuplicator();
        bool captureInit = capturer.Initialize();
        if (captureInit)
        {
            string engineName = capturer.IsUsingFallback ? "GDI BitBlt Fallback" : "DXGI Desktop Duplication";
            Console.WriteLine($"[+] Screen Capture Initialized: {engineName} ({capturer.Display.Width}x{capturer.Display.Height})");
        }
        else
        {
            Console.WriteLine("[!] Screen capture initialization deferred or unavailable (headless).");
        }
        // 2. Initialize Input Injection Engine
        var inputEngine = new InputInjectionEngine(capturer.Display);
        Console.WriteLine("[+] Input Injection Engine bounds-checked and active.");
        // 3. Initialize Window Affinity Manager for anti-capture neutralization
        var affinityManager = new WindowAffinityManager();
        // 4. Initialize Live WebSocket Desktop Streamer
        EnterpriseRemote.Host.UserAgent.Streaming.DesktopStreamingService? streamer = null;
        if (!string.IsNullOrEmpty(serverUrl) && !string.IsNullOrEmpty(deviceId))
        {
            Console.WriteLine($"[+] Starting real-time Desktop Streamer for Device: {deviceId} -> {serverUrl}");
            streamer = new EnterpriseRemote.Host.UserAgent.Streaming.DesktopStreamingService(serverUrl, deviceId, capturer, inputEngine, affinityManager);
            streamer.Start();
        }
        var cts = new CancellationTokenSource();
        // 4. Initialize Tray Indicator in Windows Notification Area (System Tray)
        var tray = new SessionTrayIndicator(deviceId);
        tray.OnLocalEmergencyDisconnectRequested += () =>
        {
            Console.WriteLine("[!] Local Emergency Stop triggered! Disconnecting session...");
        };
        tray.OnExitRequested += () =>
        {
            cts.Cancel();
            try { Application.Exit(); } catch { }
        };
        // 5. Connect to Session 0 Named Pipe with resilient background auto-reconnect watchdog
        Console.WriteLine($"[*] Initializing Session 0 Host Service IPC watchdog (\\.\\pipe\\EnterpriseRemote_IPC_{sessionId})...");
        using var pipeClient = new NamedPipeIpcClient(sessionId);
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
            Console.WriteLine("\n[*] Shutting down User Agent cleanly...");
        };
        // Bind fallbacks so elevated clicks and anti-affinity routing work whenever the pipe is connected
        inputEngine.SetElevatedFallback(payload =>
        {
            if (pipeClient.IsConnected)
            {
                var frame = new IpcFrame
                {
                    MessageType = IpcMessageType.InjectInputElevated,
                    SequenceNumber = 0,
                    Payload = payload
                };
                _ = pipeClient.SendFrameAsync(frame);
            }
        });
        affinityManager.RequestElevatedNeutralization = (hWnd, pid) =>
        {
            if (pipeClient.IsConnected)
            {
                byte[] payload = new byte[12];
                BitConverter.TryWriteBytes(payload.AsSpan(0, 8), hWnd.ToInt64());
                BitConverter.TryWriteBytes(payload.AsSpan(8, 4), pid);
                var frame = new IpcFrame
                {
                    MessageType = IpcMessageType.NeutralizeAffinity,
                    SequenceNumber = 0,
                    Payload = payload
                };
                _ = pipeClient.SendFrameAsync(frame);
            }
        };
        pipeClient.OnMessageReceived += frame =>
        {
            if (frame.MessageType == IpcMessageType.PromptConsent)
            {
                string opName = System.Text.Encoding.UTF8.GetString(frame.Payload);
                bool approved = tray.PromptLocalConsent(opName, "Remote Desktop Assistance");
                var respFrame = new IpcFrame
                {
                    MessageType = IpcMessageType.ConsentResult,
                    SequenceNumber = frame.SequenceNumber + 1,
                    Payload = System.Text.Encoding.UTF8.GetBytes(approved ? "Approved" : "Denied")
                };
                _ = pipeClient.SendFrameAsync(respFrame);
            }
            else if (frame.MessageType == IpcMessageType.TerminateSession)
            {
                tray.CloseActiveSession();
            }
        };
        // Resilient background reconnection loop
        _ = Task.Run(async () =>
        {
            bool hasLoggedConnected = false;
            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    if (!pipeClient.IsConnected)
                    {
                        bool ok = await pipeClient.ConnectAsync(timeoutMilliseconds: 2000, cts.Token);
                        if (ok && !hasLoggedConnected)
                        {
                            hasLoggedConnected = true;
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine("[+] Connected to Session 0 Host Service via Authenticated Named Pipe.");
                            Console.WriteLine("[+] Ephemeral HMAC-SHA256 handshake established.");
                            Console.WriteLine("[+] Dual-Route UIPI Input Bypass active: elevated windows routed via SYSTEM service.");
                            Console.ResetColor();
                        }
                    }
                }
                catch { }
                try
                {
                    await Task.Delay(2000, cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }, cts.Token);
        
        Console.WriteLine("[+] User Agent ready and running in hidden system tray.");
        // Keep running until canceled or test completes
        if (args.Length > 0 && args[0].Equals("--oneshot", StringComparison.OrdinalIgnoreCase))
        {
            streamer?.Dispose();
            tray.Dispose();
            return 0;
        }
        // Run Windows message loop for the background system tray icon (no taskbar window)
        Application.ApplicationExit += (_, _) =>
        {
            cts.Cancel();
        };
        Application.Run();
        streamer?.Dispose();
        tray.CloseActiveSession();
        tray.Dispose();
        return 0;
    }
    private static LocalDeviceIdentity? LoadLocalIdentity()
    {
        string[] candidates = new[]
        {
            System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "device_identity.json"),
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "EnterpriseRemote", "device_identity.json"),
            System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "device_identity.json")
        };
        foreach (var path in candidates)
        {
            if (System.IO.File.Exists(path))
            {
                try
                {
                    string json = System.IO.File.ReadAllText(path);
                    return System.Text.Json.JsonSerializer.Deserialize<LocalDeviceIdentity>(json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
                catch
                {
                }
            }
        }
        return null;
    }
    private sealed class LocalDeviceIdentity
    {
        public string DeviceId { get; set; } = string.Empty;
        public string Hostname { get; set; } = string.Empty;
        public string ControlPlaneUrl { get; set; } = string.Empty;
        public string? PermanentAccessToken { get; set; }
    }
}
