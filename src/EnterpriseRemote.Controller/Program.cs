using System;
using System.Threading;
using EnterpriseRemote.Common.Models;

namespace EnterpriseRemote.Controller;

public static class Program
{
    public static void Main(string[] args)
    {
        Console.WriteLine("===============================================================================");
        Console.WriteLine("   Enterprise Windows Remote Access: Operator Controller Application");
        Console.WriteLine("===============================================================================\n");

        Console.WriteLine("[*] Authenticating Operator via Enterprise Identity Provider...");
        var client = new RemoteSessionClient("USR-ALICE", "DEV-FIN-01");
        client.InitializeSession("SES-DEMO-9901", SessionCapabilities.View | SessionCapabilities.Input | SessionCapabilities.Clipboard, requiresAttended: false);

        Console.WriteLine($"[+] Session Established: {client.SessionId}");
        Console.WriteLine($"[+] Granted Capabilities: {client.GrantedCapabilities}");
        Console.WriteLine($"[+] Attended Consent Required: {client.RequiresAttendedConsent}");
        Console.WriteLine("\n[+] Controller Ready. Remote desktop stream initialized.");
    }
}
