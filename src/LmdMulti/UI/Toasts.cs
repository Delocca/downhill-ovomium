using System.Collections.Generic;
using LmdMulti.Core.Net;
using UnityEngine;

namespace LmdMulti.UI;

/// Notifications brèves (arrivée/départ d'un joueur, version incompatible…), visibles même panneau masqué.
internal sealed class Toasts
{
    private const double LifeMs = 5000;
    private readonly List<(string text, Color color, double until, string key)> _items = new();

    /// key : une notification de même clé remplace la précédente (ex. réglage changé plusieurs fois de suite).
    public void Show(string text, Color color, string key = null)
    {
        if (key != null) _items.RemoveAll(t => t.key == key);
        _items.Add((text, color, NetClock.NowMs + LifeMs, key));
        if (_items.Count > 5) _items.RemoveAt(0);
        Log.Info($"[écran] {text}");
    }

    public void Draw()
    {
        double now = NetClock.NowMs;
        _items.RemoveAll(t => t.until < now);
        if (_items.Count == 0) return;
        GuiKit.Ensure();
        float lineH = GuiKit.Centered.lineHeight * 1.25f, w = Screen.width * 0.9f;
        float y = Screen.height * 0.18f;
        foreach (var (text, color, until, _) in _items)
        {
            float alpha = (float)System.Math.Min(1.0, (until - now) / 600.0); // fondu final
            var r = new Rect((Screen.width - w) / 2, y, w, lineH);
            GuiKit.Shadowed(r, text, GuiKit.Centered, new Color(color.r, color.g, color.b, alpha));
            y += lineH;
        }
    }
}
