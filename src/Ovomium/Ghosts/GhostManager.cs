using System;
using System.Collections.Generic;
using Ovomium.Core.Net;
using Ovomium.Core.Poses;
using UnityEngine;

namespace Ovomium.Ghosts;

/// Un fantôme par pair : tampon de poses + marionnette, affichée seulement si le pair roule
/// sur la même piste que nous. Fil principal uniquement.
internal sealed class GhostManager
{
    private sealed class Ghost
    {
        public PeerInfo Info;
        public SnapshotBuffer Buffer;
        public Puppet Puppet;
        public readonly PoseFrame Sampled = new();
        public double RetryAtMs; // après un échec de création de marionnette
    }

    private readonly Dictionary<ulong, Ghost> _ghosts = new();

    public int VisibleCount { get; private set; }

    public void OnPeer(PeerInfo info)
    {
        if (!info.IsCompatible) return;
        if (_ghosts.TryGetValue(info.Id, out var g)) { g.Info = info; return; }
        // Historique gardé : retard d'affichage (jusqu'à quelques s en écho) + marge.
        _ghosts[info.Id] = new Ghost { Info = info, Buffer = new SnapshotBuffer(DelayMs(info) + 1500) };
    }

    public void OnLeft(ulong id)
    {
        if (!_ghosts.Remove(id, out var g)) return;
        g.Puppet?.Destroy();
    }

    public void OnPose(ulong id, PoseFrame frame, double recvMs)
    {
        if (_ghosts.TryGetValue(id, out var g)) g.Buffer.Add(frame, recvMs);
    }

    public bool IsVisible(ulong id) => _ghosts.TryGetValue(id, out var g) && g.Puppet != null;

    /// Crée, place ou détruit les marionnettes. myTrack : piste locale (null hors piste) ;
    /// source : vélo local servant de modèle.
    public void Update(string myTrack, GameObject source, double nowMs)
    {
        int visible = 0;
        foreach (var g in _ghosts.Values)
        {
            try { if (UpdateGhost(g, myTrack, source, nowMs)) visible++; }
            catch (Exception e)
            {
                if (g.Puppet == null) g.RetryAtMs = nowMs + 5000; // création ratée : pas un essai par image
                Log.ErrorThrottled($"fantôme {g.Info.Name}", e);
            }
        }
        VisibleCount = visible;
    }

    private bool UpdateGhost(Ghost g, string myTrack, GameObject source, double nowMs)
    {
        double delay = DelayMs(g.Info);
        bool fresh = nowMs - g.Buffer.LastRecvMs < delay + 3000; // le pair émet encore
        bool show = myTrack != null && g.Info.OnTrack && g.Info.Track == myTrack && fresh && source != null;
        if (g.Puppet != null && (!show || !g.Puppet.IsAlive)) Hide(g);
        if (!show) return false;
        if (g.Buffer.Sample(nowMs, delay, g.Sampled) == SampleStatus.Empty) return false;
        if (g.Puppet == null)
        {
            if (nowMs < g.RetryAtMs) return false;
            g.Puppet = Puppet.Create(source, g.Info.Name, ColorOf(g), GhostLook.Parse(Config.GhostStyle.Value));
            if (g.Puppet == null) { g.RetryAtMs = nowMs + 5000; Log.Warn($"marionnette impossible pour {g.Info.Name}"); return false; }
            Log.Info($"fantôme créé : {g.Info.Name} ({g.Info.Track})");
        }
        g.Puppet.Apply(g.Sampled);
        return true;
    }

    private static void Hide(Ghost g)
    {
        g.Puppet.Destroy();
        g.Puppet = null;
        Log.Info($"fantôme masqué : {g.Info.Name}");
    }

    private static double DelayMs(PeerInfo p) =>
        Config.InterpolationDelayMs.Value + (p.IsEcho ? Config.EchoDelaySeconds.Value * 1000.0 : 0);

    private static Color ColorOf(Ghost g) => GhostLook.ColorFor(g.Info.Id, g.Info.IsEcho);

    /// Pseudos à afficher : (nom, position monde au-dessus de la tête, couleur du joueur).
    public IEnumerable<(string name, Vector3 pos, Color color)> Labels()
    {
        foreach (var g in _ghosts.Values)
            if (g.Puppet != null && g.Puppet.IsAlive) yield return (g.Info.Name, g.Puppet.LabelPosition, ColorOf(g));
    }

    /// Diagnostic : pose échantillonnée vs position réelle de la marionnette (relue dans Unity).
    public string Describe()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var g in _ghosts.Values)
        {
            if (g.Puppet == null || !g.Puppet.IsAlive) continue;
            sb.Append($" [{g.Info.Name} visé={g.Sampled.RootPos}{(g.Sampled.IsRagdoll ? " CRASH" : "")} marionnette={g.Puppet.BindPosition.ToCore()}]");
        }
        return sb.ToString();
    }

    /// Total des échantillons « figés » (paquets en retard) : indicateur de qualité réseau.
    public int TotalHolds()
    {
        int n = 0;
        foreach (var g in _ghosts.Values) n += g.Buffer.HoldCount;
        return n;
    }

    /// Détruit les marionnettes (aspect changé) : Update les recrée à l'image suivante.
    public void RebuildPuppets()
    {
        foreach (var g in _ghosts.Values)
        {
            g.Puppet?.Destroy();
            g.Puppet = null;
        }
    }
}
