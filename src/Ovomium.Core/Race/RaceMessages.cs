using System;
using Ovomium.Core.Protocol;

namespace Ovomium.Core.Race;

public enum RaceMsgKind : byte { Invite = 1, Reply = 2, Status = 3, Standings = 4, Cancel = 5 }

/// Message de course décodé (contenu d'un message fiable ; seuls les champs de son type sont remplis).
public sealed class RaceMessage
{
    public RaceMsgKind Kind;
    public uint RaceId;
    public string Track = "", HostName = "";   // Invite
    public bool Accept;                        // Reply
    public RacerState State;                   // Status
    public byte Progress;
    public uint TimeMs;
    public Standings Standings;                // Standings (heures dans l'horloge de l'hôte)
    public double HostNowMs;                   // Standings : heure de l'hôte à l'encodage
    public CancelReason Reason;                // Cancel
}

/// Format des messages de course (dans un message fiable) : type u8, course u32, puis selon le type.
/// Heures en u32 ms de l'horloge de l'hôte (0 = aucune). Détail : docs/architecture.md.
public static class RaceMessages
{
    private const int NameBytes = 32;

    public static byte[] Invite(uint raceId, string track, string hostName) => Build(RaceMsgKind.Invite, raceId, w =>
    {
        w.Str(track, 32);
        w.Str(hostName, NameBytes);
    });

    public static byte[] Reply(uint raceId, bool accept) => Build(RaceMsgKind.Reply, raceId, w => w.U8(accept ? (byte)1 : (byte)0));

    public static byte[] Status(uint raceId, RacerState state, byte progress, uint timeMs) => Build(RaceMsgKind.Status, raceId, w =>
    {
        w.U8((byte)state);
        w.U8(progress);
        w.U32(timeMs);
    });

    public static byte[] Cancel(uint raceId, CancelReason reason) => Build(RaceMsgKind.Cancel, raceId, w => w.U8((byte)reason));

    /// Encodé par l'hôte (heures de son horloge). Au plus RaceRules.MaxRacers coureurs.
    public static byte[] Standings(Standings s, double hostNowMs) => Build(RaceMsgKind.Standings, s.RaceId, w =>
    {
        w.U32(s.Revision);
        w.U8((byte)s.Phase);
        w.U8((byte)s.Reason);
        w.Str(s.Track, 32);
        w.U64(s.HostId);
        w.U32(Time(hostNowMs));
        w.U32(Time(s.StartAtMs));
        w.U32(Time(s.EndAtMs));
        int n = Math.Min(s.Entries.Count, RaceRules.MaxRacers);
        w.U8((byte)n);
        for (int i = 0; i < n; i++)
        {
            var e = s.Entries[i];
            w.U64(e.Id); w.Str(e.Name, NameBytes); w.U8((byte)e.State); w.U8(e.Progress); w.U32(e.TimeMs);
        }
    });

    public static RaceMessage Read(byte[] data)
    {
        var r = new PacketReader(data, data.Length);
        var m = new RaceMessage { Kind = (RaceMsgKind)r.U8(), RaceId = r.U32() };
        switch (m.Kind)
        {
            case RaceMsgKind.Invite: m.Track = r.Str(); m.HostName = r.Str(); break;
            case RaceMsgKind.Reply: m.Accept = r.U8() != 0; break;
            case RaceMsgKind.Status: m.State = (RacerState)r.U8(); m.Progress = r.U8(); m.TimeMs = r.U32(); break;
            case RaceMsgKind.Cancel: m.Reason = (CancelReason)r.U8(); break;
            case RaceMsgKind.Standings: ReadStandings(r, m); break;
            default: throw new FormatException($"message de course inconnu ({(byte)m.Kind})");
        }
        return m;
    }

    private static void ReadStandings(PacketReader r, RaceMessage m)
    {
        var s = m.Standings = new Standings { RaceId = m.RaceId, Revision = r.U32(), Phase = (RacePhase)r.U8(), Reason = (CancelReason)r.U8() };
        s.Track = r.Str();
        s.HostId = r.U64();
        m.HostNowMs = r.U32();
        s.StartAtMs = FromTime(r.U32());
        s.EndAtMs = FromTime(r.U32());
        int n = r.U8();
        for (int i = 0; i < n; i++)
            s.Entries.Add(new RaceEntry { Id = r.U64(), Name = r.Str(), State = (RacerState)r.U8(), Progress = r.U8(), TimeMs = r.U32() });
    }

    private static uint Time(double ms) => double.IsNaN(ms) ? 0 : Math.Max(1u, (uint)Math.Round(ms));
    private static double FromTime(uint t) => t == 0 ? double.NaN : t;

    private static byte[] Build(RaceMsgKind kind, uint raceId, Action<PacketWriter> body)
    {
        var w = new PacketWriter();
        w.U8((byte)kind);
        w.U32(raceId);
        body(w);
        return w.ToArray();
    }
}
