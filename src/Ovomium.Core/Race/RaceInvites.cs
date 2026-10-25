namespace Ovomium.Core.Race;

public enum InviteChoice
{
    /// Afficher l'invitation (remplace une invitation en attente).
    Show,
    /// Refuser tout de suite (déjà en course, ou invitation concurrente moins prioritaire).
    Decline,
    /// Abandonner notre course toute neuve au profit de celle-ci (lancées au même moment).
    Yield,
}

/// Invitation reçue : que faire, selon notre session en cours ? Deux joueurs qui lancent une course au
/// même moment s'invitent l'un l'autre avant d'avoir vu l'invitation de l'autre : la règle doit donner la
/// même réponse partout, sans échange. Priorité à l'hôte d'identifiant d'instance le plus petit :
///  - l'hôte prioritaire refuse l'invitation de l'autre ; l'autre annule sa course (Superseded) et
///    reçoit l'invitation du premier ;
///  - un tiers qui reçoit les deux garde celle de l'hôte prioritaire, quel que soit l'ordre d'arrivée.
/// Une course dont un invité a déjà accepté n'est jamais abandonnée (ce n'est plus un lancement simultané).
/// Un invité déjà arrivé ou qui a abandonné est libre (l'hôte, lui, reste occupé : il mène la course).
public static class RaceInvites
{
    public static InviteChoice Decide(IRaceSession current, ulong inviter)
    {
        if (current == null || current.View.IsOver) return InviteChoice.Show;
        if (current is RaceGuest { IsPending: true } g)
            return inviter == g.HostId || inviter < g.HostId ? InviteChoice.Show : InviteChoice.Decline;
        if (current is RaceGuest && current.Me?.IsActive != true) return InviteChoice.Show; // déjà arrivé ou abandonné : libre
        if (current is RaceHost { View.Phase: RacePhase.Gathering } h && !h.HasAcceptedGuests && inviter < h.MyId)
            return InviteChoice.Yield;
        return InviteChoice.Decline;
    }
}
