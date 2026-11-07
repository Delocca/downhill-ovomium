using System;
using System.Collections.Generic;
using Ovomium.Core.Net;
using UnityEngine;

namespace Ovomium.UI;

/// Avis de l'écran titre (mise à jour, touches en conflit avec LMD-VR…), en cartes (NoticeCard) empilées dans le ciel
/// libre à droite de la colonne du menu (MenuLayout), sous la bannière d'actualité du jeu et sous le panneau des joueurs. Gardés jusqu'au menu principal
/// (affichés plus tôt, ils disparaîtraient pendant les logos) ; masqués en piste. Leur durée court dès leur première
/// apparition, journalisée « [écran] » comme les notifications ; une carte qui n'a pas la place attend que les
/// précédentes partent. Fil principal uniquement.
internal sealed class MenuNotices
{
    private const double FadeInMs = 250, FadeOutMs = 600;
    private const float MaxWidth = 560; // × GuiKit.Scale : ~55 caractères par ligne

    private sealed class Entry
    {
        public readonly NoticeCard Card;
        public readonly double LifeMs;
        public double Start = -1;
        public Entry(NoticeCard card, double lifeMs) { Card = card; LifeMs = lifeMs; }
    }

    private readonly List<Entry> _items = new();

    /// body : texte sous le titre, sur plusieurs lignes si besoin (notes de version, raison d'un refus).
    public void Post(string title, Color color, double lifeMs = 12000, string body = null)
    {
        if (!string.IsNullOrEmpty(title)) _items.Add(new Entry(new NoticeCard(title, color, body), lifeMs));
    }

    /// À l'événement Repaint. below : bas de ce qui occupe déjà le ciel (panneau des joueurs), 0 si rien.
    public void Draw(bool onTrack, float below)
    {
        if (_items.Count == 0 || onTrack || !Tweaks.IntroSkip.TitlePassed) return;
        GuiKit.Ensure();
        double now = NetClock.NowMs;
        _items.RemoveAll(e => e.Start >= 0 && now >= e.Start + e.LifeMs);
        var area = MenuLayout.Sky(MaxWidth * GuiKit.Scale, below);
        float y = area.y;
        foreach (var e in _items)
        {
            if (!e.Card.Fit(area.width, area.yMax - y)) break;
            if (e.Start < 0)
            {
                e.Start = now;
                e.Card.LogShown();
            }
            double left = e.Start + e.LifeMs - now;
            float alpha = (float)Math.Min(Math.Min(1.0, (now - e.Start) / FadeInMs), left / FadeOutMs);
            e.Card.Draw(area.xMax - e.Card.Width, y, alpha, (float)(left / e.LifeMs));
            y += e.Card.Height + 10 * GuiKit.Scale;
        }
    }
}
