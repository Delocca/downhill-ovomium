using System.Collections.Generic;
using UnityEngine;

namespace Ovomium.UI;

/// Une carte d'avis (MenuNotices) : barre d'accent et titre dans la couleur de l'avis (vert réussi, rouge refus…),
/// puis le texte : intertitres (ligne « X : » non indentée, en-tête de version des notes), puces « · » en retrait
/// suspendu, retour à la ligne à la largeur de la carte, « … » si la hauteur permise ne suffit pas.
/// Mise en page refaite seulement quand la place ou l'échelle change.
internal sealed class NoticeCard
{
    private static readonly Color TextColor = new(0.93f, 0.94f, 0.96f), HeadingColor = new(0.7f, 0.78f, 0.88f);

    private enum Kind { Text, Heading, Bullet }

    public readonly string Title;
    public readonly Color Color;
    private readonly string _body;
    private readonly List<(Kind kind, string text)> _lines;
    private readonly List<(Kind kind, string text, float y, float h)> _rows = new(); // y relatif au haut de la carte
    private float _forMaxW = -1, _forMaxH = -1, _forScale = -1, _titleH;

    public float Width { get; private set; }
    public float Height { get; private set; }

    public NoticeCard(string title, Color color, string body)
    {
        Title = title;
        Color = color;
        _body = body ?? "";
        _lines = Parse(_body);
    }

    private static float S => GuiKit.Scale;
    private static float PadX => 16 * S;
    private static float PadY => 12 * S;
    private static float Accent => Mathf.Max(2, 4 * S);
    private static float TimeBar => Mathf.Max(1, 2 * S);
    private static float BulletW => GuiKit.CardText.fontSize * 1.1f;

    /// Lignes du texte. Une ligne indentée qui suit une puce la continue (notes déjà coupées à 90 caractères par
    /// ReleaseFeed) : recollée, pour que le retour à la ligne se fasse à la largeur réelle de la carte.
    private static List<(Kind, string)> Parse(string body)
    {
        var lines = new List<(Kind kind, string text)>();
        foreach (var raw in body.Replace("\r", "").Split('\n'))
        {
            string t = raw.Trim();
            if (t.Length == 0) continue;
            bool indented = raw[0] == ' ';
            if (t.StartsWith("· ")) lines.Add((Kind.Bullet, t[2..].Trim()));
            else if (indented && lines.Count > 0 && lines[^1].kind == Kind.Bullet) lines[^1] = (Kind.Bullet, lines[^1].text + " " + t);
            else if (!indented && t.EndsWith(" :")) lines.Add((Kind.Heading, t[..^2]));
            else lines.Add((Kind.Text, t));
        }
        return lines;
    }

    /// Journal : titre puis chaque ligne du texte, toutes préfixées (cherchables, horodatées).
    public void LogShown()
    {
        Log.Info($"[écran] {Title}");
        foreach (var line in _body.Replace("\r", "").Split('\n'))
            if (line.Trim().Length > 0) Log.Info($"[écran] {line}");
    }

    /// Met en page dans maxW × maxH (largeur ajustée au texte) ; faux si même le titre n'y tient pas.
    public bool Fit(float maxW, float maxH)
    {
        if (maxW == _forMaxW && maxH == _forMaxH && S == _forScale) return Height > 0;
        _forMaxW = maxW;
        _forMaxH = maxH;
        _forScale = S;
        Width = Mathf.Min(maxW, NaturalWidth());
        float inner = Width - Accent - 2 * PadX;
        _titleH = GuiKit.CardTitle.CalcHeight(new GUIContent(Title), inner);
        Height = 0;
        if (PadY * 2 + TimeBar + _titleH > maxH) return false;
        LayoutRows(inner, maxH - PadY - TimeBar);
        Height = (_rows.Count > 0 ? _rows[^1].y + _rows[^1].h : PadY + _titleH) + PadY + TimeBar;
        return true;
    }

    private float NaturalWidth()
    {
        float w = GuiKit.CardTitle.CalcSize(new GUIContent(Title)).x;
        foreach (var (kind, text) in _lines)
            w = Mathf.Max(w, (kind == Kind.Bullet ? BulletW : 0) + Style(kind).CalcSize(new GUIContent(text)).x);
        return Accent + 2 * PadX + w + 2; // + 2 : arrondis de CalcSize, sinon retour à la ligne parasite
    }

    /// Lignes sous le titre, jusqu'à `bottom` (relatif au haut de la carte) ; au-delà, dernière(s) ligne(s) → « … ».
    private void LayoutRows(float inner, float bottom)
    {
        _rows.Clear();
        float line = GuiKit.CardText.lineHeight, y = PadY + _titleH;
        for (int i = 0; i < _lines.Count; i++)
        {
            var (kind, text) = _lines[i];
            y += i == 0 ? line * 0.45f : kind == Kind.Heading ? line * 0.5f : line * 0.15f;
            float h = Style(kind).CalcHeight(new GUIContent(text), kind == Kind.Bullet ? inner - BulletW : inner);
            if (y + h > bottom) { Ellipsis(line, bottom); return; }
            _rows.Add((kind, text, y, h));
            y += h;
        }
    }

    private void Ellipsis(float line, float bottom)
    {
        while (_rows.Count > 0 && _rows[^1].y + _rows[^1].h + line * 1.15f > bottom) _rows.RemoveAt(_rows.Count - 1);
        float y = (_rows.Count > 0 ? _rows[^1].y + _rows[^1].h : PadY + _titleH) + line * 0.15f;
        if (y + line <= bottom) _rows.Add((Kind.Text, "…", y, line));
    }

    private static GUIStyle Style(Kind k) => k == Kind.Heading ? GuiKit.CardHeading : GuiKit.CardText;

    /// Dessine la carte (après Fit). remaining : part du temps d'affichage restante (barre fine en bas).
    public void Draw(float x, float y, float alpha, float remaining)
    {
        var tint = GUI.color;
        GUI.color = new Color(1, 1, 1, alpha);
        GuiKit.Panel(new Rect(x, y, Width, Height));
        GUI.color = tint;
        GuiKit.Fill(new Rect(x, y, Accent, Height), Fade(Color, alpha));
        GuiKit.Fill(new Rect(x + Accent, y + Height - TimeBar, (Width - Accent) * Mathf.Clamp01(remaining), TimeBar), Fade(Color, alpha * 0.5f));
        float left = x + Accent + PadX, inner = Width - Accent - 2 * PadX;
        GuiKit.Shadowed(new Rect(left, y + PadY, inner, _titleH), Title, GuiKit.CardTitle, Fade(Color, alpha));
        foreach (var (kind, text, ry, h) in _rows)
        {
            if (kind == Kind.Bullet)
            {
                GuiKit.Shadowed(new Rect(left, y + ry, BulletW, h), "•", GuiKit.CardText, Fade(Color, alpha));
                GuiKit.Shadowed(new Rect(left + BulletW, y + ry, inner - BulletW, h), text, GuiKit.CardText, Fade(TextColor, alpha));
            }
            else GuiKit.Shadowed(new Rect(left, y + ry, inner, h), text, Style(kind), Fade(kind == Kind.Heading ? HeadingColor : TextColor, alpha));
        }
    }

    private static Color Fade(Color c, float alpha) => new(c.r, c.g, c.b, c.a * alpha);
}
