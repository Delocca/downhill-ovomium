using System;
using System.Collections.Generic;
using System.Diagnostics;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppMegagon.Downhill.Config;
using Il2CppMegagon.Downhill.Rider;
using Il2CppMegagon.Downhill.Vehicle.Configuration;
using Ovomium.Core.Appearance;
using Ovomium.Ghosts;
using UnityEngine;
using ColorizedMeshes = Il2CppSystem.Collections.Generic.List<Il2CppMegagon.Framework.Graphics.ColorizedMesh>;
using Object = UnityEngine.Object;

namespace Ovomium.Appearance;

/// Gabarit d'une apparence : modèle vélo + rider inerte et inactif, que les marionnettes copient.
/// Possède ses maillages (corps fusionné neuf, copies des attaches) : à détruire avec lui.
internal sealed class LookModel
{
    public GameObject Template;
    public readonly List<Mesh> Owned = new();

    public bool IsAlive => Template != null; // détruit avec sa scène

    public void Destroy()
    {
        if (Template != null) Object.Destroy(Template);
        foreach (var m in Owned) if (m != null) Object.Destroy(m);
        Owned.Clear();
    }
}

/// Construit un gabarit avec la fabrique du joueur du jeu (chaîne de son lambda CreatePlayerInstance,
/// docs/game-api.md § 3). Lève une exception si l'apparence est inconnue de ce jeu (DLC absent…).
internal static class LookBuilder
{
    public static LookModel Build(RiderLook look, GameObject localBike)
    {
        var watch = Stopwatch.StartNew();
        var (vc, bikeMeshes, riderMeshes) = Parts(look);
        // La fabrique réécrit les UV (teintes) des maillages d'attache partagés : on les remet ensuite.
        var shared = MeshSnapshot.Take(localBike);
        var model = new LookModel();
        try
        {
            var go = CallFactory(look, vc, bikeMeshes, riderMeshes) ?? throw new InvalidOperationException("la fabrique a renvoyé null");
            go.name = "OvomiumLook";
            Puppet.Strip(go); // ses scripts se sont réveillés : vrai Bike(Clone) le temps de l'appel
            go.SetActive(false);
            model.Template = go;
            OwnMeshes(go, model.Owned);
        }
        catch
        {
            model.Destroy();
            throw;
        }
        finally { shared.Restore(); }
        Log.Info($"apparence construite en {watch.ElapsedMilliseconds} ms : {look}");
        return model;
    }

    private static (VehicleConfiguration, ColorizedMeshes, ColorizedMeshes) Parts(RiderLook l)
    {
        var cfg = GameConfig.ActiveConfig;
        var vc = cfg.vehicleDB.Method_Public_VehicleConfiguration_Int32_0(l.Vehicle)
                 ?? throw new ArgumentException($"vélo {l.Vehicle} inconnu");
        if (vc.ID != l.Vehicle) throw new ArgumentException($"vélo {l.Vehicle} inconnu (le jeu propose {vc.ID})");
        var bike = vc.Method_Public_List_1_ColorizedMesh_Int32_0(l.Variant);
        if (bike == null || bike.Count == 0) throw new ArgumentException($"variante {l.Variant} du vélo {l.Vehicle} inconnue");
        var rs = cfg.riderDB.Method_Public_RiderSkinConfiguration_Int32_Boolean_0(l.Skin, true)
                 ?? throw new ArgumentException($"tenue {l.Skin} inconnue");
        // Le jeu remplace lui-même une tenue inconnue (ex. 0, profil jamais personnalisé → 100, la tenue de base) :
        // on fait comme lui plutôt que de copier le vélo local.
        if (rs.ID != l.Skin) Log.Info($"tenue {l.Skin} remplacée par {rs.ID}, comme le fait le jeu");
        var rider = rs.Method_Public_List_1_ColorizedMesh_BodyType_HairType_FacialHairType_HelmetType_0(
            (BodyType)l.Body, (HairType)l.Hair, (FacialHairType)l.Beard, (HelmetType)l.Helmet);
        if (rider == null || rider.Count == 0) throw new ArgumentException($"tenue {l.Skin} sans maillage pour le corps {l.Body}");
        return (vc, bike, rider);
    }

