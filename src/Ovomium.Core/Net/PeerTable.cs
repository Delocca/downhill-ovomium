using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Ovomium.Core.Protocol;

namespace Ovomium.Core.Net;

/// Registre des pairs connus, partagé entre le fil réseau (écriture) et le fil principal (instantanés).
/// Chaque arrivée, changement ou départ est signalé par un NetEvent.
internal sealed class PeerTable
{
    private const int MaxPeers = 64;
    private readonly object _lock = new();
    private readonly Dictionary<ulong, PeerInfo> _peers = new();
    private readonly ConcurrentQueue<NetEvent> _events;
    private readonly Dictionary<ulong, ClockSync> _clocks = new();

    /// Pair retiré (Bye, expiration) : le canal fiable l'oublie.
    public System.Action<ulong> OnLeft;

    public PeerTable(ConcurrentQueue<NetEvent> events) => _events = events;

    public void OnAnnounce(ulong id, Announce a, IPEndPoint ep, double now, bool isEcho = false)
    {
        lock (_lock)
        {
            bool isNew = !_peers.TryGetValue(id, out var p);
            if (isNew)
            {
                if (_peers.Count >= MaxPeers) return;
                p = _peers[id] = new PeerInfo { Id = id, IsEcho = isEcho };
            }
            bool changed = p.Name != a.Name || p.Track != a.Track || p.OnTrack != a.OnTrack || p.Look != a.Look;
            p.SteamId = a.SteamId; p.Name = a.Name; p.Track = a.Track; p.OnTrack = a.OnTrack; p.Look = a.Look;
            p.ModVersion = a.ModVersion; p.EndPoint = ep; p.LastSeenMs = now;
            if (isNew || changed) Emit(isNew ? NetEventKind.PeerJoined : NetEventKind.PeerChanged, p);
        }
    }

    /// Paquet reçu d'un pair : rafraîchit sa présence. Faux si le pair est inconnu ou incompatible.
    public bool Touch(ulong id, IPEndPoint ep, double now, bool countPose = false)
    {
        lock (_lock)
        {
            if (!_peers.TryGetValue(id, out var p) || !p.IsCompatible) return false;
            if (ep != null) p.EndPoint = ep;
            p.LastSeenMs = now;
            if (countPose) p.PosesReceived++;
            return true;
        }
    }

    public void OnIncompatible(ulong id, byte version, IPEndPoint ep, double now)
    {
        lock (_lock)
        {
            if (_peers.TryGetValue(id, out var p)) { p.LastSeenMs = now; p.EndPoint = ep; return; }
            if (_peers.Count >= MaxPeers) return;
            p = _peers[id] = new PeerInfo { Id = id, Name = ep.ToString(), EndPoint = ep, LastSeenMs = now, IncompatibleVersion = version };
            Emit(NetEventKind.PeerJoined, p);
        }
    }

    /// Pong reçu : aller-retour (lissé, affiché) et décalage d'horloge (meilleur échantillon récent).
    public void OnPong(ulong id, uint pingSentMs, uint responderMs, uint recvMs)
    {
        lock (_lock)
        {
            if (!_peers.TryGetValue(id, out var p)) return;
            int rttMs = (int)(recvMs - pingSentMs);
            p.RttMs = p.RttMs < 0 ? rttMs : (p.RttMs * 3 + rttMs) / 4; // lissage léger
            if (!_clocks.TryGetValue(id, out var clock)) _clocks[id] = clock = new ClockSync();
            clock.Add(pingSentMs, responderMs, recvMs);
            p.HasClock = clock.HasValue;
            p.ClockOffsetMs = clock.OffsetMs;
        }
    }

    public PeerInfo Get(ulong id)
    {
        lock (_lock) return _peers.TryGetValue(id, out var p) ? p.Clone() : null;
    }

    public void Remove(ulong id)
    {
        lock (_lock)
            if (_peers.Remove(id, out var p)) Left(p);
    }

    public void Expire(double now, double timeoutMs)
    {
        lock (_lock)
            foreach (var p in _peers.Values.Where(p => now - p.LastSeenMs > timeoutMs).ToList())
            {
                _peers.Remove(p.Id);
                Left(p);
            }
    }

    private void Left(PeerInfo p)
    {
        _clocks.Remove(p.Id);
        OnLeft?.Invoke(p.Id);
        Emit(NetEventKind.PeerLeft, p);
    }

    /// Adresses des pairs compatibles réels (hors écho) : destinataires des poses et pings.
    public List<(ulong id, IPEndPoint ep)> Targets()
    {
        lock (_lock)
            return _peers.Values.Where(p => p.IsCompatible && !p.IsEcho && p.EndPoint != null)
                                .Select(p => (p.Id, p.EndPoint)).ToList();
    }

    public List<PeerInfo> Snapshot()
    {
        lock (_lock) return _peers.Values.Select(p => p.Clone()).ToList();
    }

    private void Emit(NetEventKind kind, PeerInfo p) =>
        _events.Enqueue(new NetEvent { Kind = kind, Peer = p.Clone(), PeerId = p.Id });
}
