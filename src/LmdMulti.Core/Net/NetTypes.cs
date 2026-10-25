using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using LmdMulti.Core.Poses;
using LmdMulti.Core.Protocol;

namespace LmdMulti.Core.Net;

/// Horloge monotone commune au fil réseau et au fil principal (ms depuis le démarrage du processus).
public static class NetClock
{
    private static readonly Stopwatch Watch = Stopwatch.StartNew();
    public static double NowMs => Watch.Elapsed.TotalMilliseconds;
    public static uint NowMsU32 => (uint)Watch.ElapsedMilliseconds;
}

/// Réglages du nœud réseau.
public sealed class NodeOptions
{
    /// Port d'écoute souhaité ; si occupé, on essaie les suivants (PortAttempts au total).
    public int Port = ProtocolInfo.DefaultPort;
    public int PortAttempts = 10;
    /// Port visé par les annonces broadcast (celui de tout le monde, en principe).
    public int DiscoveryPort = ProtocolInfo.DefaultPort;
    public bool Broadcast = true;
    /// Pairs joints directement (IP manuelles, Tailscale…), en plus de la découverte.
    public List<IPEndPoint> ManualPeers = new();
    public double AnnounceIntervalMs = 1000;
    public double PingIntervalMs = 1000;
    public double PeerTimeoutMs = 5000;
    public string ModVersion = "";
    /// Identifiant de cette instance (aléatoire) : distingue aussi deux jeux sur un même compte Steam.
    public ulong InstanceId = (ulong)Random.Shared.NextInt64() ^ (ulong)Environment.TickCount64 << 32;
}

/// Ce qu'on sait d'un pair. Les copies remises au fil principal sont des instantanés (Clone).
public sealed class PeerInfo
{
    public ulong Id;
    public ulong SteamId;
    public string Name = "";
    public string Track = "";
    public bool OnTrack;
    public string ModVersion = "";
    public IPEndPoint EndPoint;
    public double LastSeenMs;
    /// Temps aller-retour lissé, -1 tant qu'inconnu.
    public int RttMs = -1;
    /// Fantôme écho : notre propre pose revenue par la boucle réseau (mode debug).
    public bool IsEcho;
    /// 0 si compatible, sinon version de protocole du pair (jamais décodé).
    public byte IncompatibleVersion;
    public int PosesReceived;

    public bool IsCompatible => IncompatibleVersion == 0;
    public PeerInfo Clone() => (PeerInfo)MemberwiseClone();
}

public enum NetEventKind { PeerJoined, PeerChanged, PeerLeft, Pose, Log }

/// Message du fil réseau vers le fil principal.
public sealed class NetEvent
{
    public NetEventKind Kind;
    /// Instantané du pair (PeerJoined / PeerChanged / PeerLeft).
    public PeerInfo Peer;
    public ulong PeerId;
    public PoseFrame Pose;
    /// Instant de réception (NetClock) d'une pose.
    public double RecvMs;
    public string Text;

    public static NetEvent Log(string text) => new() { Kind = NetEventKind.Log, Text = text };
}
