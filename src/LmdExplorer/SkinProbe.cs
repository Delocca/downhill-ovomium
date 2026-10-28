using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Il2Cpp;
using Il2CppMegagon.Downhill.Config;
using Il2CppMegagon.Downhill.Vehicle.Controller;
using UnityEngine;

namespace LmdExplorer;

/// Sonde « apparence d'un autre joueur » : construit le vélo + rider d'une config donnée avec la fabrique
/// du jeu (chaîne reprise de son lambda CreatePlayerInstance), puis le compare au Bike(Clone) local.
/// Lecture seule pour le profil et les bases ; les champs de la fabrique partagée sont remis après l'appel.
///   skin                      -> skin.txt : config locale + catalogue (vélos, variantes, tenues, couleurs)
///   skin build [a b c d] [clé=val…] -> construit à côté du vélo (clés : vhl var sk sc hc bc body hair beard helmet back)
///   skin off                  -> détruit le modèle construit
///   skin face                 -> bascule : modèle à côté du vélo / devant, tourné vers la caméra
internal static class SkinProbe
{
    private static GameObject _built;
    private static readonly List<(Transform src, Transform dst)> _pairs = new();
    private static readonly Vector3 Offset = new(-2.5f, 0f, 0f); // la marionnette de test est à +2,5 m
    private static readonly Vector3 FacingOffset = new(-1.3f, 0f, 0f); // à hauteur du vélo : net (profondeur de champ)
    private static bool _facing;

    public static void Run(string arg)
    {
        string[] p = (arg ?? "").Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
        if (p.Length == 0) { Out.WriteFile("skin.txt", SkinCatalog.Report()); return; }
        if (p[0] == "off") { Destroy(); return; }
        if (p[0] == "face") { _facing = !_facing; Out.Info($"modèle face caméra : {_facing}"); return; }
        if (p[0] == "pose") { LogPose(); return; }
        if (p[0] == "build") Build(p);
        else Out.Info("usage : skin | skin build [a b c d] [clé=val…] | skin off");
    }

    private static void Build(string[] p)
    {
        Destroy();
        var look = SkinLook.Local();
        var ints = new List<int>();
        for (int i = 1; i < p.Length; i++)
        {
            if (p[i].Contains('=')) look.Set(p[i]);
            else ints.Add(int.Parse(p[i], CultureInfo.InvariantCulture));
        }
        // Les 4 entiers de la méthode privée sont inconnus : défaut supposé = couleurs peau/cheveux/barbe + n° joueur.
        int[] abcd = ints.Count == 4 ? ints.ToArray() : new[] { look.SkinColor, look.HairColor, look.BeardColor, 0 };
        int locosBefore = Object.FindObjectsOfType<BikeLocomotion>().Length;
        var report = new StringBuilder();
        report.Append($"look demandé : {look}\nentiers a b c d = {string.Join(' ', abcd)}\n");
        _built = Call(look, abcd, report);
        report.Append($"BikeLocomotion avant/après : {locosBefore}/{Object.FindObjectsOfType<BikeLocomotion>().Length}\n");
        if (_built == null) { report.Append("résultat : null\n"); Out.WriteFile("skinbuild.txt", report.ToString()); return; }
        report.Append($"résultat : {_built.name} actif={_built.activeSelf} scène={_built.scene.name}\n");
        report.Append("composants avant nettoyage :\n").Append(Hierarchy.ComponentList(_built));
        report.Append($"nettoyage : {PuppetTest.Strip(_built)}\n");
        report.Append($"BikeLocomotion après nettoyage : {Object.FindObjectsOfType<BikeLocomotion>().Length}\n");
        _built.name = "LmdSkinProbe";
        var bike = BikeProbe.Find();
        report.Append(SkinCompare.Compare(bike != null ? bike.gameObject : null, _built));
        if (bike != null) PairByName(bike.transform, _built.transform);
        report.Append($"transforms appariés par nom : {_pairs.Count}\n").Append(Hierarchy.Dump(_built.transform));
        Out.WriteFile("skinbuild.txt", report.ToString());
    }

