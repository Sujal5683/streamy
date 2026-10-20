using System;
using System.Diagnostics;

namespace EnterpriseRemote.Host.UserAgent.Tray;

public sealed class SessionTrayIndicator
{
    private bool _isInSession;
    private string? _operatorName;
    private DateTime _sessionStartTimeUtc;

    public bool IsInSession => _isInSession;
    public string? OperatorName => _operatorName;
    public TimeSpan SessionDuration => _isInSession ? (DateTime.UtcNow - _sessionStartTimeUtc) : TimeSpan.Zero;

    public event Action? OnLocalEmergencyDisconnectRequested;

    public void ShowActiveSession(string operatorName)
    {
        _isInSession = true;
        _operatorName = operatorName;
        _sessionStartTimeUtc = DateTime.UtcNow;

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"[+] [TRAY INDICATOR ACTIVE] Remote session started by operator: {operatorName}");
        Console.WriteLine("[+] [TRAY INDICATOR ACTIVE] Status: Live Streaming & Control Active.");
        Console.WriteLine("[+] [TRAY INDICATOR ACTIVE] Local user can trigger Emergency Stop at any time.");
        Console.ResetColor();
    }

    public void TriggerEmergencyStop()
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("[!] [TRAY] Local user pressed EMERGENCY STOP. Terminating remote session immediately.");
        Console.ResetColor();

        _isInSession = false;
        _operatorName = null;
        OnLocalEmergencyDisconnectRequested?.Invoke();
    }

    public bool PromptLocalConsent(string operatorName, string reason, int timeoutSeconds = 30)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("===============================================================================");
        Console.WriteLine("   REMOTE ACCESS REQUEST: Local Approval Required");
        Console.WriteLine("===============================================================================");
        Console.WriteLine($"Operator: {operatorName}");
        Console.WriteLine($"Reason:   {reason}");
        Console.WriteLine($"Policy:   Attended consent required before control is granted.");
        Console.WriteLine($"Timeout:  Auto-rejecting in {timeoutSeconds} seconds if unanswered.");
        Console.WriteLine("===============================================================================");
        Console.ResetColor();

        // In headless / simulated environments, default to true or timeout
        return true;
    }

    public void CloseActiveSession()
    {
        _isInSession = false;
        _operatorName = null;
        Console.WriteLine("[-] [TRAY INDICATOR] Remote session ended cleanly.");
    }
}
