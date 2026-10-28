using System.Collections.Generic;
using Ovomium.Core.Net;
using UnityEngine;

namespace Ovomium.UI;

/// Notifications brèves (arrivée/départ d'un joueur, version incompatible…), visibles même panneau masqué.
internal sealed class Toasts
{
    private const double LifeMs = 5000;
    private readonly List<(string text, Color color, double until, string key, bool block)> _items = new();

    /// key : une notification de même clé remplace la précédente (ex. réglage changé plusieurs fois de suite).
    /// lifeMs : plus long pour un message à lire en entier (avis au démarrage).
    /// block : texte de plusieurs lignes aligné à gauche, en caractères normaux (notes de version).
    public void Show(string text, Color color, string key = null, double lifeMs = LifeMs, bool block = false)
    {
        if (key != null) _items.RemoveAll(t => t.key == key);
        _items.Add((text, color, NetClock.NowMs + lifeMs, key, block));
        if (_items.Count > 5) _items.RemoveAt(0);
        Log.Info($"[écran] {text}");
    }

    /// minTop : sous un bandeau central éventuel (invitation), pour ne pas écrire par-dessus.
    public void Draw(float minTop = 0)
    {
        double now = NetClock.NowMs;
        _items.RemoveAll(t => t.until < now);
        if (_items.Count == 0) return;
        GuiKit.Ensure();
        float lineH = GuiKit.Centered.lineHeight * 1.25f, w = Screen.width * 0.9f;
        float y = Mathf.Max(Screen.height * 0.18f, minTop + lineH * 0.3f);
        foreach (var (text, color, until, _, block) in _items)
        {
            float alpha = (float)System.Math.Min(1.0, (until - now) / 600.0); // fondu final
            var style = block ? GuiKit.Text : GuiKit.Centered;
            // Fond ajusté au texte : lisible même par-dessus les panneaux (petite fenêtre).
            var size = style.CalcSize(new GUIContent(text));
            float tw = Mathf.Min(size.x + lineH, w), h = Mathf.Max(lineH, size.y + lineH - style.lineHeight);
            var oldTint = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GuiKit.Panel(new Rect((Screen.width - tw) / 2, y + lineH * 0.06f, tw, h - lineH * 0.12f));
            GUI.color = oldTint;
            // Bloc : texte à gauche dans son encadré centré ; ligne simple : centrée sur la largeur utile.
            var r = block ? new Rect((Screen.width - size.x) / 2, y + (h - size.y) / 2, size.x + 1, size.y) : new Rect((Screen.width - w) / 2, y, w, h);
            GuiKit.Shadowed(r, text, style, new Color(color.r, color.g, color.b, alpha));
            y += h;
        }
    }
}
