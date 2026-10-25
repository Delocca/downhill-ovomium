using System;
using System.Linq;
using System.Text;
using Ovomium.Core;
using Ovomium.Core.Net;
using Ovomium.Core.Poses;
using Ovomium.Core.Protocol;
using UnityEngine;

namespace Ovomium.Diagnostics;

/// Outils de test : statistiques périodiques et relevé des os
/// qui translatent (DebugLog), pour valider une session sans débogueur.
internal sealed class SessionTools
{
    private double _nextStats;
    // Relevé des positions locales min/max par os, sur les poses hors crash.
    private readonly Vec3[] _min = new Vec3[Skeleton.Count], _max = new Vec3[Skeleton.Count];
    private int _surveyed;
    private PoseFrame _lastSurveyed;
    // Performance sur la fenêtre de stats : images, durée réelle, temps passé dans le mod, pire image.
    private int _frames;
    private double _frameSec, _modMs, _worstFrameMs;

    /// À appeler une fois par image : modMs = temps passé dans le mod (Update + OnGUI) pour cette image.
    public void Frame(double modMs)
    {
        if (!Config.DebugLog.Value) return;
        float dt = Time.unscaledDeltaTime;
        _frames++;
        _frameSec += dt;
        _modMs += modMs;
        _worstFrameMs = Math.Max(_worstFrameMs, dt * 1000.0);
    }

    private string PerfLine()
    {
        if (_frames == 0) return "";
        string line = $" | {_frames / _frameSec:F1} img/s (pire {_worstFrameMs:F0} ms), mod {_modMs / _frames:F3} ms/img";
        _frames = 0; _frameSec = _modMs = _worstFrameMs = 0;
        return line;
    }

    /// En DebugLog, on envoie la position de tous les os : le relevé dit lesquels bougent vraiment.
    public uint PositionMask => Config.DebugLog.Value ? Skeleton.AllMask : Skeleton.DefaultPositionMask;

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
                 $"envoyées={node.PosesSent} paquets in/out={node.PacketsIn}/{node.PacketsOut} figés={holds}{PerfLine()}");
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
