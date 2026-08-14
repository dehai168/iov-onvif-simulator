using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Iov.OnvifSimulator.Models;

public sealed record HostInterface(
    string Id,
    string Name,
    string IPv4,
    int PrefixLength,
    string MacAddress);

public static class NetworkDefaults
{
    public static IReadOnlyList<HostInterface> GetListenInterfaces()
    {
        var result = new List<HostInterface>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up ||
                nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            {
                continue;
            }

            var mac = FormatMac(nic.GetPhysicalAddress());
            foreach (var address in nic.GetIPProperties().UnicastAddresses)
            {
                if (address.Address.AddressFamily != AddressFamily.InterNetwork ||
                    IPAddress.IsLoopback(address.Address) ||
                    IsLinkLocal(address.Address))
                {
                    continue;
                }

                result.Add(new HostInterface(
                    nic.Id,
                    nic.Name,
                    address.Address.ToString(),
                    address.PrefixLength,
                    mac));
            }
        }

        return result;
    }

    public static IReadOnlyList<string> GetAllIPv4()
    {
        var ips = GetListenInterfaces()
            .Select(x => x.IPv4)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return ips.Count > 0 ? ips : ["127.0.0.1"];
    }

    public static string GetPrimaryIPv4() => GetAllIPv4()[0];

    public static string CreateLocalMac()
    {
        var bytes = Guid.NewGuid().ToByteArray();
        bytes[0] = (byte)((bytes[0] & 0xFE) | 0x02);
        return FormatMac(bytes.Take(6).ToArray());
    }

    private static bool IsLinkLocal(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes.Length == 4 && bytes[0] == 169 && bytes[1] == 254;
    }

    private static string FormatMac(PhysicalAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes.Length == 0 ? "00:00:00:00:00:00" : FormatMac(bytes);
    }

    private static string FormatMac(IEnumerable<byte> bytes)
    {
        return string.Join(":", bytes.Select(b => b.ToString("X2")));
    }
}
