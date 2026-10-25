using System;
using System.Collections.Generic;

namespace Ovomium.Core.Race;

/// Invitation traitée : choix (RaceInvites), message reçu, acceptée d'office (course abandonnée au profit
/// d'une autre sur la même piste).
public sealed record InviteResult(InviteChoice Choice, RaceMessage Invite, bool AutoAccepted);

/// Course du joueur local (une à la fois) et aiguillage des messages de course : invitations arbitrées
/// par RaceInvites, le reste confié à la session. Commun au mod et au bot (LmdBot), testé sans réseau.
public sealed class RaceLobby
{
    private readonly ulong _myId;
    private readonly Action<ulong, byte[]> _send;
    private readonly Func<ulong, double?> _clockOffset;
    private readonly Random _rng;

    /// Course en cours ou dernière course (null avant la première).
    public IRaceSession Session { get; private set; }

    /// clockOffset(pair) : horloge du pair − la nôtre (ms), null si inconnue.
    public RaceLobby(ulong myId, Action<ulong, byte[]> send, Func<ulong, double?> clockOffset, int? seed = null)
    {
        _myId = myId;
        _send = send;
        _clockOffset = clockOffset;
        _rng = seed.HasValue ? new Random(seed.Value) : new Random();
    }

    /// Engagé dans une course non terminée : hôte, ou invité encore en lice (une invitation en attente,
    /// une arrivée ou un abandon ne comptent pas).
    public bool Busy => Session != null && !Session.View.IsOver
                        && (Session is RaceHost || Session is RaceGuest { IsPending: false } && Session.Me?.IsActive == true);

    public RaceGuest PendingInvite => Session is RaceGuest { IsPending: true } g ? g : null;

    /// Lance une course comme hôte (invitations envoyées).
    public RaceHost Host(string track, IEnumerable<(ulong id, string name)> invitees, string myName, double now)
    {
        var host = new RaceHost((uint)_rng.Next(1, int.MaxValue), track, _myId, myName, invitees, now, _send);
        Session = host;
        return host;
    }

    /// Message de course reçu. Renvoie le traitement d'une invitation, null pour les autres messages
    /// (et les messages illisibles, ignorés).
    public InviteResult OnMessage(ulong from, byte[] data, double now, string myName)
    {
        RaceMessage m;
        try { m = RaceMessages.Read(data); }
        catch (FormatException) { return null; }
        if (m.Kind != RaceMsgKind.Invite) { Session?.OnMessage(from, m, now); return null; }
        var choice = RaceInvites.Decide(Session, from);
        if (choice == InviteChoice.Decline) { _send(from, RaceMessages.Reply(m.RaceId, false)); return new InviteResult(choice, m, false); }
        bool sameTrack = false;
        if (Session is RaceHost mine && choice == InviteChoice.Yield)
        {
            sameTrack = mine.View.Track == m.Track;
            mine.Cancel(CancelReason.Superseded);
            mine.Tick(now); // publie l'annulation : la session va être remplacée
        }
        PendingInvite?.Decline(); // une invitation prioritaire remplace celle en attente
        var guest = new RaceGuest(m, from, _myId, myName, now, _send, () => _clockOffset(from));
        Session = guest;
        if (sameTrack) guest.Accept(); // on voulait courir sur cette piste : rien à redemander
        return new InviteResult(choice, m, sameTrack);
    }

    public void OnPeerLeft(ulong id, double now) => Session?.OnPeerLeft(id, now);

    /// Pair revenu après une coupure : l'hôte lui rend sa place s'il en avait une.
    public void OnPeerBack(ulong id, double now) => (Session as RaceHost)?.OnPeerBack(id, now);

    public void Tick(double now) => Session?.Tick(now);
}