    /// Les types d'attache sont lus dans les champs de la fabrique (partagée avec le joueur local) :
    /// écrits pour l'appel, puis remis. Entiers : couleur de peau, de cheveux, 0 (inconnu, le jeu passe 0), de barbe.
    private static GameObject CallFactory(RiderLook l, VehicleConfiguration vc, ColorizedMeshes bike, ColorizedMeshes rider)
    {
        var fac = ObjectPublicObObObObUnique.prop_ObjectPublicObObObObUnique_0.field_Private_ObjectPublicLi1CoHaFaHeBaSiReSiUnique_0;
        var saved = (fac.field_Private_HairType_0, fac.field_Private_FacialHairType_0, fac.field_Private_HelmetType_0,
                     fac.field_Private_BackAccessoryType_0, fac.field_Private_List_1_Renderer_0);
        fac.field_Private_HairType_0 = (HairType)l.Hair;
        fac.field_Private_FacialHairType_0 = (FacialHairType)l.Beard;
        fac.field_Private_HelmetType_0 = (HelmetType)l.Helmet;
        fac.field_Private_BackAccessoryType_0 = (BackAccessoryType)l.Back;
        try
        {
            return fac.Method_Private_GameObject_VehicleConfiguration_List_1_ColorizedMesh_List_1_ColorizedMesh_Int32_Int32_Int32_Int32_0(
                vc, bike, rider, l.SkinColor, l.HairColor, 0, l.BeardColor);
        }
        finally
        {
            (fac.field_Private_HairType_0, fac.field_Private_FacialHairType_0, fac.field_Private_HelmetType_0,
             fac.field_Private_BackAccessoryType_0, fac.field_Private_List_1_Renderer_0) = saved;
        }
    }

    /// Corps fusionné : maillage neuf, gardé. Attaches : maillages partagés, remplacés par des copies
    /// (avant la remise des UV d'origine, pour garder les teintes de ce joueur).
    private static void OwnMeshes(GameObject go, List<Mesh> owned)
    {
        foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (smr.sharedMesh != null) smr.sharedMesh = Own(smr.sharedMesh, smr.name == "mergedMesh", owned);
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            if (mf.sharedMesh != null) mf.sharedMesh = Own(mf.sharedMesh, false, owned);
    }

    private static Mesh Own(Mesh mesh, bool isNew, List<Mesh> owned)
    {
        var m = isNew || !mesh.isReadable ? mesh : Object.Instantiate(mesh);
        if (isNew || m != mesh) owned.Add(m);
        return m;
    }
}

/// UV et couleurs des maillages d'un modèle, pour les remettre après un appel à la fabrique.
internal sealed class MeshSnapshot
{
    private readonly List<(Mesh mesh, Il2CppStructArray<Vector2> uv, Il2CppStructArray<Color32> colors)> _saved = new();

    public static MeshSnapshot Take(GameObject root)
    {
        var s = new MeshSnapshot();
        if (root == null) return s;
        foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true)) s.Add(smr.sharedMesh);
        foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true)) s.Add(mf.sharedMesh);
        return s;
    }

    private void Add(Mesh m)
    {
        if (m == null || !m.isReadable) return;
        foreach (var e in _saved) if (e.mesh == m) return;
        _saved.Add((m, m.uv, m.colors32));
    }

    public void Restore()
    {
        foreach (var (mesh, uv, colors) in _saved)
        {
            if (mesh == null) continue;
            if (uv.Length == mesh.vertexCount) mesh.uv = uv;
            if (colors.Length == mesh.vertexCount) mesh.colors32 = colors;
        }
    }
}
