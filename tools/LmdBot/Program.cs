using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using LmdMulti.Core.Net;
using LmdMulti.Core.Poses;
using LmdMulti.Core.Protocol;

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

        IPoseSource source = new OrbitSource(o.Radius);
        string track = o.Track;
        if (o.Replay != null)
        {
            string path = BotOptions.ResolveReplay(o.Replay);
            var (recTrack, frames) = PoseRecorder.Read(path);
            var replay = new ReplaySource(frames);
            source = replay;
            track ??= recTrack;
            Say($"rejeu de {path} : {frames.Count} poses, {replay.LengthS:F1} s, piste {recTrack}");
        }

        using var node = new UdpNode(o.ToNodeOptions());
        if (!node.Start()) { Console.Error.WriteLine(node.Status); return 1; }
        Say($"{o.Name} : {node.Status}, protocole v{ProtocolInfo.Version}, annonces vers le port {o.TargetPort}");
        var stop = new ManualResetEventSlim();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Set(); };
        Run(o, node, source, track, stop);
        Say("arrêt (Bye envoyé)");
        return 0;
    }

    private static void Run(BotOptions o, UdpNode node, IPoseSource source, string fixedTrack, ManualResetEventSlim stop)
    {
        var stats = new Dictionary<ulong, PeerStats>();
        var names = new Dictionary<ulong, PeerInfo>();
        var w = new PacketWriter();
        double start = NetClock.NowMs, nextReport = start + 2000;
        while (!stop.IsSet && NetClock.NowMs - start < o.DurationS * 1000)
        {
            Drain(node, stats, names);
            // Ancre de l'orbite : un vrai joueur (deux bots qui s'orbitent mutuellement divergent).
            var anchorPeer = names.Values.FirstOrDefault(p => p.IsCompatible && p.OnTrack && p.ModVersion != "bot" && stats.ContainsKey(p.Id));
            string track = fixedTrack ?? anchorPeer?.Track ?? names.Values.FirstOrDefault(p => p.OnTrack)?.Track;
            node.SetLocal(o.Name, 76561190000000000UL + (node.Id & 0xFFFF), track ?? "", track != null);
            if (track != null)
            {
                double now = NetClock.NowMs;
                var f = source.Next(now, anchorPeer != null ? stats[anchorPeer.Id].Last : null);
                f.TimeMs = NetClock.NowMsU32;
                Messages.WritePose(w, node.Id, f);
                node.SendPose(w.ToArray());
            }
            if (NetClock.NowMs >= nextReport) { nextReport += 2000; Report(node, stats, track); }
            stop.Wait(1000 / o.Rate);
        }
    }

    private static void Drain(UdpNode node, Dictionary<ulong, PeerStats> stats, Dictionary<ulong, PeerInfo> names)
    {
        while (node.TryGetEvent(out var e))
        {
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

    private static void Say(string msg) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {msg}");
}
