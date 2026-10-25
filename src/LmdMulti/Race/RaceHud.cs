using System.Collections.Generic;
using System.Linq;
using LmdMulti.Core.Race;
using LmdMulti.Game;
using LmdMulti.Ghosts;
using LmdMulti.UI;
using UnityEngine;

namespace LmdMulti.Race;

/// Affichage de la course (OnGUI, à l'événement Repaint) : invitation, panneau de course (attente,
/// classement en direct, chrono), compte à rebours géant au centre, annonce d'arrivée, tableau final.
/// Tailles proportionnelles à la hauteur d'écran (GuiKit.Scale), lisibles de 640×480 à 1080p et plus.
internal static class RaceHud
{
    private static readonly Color Gold = new(1f, 0.85f, 0.4f), Green = new(0.62f, 0.91f, 0.44f), Grey = new(0.67f, 0.67f, 0.67f);
    private static readonly Color DnfRed = new(1f, 0.5f, 0.45f);
    private static GUIStyle _huge, _big, _row, _rowRight, _title, _chrono;
    private static int _builtFor, _sectionCount;

    private static float S => GuiKit.Scale;

    private static void Ensure()
    {
        GuiKit.Ensure();
        if (_builtFor == Screen.height && _huge != null) return;
        _builtFor = Screen.height;
        _huge = Style(GuiKit.Centered, 150, FontStyle.Bold);
        _big = Style(GuiKit.Centered, 34, FontStyle.Bold);
        _title = Style(GuiKit.Text, 19, FontStyle.Bold);
        _row = Style(GuiKit.Text, 18, FontStyle.Normal);
        _chrono = Style(GuiKit.Text, 32, FontStyle.Bold);
        _rowRight = Style(GuiKit.Text, 18, FontStyle.Normal);
        _rowRight.alignment = TextAnchor.UpperRight;
    }

    private static GUIStyle Style(GUIStyle from, int size, FontStyle fs) =>
        new(from) { fontSize = Mathf.RoundToInt(size * S), fontStyle = fs };

    public static void Draw(RaceController c, double now)
    {
        var s = c.Session;
        if (s == null) return;
        Ensure();
        if (s is RaceGuest g && g.IsPending) { DrawInvite(g, now); return; }
        var v = s.View;
        if (v.Phase == RacePhase.Cancelled) return;
        if (c.ResultsVisible(now)) { DrawResults(s); return; }
        if (v.Phase == RacePhase.Finished) return;
        if (s.Me == null || !s.Me.IsListed) return;
        _sectionCount = GameInfo.CurrentTrackId() == v.Track ? RaceGame.CheckpointCount() - 1 : 0; // départ exclu
        DrawPanel(s, now);
        DrawCountdown(v, now);
        DrawMyFinish(s, c.Run.FinishedAtMs, now);
    }

    // ---- Invitation : bandeau central, impossible à rater, touches bien visibles ----

    private static void DrawInvite(RaceGuest g, double now)
    {
        int left = Mathf.Max(0, Mathf.CeilToInt((float)((g.InviteExpiresAtMs - now) / 1000)));
        string keys = $"<b><color=#9fe870>{Config.RaceKey.Value}</color></b> rejoindre     <b><color=#ff8c78>{Config.RaceQuitKey.Value}</color></b> refuser     <color=#aaaaaa>{left} s</color>";
        var lines = new[]
        {
            (Esc(g.View.HostName) + " lance une course", _big, Gold),
            (GameInfo.TrackName(g.View.Track), _title, Color.white),
            (keys, _row, Color.white),
        };
        CenteredBox(lines, Screen.height * 0.26f);
    }

    // ---- Panneau de course (haut gauche) ----

    private static void DrawPanel(IRaceSession s, double now)
    {
        var v = s.View;
        var lines = new List<(string left, string right, GUIStyle style)>
        {
            ($"<b>Course</b> · {GameInfo.TrackName(v.Track)}", "", _row),
        };
        if (v.Phase == RacePhase.Racing) lines.Add((Ranking.FormatTime(MyClock(s, now)), EndText(v, now), _chrono));
        else lines.Add((WaitText(s, now), "", _row));
        int pos = 0;
        foreach (var e in v.Entries.Where(e => e.IsListed || v.Phase == RacePhase.Gathering && e.State == RacerState.Invited))
            lines.Add(EntryLine(s, e, ++pos));
        lines.Add(($"<size={(int)(13 * S)}><color=#aaaaaa>{Hints(s)}</color></size>", "", _row));
        DrawTable(lines, 8 * S, PanelTop(), Mathf.Max(260 * S, Screen.width * 0.26f));
    }

