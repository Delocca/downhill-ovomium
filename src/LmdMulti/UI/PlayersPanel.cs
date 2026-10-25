using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using LmdMulti.Core.Net;
using LmdMulti.Core.Protocol;
using LmdMulti.Game;
using LmdMulti.Ghosts;
using UnityEngine;

namespace LmdMulti.UI;

/// Panneau discret en haut à droite : état réseau et joueurs détectés.
/// En piste, il reste court : les joueurs de la piste (pastille = couleur de leur fantôme), puis un
/// résumé des autres. Au menu, tout le monde avec sa piste.
internal sealed class PlayersPanel
{
    private const int MaxHere = 8, MaxMenu = 12;
    public bool Visible = true;
    private readonly List<string> _lines = new();
    private double _nextRefresh;
    private float _width = -1;
    private bool _onTrack;

    /// Recalcule le texte (2×/s : inutile de copier la table des pairs à chaque image).
    public void Refresh(UdpNode node, string myName, string myTrack, System.Func<ulong, bool> isGhostVisible)
    {
        double now = NetClock.NowMs;
        if (!Visible || now < _nextRefresh) return;
        _nextRefresh = now + 500;
        _lines.Clear();
        _width = -1;
        _onTrack = myTrack != null;
        string echo = node.Echo ? "  ·  <color=#ffd866>écho</color>" : "";
        _lines.Add($"<b>LmdMulti {LmdMultiMod.Version}</b>  ·  {node.Status}{echo}");
        if (Diagnostics.TestMode.On) _lines.Add("<color=#ff8c78><b>MODE TEST</b> : aucun résultat enregistré (TestMode)</color>");
        _lines.Add($"<color=#9fe870>●</color> <b>{Esc(myName)}</b> (toi) — {GameInfo.TrackName(myTrack)}");
        var peers = node.Peers().OrderBy(p => NaturalKey(p.Name)).ToList();
        foreach (var p in peers.Where(p => !p.IsCompatible))
            _lines.Add($"<color=#ff6b6b>× {Esc(p.Name)} — version incompatible (v{p.IncompatibleVersion}, toi v{ProtocolInfo.Version})</color>");
        var ok = peers.Where(p => p.IsCompatible).ToList();
        if (_onTrack) AddTrackLines(ok, myTrack, isGhostVisible);
        else AddMenuLines(ok);
        if (peers.Count == 0) _lines.Add(Grey("aucun autre joueur détecté"));
        _lines.Add($"<size={(int)(13 * GuiKit.Scale)}>{Grey($"{Race.RaceKeys.Go} course ·{Config.PanelKey.Value} panneau · {Config.StyleKey.Value} aspect · {Config.EchoKey.Value} écho · {Config.CaptureKey.Value} capture")}</size>");
    }

    private void AddTrackLines(List<PeerInfo> peers, string myTrack, System.Func<ulong, bool> isGhostVisible)
    {
        var here = peers.Where(p => p.OnTrack && p.Track == myTrack).ToList();
        foreach (var p in here.Take(MaxHere))
        {
            string ping = p.IsEcho ? "" : p.RttMs >= 0 ? Grey($" · {p.RttMs} ms") : Grey(" · … ms");
            string missing = isGhostVisible(p.Id) ? "" : Grey(" · pas encore de pose");
            _lines.Add($"{Dot(p)} {Esc(p.Name)}{ping}{missing}");
        }
        if (here.Count > MaxHere) _lines.Add(Grey($"+ {here.Count - MaxHere} autres sur cette piste"));
        int elsewhere = peers.Count - here.Count;
        if (elsewhere > 0) _lines.Add(Grey($"○ {elsewhere} ailleurs (menu ou autre piste)"));
    }

    private void AddMenuLines(List<PeerInfo> peers)
    {
        foreach (var p in peers.Take(MaxMenu))
        {
            string ping = p.IsEcho ? "" : p.RttMs >= 0 ? Grey($" · {p.RttMs} ms") : Grey(" · … ms");
            _lines.Add($"{Grey("○")} {Esc(p.Name)} — {(p.OnTrack ? GameInfo.TrackName(p.Track) : "menu")}{ping}");
        }
        if (peers.Count > MaxMenu) _lines.Add(Grey($"+ {peers.Count - MaxMenu} autres"));
    }

    /// Pastille pleine dans la couleur du fantôme (même couleur que son pseudo en piste).
    private static string Dot(PeerInfo p) => $"<color=#{GuiKit.Hex(GhostLook.ColorFor(p.Id, p.IsEcho))}>●</color>";

    private static string Grey(string s) => $"<color=#aaaaaa>{s}</color>";

    /// Tri naturel : « Bot-2 » avant « Bot-10 ».
    private static string NaturalKey(string s) => Regex.Replace(s.ToLowerInvariant(), @"\d+", m => m.Value.PadLeft(8, '0'));

    /// Un pseudo ne doit pas pouvoir injecter de balises de mise en forme.
    private static string Esc(string s) => s.Replace("<", "‹").Replace(">", "›");

    public void Draw()
    {
        if (!Visible || _lines.Count == 0) return;
        GuiKit.Ensure();
        float pad = 8 * GuiKit.Scale, lineH = GuiKit.Text.lineHeight + 2 * GuiKit.Scale;
        if (_width < 0) // largeur ajustée au texte, recalculée seulement quand il change
            foreach (var line in _lines) _width = Mathf.Max(_width, GuiKit.Text.CalcSize(new GUIContent(line)).x);
        float w = Mathf.Min(_width + pad * 2, Screen.width * 0.6f);
        // Menus : sous la barre d'onglets du jeu ; en piste, sous le temps total affiché par le jeu dans ce coin.
        float top = Screen.height * (_onTrack ? 0.1f : 0.075f);
        var box = new Rect(Screen.width - w - pad, top, w, _lines.Count * lineH + pad * 2);
        GuiKit.Panel(box);
        for (int i = 0; i < _lines.Count; i++)
            GUI.Label(new Rect(box.x + pad, box.y + pad + i * lineH, w - pad * 2, lineH), _lines[i], GuiKit.Text);
    }
}
