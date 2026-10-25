using System.Collections.Generic;
using System.Linq;
using LmdMulti.Core.Net;
using LmdMulti.Core.Protocol;
using LmdMulti.Game;
using UnityEngine;

namespace LmdMulti.UI;

/// Panneau discret en haut à droite : état réseau et joueurs détectés (piste, ping, fantôme visible).
internal sealed class PlayersPanel
{
    public bool Visible = true;
    private readonly List<string> _lines = new();
    private double _nextRefresh;

    /// Recalcule le texte (2×/s : inutile de copier la table des pairs à chaque image).
    public void Refresh(UdpNode node, string myName, string myTrack, System.Func<ulong, bool> isGhostVisible)
    {
        double now = NetClock.NowMs;
        if (!Visible || now < _nextRefresh) return;
        _nextRefresh = now + 500;
        _lines.Clear();
        string echo = node.Echo ? "  ·  <color=#ffd866>écho</color>" : "";
        _lines.Add($"<b>LmdMulti {LmdMultiMod.Version}</b>  ·  {node.Status}{echo}");
        _lines.Add($"<color=#9fe870>●</color> <b>{Esc(myName)}</b> (toi) — {GameInfo.TrackName(myTrack)}");
        var peers = node.Peers().OrderBy(p => p.IsCompatible ? 0 : 1).ThenBy(p => p.Track != myTrack).ThenBy(p => p.Name);
        foreach (var p in peers) _lines.Add(PeerLine(p, myTrack, isGhostVisible(p.Id)));
        if (_lines.Count == 2) _lines.Add("<color=#aaaaaa>aucun autre joueur détecté</color>");
        _lines.Add($"<size={(int)(13 * GuiKit.Scale)}><color=#aaaaaa>{Config.PanelKey.Value} panneau · {Config.EchoKey.Value} écho · {Config.CaptureKey.Value} capture</color></size>");
    }

    private static string PeerLine(PeerInfo p, string myTrack, bool visible)
    {
        if (!p.IsCompatible)
            return $"<color=#ff6b6b>×{Esc(p.Name)} — version incompatible (v{p.IncompatibleVersion}, toi v{ProtocolInfo.Version})</color>";
        string track = p.OnTrack ? GameInfo.TrackName(p.Track) : "menu";
        bool sameTrack = p.OnTrack && myTrack != null && p.Track == myTrack;
        string dot = sameTrack ? "<color=#9fe870>●</color>" : "<color=#888888>○</color>";
        string ping = p.IsEcho ? "" : p.RttMs >= 0 ? $" · {p.RttMs} ms" : " · … ms";
        string ghost = visible ? " · <color=#7fd4ff>fantôme</color>" : "";
        return $"{dot} {Esc(p.Name)} — {track}{ping}{ghost}";
    }

    /// Un pseudo ne doit pas pouvoir injecter de balises de mise en forme.
    private static string Esc(string s) => s.Replace("<", "‹").Replace(">", "›");

    public void Draw()
    {
        if (!Visible || _lines.Count == 0) return;
        GuiKit.Ensure();
        float lineH = 24 * GuiKit.Scale, pad = 10 * GuiKit.Scale, w = 470 * GuiKit.Scale;
        var box = new Rect(Screen.width - w - pad, pad, w, _lines.Count * lineH + pad * 2);
        GuiKit.Panel(box);
        for (int i = 0; i < _lines.Count; i++)
            GUI.Label(new Rect(box.x + pad, box.y + pad + i * lineH, w - pad * 2, lineH), _lines[i], GuiKit.Text);
    }
}
