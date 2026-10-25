using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace LmdExplorer;

/// Test de marionnette : copie visuelle du vélo local, sans scripts ni physique,
/// décalée de quelques mètres et qui recopie la pose (tous les transforms) à chaque LateUpdate.
internal static class PuppetTest
{
    private static GameObject _puppet;
    private static readonly List<(Transform src, Transform dst)> _pairs = new();
    private static readonly Vector3 Offset = new(2.5f, 0f, 0f);

    public static bool Active => _puppet != null;

    public static void Toggle(GameObject source)
    {
        if (_puppet != null) { Object.Destroy(_puppet); _puppet = null; _pairs.Clear(); Out.Info("marionnette détruite"); return; }
        // Parent inactif : la copie naît inactive, donc ses scripts ne reçoivent ni Awake ni OnEnable.
        var holder = new GameObject("LmdPuppetHolder");
        holder.SetActive(false);
        _puppet = Object.Instantiate(source, holder.transform);
        _puppet.name = "LmdPuppet";
        string removed = Strip(_puppet);
        _puppet.transform.SetParent(null, false);
        Object.Destroy(holder);
        _puppet.SetActive(true);
        Pair(source.transform, _puppet.transform);
        var report = new StringBuilder();
        report.Append($"composants retirés: {removed}\ntransforms appariés: {_pairs.Count}\n");
        report.Append(Hierarchy.Dump(_puppet.transform));
        Out.WriteFile("puppet.txt", report.ToString());
    }

    /// Retire tout ce qui simule : joints, scripts, colliders, rigidbodies ; désactive l'Animator.
    private static string Strip(GameObject go)
    {
        var counts = new Dictionary<string, int>();
        void Kill(Component c)
        {
            string n = c.GetIl2CppType().Name;
            counts[n] = counts.TryGetValue(n, out var k) ? k + 1 : 1;
            Object.DestroyImmediate(c);
        }
        foreach (var j in go.GetComponentsInChildren<Joint>(true)) Kill(j);
        foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true)) Kill(mb);
        foreach (var col in go.GetComponentsInChildren<Collider>(true)) Kill(col);
        foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true)) Kill(rb);
        foreach (var a in go.GetComponentsInChildren<Animator>(true)) a.enabled = false;
        var sb = new StringBuilder();
        foreach (var kv in counts) sb.Append($"{kv.Key}×{kv.Value} ");
        return sb.ToString();
    }

    private static void Pair(Transform src, Transform dst)
    {
        _pairs.Add((src, dst));
        int n = Mathf.Min(src.childCount, dst.childCount);
        for (int i = 0; i < n; i++) Pair(src.GetChild(i), dst.GetChild(i));
    }

    /// À appeler en LateUpdate : racine = source + décalage, enfants = pose locale de la source.
    public static void Follow()
    {
        if (_puppet == null || _pairs.Count == 0) return;
        var (rootSrc, rootDst) = _pairs[0];
        if (rootSrc == null) { Toggle(null); return; } // source détruite : on détruit la marionnette
        rootDst.SetPositionAndRotation(rootSrc.position + Offset, rootSrc.rotation);
        for (int i = 1; i < _pairs.Count; i++)
        {
            var (s, d) = _pairs[i];
            if (s == null || d == null) continue;
            d.localPosition = s.localPosition;
            d.localRotation = s.localRotation;
            if (d.gameObject.activeSelf != s.gameObject.activeSelf) d.gameObject.SetActive(s.gameObject.activeSelf);
        }
    }
}
