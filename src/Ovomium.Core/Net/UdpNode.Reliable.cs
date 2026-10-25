using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Ovomium.Core.Protocol;

namespace Ovomium.Core.Net;

/// Messages fiables (course) : le fil principal dépose, le fil réseau émet, renvoie et accuse
/// (logique dans ReliableChannel ; format dans docs/architecture.md).
public sealed partial class UdpNode
{
    private readonly ReliableChannel _reliable = new();
    private readonly ConcurrentQueue<(ulong peer, byte[] payload)> _outMessages = new();
    private readonly PacketWriter _wr = new(); // fil réseau seulement

    /// Envoie un message fiable et ordonné à un pair (livré chez lui en NetEvent Message).
    public void SendMessage(ulong peerId, byte[] payload)
    {
        if (payload.Length > Messages.MaxReliablePayload) throw new ArgumentException("message fiable trop long");
        _outMessages.Enqueue((peerId, payload));
    }

    /// Messages encore sans accusé vers ce pair (diagnostic, tests).
    public int PendingMessages(ulong peerId) => _reliable.PendingCount(peerId);

    private void FlushMessages(double now)
    {
        while (_outMessages.TryDequeue(out var m))
        {
            var (stream, seq) = _reliable.Enqueue(m.peer, m.payload, now);
            SendReliable(m.peer, stream, seq, m.payload);
        }
        var skipped = new List<(ulong peer, byte[] payload)>();
        foreach (var (peer, stream, seq, payload) in _reliable.Due(now, skipped)) SendReliable(peer, stream, seq, payload);
        foreach (var (peer, payload) in skipped) DeliverMessage(peer, payload);
    }

    private void SendReliable(ulong peer, uint stream, uint seq, byte[] payload)
    {
        var ep = _peers.Get(peer)?.EndPoint;
        if (ep == null) return; // pair pas (encore) joignable : renvoyé plus tard ou abandonné
        Messages.WriteReliable(_wr, Id, stream, seq, payload);
        Send(_wr.Buffer, _wr.Length, ep);
    }

    private void HandleReliable(ulong from, int len, System.Net.IPEndPoint ep, double now)
    {
        if (!_peers.Touch(from, ep, now)) return; // inconnu : pas d'accusé, il renverra après notre découverte
        var r = Messages.BodyReader(_rx, len);
        uint stream = r.U32(), seq = r.U32();
        byte[] payload = r.Rest();
        Messages.WriteAck(_wr, Id, stream, seq);
        Send(_wr.Buffer, _wr.Length, ep);
        foreach (var p in _reliable.OnData(from, stream, seq, payload, now)) DeliverMessage(from, p);
    }

    private void HandleAck(ulong from, int len, System.Net.IPEndPoint ep, double now)
    {
        if (!_peers.Touch(from, ep, now)) return;
        var r = Messages.BodyReader(_rx, len);
        uint stream = r.U32();
        _reliable.OnAck(from, stream, r.U32());
    }

    private void DeliverMessage(ulong from, byte[] payload) =>
        _events.Enqueue(new NetEvent { Kind = NetEventKind.Message, PeerId = from, Data = payload, RecvMs = NetClock.NowMs });
}
