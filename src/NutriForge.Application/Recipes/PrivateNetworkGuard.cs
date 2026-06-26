using System.Net;
using System.Net.Sockets;

namespace NutriForge.Application.Recipes;

/// <summary>
/// SSRF defense: classifies an IP address as "must not be reached" — loopback, private (RFC1918),
/// link-local (incl. the 169.254.169.254 cloud-metadata endpoint), CGNAT, multicast/reserved, and the
/// IPv6 equivalents (ULA, link-local, mapped IPv4). The URL fetcher resolves a host, drops every blocked
/// address, and connects only to what survives — so a pasted URL (or a redirect / DNS rebind to an
/// internal name) can never reach internal infrastructure. Pure + deterministic for unit testing.
/// </summary>
public static class PrivateNetworkGuard
{
    public static bool IsBlocked(IPAddress ip)
    {
        ArgumentNullException.ThrowIfNull(ip);

        if (IPAddress.IsLoopback(ip))
        {
            return true;
        }

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            return b[0] == 0                                   // 0.0.0.0/8 "this network"
                || b[0] == 10                                  // 10.0.0.0/8 private
                || b[0] == 127                                 // loopback (defensive)
                || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)  // 100.64.0.0/10 CGNAT
                || (b[0] == 169 && b[1] == 254)                // 169.254.0.0/16 link-local + metadata
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)   // 172.16.0.0/12 private
                || (b[0] == 192 && b[1] == 0 && b[2] == 0)     // 192.0.0.0/24 IETF protocol assignments
                || (b[0] == 192 && b[1] == 168)                // 192.168.0.0/16 private
                || (b[0] == 198 && (b[1] & 0xfe) == 18)        // 198.18.0.0/15 benchmarking
                || b[0] >= 224;                                // 224.0.0.0/4 multicast + 240.0.0.0/4 reserved
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv4MappedToIPv6)
            {
                return IsBlocked(ip.MapToIPv4());
            }

            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast)
            {
                return true;
            }

            var b = ip.GetAddressBytes();
            return (b[0] & 0xfe) == 0xfc; // fc00::/7 unique-local
        }

        return true; // unknown address family — block by default
    }
}
