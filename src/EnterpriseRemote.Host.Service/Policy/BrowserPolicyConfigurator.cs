using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace EnterpriseRemote.Host.Service.Policy;

/// <summary>
/// Configures enterprise browser policies and system environment variables
/// across Google Chrome, Microsoft Edge, Microsoft Edge WebView2 runtime,
/// Safe Exam Browser, and Chromium-based kiosk containers to guarantee unblocked,
/// continuous screen streaming and eliminate window occlusion throttling.
/// </summary>
public static class BrowserPolicyConfigurator
{
    private static readonly (string SubKey, string Name)[] BrowserPolicyTargets = new[]
    {
        (@"SOFTWARE\Policies\Google\Chrome", "Google Chrome"),
        (@"SOFTWARE\Policies\Microsoft\Edge", "Microsoft Edge"),
        (@"SOFTWARE\Policies\Microsoft\Edge\WebView2", "Microsoft Edge WebView2"),
        (@"SOFTWARE\Policies\Chromium", "Chromium"),
        (@"SOFTWARE\Policies\BraveSoftware\Brave", "Brave Browser"),
        (@"SOFTWARE\SafeExamBrowser", "Safe Exam Browser"),
        (@"SOFTWARE\WOW6432Node\SafeExamBrowser", "Safe Exam Browser (WoW64)")
    };

    /// <summary>
    /// Applies enterprise policies to prevent secure browsers and web engines from blocking remote capture.
    /// </summary>
    public static bool ApplyBrowserPolicies()
    {
        bool anyApplied = false;

        foreach (var (subKey, name) in BrowserPolicyTargets)
        {
            if (ConfigurePolicyKey(subKey, name))
            {
                anyApplied = true;
            }
        }

        // Configure system-wide environment variables to disable occlusion throttling in WebView2 / CEF
        try
        {
            Environment.SetEnvironmentVariable(
                "WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS",
                "--disable-features=CalculateNativeWinOcclusion --disable-backgrounding-occluded-windows",
                EnvironmentVariableTarget.Machine);
            anyApplied = true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Notice: Could not write system environment variable: {ex.Message}");
        }

        return anyApplied;
    }

    private static bool ConfigurePolicyKey(string subKey, string browserName)
    {
        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(subKey, writable: true);
            if (key == null) return false;

            // 1. Force Screen Capture Allowed (bypasses DLP and anti-screencast locks)
            key.SetValue("ScreenCaptureAllowed", 1, RegistryValueKind.DWord);

            // 2. Ensure Hardware Acceleration is enabled and stable
            key.SetValue("HardwareAccelerationModeEnabled", 1, RegistryValueKind.DWord);

            // 3. Disable Native Window Occlusion (prevents browser from stopping rendering when remote)
            key.SetValue("NativeWindowOcclusionEnabled", 0, RegistryValueKind.DWord);

            // 4. Safe Exam Browser specific kiosk screen sharing flags if applicable
            if (browserName.Contains("Safe Exam Browser", StringComparison.OrdinalIgnoreCase))
            {
                key.SetValue("AllowScreenSharing", 1, RegistryValueKind.DWord);
                key.SetValue("AllowRemoteDesktop", 1, RegistryValueKind.DWord);
            }

            Debug.WriteLine($"[+] Applied enterprise streaming policies for {browserName} in HKLM\\{subKey}");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Notice: Could not write browser policy for {browserName}: {ex.Message}");
            return false;
        }
    }
}
