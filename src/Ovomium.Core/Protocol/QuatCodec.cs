using System;

namespace Ovomium.Core.Protocol;

/// Compression « smallest three » d'un quaternion unitaire sur 32 bits :
/// 2 bits = indice de la plus grande composante (omise, recalculée), 3 × 10 bits pour les autres.
/// Erreur angulaire max ≈ 0,2°.
public static class QuatCodec
{
    private const float Range = 0.70710678f; // les 3 plus petites composantes sont dans [-1/√2, 1/√2]
    private const int Max = 1023;

    public static uint Pack(Quat q)
    {
        q = q.Normalized();
        Span<float> c = stackalloc float[] { q.X, q.Y, q.Z, q.W };
        int largest = 0;
        for (int i = 1; i < 4; i++)
            if (MathF.Abs(c[i]) > MathF.Abs(c[largest])) largest = i;
        float sign = c[largest] < 0 ? -1f : 1f; // q et -q sont la même rotation
        uint bits = (uint)largest << 30;
        int shift = 20;
        for (int i = 0; i < 4; i++)
        {
            if (i == largest) continue;
            bits |= Quantize(c[i] * sign) << shift;
            shift -= 10;
        }
        return bits;
    }

    public static Quat Unpack(uint bits)
    {
        int largest = (int)(bits >> 30);
        Span<float> c = stackalloc float[4];
        float sum = 0;
        int shift = 20;
        for (int i = 0; i < 4; i++)
        {
            if (i == largest) continue;
            c[i] = Dequantize((bits >> shift) & Max);
            sum += c[i] * c[i];
            shift -= 10;
        }
        c[largest] = MathF.Sqrt(MathF.Max(0f, 1f - sum));
        return new Quat(c[0], c[1], c[2], c[3]).Normalized();
    }

    private static uint Quantize(float v)
    {
        float t = (v + Range) / (2 * Range);
        return (uint)Math.Clamp((int)MathF.Round(t * Max), 0, Max);
    }

    private static float Dequantize(uint v) => v / (float)Max * 2 * Range - Range;
}
