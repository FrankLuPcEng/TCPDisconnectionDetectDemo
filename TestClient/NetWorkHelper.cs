using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.NetworkInformation;

public static class NetworkHelper
{
    public static void MonitorNetworkChanges(IPAddress targetIp, ILogger logger)
    {
        var targetBytes = targetIp.GetAddressBytes();

        NetworkChange.NetworkAddressChanged += (sender, e) =>
        {
            logger.LogInformation("🔄 網路環境變化！");

            var targetNetwork = new IPNetwork(targetIp, 24); // 假設子網掩碼為 255.255.255.0

            var sameNetworkInterfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Select(ni => new
                {
                    Name = ni.Name,
                    Status = ni.OperationalStatus,
                    IPs = ni.GetIPProperties().UnicastAddresses.Select(addr => addr.Address)
                })
                .Where(ni => ni.IPs.Any(ip => targetNetwork.Contains(ip)))
                .ToList();

            foreach (var ni in sameNetworkInterfaces)
            {
                logger.LogInformation($"網路介面: {ni.Name}, 狀態: {ni.Status}, IPs: {string.Join(", ", ni.IPs)}");
            }
        };
    }
}

public class IPNetwork
{
    private readonly IPAddress _network;
    private readonly int _prefixLength;

    public IPNetwork(IPAddress network, int prefixLength)
    {
        _network = network;
        _prefixLength = prefixLength;
    }

    public bool Contains(IPAddress address)
    {
        var networkBytes = _network.GetAddressBytes();
        var addressBytes = address.GetAddressBytes();

        if (networkBytes.Length != addressBytes.Length)
            return false;

        int byteCount = _prefixLength / 8;
        int bitCount = _prefixLength % 8;

        for (int i = 0; i < byteCount; i++)
        {
            if (networkBytes[i] != addressBytes[i])
                return false;
        }

        if (bitCount > 0)
        {
            int mask = 0xFF << (8 - bitCount);
            if ((networkBytes[byteCount] & mask) != (addressBytes[byteCount] & mask))
                return false;
        }

        return true;
    }
}