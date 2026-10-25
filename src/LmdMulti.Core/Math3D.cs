using System;

namespace LmdMulti.Core;

/// Vecteur 3D minimal (indépendant de Unity, pour le protocole et l'interpolation).
public struct Vec3
{
    public float X, Y, Z;

    public Vec3(float x, float y, float z) { X = x; Y = y; Z = z; }

    public static Vec3 Lerp(Vec3 a, Vec3 b, float t) =>
        new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);

    public static float Distance(Vec3 a, Vec3 b)
    {
        float dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
        return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    public override string ToString() => $"({X:F2}, {Y:F2}, {Z:F2})";
}

/// Quaternion minimal (x, y, z, w), convention Unity.
public struct Quat
{
    public float X, Y, Z, W;

    public Quat(float x, float y, float z, float w) { X = x; Y = y; Z = z; W = w; }

    public static readonly Quat Identity = new(0, 0, 0, 1);

    public static float Dot(Quat a, Quat b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;

    public Quat Normalized()
    {
        float n = MathF.Sqrt(X * X + Y * Y + Z * Z + W * W);
        return n < 1e-6f ? Identity : new Quat(X / n, Y / n, Z / n, W / n);
    }

    /// Interpolation linéaire normalisée, par le plus court chemin : suffisante à 20-30 Hz.
    public static Quat Nlerp(Quat a, Quat b, float t)
    {
        if (Dot(a, b) < 0) b = new Quat(-b.X, -b.Y, -b.Z, -b.W);
        return new Quat(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t,
                        a.Z + (b.Z - a.Z) * t, a.W + (b.W - a.W) * t).Normalized();
    }

    /// Rotation d'angle (radians) autour de l'axe vertical Y.
    public static Quat FromYaw(float radians) => new(0, MathF.Sin(radians / 2), 0, MathF.Cos(radians / 2));

    public static Quat operator *(Quat a, Quat b) => new(
        a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
        a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X,
        a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W,
        a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z);

    /// Angle (degrés) entre deux orientations.
    public static float AngleDeg(Quat a, Quat b) =>
        2f * MathF.Acos(MathF.Min(1f, MathF.Abs(Dot(a.Normalized(), b.Normalized())))) * 180f / MathF.PI;
}
