using System.Collections.Generic;
using UnityEngine;

namespace Ovomium.UI;

/// Avis gardés jusqu'au menu principal : affichés plus tôt, ils disparaîtraient pendant les logos.
/// Fil principal uniquement.
internal sealed class MenuNotices
{
    private readonly List<(string text, Color color, double lifeMs, bool block)> _pending = new();

    public void Post(string text, Color color, double lifeMs = 12000, bool block = false)
    {
        if (!string.IsNullOrEmpty(text)) _pending.Add((text, color, lifeMs, block));
    }

    /// À chaque image : une fois l'écran titre passé, tout part dans les notifications.
    public void Flush(Toasts toasts)
    {
        if (_pending.Count == 0 || !Tweaks.IntroSkip.TitlePassed) return;
        foreach (var (text, color, life, block) in _pending) toasts.Show(text, color, lifeMs: life, block: block);
        _pending.Clear();
    }
}
