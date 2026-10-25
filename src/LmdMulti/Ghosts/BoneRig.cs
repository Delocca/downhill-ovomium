using System.Collections.Generic;
using LmdMulti.Core;
using LmdMulti.Core.Protocol;
using UnityEngine;

namespace LmdMulti.Ghosts;

/// Squelette d'un vélo (local, ragdoll ou marionnette) : le nœud `bike/Bind_JNT` et ses os,
/// rangés dans l'ordre du protocole (Skeleton.Bones).
internal sealed class BoneRig
{
    public readonly Transform Bind;
    public readonly Transform[] Bones = new Transform[Skeleton.Count];
    public readonly int Found;

    public bool Complete => Found == Skeleton.Count;
    public bool IsAlive => Bind != null; // test d'existence Unity (objet détruit = null)
    public Transform Bone(string name) => Bones[Skeleton.IndexOf(name)];

    private BoneRig(Transform bind)
    {
        Bind = bind;
        var byName = new Dictionary<string, Transform>();
        Collect(bind, byName);
        for (int i = 0; i < Skeleton.Count; i++)
            if (byName.TryGetValue(Skeleton.Bones[i] + Skeleton.Suffix, out var t)) { Bones[i] = t; Found++; }
    }

    /// Squelette sous `owner/bike/Bind_JNT`, ou null si absent.
    public static BoneRig Find(Transform owner)
    {
        var bind = owner != null ? owner.Find("bike/Bind_JNT") : null;
        return bind != null ? new BoneRig(bind) : null;
    }

    private static void Collect(Transform t, Dictionary<string, Transform> byName)
    {
        byName.TryAdd(t.name, t);
        for (int i = 0; i < t.childCount; i++) Collect(t.GetChild(i), byName);
    }
}

/// Conversions entre types Unity et types du protocole.
internal static class Conv
{
    public static Vec3 ToCore(this Vector3 v) => new(v.x, v.y, v.z);
    public static Quat ToCore(this Quaternion q) => new(q.x, q.y, q.z, q.w);
    public static Vector3 ToUnity(this Vec3 v) => new(v.X, v.Y, v.Z);
    public static Quaternion ToUnity(this Quat q) => new(q.X, q.Y, q.Z, q.W);
}