    /// Mon chrono : figé à mon temps une fois arrivé.
    private static double MyClock(IRaceSession s, double now) =>
        s.Me.State == RacerState.Finished ? s.Me.TimeMs : System.Math.Max(0, now - s.View.StartAtMs);

    /// En piste, sous les compteurs du jeu (coin haut gauche) ; au menu, sous la barre d'onglets.
    private static float PanelTop() => GameInfo.CurrentTrackId() != null ? Screen.height * 0.16f : Screen.height * 0.075f;

    private static string WaitText(IRaceSession s, double now)
    {
        var v = s.View;
        if (v.Phase == RacePhase.Countdown) return $"<color=#{GuiKit.Hex(Gold)}><b>Départ imminent</b></color>";
        int ready = v.Entries.Count(e => e.State == RacerState.Ready), total = v.Entries.Count(e => e.IsActive || e.State == RacerState.Invited);
        string me = s.Me.State == RacerState.Loading ? "chargement de la piste…" : "prêt, bloqué au départ";
        return $"{me}  <color=#aaaaaa>· prêts {ready}/{total}</color>";
    }

    private static string EndText(Standings v, double now)
    {
        if (double.IsNaN(v.EndAtMs)) return "";
        int left = Mathf.Max(0, Mathf.CeilToInt((float)((v.EndAtMs - now) / 1000)));
        return $"<color=#{GuiKit.Hex(Gold)}>fin dans {left} s</color>";
    }

    private static (string, string, GUIStyle) EntryLine(IRaceSession s, RaceEntry e, int pos)
    {
        var v = s.View;
        bool me = e.Id == s.MyId;
        string name = me ? $"<b>{Esc(e.Name)}</b>" : Esc(e.Name);
        // Moi en blanc (nom en gras) : le vert du panneau des joueurs peut être la couleur d'un fantôme.
        string dot = $"<color=#{GuiKit.Hex(me ? Color.white : GhostLook.ColorFor(e.Id, false))}>●</color>";
        string place = v.Phase >= RacePhase.Racing && e.State is RacerState.Finished or RacerState.Racing ? $"{pos}." : " ";
        string right = e.State switch
        {
            RacerState.Invited => Colored("invité…", Grey),
            RacerState.Loading => Colored("chargement…", Grey),
            RacerState.Ready => v.Phase >= RacePhase.Countdown ? "" : Colored("prêt", Green),
            RacerState.Racing => Colored(SectionsText(e, s), Grey),
            RacerState.Finished => FinishText(v, e),
            RacerState.Dnf => Colored("abandon", DnfRed),
            _ => "",
        };
        return ($"{place} {dot} {name}{(e.Id == v.HostId ? Colored("  hôte", Grey) : "")}", right, _row);
    }

    private static string SectionsText(RaceEntry e, IRaceSession s)
    {
        int total = _sectionCount;
        return e.Progress == 0 ? "en course" : total > 0 ? $"cp {e.Progress}/{total}" : $"cp {e.Progress}";
    }

    private static string FinishText(Standings v, RaceEntry e)
    {
        var first = v.Entries.FirstOrDefault(x => x.State == RacerState.Finished);
        string gap = first != null && first.Id != e.Id ? Colored($"  +{Ranking.FormatTime(e.TimeMs - first.TimeMs)}", Grey) : "";
        return $"<b>{Ranking.FormatTime(e.TimeMs)}</b>{gap}";
    }

    private static string Hints(IRaceSession s)
    {
        string go = Config.RaceKey.Value, quit = Config.RaceQuitKey.Value;
        if (s.View.Phase == RacePhase.Gathering && s.IsHost) return $"{go} partir sans attendre · {quit} annuler";
        if (s.Me.IsActive) return $"{quit} ×2 abandonner";
        return "";
    }

    // ---- Compte à rebours ----

