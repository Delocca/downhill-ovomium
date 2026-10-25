using System.Text;
using LmdMulti.Core.Poses;
using LmdMulti.Core.Protocol;
using UnityEngine;

namespace LmdMulti.Ghosts;

/// Marionnette : copie visuelle du vélo local sans scripts ni physique, pilotée par des poses reçues.
/// Méthode vérifiée par l'explorateur (docs/game-api.md § 3). Apparence = celle du joueur local (v0).
internal sealed class Puppet
{
    private static readonly Color Tint = new(0.75f, 0.9f, 1.25f, 1f); // bleuté, légèrement éclairci
    private static bool _materialsLogged;

    private readonly GameObject _go;
    private readonly BoneRig _rig;
    private readonly Transform _head;
    // Position/rotation de Bind_JNT relatives à la racine de la marionnette (fixes : Animator coupé).
    private readonly Vector3 _bindLocalPos;
    private readonly Quaternion _bindLocalRot;

    public bool IsAlive => _go != null && _rig.IsAlive; // détruite avec sa scène au changement de piste
    public Vector3 BindPosition => _rig.Bind.position;
    public Vector3 LabelPosition => (_head != null ? _head.position : _go.transform.position) + Vector3.up * 0.45f;

    private Puppet(GameObject go, BoneRig rig)
    {
        _go = go;
        _rig = rig;
        _head = rig.Bone("head_tip");
        _bindLocalPos = go.transform.InverseTransformPoint(_rig.Bind.position);
        _bindLocalRot = Quaternion.Inverse(go.transform.rotation) * _rig.Bind.rotation;
    }

    public static Puppet Create(GameObject source, string name, bool tint)
    {
        // Parent inactif : la copie naît inactive, ses scripts ne reçoivent ni Awake ni OnEnable.
        var holder = new GameObject("LmdMultiHolder");
        holder.SetActive(false);
        var go = Object.Instantiate(source, holder.transform);
        go.name = "LmdGhost " + name;
        Strip(go);
        go.transform.SetParent(null, false);
        Object.Destroy(holder);
        var lights = go.transform.Find("lights");
        if (lights != null) lights.gameObject.SetActive(false); // phares : pas pour les fantômes
        go.SetActive(true);
        foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true)) smr.enabled = true; // vélo local masqué pendant un crash
        var mesh = go.transform.Find("bike/mergedMesh");
        if (mesh != null) mesh.gameObject.SetActive(true);
        var rig = BoneRig.Find(go.transform);
        if (rig == null) { Object.Destroy(go); return null; }
        if (tint) ApplyTint(go);
        return new Puppet(go, rig);
    }

    /// Retire tout ce qui simule ou réagit : joints, scripts, colliders, rigidbodies ; coupe l'Animator.
    private static void Strip(GameObject go)
    {
        foreach (var j in go.GetComponentsInChildren<Joint>(true)) Object.DestroyImmediate(j);
        foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(mb);
        foreach (var col in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(col);
        foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(rb);
        foreach (var a in go.GetComponentsInChildren<Animator>(true)) a.enabled = false;
    }

    /// Teinte par MaterialPropertyBlock (n'altère pas les matériaux partagés avec le vélo local) :
    /// chaque matériau qui a une couleur `_Color` est multiplié par la teinte.
    private static void ApplyTint(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>(true);
        var log = new StringBuilder($"matériaux des fantômes ({renderers.Length} renderers) :");
        foreach (var r in renderers)
        {
            var mats = r.sharedMaterials;
            log.Append($"\n  {r.name} ({r.GetIl2CppType().Name}) : {mats.Length} matériau(x)");
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (m == null) continue;
                // Shader du rider « Megagon/Standard VertexColor Simple » : déclare _Color, mais
                // HasProperty a répondu faux en batchmode ; on vérifie aussi la déclaration du shader.
                bool hasColor = m.HasProperty("_Color") || (m.shader != null && m.shader.FindPropertyIndex("_Color") >= 0);
                log.Append($"\n  {r.name}[{i}] {m.name} shader={m.shader?.name} _Color={hasColor}");
                if (!_materialsLogged && m.shader != null && r.TryCast<SkinnedMeshRenderer>() != null) log.Append(" propriétés: ").Append(ShaderProps(m.shader));
                if (!hasColor) continue;
                var block = new MaterialPropertyBlock();
                block.SetColor("_Color", m.GetColor("_Color") * Tint);
                r.SetPropertyBlock(block, i);
            }
        }
        if (!_materialsLogged) Log.Info(log.ToString());
        _materialsLogged = true;
    }

    private static string ShaderProps(Shader s)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < s.GetPropertyCount(); i++) sb.Append($"{s.GetPropertyName(i)}:{s.GetPropertyType(i)} ");
        return sb.ToString();
    }

    /// Place la marionnette : racine telle que Bind_JNT tombe sur la pose reçue, puis les os.
    public void Apply(PoseFrame f)
    {
        var pos = f.RootPos.ToUnity();
        var rot = f.RootRot.ToUnity();
        var root = _go.transform;
        var rootRot = rot * Quaternion.Inverse(_bindLocalRot);
        root.SetPositionAndRotation(pos - rootRot * Vector3.Scale(_bindLocalPos, root.lossyScale), rootRot);
        _rig.Bind.SetPositionAndRotation(pos, rot);
        if (!f.HasBones) return;
        for (int i = 0; i < Skeleton.Count; i++)
        {
            var t = _rig.Bones[i];
            if (t == null) continue;
            t.localRotation = f.BoneRot[i].ToUnity();
            if ((f.PosMask >> i & 1) != 0) t.localPosition = f.BonePos[i].ToUnity();
        }
    }

    public void Destroy()
    {
        if (_go != null) Object.Destroy(_go);
    }
}
