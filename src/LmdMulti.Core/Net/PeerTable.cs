using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using LmdMulti.Core.Protocol;

namespace LmdMulti.Core.Net;

/// Registre des pairs connus, partagé entre le fil réseau (écriture) et le fil principal (instantanés).
/// Chaque arrivée, changement ou départ est signalé par un NetEvent.
internal sealed class PeerTable
{
    private const int MaxPeers = 64;
    private readonly object _lock = new();
    private readonly Dictionary<ulong, PeerInfo> _peers = new();
    private readonly ConcurrentQueue<NetEvent> _events;

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
            bool changed = p.Name != a.Name || p.Track != a.Track || p.OnTrack != a.OnTrack;
            p.SteamId = a.SteamId; p.Name = a.Name; p.Track = a.Track; p.OnTrack = a.OnTrack;
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

    public void SetRtt(ulong id, int rttMs)
    {
        lock (_lock)
            if (_peers.TryGetValue(id, out var p))
                p.RttMs = p.RttMs < 0 ? rttMs : (p.RttMs * 3 + rttMs) / 4; // lissage léger
    }

    public void Remove(ulong id)
    {
        lock (_lock)
            if (_peers.Remove(id, out var p)) Emit(NetEventKind.PeerLeft, p);
    }

    public void Expire(double now, double timeoutMs)
    {
        lock (_lock)
            foreach (var p in _peers.Values.Where(p => now - p.LastSeenMs > timeoutMs).ToList())
            {
                _peers.Remove(p.Id);
                Emit(NetEventKind.PeerLeft, p);
            }
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
