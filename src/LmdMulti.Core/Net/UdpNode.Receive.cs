using System;
using System.Net;
using System.Net.Sockets;
using LmdMulti.Core.Protocol;

namespace LmdMulti.Core.Net;

/// Réception et aiguillage des paquets (fil réseau uniquement).
public sealed partial class UdpNode
{
    private EndPoint _from = new IPEndPoint(IPAddress.Any, 0);

    private void ReceiveAll()
    {
        while (_sock.Available > 0)
        {
            int len;
            try { len = _sock.ReceiveFrom(_rx, ref _from); }
            catch (SocketException) { continue; } // ex. ICMP « port injoignable » remonté par l'OS
            PacketsIn++;
            var ep = (IPEndPoint)_from;
            try { Handle(len, new IPEndPoint(ep.Address, ep.Port), NetClock.NowMs); }
            catch (FormatException) { /* paquet malformé : ignoré */ }
        }
    }

    private void Handle(int len, IPEndPoint ep, double now)
    {
        var status = Messages.ReadHeader(_rx, len, out var h);
        if (status == HeaderStatus.NotOurs) return;
        bool fromSelf = h.SenderId == Id;
        if (status == HeaderStatus.WrongVersion)
        {
            if (!fromSelf) _peers.OnIncompatible(h.SenderId, h.Version, ep, now);
            return;
        }
        if (fromSelf)
        {
            if (h.Type == MessageType.Pose && _echo) HandleEchoPose(len, now);
            return; // nos propres broadcasts reviennent : ignorés
        }
        switch (h.Type)
        {
            case MessageType.Announce:
                _peers.OnAnnounce(h.SenderId, Messages.ReadAnnounce(Messages.BodyReader(_rx, len)), ep, now);
                break;
            case MessageType.Pose:
                if (_peers.Touch(h.SenderId, ep, now, countPose: true)) EnqueuePose(h.SenderId, len, now);
                break;
            case MessageType.Ping:
                if (!_peers.Touch(h.SenderId, ep, now)) break;
                uint t = Messages.BodyReader(_rx, len).U32();
                Messages.WritePing(_w, MessageType.Pong, Id, t);
                Send(_w.Buffer, _w.Length, ep);
                break;
            case MessageType.Pong:
                if (_peers.Touch(h.SenderId, ep, now))
                    _peers.SetRtt(h.SenderId, (int)(NetClock.NowMsU32 - Messages.BodyReader(_rx, len).U32()));
                break;
            case MessageType.Bye:
                _peers.Remove(h.SenderId);
                break;
        }
    }

    private void EnqueuePose(ulong peerId, int len, double now) =>
        _events.Enqueue(new NetEvent
        {
            Kind = NetEventKind.Pose, PeerId = peerId, RecvMs = now,
            Pose = Messages.ReadPose(Messages.BodyReader(_rx, len)),
        });

    /// Notre pose revenue par la boucle locale : livrée comme celle d'un pair « écho ».
    private void HandleEchoPose(int len, double now)
    {
        var local = Local();
        var echo = new Announce { Name = local.Name + " (écho)", SteamId = local.SteamId, Track = local.Track, OnTrack = local.OnTrack, ModVersion = local.ModVersion };
        _peers.OnAnnounce(EchoId, echo, null, now, isEcho: true);
        _peers.Touch(EchoId, null, now, countPose: true);
        EnqueuePose(EchoId, len, now);
    }
}
