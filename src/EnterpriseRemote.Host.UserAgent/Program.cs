using System;
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
    public static async Task<int> Main(string[] args)
    {
        Console.WriteLine("===============================================================================");
        Console.WriteLine("   Enterprise Windows Remote Access: Interactive Desktop Agent (Session 1+)");
        Console.WriteLine("===============================================================================\n");

        int sessionId = 1;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("--session", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                int.TryParse(args[i + 1], out sessionId);
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

        // 3. Initialize Tray Indicator
        var tray = new SessionTrayIndicator();
        tray.OnLocalEmergencyDisconnectRequested += () =>
        {
            Console.WriteLine("[!] Local Emergency Stop triggered! Disconnecting session...");
        };

        // 4. Connect to Session 0 Named Pipe
        Console.WriteLine($"[*] Connecting to Session 0 Host Service IPC (\\.\\pipe\\EnterpriseRemote_IPC_{sessionId})...");
        using var pipeClient = new NamedPipeIpcClient(sessionId);

        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
            Console.WriteLine("\n[*] Shutting down User Agent cleanly...");
        };

        bool connected = await pipeClient.ConnectAsync(timeoutMilliseconds: 3000, cts.Token);
        if (connected)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("[+] Connected to Session 0 Host Service via Authenticated Named Pipe.");
            Console.WriteLine("[+] Ephemeral HMAC-SHA256 handshake established.");
            Console.ResetColor();

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
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("[!] Host Service pipe not connected (Service may be starting or inactive). Running in standalone interactive mode.");
            Console.ResetColor();
        }

        // Display sample tray indicator
        tray.ShowActiveSession("Operator Standalone Monitor");
        Console.WriteLine("[+] User Agent ready and monitoring interactive session.");

        // Keep running until canceled or test completes
        if (args.Length > 0 && args[0].Equals("--oneshot", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        try
        {
            await Task.Delay(Timeout.Infinite, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Clean exit
        }

        tray.CloseActiveSession();
        return 0;
    }
}
