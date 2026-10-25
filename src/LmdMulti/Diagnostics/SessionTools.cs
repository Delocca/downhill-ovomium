using System;
using System.IO;
using System.Linq;
using System.Text;
using LmdMulti.Core;
using LmdMulti.Core.Net;
using LmdMulti.Core.Poses;
using LmdMulti.Core.Protocol;
using UnityEngine;

namespace LmdMulti.Diagnostics;

/// Outils de test : capture d'écran par touche, statistiques périodiques et relevé des os
/// qui translatent (DebugLog), pour valider une session sans débogueur.
internal sealed class SessionTools
{
    private double _nextStats;
    private int _captureIndex;
    // Relevé des positions locales min/max par os, sur les poses hors crash.
    private readonly Vec3[] _min = new Vec3[Skeleton.Count], _max = new Vec3[Skeleton.Count];
    private int _surveyed;
    private PoseFrame _lastSurveyed;

    /// En DebugLog, on envoie la position de tous les os : le relevé dit lesquels bougent vraiment.
    public uint PositionMask => Config.DebugLog.Value ? (1u << Skeleton.Count) - 1 : Skeleton.DefaultPositionMask;

    public void Capture()
    {
        string dir = Path.Combine(Config.DataDir, "captures");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, $"capture-{DateTime.Now:HHmmss}-{++_captureIndex:D2}.png");
        ScreenCapture.CaptureScreenshot(path);
        Log.Info($"CAPTURE {_captureIndex} : {path}");
    }

    public void Survey(PoseFrame f)
    {
        if (f == null || f == _lastSurveyed || !Config.DebugLog.Value || f.IsRagdoll || !f.HasBones) return;
        _lastSurveyed = f;
        for (int i = 0; i < Skeleton.Count; i++)
        {
            if ((f.PosMask >> i & 1) == 0) continue;
            var p = f.BonePos[i];
            if (_surveyed == 0) { _min[i] = p; _max[i] = p; continue; }
            _min[i] = new Vec3(Math.Min(_min[i].X, p.X), Math.Min(_min[i].Y, p.Y), Math.Min(_min[i].Z, p.Z));
            _max[i] = new Vec3(Math.Max(_max[i].X, p.X), Math.Max(_max[i].Y, p.Y), Math.Max(_max[i].Z, p.Z));
        }
        _surveyed++;
    }

    /// Toutes les 10 s en DebugLog : état réseau, fantômes, os qui translatent.
    public void Tick(UdpNode node, int visibleGhosts, int holds, string myTrack, System.Func<string> describeGhosts)
    {
        if (!Config.DebugLog.Value || NetClock.NowMs < _nextStats) return;
        _nextStats = NetClock.NowMs + 10000;
        var peers = node.Peers();
        string list = string.Join(", ", peers.Select(p => $"{p.Name}[{(p.OnTrack ? p.Track : "menu")} {p.RttMs}ms {p.PosesReceived}p]"));
        Log.Info($"stats : piste={myTrack ?? "-"} pairs={peers.Count} ({list}) fantômes={visibleGhosts} " +
                 $"envoyées={node.PosesSent} paquets in/out={node.PacketsIn}/{node.PacketsOut} figés={holds}");
        if (visibleGhosts > 0) Log.Info($"fantômes :{describeGhosts()}");
        if (_surveyed > 0) Log.Info(SurveyLine());
    }

    private string SurveyLine()
    {
        var sb = new StringBuilder($"os qui translatent ({_surveyed} poses, amplitude > 1 mm) :");
        for (int i = 0; i < Skeleton.Count; i++)
        {
            float range = Vec3.Distance(_min[i], _max[i]);
            if (range > 0.001f) sb.Append($" {Skeleton.Bones[i]}={range * 100:F1}cm");
        }
        return sb.ToString();
    }
}
