using System;
using System.Collections.Generic;
using Il2CppMegagon.Framework.Graphics;
using UnityEngine;
using PostDepthOfField = UnityEngine.Rendering.PostProcessing.DepthOfField;

namespace Ovomium.View;

/// Coupe en mémoire le flou de focale du jeu quand Core/View/FocusBlur le demande, puis remet chaque objet
/// exactement dans l'état trouvé (même si l'option du jeu l'avait déjà coupé). Recette de la commande nodof de
/// LmdExplorer : le pont DepthOfFieldBridge du jeu (qui pilote le réglage) et les réglages DepthOfField du
/// post-traitement. Rien n'est écrit dans les réglages ni dans le profil du jeu.
internal sealed class FocusBlurSwitch
{
    private const float RescanSeconds = 2f; // objets apparus depuis (nouvelle scène)

    // Clé : identifiant Unity (les enveloppes Il2Cpp rendues par FindObjectsOfTypeAll changent à chaque appel).
    private readonly Dictionary<int, (DepthOfFieldBridge obj, bool enabled)> _bridges = new();
    private readonly Dictionary<int, (PostDepthOfField obj, bool enabled)> _settings = new();
    private float _nextScan;
    private bool _suppressed;

    public void Set(bool suppress, string view)
    {
        if (suppress == _suppressed && (!suppress || Time.unscaledTime < _nextScan)) return;
        if (suppress) Suppress(view);
        else Restore();
    }

    private void Suppress(string view)
    {
        bool first = !_suppressed;
        _suppressed = true;
        _nextScan = Time.unscaledTime + RescanSeconds;
        int found = 0;
        foreach (var b in Resources.FindObjectsOfTypeAll<DepthOfFieldBridge>())
        {
            if (_bridges.TryAdd(b.GetInstanceID(), (b, b.enabled))) found++;
            b.enabled = false;
        }
        foreach (var d in Resources.FindObjectsOfTypeAll<PostDepthOfField>())
        {
            if (_settings.TryAdd(d.GetInstanceID(), (d, d.enabled.value))) found++;
            d.enabled.value = false;
        }
        if (first || found > 0) Log.Info($"flou de focale coupé (vue {view}, {found} objet(s))");
    }

    private void Restore()
    {
        _suppressed = false;
        foreach (var (b, enabled) in _bridges.Values) TryRestore(() => { if (b != null) b.enabled = enabled; });
        foreach (var (d, enabled) in _settings.Values) TryRestore(() => { if (d != null) d.enabled.value = enabled; });
        Log.Info($"flou de focale remis comme avant ({_bridges.Count + _settings.Count} objet(s))");
        _bridges.Clear();
        _settings.Clear();
    }

    private static void TryRestore(Action restore)
    {
        try { restore(); }
        catch (Exception) { } // objet détruit avec sa scène : rien à remettre
    }
}