    /// Chaîne du jeu : bases -> listes de ColorizedMesh -> types d'attachement dans la fabrique -> construction.
    private static GameObject Call(SkinLook l, int[] abcd, StringBuilder report)
    {
        var cfg = GameConfig.ActiveConfig;
        var vc = cfg.vehicleDB.Method_Public_VehicleConfiguration_Int32_0(l.Vehicle);
        var bikeMeshes = vc.Method_Public_List_1_ColorizedMesh_Int32_0(l.Variant);
        var rs = cfg.riderDB.Method_Public_RiderSkinConfiguration_Int32_Boolean_0(l.Skin, true);
        var riderMeshes = rs.Method_Public_List_1_ColorizedMesh_BodyType_HairType_FacialHairType_HelmetType_0(l.Body, l.Hair, l.Beard, l.Helmet);
        report.Append($"vélo {vc.ID} ({vc.nameLocaKey}) : {bikeMeshes?.Count} meshes ; tenue {rs.ID} ({rs.nameLocaKey}) : {riderMeshes?.Count} meshes\n");
        var fac = ObjectPublicObObObObUnique.prop_ObjectPublicObObObObUnique_0.field_Private_ObjectPublicLi1CoHaFaHeBaSiReSiUnique_0;
        var saved = (fac.field_Private_HairType_0, fac.field_Private_FacialHairType_0, fac.field_Private_HelmetType_0,
                     fac.field_Private_BackAccessoryType_0, fac.field_Private_List_1_Renderer_0);
        report.Append($"fabrique avant : {saved.Item1} {saved.Item2} {saved.Item3} {saved.Item4} renderers={saved.Item5?.Count}\n");
        fac.field_Private_HairType_0 = l.Hair;
        fac.field_Private_FacialHairType_0 = l.Beard;
        fac.field_Private_HelmetType_0 = l.Helmet;
        fac.field_Private_BackAccessoryType_0 = l.Back;
        try
        {
            return fac.Method_Private_GameObject_VehicleConfiguration_List_1_ColorizedMesh_List_1_ColorizedMesh_Int32_Int32_Int32_Int32_0(
                vc, bikeMeshes, riderMeshes, abcd[0], abcd[1], abcd[2], abcd[3]);
        }
        finally
        {
            report.Append($"fabrique après appel : renderers={fac.field_Private_List_1_Renderer_0?.Count} (remise à l'état d'avant)\n");
            (fac.field_Private_HairType_0, fac.field_Private_FacialHairType_0, fac.field_Private_HelmetType_0,
             fac.field_Private_BackAccessoryType_0, fac.field_Private_List_1_Renderer_0) = saved;
        }
    }

    /// Appariement par nom (la hiérarchie construite peut différer de Bike(Clone)) : premier homonyme.
    private static void PairByName(Transform src, Transform dst)
    {
        var byName = new Dictionary<string, Transform>();
        foreach (var t in src.GetComponentsInChildren<Transform>(true)) byName.TryAdd(t.name, t);
        _pairs.Add((src, dst));
        foreach (var t in dst.GetComponentsInChildren<Transform>(true))
            if (t != dst && byName.TryGetValue(t.name, out var s)) _pairs.Add((s, t));
    }

    /// LateUpdate : suit le vélo local, décalé, en recopiant la pose locale des homonymes.
    public static void Follow()
    {
        if (_built == null || _pairs.Count == 0) return;
        var (rootSrc, rootDst) = _pairs[0];
        if (rootSrc == null) { Destroy(); return; }
        if (_facing) // à côté du vélo, demi-tour : visage et torse vers la caméra
            rootDst.SetPositionAndRotation(rootSrc.position + rootSrc.rotation * FacingOffset, rootSrc.rotation * Quaternion.Euler(0f, 180f, 0f));
        else rootDst.SetPositionAndRotation(rootSrc.position + Offset, rootSrc.rotation);
        for (int i = 1; i < _pairs.Count; i++)
        {
            var (s, d) = _pairs[i];
            if (s == null || d == null) continue;
            d.localPosition = s.localPosition;
            d.localRotation = s.localRotation;
        }
    }

    /// Diagnostic : rotations monde de quelques transforms clés, source et modèle.
    private static void LogPose()
    {
        if (_pairs.Count == 0) { Out.Info("pas de modèle"); return; }
        foreach (var (s, d) in _pairs)
            if (s != null && d != null && (s == _pairs[0].src || s.name is "bike" or "Bind_JNT" or "root_BindJNT" or "cog_BindJNT"))
                Out.Info($"pose {s.name} : src pos={s.position} rot={s.eulerAngles} loc={s.localEulerAngles} | " +
                         $"dst pos={d.position} rot={d.eulerAngles} loc={d.localEulerAngles} parent={d.parent?.name}");
    }

    private static void Destroy()
    {
        if (_built != null) { Object.Destroy(_built); Out.Info("modèle de la sonde détruit"); }
        _built = null;
        _pairs.Clear();
    }
}
