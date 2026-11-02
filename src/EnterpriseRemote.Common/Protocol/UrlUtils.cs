using System;

namespace EnterpriseRemote.Common.Protocol;

public static class UrlUtils
{
    /// <summary>
    /// Bulletproof normalization of server and control plane URLs.
    /// Handles missing scheme, duplicate schemes (e.g. "http://https://"),
    /// redundant port declarations, trailing slashes, quotes, and whitespace.
    /// </summary>
    public static string NormalizeServerUrl(string? rawUrl)
    {
        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            return "http://127.0.0.1:5293";
        }

        string rawTrimmed = rawUrl.Trim().Trim('"', '\'');
        bool wasExplicitHttps = rawTrimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                                rawTrimmed.StartsWith("wss://", StringComparison.OrdinalIgnoreCase);

        string s = rawTrimmed;

        // Repeatedly strip any leading http://, https://, ws://, wss://, and spaces
        while (true)
        {
            s = s.Trim();
            if (s.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                s = s.Substring(7);
            }
            else if (s.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                s = s.Substring(8);
            }
            else if (s.StartsWith("ws://", StringComparison.OrdinalIgnoreCase))
            {
                s = s.Substring(5);
            }
            else if (s.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
            {
                s = s.Substring(6);
            }
            else
            {
                break;
            }
        }

        s = s.Trim().TrimEnd('/');

        if (string.IsNullOrWhiteSpace(s))
        {
            return "http://127.0.0.1:5293";
        }

        // Check if Cloudflare tunnel or HTTPS external domain
        bool isCloudflare = s.Contains("trycloudflare.com", StringComparison.OrdinalIgnoreCase) ||
                            s.Contains("cloudflare", StringComparison.OrdinalIgnoreCase);

        if (isCloudflare || wasExplicitHttps)
        {
            // Cloudflare tunnels terminate on port 443; strip any accidental port like :5293
            if (isCloudflare)
            {
                int colonIdx = s.IndexOf(':');
                if (colonIdx > 0)
                {
                    s = s.Substring(0, colonIdx);
                }
            }
            return $"https://{s}";
        }

        // Tailscale IPs are always in the 100.64.0.0/10 CGNAT range (100.x.x.x).
        // They are plain HTTP like LAN — never upgrade to HTTPS, never strip the port.
        bool isTailscale = IsTailscaleAddress(s);
        if (isTailscale)
        {
            // Parse host:port from the Tailscale address
            string tsHost = s;
            int tsPort = 5293;
            if (s.Contains(':'))
            {
                var parts = s.Split(':', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                tsHost = parts[0];
                if (parts.Length > 1 && int.TryParse(parts[1], out int p) && p > 0 && p <= 65535)
                    tsPort = p;
            }
            return $"http://{tsHost}:{tsPort}";
        }

        // It is a LAN IP or hostname or localhost
        string host = s;
        int port = 5293;

        if (s.Contains(':'))
        {
            var parts = s.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length > 0)
            {
                host = parts[0];
            }
            if (parts.Length > 1 && int.TryParse(parts[1], out int parsedPort) && parsedPort > 0 && parsedPort <= 65535)
            {
                port = parsedPort;
            }
        }

        return $"http://{host}:{port}";
    }

    /// <summary>
    /// Returns true when the address is a Tailscale CGNAT IP (100.64.0.0/10).
    /// Tailscale assigns every device an IP in 100.64.x.x–100.127.x.x.
    /// These are treated like LAN — plain HTTP, no port stripping.
    /// </summary>
    public static bool IsTailscaleAddress(string? hostOrUrl)
    {
        if (string.IsNullOrWhiteSpace(hostOrUrl)) return false;
        string host = hostOrUrl.Trim();

        // Extract host from full URL if needed
        if (System.Net.IPAddress.TryParse(host.Split(':')[0], out var ip))
        {
            host = ip.ToString();
        }
        else if (Uri.TryCreate(host, UriKind.Absolute, out var uri))
        {
            host = uri.Host;
        }
        else if (host.Contains(':'))
        {
            host = host.Split(':')[0];
        }

        // Tailscale CGNAT range: 100.64.0.0/10  →  100.64.x.x to 100.127.x.x
        if (System.Net.IPAddress.TryParse(host, out var parsed))
        {
            byte[] b = parsed.GetAddressBytes();
            if (b.Length == 4 && b[0] == 100 && b[1] >= 64 && b[1] <= 127)
                return true;
        }

        // Also detect the "tailscale" hostname suffix used by MagicDNS (e.g., my-pc.tail1234.ts.net)
        if (host.EndsWith(".ts.net", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".tailscale.net", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }
}
