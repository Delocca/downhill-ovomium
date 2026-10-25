using System;
using System.Collections.Generic;

namespace LmdMulti.Core.Net;

/// Livraison fiable et ordonnée de petits messages, pair par pair, au-dessus d'UDP (sans socket : testable).
/// - Émission : chaque message reçoit un numéro (1, 2, 3…) et est renvoyé toutes les ResendMs jusqu'à
///   son accusé ; abandonné après GiveUpMs (pair parti).
/// - Réception : on accuse tout ; on livre dans l'ordre, les doublons sont ignorés, les messages en
///   avance attendent le manquant (au plus GapSkipMs, puis on saute le trou : l'émetteur a abandonné).
/// - Flux : identifiant aléatoire choisi par l'émetteur pour chaque pair. S'il oublie ce pair (départ,
///   expiration) et recommence à 1, le nouveau flux remet la réception à zéro au lieu de passer pour des
///   doublons.
/// Thread-safe (verrou interne) : le fil principal dépose, le fil réseau envoie et reçoit.
public sealed class ReliableChannel
{
    public const double ResendMs = 150, GiveUpMs = 15000, GapSkipMs = 3000;
    private const int MaxEarly = 256;

    private sealed class Pending { public uint Seq; public byte[] Payload; public double FirstSent, NextSend; }

    private sealed class PeerState
    {
        public uint OutStream, NextOutSeq = 1;
        public readonly List<Pending> Out = new();
        public uint InStream, NextInSeq = 1;
        public readonly SortedDictionary<uint, byte[]> Early = new();
        public double EarlySince;
    }

    private readonly object _lock = new();
    private readonly Dictionary<ulong, PeerState> _peers = new();
    private readonly Random _rng;

    public ReliableChannel(int? seed = null) => _rng = seed.HasValue ? new Random(seed.Value) : new Random();

    /// Nombre de messages abandonnés (sans accusé après GiveUpMs).
    public int GivenUp { get; private set; }

    private PeerState State(ulong peer)
    {
        if (_peers.TryGetValue(peer, out var s)) return s;
        s = _peers[peer] = new PeerState();
        do s.OutStream = (uint)_rng.Next(); while (s.OutStream == 0);
        return s;
    }

    /// Ajoute un message pour `peer` ; renvoie (flux, numéro) à émettre tout de suite.
    public (uint stream, uint seq) Enqueue(ulong peer, byte[] payload, double now)
    {
        lock (_lock)
        {
            var s = State(peer);
            var p = new Pending { Seq = s.NextOutSeq++, Payload = payload, FirstSent = now, NextSend = now + ResendMs };
            s.Out.Add(p);
            return (s.OutStream, p.Seq);
        }
    }

    public void OnAck(ulong peer, uint stream, uint seq)
    {
        lock (_lock)
            if (_peers.TryGetValue(peer, out var s) && s.OutStream == stream)
                s.Out.RemoveAll(p => p.Seq == seq);
    }

    /// Message reçu (à accuser dans tous les cas) : renvoie les contenus livrables, dans l'ordre.
    public List<byte[]> OnData(ulong peer, uint stream, uint seq, byte[] payload, double now)
    {
        var ready = new List<byte[]>();
        lock (_lock)
        {
            var s = State(peer);
            if (s.InStream != stream) { s.InStream = stream; s.NextInSeq = 1; s.Early.Clear(); }
            if (seq < s.NextInSeq) return ready; // doublon (accusé perdu)
            if (seq == s.NextInSeq) { ready.Add(payload); s.NextInSeq++; }
            else if (s.Early.Count < MaxEarly && s.Early.TryAdd(seq, payload) && s.Early.Count == 1) s.EarlySince = now;
            Flush(s, ready, now);
        }
        return ready;
    }

    private static void Flush(PeerState s, List<byte[]> ready, double now)
    {
        while (s.Early.Remove(s.NextInSeq, out var next)) { ready.Add(next); s.NextInSeq++; }
        if (s.Early.Count > 0 && ready.Count > 0) s.EarlySince = now; // le trou a avancé
    }

    /// Messages à renvoyer maintenant, et trous trop vieux sautés (livrés via `skipped`).
    public List<(ulong peer, uint stream, uint seq, byte[] payload)> Due(double now, List<(ulong peer, byte[] payload)> skipped = null)
    {
        var due = new List<(ulong, uint, uint, byte[])>();
        lock (_lock)
            foreach (var (id, s) in _peers)
            {
                GivenUp += s.Out.RemoveAll(p => now - p.FirstSent > GiveUpMs);
                foreach (var p in s.Out)
                    if (now >= p.NextSend) { p.NextSend = now + ResendMs; due.Add((id, s.OutStream, p.Seq, p.Payload)); }
                if (skipped != null && s.Early.Count > 0 && now - s.EarlySince > GapSkipMs) SkipGap(id, s, skipped, now);
            }
        return due;
    }

    private static void SkipGap(ulong id, PeerState s, List<(ulong, byte[])> skipped, double now)
    {
        using var it = s.Early.Keys.GetEnumerator();
        it.MoveNext();
        s.NextInSeq = it.Current;
        var ready = new List<byte[]>();
        Flush(s, ready, now);
        foreach (var p in ready) skipped.Add((id, p));
    }

    /// Pair parti : on oublie tout (un retour éventuel repartira sur un nouveau flux).
    public void Forget(ulong peer)
    {
        lock (_lock) _peers.Remove(peer);
    }

    /// Messages encore sans accusé vers ce pair.
    public int PendingCount(ulong peer)
    {
        lock (_lock) return _peers.TryGetValue(peer, out var s) ? s.Out.Count : 0;
    }
}
