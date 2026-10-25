using System;
using System.Net;
using System.Net.Sockets;
using Ovomium.Core.Protocol;

namespace Ovomium.Core.Net;

/// Réception et aiguillage des paquets (fil réseau uniquement).
public sealed partial class UdpNode
{
    private EndPoint _from = new IPEndPoint(IPAddress.Any, 0);

    /// Lit tout ce qui attend (au plus 1000 paquets d'affilée : les envois ne doivent pas attendre).
    /// Pas de boucle sur `Available` : un datagramme vide (scan de ports…) y vaut 0 et ne serait jamais lu,
    /// bloquant toute réception (test EmptyDatagram_DoesNotStallReception).
    private void ReceiveAll()
    {
        for (int n = 0; n < 1000 && _sock.Poll(0, SelectMode.SelectRead); n++)
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
                Messages.WritePong(_w, Id, t, NetClock.NowMsU32);
                Send(_w.Buffer, _w.Length, ep);
                break;
            case MessageType.Pong:
                if (!_peers.Touch(h.SenderId, ep, now)) break;
                var pong = Messages.BodyReader(_rx, len);
                uint sent = pong.U32();
                _peers.OnPong(h.SenderId, sent, pong.U32(), NetClock.NowMsU32);
                break;
            case MessageType.Bye:
                _peers.Remove(h.SenderId);
                break;
            case MessageType.Reliable: HandleReliable(h.SenderId, len, ep, now); break;
            case MessageType.Ack: HandleAck(h.SenderId, len, ep, now); break;
        }
    }

    /// Au-delà de MaxQueuedEvents en attente (fil principal arrêté : chargement long, jeu en arrière-plan),
    /// les poses sont jetées (jamais les arrivées, départs ni messages) : la mémoire ne croît pas sans fin.
    private const int MaxQueuedEvents = 4000;

    private void EnqueuePose(ulong peerId, int len, double now)
    {
        if (_events.Count < MaxQueuedEvents) _events.Enqueue(new NetEvent
        {
            Kind = NetEventKind.Pose, PeerId = peerId, RecvMs = now,
            Pose = Messages.ReadPose(Messages.BodyReader(_rx, len)),
        });
    }

    /// Notre pose revenue par la boucle locale : livrée comme celle d'un pair « écho ».
    private void HandleEchoPose(int len, double now)
    {
        var local = Local() ?? new Announce();
        var echo = new Announce { Name = local.Name + " (écho)", SteamId = local.SteamId, Track = local.Track, OnTrack = local.OnTrack, ModVersion = local.ModVersion };
        _peers.OnAnnounce(EchoId, echo, null, now, isEcho: true);
        _peers.Touch(EchoId, null, now, countPose: true);
        EnqueuePose(EchoId, len, now);
    }
}
