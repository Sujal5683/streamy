using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using EnterpriseRemote.Common.Network;

namespace EnterpriseRemote.Controller.UI.Utils;

public record struct LanAdapterInfo(string IpAddress, string Name, string Description, bool HasGateway, bool IsWireless);

public static class NetworkDiscoveryHelper
{
    public static List<LanAdapterInfo> GetActiveLanAdapters()
    {
        var raw = LanDiscoveryUtils.GetActiveLanAdapters();
        var list = new List<LanAdapterInfo>(raw.Count);
        foreach (var r in raw)
        {
            list.Add(new LanAdapterInfo(r.IpAddress, r.Name, r.Description, r.HasGateway, r.IsWireless));
        }
        return list;
    }

    public static string GetPrimaryLanIp() => LanDiscoveryUtils.GetPrimaryLanIp();

    public static string? TryReadPublicTunnelUrl() => LanDiscoveryUtils.TryReadPublicTunnelUrl();

    public static string? GetActiveTunnelUrl() => LanDiscoveryUtils.TryReadPublicTunnelUrl();

    public static Task<bool> IsUrlReachableAsync(string url, int timeoutSeconds = 2) =>
        LanDiscoveryUtils.IsEndpointReachableAsync(url, timeoutSeconds * 1000);
}
