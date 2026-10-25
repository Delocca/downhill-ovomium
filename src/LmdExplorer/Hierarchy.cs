using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LmdExplorer;

/// Dumps texte de hiérarchies Unity (chemins, état actif, types de composants).
internal static class Hierarchy
{
    public static string Dump(Transform root, int maxDepth = 64)
    {
        var sb = new StringBuilder();
        Walk(root, 0, maxDepth, sb);
        return sb.ToString();
    }

    private static void Walk(Transform t, int depth, int maxDepth, StringBuilder sb)
    {
        sb.Append(' ', depth * 2).Append(t.name)
          .Append(t.gameObject.activeSelf ? "" : " [inactif]")
          .Append("  {").Append(ComponentList(t.gameObject)).Append('}');
        if (depth == 0)
            sb.Append($"  pos={t.position} rot={t.rotation.eulerAngles}");
        sb.Append('\n');
        if (depth >= maxDepth) return;
        for (int i = 0; i < t.childCount; i++)
            Walk(t.GetChild(i), depth + 1, maxDepth, sb);
    }

    public static string ComponentList(GameObject go)
    {
        var comps = go.GetComponents<Component>();
        var sb = new StringBuilder();
        foreach (var c in comps)
        {
            if (c == null) continue;
            if (sb.Length > 0) sb.Append(", ");
            sb.Append(c.GetIl2CppType().FullName);
            var b = c.TryCast<Behaviour>();
            if (b != null && !b.enabled) sb.Append("(off)");
        }
        return sb.ToString();
    }

    /// Racines de toutes les scènes chargées (noms + composants), sans descendre.
    public static string SceneRoots()
    {
        var sb = new StringBuilder();
        for (int s = 0; s < SceneManager.sceneCount; s++)
        {
            var scene = SceneManager.GetSceneAt(s);
            sb.Append($"== scène {scene.name} (index {scene.buildIndex}, chargée={scene.isLoaded})\n");
            if (!scene.isLoaded) continue;
            foreach (var go in scene.GetRootGameObjects())
                sb.Append($"  {go.name}{(go.activeSelf ? "" : " [inactif]")}  {{{ComponentList(go)}}}\n");
        }
        return sb.ToString();
    }

    public static GameObject FindRoot(string name)
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
