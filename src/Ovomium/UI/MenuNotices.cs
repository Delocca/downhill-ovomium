using System;
using System.Collections.Generic;
using Ovomium.Core.Net;
using UnityEngine;

namespace Ovomium.UI;

/// Avis de l'écran titre (mise à jour, touches en conflit avec LMD-VR…), en cartes (NoticeCard) empilées dans le ciel
/// libre à droite de la colonne du menu, sous la bannière d'actualité du jeu. Gardés jusqu'au menu principal
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

    /// À l'événement Repaint.
    public void Draw(bool onTrack)
    {
        if (_items.Count == 0 || onTrack || !Tweaks.IntroSkip.TitlePassed) return;
        GuiKit.Ensure();
        double now = NetClock.NowMs;
        _items.RemoveAll(e => e.Start >= 0 && now >= e.Start + e.LifeMs);
        var area = Area();
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

    /// Zone des cartes, alignées sur le bord droit de la bannière du jeu. Repères mesurés à l'écran titre en 1024×768,
    /// en fractions de la hauteur (l'interface du jeu suit la hauteur) : bannière jusqu'à 0,35 H, son bord droit à
    /// 0,12 H du bord de l'écran ; colonne du menu jusqu'à 0,55 H du bord gauche ; « jouer avec une manette » dès 0,92 H.
    private static Rect Area()
    {
        float h = Screen.height, s = GuiKit.Scale;
        float right = Screen.width - 0.118f * h, top = 0.38f * h, bottom = 0.9f * h;
        float w = Mathf.Min(MaxWidth * s, right - 0.58f * h);
        w = Mathf.Max(w, Mathf.Min(300 * s, right - 8)); // fenêtre très étroite : plutôt chevaucher le menu qu'écrire en colonne
        return new Rect(right - w, top, w, bottom - top);
    }
}
