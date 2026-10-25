using Il2CppMegagon.Downhill.Vehicle.Controller;
using Ovomium.Core;
using Ovomium.Core.Net;
using Ovomium.Core.Poses;
using Ovomium.Core.Protocol;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ovomium.Ghosts;

/// Capture de la pose du joueur local : vélo `Bike(Clone)` en temps normal, arbre de
/// `RagDoll(Clone)` pendant un crash (mêmes noms d'os, même format).
internal sealed class PoseCapture
{
    private BikeLocomotion _bike;
    private BoneRig _bikeRig, _ragRig;
    private GameObject _ragdoll;
    private double _nextSearch;
    private bool _warnedIncomplete;

    /// GameObject du vélo local (modèle des marionnettes), ou null.
    public GameObject BikeObject => _bike != null ? _bike.gameObject : null;

    /// Retrouve vélo et ragdoll s'ils ont disparu (changement de scène). Recherche au plus 1×/s.
    public bool Refresh()
    {
        bool bikeOk = _bike != null && _bikeRig != null && _bikeRig.IsAlive;
        bool ragOk = _ragdoll != null && _ragRig != null && _ragRig.IsAlive;
        if (bikeOk && ragOk) return true;
        double now = NetClock.NowMs;
        if (now < _nextSearch) return bikeOk;
        _nextSearch = now + 1000;
        if (!bikeOk)
        {
            var all = Object.FindObjectsOfType<BikeLocomotion>();
            _bike = all.Length > 0 ? all[0] : null;
            _bikeRig = _bike != null ? BoneRig.Find(_bike.transform) : null;
            if (_bikeRig != null) Log.Info($"vélo local trouvé : {_bikeRig.Found}/{Skeleton.Count} os");
        }
        if (!ragOk)
        {
            _ragdoll = FindRoot("RagDoll(Clone)");
            _ragRig = _ragdoll != null ? BoneRig.Find(_ragdoll.transform) : null;
            if (_ragRig != null) Log.Info($"ragdoll trouvé : {_ragRig.Found}/{Skeleton.Count} os");
        }
        return _bikeRig != null;
    }

    /// Remplit `f` (sauf TimeMs). posMask : os dont on envoie aussi la position locale.
    public bool Capture(PoseFrame f, uint posMask)
    {
        if (!Refresh()) return false;
        bool crashed = _ragdoll != null && _ragRig != null && _ragRig.IsAlive && _ragdoll.activeInHierarchy;
        var rig = crashed ? _ragRig : _bikeRig;
        f.RootPos = rig.Bind.position.ToCore();
        f.RootRot = rig.Bind.rotation.ToCore();
        f.Flags = crashed ? PoseFlags.Ragdoll : PoseFlags.None;
        if (!rig.Complete)
        {
            if (!_warnedIncomplete) Log.Warn($"squelette incomplet ({rig.Found}/{Skeleton.Count} os) : envoi de la racine seule");
            _warnedIncomplete = true;
            return true;
        }
        f.Flags |= PoseFlags.HasBones;
        // Ragdoll : chaque os est placé par la physique (contraintes en monde), ses positions locales
        // varient toutes. On les envoie toutes (≈ 420 octets/pose au lieu de 250, le temps du crash).
        f.PosMask = crashed ? Skeleton.AllMask : posMask;
        for (int i = 0; i < Skeleton.Count; i++)
        {
            var t = rig.Bones[i];
            f.BoneRot[i] = t.localRotation.ToCore();
            if ((f.PosMask >> i & 1) != 0) f.BonePos[i] = t.localPosition.ToCore();
        }
        return true;
    }

    private static GameObject FindRoot(string name)
    {
        for (int s = 0; s < SceneManager.sceneCount; s++)
        {
            var scene = SceneManager.GetSceneAt(s);
            if (!scene.isLoaded) continue;
            foreach (var go in scene.GetRootGameObjects())
                if (go.name == name) return go;
        }
        return null;
    }
}
