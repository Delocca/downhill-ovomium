using System;
using LmdMulti.Core.Protocol;

namespace LmdMulti.Core.Poses;

[Flags]
public enum PoseFlags : byte
{
    None = 0,
    Ragdoll = 1,   // pose prise sur l'arbre du ragdoll (crash en cours)
    HasBones = 2,  // rotations des os présentes (sinon : racine seule, la marionnette garde sa pose de repos)
}

/// Pose d'un joueur à un instant : racine `Bind_JNT` en monde + rotations/positions locales des os.
public sealed class PoseFrame
{
    /// Horloge de l'émetteur (ms depuis son démarrage).
    public uint TimeMs;
    public PoseFlags Flags;
    public Vec3 RootPos;
    public Quat RootRot = Quat.Identity;
    public readonly Quat[] BoneRot = new Quat[Skeleton.Count];
    /// Bit i = BonePos[i] valide (os qui translatent).
    public uint PosMask;
    public readonly Vec3[] BonePos = new Vec3[Skeleton.Count];

    public bool HasBones => (Flags & PoseFlags.HasBones) != 0;
    public bool IsRagdoll => (Flags & PoseFlags.Ragdoll) != 0;

    public void CopyFrom(PoseFrame o)
    {
        TimeMs = o.TimeMs; Flags = o.Flags; RootPos = o.RootPos; RootRot = o.RootRot; PosMask = o.PosMask;
        Array.Copy(o.BoneRot, BoneRot, BoneRot.Length);
        Array.Copy(o.BonePos, BonePos, BonePos.Length);
    }

    /// Pose intermédiaire entre a (t=0) et b (t=1). Drapeaux et masque : ceux de la plus proche.
    public void SetLerp(PoseFrame a, PoseFrame b, float t)
    {
        var near = t < 0.5f ? a : b;
        TimeMs = (uint)(a.TimeMs + (b.TimeMs - a.TimeMs) * t);
        Flags = near.Flags;
        PosMask = near.PosMask;
        RootPos = Vec3.Lerp(a.RootPos, b.RootPos, t);
        RootRot = Quat.Nlerp(a.RootRot, b.RootRot, t);
        bool bothBones = a.HasBones && b.HasBones;
        for (int i = 0; i < BoneRot.Length; i++)
        {
            BoneRot[i] = bothBones ? Quat.Nlerp(a.BoneRot[i], b.BoneRot[i], t) : near.BoneRot[i];
            bool bothPos = ((a.PosMask & b.PosMask) >> i & 1) != 0;
            BonePos[i] = bothPos ? Vec3.Lerp(a.BonePos[i], b.BonePos[i], t) : near.BonePos[i];
        }
    }
}
