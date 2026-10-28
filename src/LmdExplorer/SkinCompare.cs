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
                      $"os={bones.Length} mesh={mesh?.name}#{mesh?.GetInstanceID()} sommets={mesh?.vertexCount} sous-meshes={mesh?.subMeshCount} " +
                      $"couleurs={ColorSum(mesh)} matériaux={Materials(smr)}\n  os : {names}\n");
            foreach (var m in smr.sharedMaterials) Out.Try("propriétés", () => sb.Append("  ").Append(Props(m)).Append('\n'));
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

    /// Valeurs des propriétés du shader (couleurs, flottants, vecteurs, textures) : où vivent peau et cheveux ?
    private static string Props(Material m)
    {
        if (m == null) return "null";
        var sb = new StringBuilder($"{m.name}#{m.GetInstanceID()} shader={m.shader.name} :");
        var sh = m.shader;
        for (int i = 0; i < sh.GetPropertyCount(); i++)
        {
            string n = sh.GetPropertyName(i);
            string v = sh.GetPropertyType(i) switch
            {
                UnityEngine.Rendering.ShaderPropertyType.Color => m.GetColor(n).ToString(),
                UnityEngine.Rendering.ShaderPropertyType.Vector => m.GetVector(n).ToString(),
                UnityEngine.Rendering.ShaderPropertyType.Texture => m.GetTexture(n) is { } t ? $"{t.name}#{t.GetInstanceID()}" : "null",
                _ => m.GetFloat(n).ToString("G4"),
            };
            sb.Append($" {n}={v}");
        }
        return sb.ToString();
    }

    private static string Materials(Renderer r)
    {
        var sb = new StringBuilder();
        foreach (var m in r.sharedMaterials) sb.Append(m != null ? $"{m.name}#{m.GetInstanceID()}" : "null").Append(',');
        return sb.ToString();
    }

    private static string Path(Transform t, Transform root)
    {
        string p = t.name;
        for (var x = t.parent; x != null && x != root; x = x.parent) p = x.name + "/" + p;
        return p;
    }
}
