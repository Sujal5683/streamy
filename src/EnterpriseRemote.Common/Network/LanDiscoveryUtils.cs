using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace EnterpriseRemote.Common.Network;

public readonly record struct LanAdapterInfo(
    string IpAddress,
    string Name,
    string Description,
    bool HasGateway,
    bool IsWireless);

public static class LanDiscoveryUtils
{
    private static readonly string[] VirtualKeywords = new[]
    {
        "virtual", "vbox", "vmware", "hyper-v", "vethernet", "pseudo", "loopback", "wsl", "teredo", "tap", "host-only", "npcap"
    };

    /// <summary>
    /// Discovers all active, non-loopback IPv4 addresses on the host machine,
    /// prioritizing primary physical Wi-Fi and Ethernet adapters with active default gateways.
    /// </summary>
    public static List<LanAdapterInfo> GetActiveLanAdapters()
    {
        var results = new List<LanAdapterInfo>();
        var seenIps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces();
            foreach (var nic in interfaces)
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                var ipProps = nic.GetIPProperties();
                bool hasGateway = false;
                foreach (var gw in ipProps.GatewayAddresses)
                {
                    if (gw.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(gw.Address) &&
                        !gw.Address.Equals(IPAddress.Any))
                    {
                        hasGateway = true;
                        break;
                    }
                }

                bool isWireless = nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;

                foreach (var unicast in ipProps.UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(unicast.Address))
                    {
                        string ipStr = unicast.Address.ToString();
                        if (ipStr.StartsWith("169.254.", StringComparison.OrdinalIgnoreCase)) continue;

                        if (seenIps.Add(ipStr))
                        {
                            results.Add(new LanAdapterInfo(
                                IpAddress: ipStr,
                                Name: nic.Name ?? "Ethernet",
                                Description: nic.Description ?? "Network Adapter",
                                HasGateway: hasGateway,
                                IsWireless: isWireless
                            ));
                        }
                    }
                }
            }
        }
        catch { }

        // Sort priority:
        // 1. Physical Wi-Fi / Ethernet adapters with gateway
        // 2. Wireless adapters with gateway
        // 3. Other adapters with gateway
        // 4. Other physical adapters
        // 5. Virtual adapters
        results.Sort((a, b) =>
        {
            bool aVirtual = IsVirtualAdapter(a);
            bool bVirtual = IsVirtualAdapter(b);
            if (aVirtual != bVirtual) return aVirtual ? 1 : -1;

            if (a.HasGateway != b.HasGateway) return a.HasGateway ? -1 : 1;
            if (a.IsWireless != b.IsWireless) return a.IsWireless ? -1 : 1;

            return string.Compare(a.IpAddress, b.IpAddress, StringComparison.OrdinalIgnoreCase);
        });

        if (results.Count == 0)
        {
            try
            {
                using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
                socket.Connect("8.8.8.8", 65530);
                if (socket.LocalEndPoint is IPEndPoint ep && !IPAddress.IsLoopback(ep.Address))
                {
                    results.Add(new LanAdapterInfo(ep.Address.ToString(), "Default LAN Adapter", "Auto-probed", true, false));
                }
            }
            catch { }
        }

        if (results.Count == 0)
        {
            results.Add(new LanAdapterInfo("127.0.0.1", "Local PC", "Loopback", false, false));
        }

        return results;
    }

    private static bool IsVirtualAdapter(LanAdapterInfo info)
    {
        string combined = $"{info.Name} {info.Description}".ToLowerInvariant();
        foreach (var kw in VirtualKeywords)
        {
            if (combined.Contains(kw)) return true;
        }
        return false;
    }

    public static string GetPrimaryLanIp()
    {
        var adapters = GetActiveLanAdapters();
        return adapters.Count > 0 ? adapters[0].IpAddress : "127.0.0.1";
    }

    /// <summary>
    /// Returns this machine's Tailscale IP (100.64.0.0/10 CGNAT range), or null if
    /// Tailscale is not installed / not connected. This IP is stable across sessions
    /// and reachable from any other Tailscale-enrolled device on the same tailnet.
    /// </summary>
    public static string? GetTailscaleIp()
    {
        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces();
            foreach (var nic in interfaces)
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;

                // Tailscale adapter names contain "Tailscale" on Windows
                bool isTailscaleAdapter =
                    nic.Name.Contains("Tailscale", StringComparison.OrdinalIgnoreCase) ||
                    nic.Description.Contains("Tailscale", StringComparison.OrdinalIgnoreCase) ||
                    nic.Name.Contains("WireGuard", StringComparison.OrdinalIgnoreCase);

                var ipProps = nic.GetIPProperties();
                foreach (var unicast in ipProps.UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    byte[] b = unicast.Address.GetAddressBytes();
                    // Tailscale CGNAT range: 100.64.0.0/10
                    if (b[0] == 100 && b[1] >= 64 && b[1] <= 127)
                    {
                        return unicast.Address.ToString();
                    }
                    // Also catch if adapter is named Tailscale but uses another range (rare)
                    if (isTailscaleAdapter && !IPAddress.IsLoopback(unicast.Address))
                    {
                        return unicast.Address.ToString();
                    }
                }
            }
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Returns true when Tailscale is installed and currently connected (has a CGNAT IP).
    /// </summary>
    public static bool IsTailscaleConnected() => GetTailscaleIp() != null;

    /// <summary>
    /// Searches for active public_tunnel_url.txt in app and parent directories.
    /// </summary>
    public static string? TryReadPublicTunnelUrl()
    {
        try
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string rootDir = baseDir;
            for (int i = 0; i < 6; i++)
            {
                string candidate = Path.Combine(rootDir, "public_tunnel_url.txt");
                if (File.Exists(candidate))
                {
                    string url = File.ReadAllText(candidate).Trim();
                    if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                        url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                    {
                        return url;
                    }
                }
                string? parent = Directory.GetParent(rootDir)?.FullName;
                if (parent == null) break;
                rootDir = parent;
            }

            string localCandidate = Path.Combine(baseDir, "public_tunnel_url.txt");
            if (File.Exists(localCandidate))
            {
                string url = File.ReadAllText(localCandidate).Trim();
                if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                    url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                {
                    return url;
                }
            }
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Checks whether an HTTP/WebSocket gateway URL responds to a health check within timeoutMs.
    /// </summary>
    public static async Task<bool> IsEndpointReachableAsync(string url, int timeoutMs = 800)
    {
        try
        {
            string clean = url.TrimEnd('/');
            using var http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(timeoutMs) };
            var resp = await http.GetAsync($"{clean}/health");
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Checks whether the given URL, hostname, or IP corresponds to this local machine.
    /// Matches 127.0.0.1, localhost, 0.0.0.0, ::1, or any IP bound to an active network adapter on this PC.
    /// </summary>
    public static bool IsLocalMachine(string? urlOrHost)
    {
        if (string.IsNullOrWhiteSpace(urlOrHost)) return true;
        string host = urlOrHost.Trim();
        if (Uri.TryCreate(host, UriKind.Absolute, out var uri))
        {
            host = uri.Host;
        }
        else if (host.Contains(':'))
        {
            host = host.Split(':')[0];
        }

        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("0.0.0.0", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("::1", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (host.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        try
        {
            var adapters = GetActiveLanAdapters();
            foreach (var a in adapters)
            {
                if (a.IpAddress.Equals(host, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch { }

        return false;
    }

    /// <summary>
    /// Evaluates if candidate server URL is an internal/LAN IPv4 address.
    /// </summary>
    public static bool IsLanAddress(string urlOrIp)
    {
        if (string.IsNullOrWhiteSpace(urlOrIp)) return false;
        string host = urlOrIp;
        if (Uri.TryCreate(urlOrIp, UriKind.Absolute, out var uri))
        {
            host = uri.Host;
        }
        else if (host.Contains(':'))
        {
            host = host.Split(':')[0];
        }

        if (IPAddress.TryParse(host, out var ip))
        {
            byte[] bytes = ip.GetAddressBytes();
            if (bytes.Length == 4)
            {
                // 10.0.0.0/8
                if (bytes[0] == 10) return true;
                // 172.16.0.0/12
                if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;
                // 192.168.0.0/16
                if (bytes[0] == 192 && bytes[1] == 168) return true;
                // 127.0.0.1 loopback
                if (bytes[0] == 127) return true;
            }
        }
        return false;
    }
}

