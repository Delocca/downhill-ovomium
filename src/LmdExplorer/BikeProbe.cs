using System.Text;
using Il2CppMegagon.Downhill.Vehicle.Animation;
using Il2CppMegagon.Downhill.Vehicle.Controller;
using UnityEngine;

namespace LmdExplorer;

/// Vélo local + rider : localisation, état, os, crash.
internal static class BikeProbe
{
    /// Vélo du joueur local. Deux voies testées : nom du GameObject (mods existants) et type.
    public static BikeLocomotion Find()
    {
        var all = Object.FindObjectsOfType<BikeLocomotion>();
        return all.Length > 0 ? all[0] : null;
    }

    public static string Status(BikeLocomotion bike)
    {
        var t = bike.transform;
        var rb = t.GetComponent<Rigidbody>();
        string vel = rb != null ? rb.velocity.magnitude.ToString("F1") : "?";
        string crash = "?";
        Out.Try("crash", () => crash = $"ragdoll={bike.prop_Boolean_2} reason={bike.prop_CrashReason_0}");
        var ragdoll = Hierarchy.FindRoot("RagDoll(Clone)");
        string rd = ragdoll == null ? "absent" : ragdoll.activeInHierarchy.ToString();
        return $"vélo pos={t.position} rotY={t.rotation.eulerAngles.y:F0} v={vel}m/s {crash} RagDoll(Clone).actif={rd}";
    }

    public static string Report(BikeLocomotion bike)
    {
        var sb = new StringBuilder();
        var go = bike.gameObject;
        sb.Append($"GameObject={go.name} scène={go.scene.name} nbBikeLocomotion={Object.FindObjectsOfType<BikeLocomotion>().Length}\n");
        sb.Append($"GameObject.Find(\"Bike(Clone)\") identique={GameObject.Find("Bike(Clone)") == go}\n");
        sb.Append($"Rigidbody sur racine={go.GetComponent<Rigidbody>() != null}\n");
        Out.Try("champs BikeLocomotion", () => sb.Append(
            $"m_headJoint={Name(bike.m_headJoint)} m_collisionRigidbodyTransform={Name(bike.m_collisionRigidbodyTransform)} " +
            $"wheelF={Name(bike.m_wheelCenterTransformFront)} wheelR={Name(bike.m_wheelCenterTransformRear)}\n"));
        Out.Try("animation", () => AppendAnimation(sb, go));
        Out.Try("skinned meshes", () => AppendSkinnedMeshes(sb, go));
        return sb.ToString();
    }

    private static void AppendAnimation(StringBuilder sb, GameObject go)
    {
        var anim = go.GetComponentInChildren<BikeAnimation>(true);
        if (anim != null)
            sb.Append($"BikeAnimation sur {anim.name}: bikeRoot={Name(anim.m_bikeRootTransform)} riderRoot={Name(anim.m_riderRootTransform)}\n");
        foreach (var a in go.GetComponentsInChildren<Animator>(true))
        {
            sb.Append($"Animator sur {a.name} enabled={a.enabled} avatar={(a.avatar != null ? a.avatar.name : "null")} humanoid={a.isHuman}\n");
            // a.parameters / GetParameter : non utilisables via l'interop (« unstripping failed »).
        }
    }

    private static void AppendSkinnedMeshes(StringBuilder sb, GameObject go)
    {
        foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var bones = smr.bones;
            sb.Append($"SkinnedMeshRenderer {smr.name} rootBone={Name(smr.rootBone)} bones={bones.Length}:");
            foreach (var b in bones) sb.Append(' ').Append(b != null ? b.name : "null");
            sb.Append('\n');
        }
    }

    private static string Name(Transform t) => t == null ? "null" : t.name;
}
