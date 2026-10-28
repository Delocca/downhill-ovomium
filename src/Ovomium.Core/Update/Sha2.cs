using System;
using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace Ovomium.Core.Update;

/// SHA-256 et SHA-512 (FIPS 180-4) en code managé : sous Proton, la crypto de .NET passe par la bcrypt de Wine,
/// dont on ne veut pas dépendre pour la mise à jour. Pas de secret manipulé ici : pas besoin de temps constant.
/// Les constantes sont recalculées comme la norme les définit (§ 4.2.2, 4.2.3, 5.3.3, 5.3.5) : parties
/// fractionnaires des racines carrées (valeurs initiales) et cubiques (K) des premiers nombres premiers.
/// Vérifiés contre System.Security.Cryptography dans les tests.
public static class Sha2
{
    private static readonly ulong[] K512 = RootFractions(80, 3);
    private static readonly uint[] K256 = Array.ConvertAll(K512[..64], k => (uint)(k >> 32));
    private static readonly ulong[] H512 = RootFractions(8, 2);
    private static readonly uint[] H256 = Array.ConvertAll(H512, h => (uint)(h >> 32));

    /// Empreinte SHA-256 en hexadécimal minuscule (format du manifeste).
    public static string Sha256Hex(byte[] data)
    {
        var sb = new StringBuilder(64);
        foreach (byte b in Sha256(data)) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    public static byte[] Sha256(byte[] data)
    {
        var h = (uint[])H256.Clone();
        var w = new uint[64];
        byte[] m = Pad(data, 64);
        for (int off = 0; off < m.Length; off += 64) Block256(m, off, h, w);
        var res = new byte[32];
        for (int i = 0; i < 8; i++) BinaryPrimitives.WriteUInt32BigEndian(res.AsSpan(i * 4), h[i]);
        return res;
    }

    public static byte[] Sha512(byte[] data)
    {
        var h = (ulong[])H512.Clone();
        var w = new ulong[80];
        byte[] m = Pad(data, 128);
        for (int off = 0; off < m.Length; off += 128) Block512(m, off, h, w);
        var res = new byte[64];
        for (int i = 0; i < 8; i++) BinaryPrimitives.WriteUInt64BigEndian(res.AsSpan(i * 8), h[i]);
        return res;
    }

    private static void Block256(byte[] m, int off, uint[] h, uint[] w)
    {
        for (int t = 0; t < 16; t++) w[t] = BinaryPrimitives.ReadUInt32BigEndian(m.AsSpan(off + t * 4));
        for (int t = 16; t < 64; t++)
        {
            uint s0 = BitOperations.RotateRight(w[t - 15], 7) ^ BitOperations.RotateRight(w[t - 15], 18) ^ (w[t - 15] >> 3);
            uint s1 = BitOperations.RotateRight(w[t - 2], 17) ^ BitOperations.RotateRight(w[t - 2], 19) ^ (w[t - 2] >> 10);
            w[t] = w[t - 16] + s0 + w[t - 7] + s1;
        }
        uint a = h[0], b = h[1], c = h[2], d = h[3], e = h[4], f = h[5], g = h[6], hh = h[7];
        for (int t = 0; t < 64; t++)
        {
            uint t1 = hh + (BitOperations.RotateRight(e, 6) ^ BitOperations.RotateRight(e, 11) ^ BitOperations.RotateRight(e, 25))
                      + ((e & f) ^ (~e & g)) + K256[t] + w[t];
            uint t2 = (BitOperations.RotateRight(a, 2) ^ BitOperations.RotateRight(a, 13) ^ BitOperations.RotateRight(a, 22))
                      + ((a & b) ^ (a & c) ^ (b & c));
            hh = g; g = f; f = e; e = d + t1; d = c; c = b; b = a; a = t1 + t2;
        }
        h[0] += a; h[1] += b; h[2] += c; h[3] += d; h[4] += e; h[5] += f; h[6] += g; h[7] += hh;
    }

    private static void Block512(byte[] m, int off, ulong[] h, ulong[] w)
    {
        for (int t = 0; t < 16; t++) w[t] = BinaryPrimitives.ReadUInt64BigEndian(m.AsSpan(off + t * 8));
        for (int t = 16; t < 80; t++)
        {
            ulong s0 = BitOperations.RotateRight(w[t - 15], 1) ^ BitOperations.RotateRight(w[t - 15], 8) ^ (w[t - 15] >> 7);
            ulong s1 = BitOperations.RotateRight(w[t - 2], 19) ^ BitOperations.RotateRight(w[t - 2], 61) ^ (w[t - 2] >> 6);
            w[t] = w[t - 16] + s0 + w[t - 7] + s1;
        }
        ulong a = h[0], b = h[1], c = h[2], d = h[3], e = h[4], f = h[5], g = h[6], hh = h[7];
        for (int t = 0; t < 80; t++)
        {
            ulong t1 = hh + (BitOperations.RotateRight(e, 14) ^ BitOperations.RotateRight(e, 18) ^ BitOperations.RotateRight(e, 41))
                       + ((e & f) ^ (~e & g)) + K512[t] + w[t];
            ulong t2 = (BitOperations.RotateRight(a, 28) ^ BitOperations.RotateRight(a, 34) ^ BitOperations.RotateRight(a, 39))
                       + ((a & b) ^ (a & c) ^ (b & c));
            hh = g; g = f; f = e; e = d + t1; d = c; c = b; b = a; a = t1 + t2;
        }
        h[0] += a; h[1] += b; h[2] += c; h[3] += d; h[4] += e; h[5] += f; h[6] += g; h[7] += hh;
    }

    /// Message + 0x80 + zéros + longueur en bits (gros-boutiste, sur les 8 derniers octets), multiple de `block`.
    /// SHA-512 réserve 16 octets à la longueur : les 8 premiers restent nuls (message < 2^61 octets).
    private static byte[] Pad(byte[] data, int block)
    {
        int lenBytes = block / 8;
        int total = (data.Length + 1 + lenBytes + block - 1) / block * block;
        var m = new byte[total];
        data.CopyTo(m, 0);
        m[data.Length] = 0x80;
        BinaryPrimitives.WriteUInt64BigEndian(m.AsSpan(total - 8), (ulong)data.Length * 8);
        return m;
    }

    /// 64 premiers bits de la partie fractionnaire de la racine `k`-ième des `count` premiers nombres premiers :
    /// floor(racine_k(p · 2^(64k))) mod 2^64, calcul entier exact.
    private static ulong[] RootFractions(int count, int k)
    {
        var res = new ulong[count];
        int found = 0;
        for (int p = 2; found < count; p++)
        {
            if (!IsPrime(p)) continue;
            BigInteger root = IntegerRoot(new BigInteger(p) << (64 * k), k);
            res[found++] = (ulong)(root & ulong.MaxValue);
        }
        return res;
    }

    private static bool IsPrime(int n)
    {
        for (int d = 2; d * d <= n; d++) if (n % d == 0) return false;
        return true;
    }

    /// Plus grand x tel que x^k ≤ n (Newton entier, en partant d'au-dessus de la racine).
    private static BigInteger IntegerRoot(BigInteger n, int k)
    {
        var x = BigInteger.One << (int)(n.GetBitLength() / k + 1);
        while (true)
        {
            var y = ((k - 1) * x + n / BigInteger.Pow(x, k - 1)) / k;
            if (y >= x) return x;
            x = y;
        }
    }
}
