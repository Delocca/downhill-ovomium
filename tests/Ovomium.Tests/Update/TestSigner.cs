using System.Numerics;
using Ovomium.Core.Update;

namespace Ovomium.Tests.Update;

/// Signature Ed25519 (RFC 8032 § 5.1.6) pour les tests seulement : le jeu ne fait que vérifier.
/// Validée contre les vecteurs de la RFC (même signature, octet pour octet).
internal sealed class TestSigner
{
    private readonly BigInteger _a;
    private readonly byte[] _prefix;
    public byte[] PublicKey { get; }

    public TestSigner(byte[] seed)
    {
        var h = Sha2.Sha512(seed);
        var a = h[..32];
        a[0] &= 248;
        a[31] &= 127;
        a[31] |= 64;
        _a = Ed25519.ToInt(a);
        _prefix = h[32..];
        PublicKey = Encode(Ed25519.Mul(_a, Ed25519.Base));
    }

    /// Clé jetable tirée d'une graine fixe (tests reproductibles).
    public static TestSigner FromSeed(byte n) => new(Enumerable.Repeat(n, 32).ToArray());

    public byte[] Sign(byte[] message)
    {
        var r = Ed25519.Mod(Ed25519.ToInt(Sha2.Sha512(_prefix.Concat(message).ToArray())), Ed25519.L);
        var rBytes = Encode(Ed25519.Mul(r, Ed25519.Base));
        var k = Ed25519.Mod(Ed25519.ToInt(Sha2.Sha512(rBytes.Concat(PublicKey).Concat(message).ToArray())), Ed25519.L);
        var s = Ed25519.Mod(r + k * _a, Ed25519.L);
        return rBytes.Concat(Le32(s)).ToArray();
    }

    private static byte[] Encode(Ed25519.Point p)
    {
        var zInv = Ed25519.Inv(p.Z);
        var x = Ed25519.Mod(p.X * zInv);
        var bytes = Le32(Ed25519.Mod(p.Y * zInv));
        if (!x.IsEven) bytes[31] |= 0x80;
        return bytes;
    }

    private static byte[] Le32(BigInteger v)
    {
        var b = v.ToByteArray(isUnsigned: true, isBigEndian: false);
        var res = new byte[32];
        b.CopyTo(res, 0);
        return res;
    }
}
