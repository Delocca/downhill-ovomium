using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Ovomium.Core.Net;
using Ovomium.Core.Poses;
using Ovomium.Core.Protocol;

namespace LmdBot;

/// Faux joueur : s'annonce comme un vrai client (même code réseau que le mod), envoie des poses
/// (orbite autour du joueur ou rejeu d'un enregistrement) et affiche ce qu'il reçoit du jeu.
internal static class Program
{
    private sealed class PeerStats { public int Poses, Ragdoll, WithBones; public PoseFrame Last; }

    private static int Main(string[] args)
    {
        BotOptions o;
        try { o = BotOptions.Parse(args); }
        catch (Exception e) { Console.Error.WriteLine(e.Message); return 2; }
        if (o == null) { Console.WriteLine(BotOptions.Usage); return 0; }

        string recTrack = null;
        List<PoseFrame> frames = null;
        if (o.Replay != null)
        {
            string path = BotOptions.ResolveReplay(o.Replay);
            (recTrack, frames) = PoseRecorder.Read(path);
            uint firstMs = frames.Count > 0 ? frames[0].TimeMs : 0;
            frames = ReplaySource.TrimIdleStart(frames);
            if (frames.Count > 0 && frames[0].TimeMs > firstMs) Say($"attente au départ retirée : {(frames[0].TimeMs - firstMs) / 1000.0:F1} s");
            Say($"rejeu de {path} : {frames.Count} poses, {new ReplaySource(frames).LengthS:F1} s, piste {recTrack}");
        }
        var stop = new ManualResetEventSlim();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Set(); };
        // kill / pkill (SIGTERM) : arrêt propre aussi, avec « Bye ». Utile en arrière-plan, où Ctrl+C
        // (SIGINT) est souvent ignoré par le shell qui l'a lancé.
        using var term = System.Runtime.InteropServices.PosixSignalRegistration.Create(
            System.Runtime.InteropServices.PosixSignal.SIGTERM, c => { c.Cancel = true; stop.Set(); });
        var threads = new List<Thread>();
        int failures = 0;
        for (int i = 0; i < o.Count; i++)
        {
            var b = o.ForInstance(i);
            IPoseSource source = frames != null ? new ReplaySource(frames, 1500.0 * i) : new OrbitSource(b.Radius, 2.4f * i);
            var t = new Thread(() => { if (!RunBot(b, source, b.Track ?? recTrack, stop)) Interlocked.Increment(ref failures); }) { Name = b.Name };
            t.Start();
            threads.Add(t);
        }
        foreach (var t in threads) t.Join();
        return failures > 0 ? 1 : 0;
    }

    private static bool RunBot(BotOptions o, IPoseSource source, string track, ManualResetEventSlim stop)
    {
        using var node = new UdpNode(o.ToNodeOptions());
        if (!node.Start()) { Console.Error.WriteLine($"{o.Name} : {node.Status}"); return false; }
        Say($"{o.Name} : {node.Status}, protocole v{ProtocolInfo.Version}, annonces vers le port {o.TargetPort}");
        Run(o, node, source, track, stop);
        Say($"{o.Name} : arrêt (Bye envoyé)");
        return true;
    }

    private static void Run(BotOptions o, UdpNode node, IPoseSource source, string fixedTrack, ManualResetEventSlim stop)
    {
        var stats = new Dictionary<ulong, PeerStats>();
        var names = new Dictionary<ulong, PeerInfo>();
        var w = new PacketWriter();
        var race = o.RaceMode != null ? new RaceBot(o, node) : null;
        double start = NetClock.NowMs, nextReport = start + 2000;
        while (!stop.IsSet && NetClock.NowMs - start < o.DurationS * 1000)
        {
            Drain(node, stats, names, race);
            race?.Tick(NetClock.NowMs, names.Values.ToList());
            // Ancre de l'orbite : un vrai joueur (deux bots qui s'orbitent mutuellement divergent).
            var anchorPeer = names.Values.FirstOrDefault(p => p.IsCompatible && p.OnTrack && p.ModVersion != "bot" && stats.ContainsKey(p.Id));
            string track = race?.Track ?? fixedTrack ?? anchorPeer?.Track ?? names.Values.FirstOrDefault(p => p.OnTrack)?.Track;
            node.SetLocal(o.Name, 76561190000000000UL + (node.Id & 0xFFFF), track ?? "", track != null);
            if (track != null && !o.NoPoses)
            {
                double now = NetClock.NowMs;
                var f = source.Next(now, anchorPeer != null ? stats[anchorPeer.Id].Last : null);
                f.TimeMs = NetClock.NowMsU32;
                Messages.WritePose(w, node.Id, f);
                node.SendPose(w.ToArray());
            }
            if (o.Index == 0 && NetClock.NowMs >= nextReport) { nextReport += 2000; Report(node, stats, track); }
            stop.Wait(1000 / o.Rate);
        }
    }

    private static void Drain(UdpNode node, Dictionary<ulong, PeerStats> stats, Dictionary<ulong, PeerInfo> names, RaceBot race)
    {
        while (node.TryGetEvent(out var e))
        {
            race?.OnEvent(e, NetClock.NowMs);
            switch (e.Kind)
            {
                case NetEventKind.PeerJoined:
                    Say($"+ {e.Peer.Name} ({e.Peer.EndPoint}) {(e.Peer.IsCompatible ? $"mod {e.Peer.ModVersion}" : $"PROTOCOLE INCOMPATIBLE v{e.Peer.IncompatibleVersion}")} piste={Track(e.Peer)}");
                    names[e.PeerId] = e.Peer; break;
                case NetEventKind.PeerChanged:
                    Say($"~ {e.Peer.Name} : piste={Track(e.Peer)}");
                    names[e.PeerId] = e.Peer; break;
                case NetEventKind.PeerLeft:
                    Say($"- {e.Peer.Name} parti");
                    names.Remove(e.PeerId); stats.Remove(e.PeerId); break;
                case NetEventKind.Pose:
                    if (!stats.TryGetValue(e.PeerId, out var s)) stats[e.PeerId] = s = new PeerStats();
                    s.Poses++; s.Last = e.Pose;
                    if (e.Pose.IsRagdoll) s.Ragdoll++;
                    if (e.Pose.HasBones) s.WithBones++;
                    break;
                case NetEventKind.Log: Say($"! {e.Text}"); break;
            }
        }
    }

    private static void Report(UdpNode node, Dictionary<ulong, PeerStats> stats, string track)
    {
        var peers = node.Peers();
        Say($"piste annoncée={track ?? "(aucune)"} pairs={peers.Count} poses envoyées={node.PosesSent}");
        foreach (var p in peers)
        {
            string rx = stats.TryGetValue(p.Id, out var s)
                ? $"poses reçues={s.Poses} (os {s.WithBones}, ragdoll {s.Ragdoll}) racine={s.Last.RootPos}{(s.Last.IsRagdoll ? " CRASH" : "")}"
                : "aucune pose reçue";
            Say($"   {p.Name} piste={Track(p)} ping={p.RttMs} ms {rx}");
        }
    }

    private static string Track(PeerInfo p) => p.OnTrack ? p.Track : "menu";

    internal static void Say(string msg) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {msg}");
}
