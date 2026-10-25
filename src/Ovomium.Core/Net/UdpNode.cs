using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Ovomium.Core.Protocol;

namespace Ovomium.Core.Net;

/// Nœud réseau : un socket UDP unique et un fil dédié qui reçoit, annonce (~1 Hz), mesure le ping,
/// expire les pairs muets et envoie les poses en unicast à chaque pair.
/// Le fil principal ne fait que déposer des poses (SendPose) et lire des NetEvent (TryGetEvent).
public sealed partial class UdpNode : IDisposable
{
    private readonly NodeOptions _o;
    private readonly ConcurrentQueue<NetEvent> _events = new();
    private readonly ConcurrentQueue<byte[]> _outPoses = new();
    private readonly PeerTable _peers;
    private readonly PacketWriter _w = new();
    private readonly byte[] _rx = new byte[2048];
    private readonly object _localLock = new();
    private Announce _local; // null tant que SetLocal n'a pas été appelé : pas d'annonce sans pseudo
    private Socket _sock;
    private Thread _thread;
    private volatile bool _stop, _echo;
    private List<IPAddress> _broadcasts = new();
    private double _nextAnnounce, _nextPing, _nextBroadcastRefresh, _nextExpire;

    public ulong Id => _o.InstanceId;
    /// Identifiant du pair virtuel « écho » (notre pose revenue par le réseau).
    public ulong EchoId => _o.InstanceId ^ 0xEC40EC40EC40EC40UL;
    public int BoundPort { get; private set; }
    public string Status { get; private set; } = "arrêté";
    public long PacketsIn, PacketsOut, PosesSent;

    public UdpNode(NodeOptions options)
    {
        _o = options;
        _peers = new PeerTable(_events) { OnLeft = _reliable.Forget };
    }

    /// Ouvre le socket (port souhaité, sinon les suivants) et démarre le fil. Faux si aucun port libre.
    public bool Start()
    {
        for (int i = 0; i < _o.PortAttempts && _sock == null; i++)
        {
            var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            try { s.Bind(new IPEndPoint(IPAddress.Any, _o.Port + i)); _sock = s; BoundPort = _o.Port + i; }
            catch (SocketException) { s.Dispose(); }
        }
        if (_sock == null) { Status = $"aucun port libre ({_o.Port}–{_o.Port + _o.PortAttempts - 1})"; return false; }
        _sock.EnableBroadcast = true;
        DisableWindowsConnReset();
        Status = BoundPort == _o.Port ? $"écoute sur le port {BoundPort}" : $"port {_o.Port} occupé, écoute sur {BoundPort}";
        _thread = new Thread(Loop) { IsBackground = true, Name = "Ovomium.Net" };
        _thread.Start();
        return true;
    }

    /// Windows signale un « port injoignable » ICMP comme une erreur sur ReceiveFrom : on désactive.
    private void DisableWindowsConnReset()
    {
        if (!OperatingSystem.IsWindows()) return;
        const int SIO_UDP_CONNRESET = -1744830452;
        try { _sock.IOControl(SIO_UDP_CONNRESET, new byte[4], null); } catch (Exception) { /* Wine : sans effet */ }
    }

    /// État local diffusé dans les annonces (appelé par le fil principal, ~2 Hz).
    public void SetLocal(string name, ulong steamId, string track, bool onTrack)
    {
        lock (_localLock)
            _local = new Announce { Name = name, SteamId = steamId, Track = track ?? "", OnTrack = onTrack, ModVersion = _o.ModVersion };
    }

    private Announce Local() { lock (_localLock) return _local; }

    /// Mode écho : nos poses reviennent par la boucle locale et s'affichent comme un pair.
    public bool Echo
    {
        get => _echo;
        set { _echo = value; if (!value) _peers.Remove(EchoId); }
    }

    /// Dépose un paquet Pose déjà encodé (Messages.WritePose) ; il partira vers tous les pairs.
    public void SendPose(byte[] packet)
    {
        if (_outPoses.Count < 64) _outPoses.Enqueue(packet); // fil réseau bloqué : on jette plutôt qu'accumuler
    }

    public bool TryGetEvent(out NetEvent e) => _events.TryDequeue(out e);

    public List<PeerInfo> Peers() => _peers.Snapshot();

    /// Instantané d'un pair, ou null s'il n'est (plus) connu.
    public PeerInfo Peer(ulong id) => _peers.Get(id);

