using System.Net;
using System.Net.NetworkInformation;

namespace Ilot.Services;

/// <summary>
/// Dit si le PC a accès à un réseau. NetworkInterface.GetIsNetworkAvailable ne convient pas : les cartes virtuelles
/// (WSL, Docker, Tailscale) restent actives sans réseau. Seule une carte réelle dispose d'une passerelle.
/// </summary>
internal static class NetworkStatus
{
    public static bool IsOnline()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces().Any(adapter => IsUsable(
                adapter.OperationalStatus,
                adapter.NetworkInterfaceType,
                adapter.GetIPProperties().GatewayAddresses.Select(gateway => gateway.Address)));
        }
        catch (NetworkInformationException ex)
        {
            AppLog.Write(ex);
            return true; // Dans le doute, pas de fausse alerte.
        }
    }

    internal static bool IsUsable(OperationalStatus status, NetworkInterfaceType type, IEnumerable<IPAddress> gateways) =>
        status == OperationalStatus.Up
        && type is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
        && gateways.Any(gateway => !gateway.Equals(IPAddress.Any) && !gateway.Equals(IPAddress.IPv6Any));
}
