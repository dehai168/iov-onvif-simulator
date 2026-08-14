using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Iov.OnvifSimulator.Models;

public static class NetworkDefaults
{
    public static string GetPrimaryIPv4()
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up ||
                nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            {
                continue;
            }

            var ipv4 = nic.GetIPProperties().UnicastAddresses
                .FirstOrDefault(x => x.Address.AddressFamily == AddressFamily.InterNetwork);
            if (ipv4 != null)
            {
                return ipv4.Address.ToString();
            }
        }

        return "127.0.0.1";
    }

    public static string CreateLocalMac()
    {
        var bytes = Guid.NewGuid().ToByteArray();
        bytes[0] = (byte)((bytes[0] & 0xFE) | 0x02);
        return string.Join(":", bytes.Take(6).Select(b => b.ToString("X2")));
    }
}
