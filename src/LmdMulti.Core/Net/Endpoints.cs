using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace LmdMulti.Core.Net;

/// Adresses utiles : broadcast de chaque réseau local, et analyse des IP manuelles de la config.
public static class Endpoints
{
    /// 255.255.255.255 + broadcast dirigé de chaque interface IPv4 active.
    /// Windows n'envoie 255.255.255.255 que sur une interface (souvent le VPN) : le broadcast
    /// dirigé (ex. 192.168.1.255) atteint bien le réseau local de chaque carte.
    public static List<IPAddress> BroadcastAddresses()
    {
        var list = new List<IPAddress> { IPAddress.Broadcast };
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    var b = DirectedBroadcast(ua);
                    if (b != null && !list.Contains(b)) list.Add(b);
                }
            }
        }
        catch (Exception) { /* énumération indisponible (ex. Wine incomplet) : 255.255.255.255 seul */ }
        return list;
    }

    private static IPAddress DirectedBroadcast(UnicastIPAddressInformation ua)
    {
        if (ua.Address.AddressFamily != AddressFamily.InterNetwork || ua.IPv4Mask == null) return null;
        byte[] ip = ua.Address.GetAddressBytes(), mask = ua.IPv4Mask.GetAddressBytes();
        if (mask[3] == 255 || mask[0] == 0) return null; // /32 (Tailscale, VPN) ou masque inconnu
        var b = new byte[4];
        for (int i = 0; i < 4; i++) b[i] = (byte)(ip[i] | ~mask[i]);
        return new IPAddress(b);
    }

    /// "100.64.0.2, 192.168.1.20:47701, pc-paul" -> points d'accès (noms résolus par DNS).
    /// Les entrées invalides sont signalées via `onError` et ignorées.
    public static List<IPEndPoint> ParsePeers(string csv, int defaultPort, Action<string> onError)
    {
        var list = new List<IPEndPoint>();
        if (string.IsNullOrWhiteSpace(csv)) return list;
        foreach (var raw in csv.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            try { list.Add(ParseOne(raw.Trim(), defaultPort)); }
            catch (Exception e) { onError?.Invoke($"IP manuelle « {raw} » ignorée : {e.Message}"); }
        }
        return list;
    }

    private static IPEndPoint ParseOne(string s, int defaultPort)
    {
        if (IPEndPoint.TryParse(s, out var ep) && (ep.Port != 0 || !s.Contains(':')))
            return new IPEndPoint(ep.Address, ep.Port == 0 ? defaultPort : ep.Port);
        string host = s;
        int port = defaultPort;
        int colon = s.LastIndexOf(':');
        if (colon > 0) { host = s[..colon]; port = int.Parse(s[(colon + 1)..]); }
        foreach (var a in Dns.GetHostAddresses(host))
            if (a.AddressFamily == AddressFamily.InterNetwork) return new IPEndPoint(a, port);
        throw new ArgumentException("nom introuvable");
    }
}