    private static void DrawCountdown(Standings v, double now)
    {
        if (double.IsNaN(v.StartAtMs)) return;
        double t = v.StartAtMs - now;
        if (t > RaceRules.CountdownMs) { BigCenter("Prêts ?", _big, Gold, 1f, 0.40f); return; }
        if (t > 0)
        {
            float frac = (float)(t / 1000 % 1.0); // 1 → 0 pendant chaque seconde
            string digit = Mathf.CeilToInt((float)(t / 1000)).ToString();
            BigCenter(digit, _huge, Color.white, 0.35f + 0.65f * Mathf.Clamp01(frac * 1.6f), 0.36f, 1f + 0.25f * frac);
        }
        else if (t > -1200) BigCenter("GO !", _huge, Green, Mathf.Clamp01((float)(1 + (t + 200) / 1000)), 0.36f);
    }

    private static void DrawMyFinish(IRaceSession s, double finishedAt, double now)
    {
        if (double.IsNaN(finishedAt) || now - finishedAt > 6000 || s.Me.State != RacerState.Finished) return;
        int place = Ranking.PlaceOf(s.View, s.MyId);
        string text = $"Arrivée !  {Ranking.FormatTime(s.Me.TimeMs)}{(place > 0 ? $"  ·  {place}{(place == 1 ? "er" : "e")}" : "")}";
        BigCenter(text, _big, Green, Mathf.Clamp01((float)((6000 - (now - finishedAt)) / 800)), 0.33f);
    }

    // ---- Tableau final ----

    private static void DrawResults(IRaceSession s)
    {
        var v = s.View;
        var lines = new List<(string, string, GUIStyle)> { ($"<b>Résultats</b> · {GameInfo.TrackName(v.Track)}", "", _title) };
        int pos = 0;
        foreach (var e in v.Entries.Where(e => e.IsListed))
        {
            var (left, right, _) = EntryLine(s, e, ++pos);
            if (e.State == RacerState.Finished) left = $"{pos}." + left.Substring(left.IndexOf(' '));
            lines.Add((left, right, _row));
        }
        lines.Add(($"<size={(int)(13 * S)}><color=#aaaaaa>{Config.RaceKey.Value} fermer</color></size>", "", _row));
        float w = Mathf.Min(Screen.width * 0.9f, 460 * S);
        // Milieu d'écran : en haut, l'écran de résultats du jeu affiche titre et défis.
        DrawTable(lines, (Screen.width - w) / 2, Screen.height * 0.36f, w);
    }

    // ---- Primitives ----

    private static void DrawTable(List<(string left, string right, GUIStyle style)> lines, float x, float y, float w)
    {
        float pad = 10 * S, gap = 3 * S;
        float h = lines.Sum(l => l.style.lineHeight + gap) + pad * 2;
        GuiKit.Panel(new Rect(x, y, w, h));
        float cy = y + pad;
        foreach (var (left, right, style) in lines)
        {
            var r = new Rect(x + pad, cy, w - pad * 2, style.lineHeight);
            GUI.Label(r, left, style);
            if (!string.IsNullOrEmpty(right)) GUI.Label(r, right, _rowRight);
            cy += style.lineHeight + gap;
        }
    }

    private static void CenteredBox((string text, GUIStyle style, Color color)[] lines, float y)
    {
        float pad = 14 * S, w = Mathf.Min(Screen.width * 0.94f, 720 * S);
        float h = lines.Sum(l => l.style.lineHeight * 1.15f) + pad * 2;
        GuiKit.Panel(new Rect((Screen.width - w) / 2, y, w, h));
        float cy = y + pad;
        foreach (var (text, style, color) in lines)
        {
            var old = style.alignment;
            style.alignment = TextAnchor.UpperCenter;
            GuiKit.Shadowed(new Rect((Screen.width - w) / 2, cy, w, style.lineHeight), text, style, color);
            style.alignment = old;
            cy += style.lineHeight * 1.15f;
        }
    }

    private static void BigCenter(string text, GUIStyle style, Color color, float alpha, float yFrac, float scale = 1f)
    {
        var old = style.fontSize;
        style.fontSize = Mathf.RoundToInt(old * scale);
        float h = style.lineHeight * 1.2f;
        GuiKit.Shadowed(new Rect(0, Screen.height * yFrac - h / 2, Screen.width, h), text, style, new Color(color.r, color.g, color.b, alpha));
        style.fontSize = old;
    }

    private static string Colored(string s, Color c) => $"<color=#{GuiKit.Hex(c)}>{s}</color>";

    private static string Esc(string s) => (s ?? "").Replace("<", "‹").Replace(">", "›");
}
