using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;

namespace EnterpriseRemote.Host.Service.Install;

public static class ServiceInstaller
{
    public const string ServiceName = "EnterpriseRemoteHostSvc";
    public const string DisplayName = "Enterprise Remote Access Host Service";
    public const string Description = "Provides secure, enterprise-controlled remote desktop management and attended session streaming.";

    public static bool IsElevatedAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static bool InstallService(string binaryPath)
    {
        if (!IsElevatedAdministrator())
        {
            Console.WriteLine("[!] Installation requires elevated Administrator privileges.");
            return false;
        }

        Console.WriteLine($"[*] Registering Windows Service '{ServiceName}'...");
        string scArgs = $"create \"{ServiceName}\" binPath= \"\\\"{binaryPath}\\\"\" start= auto DisplayName= \"{DisplayName}\"";
        return ExecuteProcess("sc.exe", scArgs);
    }

    public static bool IsServiceInstalled()
    {
        return ExecuteProcess("sc.exe", $"query \"{ServiceName}\"");
    }

    public static bool StartService()
    {
        return ExecuteProcess("sc.exe", $"start \"{ServiceName}\"");
    }

    public static bool UninstallService()
    {
        if (!IsElevatedAdministrator())
        {
            Console.WriteLine("[!] Uninstallation requires elevated Administrator privileges.");
            return false;
        }

        Console.WriteLine($"[*] Stopping and removing Windows Service '{ServiceName}'...");
        ExecuteProcess("sc.exe", $"stop \"{ServiceName}\"");
        return ExecuteProcess("sc.exe", $"delete \"{ServiceName}\"");
    }

    private static bool ExecuteProcess(string fileName, string arguments)
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            proc?.WaitForExit(10000);
            return proc?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
