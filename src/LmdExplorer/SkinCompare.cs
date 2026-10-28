using System.Text;
using UnityEngine;

namespace LmdExplorer;

/// Compare deux modèles vélo + rider : renderers skinnés (os, sommets, couleurs) et autres renderers.
internal static class SkinCompare
{
    public static string Compare(GameObject reference, GameObject built)
    {
        var sb = new StringBuilder();
        sb.Append("\n--- Bike(Clone) local ---\n");
        if (reference != null) Out.Try("référence", () => Describe(sb, reference));
        else sb.Append("absent\n");
        sb.Append("--- modèle construit ---\n");
        Out.Try("construit", () => Describe(sb, built));
        return sb.ToString();
    }

    private static void Describe(StringBuilder sb, GameObject go)
    {
        foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var mesh = smr.sharedMesh;
            var bones = smr.bones;
            var names = new StringBuilder();
            foreach (var b in bones) names.Append(b != null ? b.name : "null").Append(' ');
            sb.Append($"SMR {Path(smr.transform, go.transform)} actif={smr.gameObject.activeInHierarchy} root={smr.rootBone?.name} " +
                      $"os={bones.Length} mesh={mesh?.name} sommets={mesh?.vertexCount} sous-meshes={mesh?.subMeshCount} " +
                      $"couleurs={ColorSum(mesh)} matériaux={Materials(smr)}\n  os : {names}\n");
        }
        foreach (var mr in go.GetComponentsInChildren<MeshRenderer>(true))
            sb.Append($"MR {Path(mr.transform, go.transform)} actif={mr.gameObject.activeInHierarchy} matériaux={Materials(mr)}\n");
    }

    /// Empreinte des couleurs de sommets (somme RGBA) : égale si la palette est la même.
    private static string ColorSum(Mesh mesh)
    {
        if (mesh == null) return "-";
        if (!mesh.isReadable) return "illisible";
        long r = 0, g = 0, b = 0, a = 0;
        var cols = mesh.colors32;
        foreach (var c in cols) { r += c.r; g += c.g; b += c.b; a += c.a; }
        return $"{cols.Length}:{r}/{g}/{b}/{a}";
    }

    private static string Materials(Renderer r)
    {
        var sb = new StringBuilder();
        foreach (var m in r.sharedMaterials) sb.Append(m != null ? m.name : "null").Append(',');
        return sb.ToString();
    }

    private static string Path(Transform t, Transform root)
    {
        string p = t.name;
        for (var x = t.parent; x != null && x != root; x = x.parent) p = x.name + "/" + p;
        return p;
    }
}
