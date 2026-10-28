using System;
using System.Collections.Generic;
using Ovomium.Core.Appearance;
using UnityEngine;

namespace Ovomium.Appearance;

/// Gabarits par apparence, construits à la demande (au plus un par image), vidés à chaque changement
/// de piste : une tenue changée au menu sert donc au chargement suivant. Fil principal uniquement.
internal sealed class LookCache
{
    private readonly Dictionary<RiderLook, LookModel> _models = new();
    private readonly List<RiderLook> _queue = new();
    private readonly HashSet<RiderLook> _failed = new();   // vidé avec le cache : un nouvel essai par piste
    private readonly HashSet<RiderLook> _reported = new(); // échecs déjà journalisés (une fois par session)
    private string _track;

    /// Gabarit de cette apparence, ou null : inconnue, en échec, ou pas encore construite (mise en file).
    public GameObject Get(RiderLook look)
    {
        if (!look.IsKnown || _failed.Contains(look)) return null;
        if (_models.TryGetValue(look, out var m))
        {
            if (m.IsAlive) return m.Template;
            m.Destroy();
            _models.Remove(look);
        }
        if (!_queue.Contains(look)) _queue.Add(look);
        return null;
    }

    /// Vrai si le gabarit de cette apparence est attendu : la copie du vélo local n'est que provisoire.
    public bool IsPending(RiderLook look) => _queue.Contains(look);

    /// À chaque image : vide le cache au changement de piste, puis construit au plus une apparence.
    public void Tick(string track, GameObject localBike)
    {
        if (track != _track) { _track = track; Clear(); }
        if (_queue.Count == 0 || track == null || localBike == null) return;
        var look = _queue[0];
        _queue.RemoveAt(0);
        try { _models[look] = LookBuilder.Build(look, localBike); }
        catch (Exception e)
        {
            _failed.Add(look);
            if (_reported.Add(look)) Log.Warn($"apparence impossible ({e.Message}) : copie du vélo local. {look}");
        }
    }

    public void Clear()
    {
        foreach (var m in _models.Values) m.Destroy();
        _models.Clear();
        _queue.Clear();
        _failed.Clear();
    }
}
