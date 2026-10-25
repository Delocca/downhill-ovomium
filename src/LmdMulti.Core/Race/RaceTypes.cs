using System;
using System.Collections.Generic;
using System.Linq;

namespace LmdMulti.Core.Race;

/// Règles de course (valeurs par défaut documentées dans docs/architecture.md).
public static class RaceRules
{
    /// Délai pour répondre à une invitation.
    public const double InviteWindowMs = 15000;
    /// Délai de chargement accordé à chaque invité, compté depuis son acceptation ; au-delà, il est exclu.
    public const double LoadTimeoutMs = 30000;
    /// Annonce du départ : le « 3 » s'affiche CountdownMs avant le départ ; la marge avant laisse le temps
    /// au message d'arriver (et d'être renvoyé s'il se perd).
    public const double CountdownLeadMs = 4500, CountdownMs = 3000;
    /// Après la première arrivée, les autres ont ce délai pour finir (sinon « abandon »).
    public const double FinishWindowMs = 60000;
    /// Course interrompue au-delà (personne n'arrive).
    public const double MaxRaceMs = 30 * 60000;
    public const int MaxRacers = 16;
}

public enum RacePhase : byte { Gathering = 1, Countdown = 2, Racing = 3, Finished = 4, Cancelled = 5 }

/// État d'un coureur. Loading → Ready → Racing → Finished/Dnf ; Invited → Declined ; Excluded = trop lent à charger.
public enum RacerState : byte { Invited = 0, Loading = 1, Ready = 2, Racing = 3, Finished = 4, Dnf = 5, Declined = 6, Excluded = 7 }

public enum CancelReason : byte
{
    None = 0,
    HostCancelled = 1,   // l'hôte a annulé avant le départ
    NobodyJoined = 2,    // aucun invité prêt
    HostLeft = 3,        // (local) l'hôte a disparu du réseau
    Excluded = 4,        // chargement trop long : course partie sans toi
    TooLate = 5,         // réponse arrivée après le départ ou le délai
    HostFailed = 6,      // l'hôte n'a pas pu charger la piste
    LoadFailed = 7,      // (local) chargement de la piste impossible chez toi
}

public sealed class RaceEntry
{
    public ulong Id;
    public string Name = "";
    public RacerState State;
    /// Temps de course (Finished), ms.
    public uint TimeMs;
    /// Sections (checkpoints) franchies, pour le classement en direct.
    public byte Progress;
    // Hôte seulement (non transmis) :
    internal double ProgressAtMs, DeadlineMs;

    public bool IsActive => State is RacerState.Loading or RacerState.Ready or RacerState.Racing;
    /// Montré dans les classements (les refus, exclus et invitations sans réponse ne le sont pas).
    public bool IsListed => State is not (RacerState.Declined or RacerState.Excluded or RacerState.Invited);
    public RaceEntry Clone() => (RaceEntry)MemberwiseClone();
}

/// Vue d'une course, identique pour l'hôte et les invités (les invités la reçoivent de l'hôte).
/// Heures en ms dans l'horloge NetClock de celui qui la détient (converties à la réception), NaN = aucune.
public sealed class Standings
{
    public uint RaceId, Revision;
    public RacePhase Phase = RacePhase.Gathering;
    public CancelReason Reason;
    public string Track = "";
    public ulong HostId;
    public double StartAtMs = double.NaN, EndAtMs = double.NaN;
    /// Triées par classement (Ranking.Sort).
    public List<RaceEntry> Entries = new();

    public bool IsOver => Phase is RacePhase.Finished or RacePhase.Cancelled;
    public RaceEntry Find(ulong id) => Entries.FirstOrDefault(e => e.Id == id);
    public string HostName => Find(HostId)?.Name ?? "?";
}

/// Classement : arrivés par temps, puis en course par sections franchies (le premier à les franchir
/// devant), puis abandons ; les autres à la fin.
public static class Ranking
{
    public static void Sort(List<RaceEntry> entries)
    {
        var sorted = entries.OrderBy(Group).ThenBy(e => e.State == RacerState.Finished ? e.TimeMs : 0)
                            .ThenByDescending(e => e.Progress).ThenBy(e => e.ProgressAtMs).ToList();
        entries.Clear();
        entries.AddRange(sorted);
    }

    private static int Group(RaceEntry e) => e.State switch
    {
        RacerState.Finished => 0,
        RacerState.Racing or RacerState.Ready or RacerState.Loading => 1,
        RacerState.Dnf => 2,
        _ => 3,
    };

    /// Place (1, 2…) parmi les arrivés, 0 sinon.
    public static int PlaceOf(Standings s, ulong id)
    {
        int place = 0;
        foreach (var e in s.Entries.Where(e => e.State == RacerState.Finished))
        {
            place++;
            if (e.Id == id) return place;
        }
        return 0;
    }

    /// « 1:02.35 » (ou « 42.35 » sous la minute).
    public static string FormatTime(double ms)
    {
        if (double.IsNaN(ms) || ms < 0) ms = 0;
        var t = TimeSpan.FromMilliseconds(ms);
        return t.TotalMinutes >= 1 ? $"{(int)t.TotalMinutes}:{t.Seconds:00}.{t.Milliseconds / 10:00}" : $"{t.Seconds}.{t.Milliseconds / 10:00}";
    }
}
