using LmdMulti.Core.Poses;
using LmdMulti.Core.Protocol;
using UnityEngine;

namespace LmdMulti.Ghosts;

/// Marionnette : copie visuelle du vélo local sans scripts ni physique, pilotée par des poses reçues.
/// Méthode vérifiée par l'explorateur (docs/game-api.md § 3). Apparence = celle du joueur local (v0).
internal sealed class Puppet
{
    private readonly GameObject _go;
    private readonly BoneRig _rig;
    private readonly Transform _head;
    private readonly SkinnedMeshRenderer _body; // vélo + rider fusionnés (mergedMesh)
    // Position/rotation de Bind_JNT relatives à la racine de la marionnette (fixes : Animator coupé).
    private readonly Vector3 _bindLocalPos;
    private readonly Quaternion _bindLocalRot;
    private System.Collections.Generic.List<Material> _materials; // créés pour ce fantôme (style translucide)

    public bool IsAlive => _go != null && _rig.IsAlive; // détruite avec sa scène au changement de piste
    public Vector3 BindPosition => _rig.Bind.position;
    public Vector3 LabelPosition => (_head != null ? _head.position : _go.transform.position) + Vector3.up * 0.45f;

    private Puppet(GameObject go, BoneRig rig)
    {
        _go = go;
        _rig = rig;
        _head = rig.Bone("head_tip");
        var mesh = go.transform.Find("bike/mergedMesh");
        _body = mesh != null ? mesh.GetComponent<SkinnedMeshRenderer>() : null;
        _bindLocalPos = go.transform.InverseTransformPoint(_rig.Bind.position);
        _bindLocalRot = Quaternion.Inverse(go.transform.rotation) * _rig.Bind.rotation;
    }

    public static Puppet Create(GameObject source, string name, Color color, GhostLook.Style style)
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
        foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            smr.enabled = true; // vélo local masqué pendant un crash
            smr.updateWhenOffscreen = true; // crash : le rider s'éloigne du vélo, les bornes doivent suivre
        }
        var mesh = go.transform.Find("bike/mergedMesh");
        if (mesh != null) mesh.gameObject.SetActive(true);
        var rig = BoneRig.Find(go.transform);
        if (rig == null) { Object.Destroy(go); return null; }
        return new Puppet(go, rig) { _materials = GhostLook.Apply(go, color, style) };
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

    /// Place la marionnette : racine telle que Bind_JNT tombe sur la pose reçue, puis les os.
    public void Apply(PoseFrame f)
    {
        var pos = f.RootPos.ToUnity();
        var rot = f.RootRot.ToUnity();
        var root = _go.transform;
        var rootRot = rot * Quaternion.Inverse(_bindLocalRot);
        root.SetPositionAndRotation(pos - rootRot * Vector3.Scale(_bindLocalPos, root.lossyScale), rootRot);
        _rig.Bind.SetPositionAndRotation(pos, rot);
        // Hors champ : la racine suffit (pseudo, distance) ; les os sont la partie coûteuse.
        if (!f.HasBones || (_body != null && !_body.isVisible)) return;
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
        foreach (var m in _materials) if (m != null) Object.Destroy(m);
        _materials.Clear();
    }
}