    private void Loop()
    {
        while (!_stop)
        {
            try
            {
                if (_sock.Poll(5000, SelectMode.SelectRead)) ReceiveAll();
                FlushPoses();
                FlushMessages(NetClock.NowMs);
                Periodic(NetClock.NowMs);
            }
            catch (ObjectDisposedException) { return; }
            catch (Exception e) { ReportError(e); Thread.Sleep(100); }
        }
    }

    private double _nextErrorReport;

    /// Erreur de la boucle réseau : journalisée au plus toutes les 10 s (une erreur qui se répète
    /// noierait le journal à 10 lignes par seconde).
    private void ReportError(Exception e)
    {
        double now = NetClock.NowMs;
        if (now < _nextErrorReport) return;
        _nextErrorReport = now + 10000;
        _events.Enqueue(NetEvent.Log($"erreur réseau : {e.GetType().Name}: {e.Message}"));
    }

    private void FlushPoses()
    {
        if (_outPoses.IsEmpty) return;
        var targets = _peers.Targets();
        while (_outPoses.TryDequeue(out var packet))
        {
            foreach (var (_, ep) in targets) Send(packet, packet.Length, ep);
            if (_echo) Send(packet, packet.Length, new IPEndPoint(IPAddress.Loopback, BoundPort));
            PosesSent++;
        }
    }

    private void Periodic(double now)
    {
        if (now >= _nextBroadcastRefresh)
        {
            _nextBroadcastRefresh = now + 30000; // interfaces qui changent (Wi-Fi reconnecté…)
            _broadcasts = _o.Broadcast ? Endpoints.BroadcastAddresses() : new List<IPAddress>();
        }
        if (now >= _nextAnnounce) { _nextAnnounce = now + _o.AnnounceIntervalMs; AnnounceAll(); }
        if (now >= _nextPing) { _nextPing = now + _o.PingIntervalMs; PingAll(); }
        if (now >= _nextExpire) { _nextExpire = now + 250; _peers.Expire(now, _o.PeerTimeoutMs); } // pas à chaque tour (≥ 200/s)
    }

    /// Annonce en broadcast + à chaque IP manuelle + en unicast à chaque pair connu
    /// (un pair qui n'entend pas nos broadcasts, ex. port différent ou Tailscale, nous découvre ainsi).
    private void AnnounceAll()
    {
        var local = Local();
        if (local == null) return;
        Messages.WriteAnnounce(_w, Id, local);
        var sent = new HashSet<IPEndPoint>();
        void To(IPEndPoint ep) { if (sent.Add(ep)) Send(_w.Buffer, _w.Length, ep); }
        foreach (var b in _broadcasts) To(new IPEndPoint(b, _o.DiscoveryPort));
        if (BoundPort != _o.DiscoveryPort) To(new IPEndPoint(IPAddress.Loopback, _o.DiscoveryPort)); // même machine
        foreach (var ep in _o.ManualPeers) To(ep);
        foreach (var (_, ep) in _peers.Targets()) To(ep);
    }

    private void PingAll()
    {
        Messages.WritePing(_w, Id, NetClock.NowMsU32);
        foreach (var (_, ep) in _peers.Targets()) Send(_w.Buffer, _w.Length, ep);
    }

    private void Send(byte[] buf, int len, IPEndPoint ep)
    {
        if (_o.SimulatedLoss > 0 && Random.Shared.NextDouble() < _o.SimulatedLoss) return; // tests de fiabilité
        try { _sock.SendTo(buf, 0, len, SocketFlags.None, ep); Interlocked.Increment(ref PacketsOut); }
        catch (SocketException) { /* hôte injoignable, réseau absent : l'expiration fera le ménage */ }
    }

    /// Arrêt : derniers messages fiables émis une fois (ex. abandon de course), puis « Bye ». Si le fil ne
    /// s'arrête pas à temps, il utilise encore les tampons : on ferme sans rien envoyer (les autres
    /// verront notre départ à l'expiration, 5 s).
    public void Dispose()
    {
        if (_sock == null || _stop) return;
        _stop = true;
        bool stopped = _thread == null || _thread.Join(500);
        if (stopped)
            try
            {
                FlushMessages(NetClock.NowMs);
                Messages.WriteHeader(_w, MessageType.Bye, Id);
                foreach (var (_, ep) in _peers.Targets()) Send(_w.Buffer, _w.Length, ep);
            }
            catch (Exception) { /* fermeture au mieux */ }
        _sock.Dispose();
        Status = "arrêté";
    }
}
