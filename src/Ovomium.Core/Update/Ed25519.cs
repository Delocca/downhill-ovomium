// Vérification de signatures Ed25519 (RFC 8032), entièrement managée : BigInteger et SHA-512 managé (Sha2),
// aucune crypto native (sous Proton, celle de .NET passe par la bcrypt de Wine).
//
// Adapté de l'implémentation de référence en Python de la RFC 8032, § 6 « Ed25519 Python Illustration »
// (https://www.rfc-editor.org/rfc/rfc8032#section-6), seule la partie vérification.
// Code Component de la RFC 8032, sous la licence BSD révisée de l'IETF Trust :
//
//   Copyright (c) 2017 IETF Trust and the persons identified as authors of the code
//   (S. Josefsson, I. Liusvaara). All rights reserved.
//
//   Redistribution and use in source and binary forms, with or without modification, are permitted provided
//   that the following conditions are met:
//   - Redistributions of source code must retain the above copyright notice, this list of conditions and the
//     following disclaimer.
//   - Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the
//     following disclaimer in the documentation and/or other materials provided with the distribution.
//   - Neither the name of Internet Society, IETF or IETF Trust, nor the names of specific contributors, may be
//     used to endorse or promote products derived from this software without specific prior written permission.
//
//   THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED
//   WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A
//   PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR ANY
//   DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO,
//   PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION)
//   HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING
//   NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE
//   POSSIBILITY OF SUCH DAMAGE.
//
// Pas en temps constant : seules des données publiques (clé publique, message, signature) passent ici.

using System;
using System.Numerics;

namespace Ovomium.Core.Update;

public static class Ed25519
{
    internal static readonly BigInteger P = BigInteger.Pow(2, 255) - 19;
    /// Ordre du groupe (q dans la RFC).
    internal static readonly BigInteger L = BigInteger.Pow(2, 252) + BigInteger.Parse("27742317777372353535851937790883648493");
    private static readonly BigInteger D = Mod(-121665 * Inv(121666));
    private static readonly BigInteger SqrtM1 = BigInteger.ModPow(2, (P - 1) / 4, P);
    internal static readonly Point Base = MakeBase();

    /// Point en coordonnées étendues (X, Y, Z, T), x = X/Z, y = Y/Z, x·y = T/Z.
    internal readonly struct Point
    {
        public readonly BigInteger X, Y, Z, T;
        public Point(BigInteger x, BigInteger y, BigInteger z, BigInteger t) { X = x; Y = y; Z = z; T = t; }
    }

    /// Signature `signature` (64 octets) de `message` par la clé publique `publicKey` (32 octets) ?
    /// Toute entrée mal formée (taille, point hors courbe, S ≥ L) donne faux.
    public static bool Verify(byte[] publicKey, byte[] message, byte[] signature)
    {
        if (publicKey == null || publicKey.Length != 32 || signature == null || signature.Length != 64 || message == null) return false;
        if (!TryDecode(publicKey, out var a)) return false;
        byte[] rBytes = signature[..32];
        if (!TryDecode(rBytes, out var r)) return false;
        var s = ToInt(signature.AsSpan(32, 32));
        if (s >= L) return false;
        var buf = new byte[64 + message.Length];
        rBytes.CopyTo(buf, 0);
        publicKey.CopyTo(buf, 32);
        message.CopyTo(buf, 64);
        var h = Mod(ToInt(Sha2.Sha512(buf)), L);
        return AreEqual(Mul(s, Base), Add(r, Mul(h, a)));
    }

    internal static BigInteger Mod(BigInteger x) => Mod(x, P);

    internal static BigInteger Mod(BigInteger x, BigInteger m)
    {
        var r = x % m;
        return r.Sign < 0 ? r + m : r;
    }

    internal static BigInteger Inv(BigInteger x) => BigInteger.ModPow(Mod(x), P - 2, P);

    /// Entier petit-boutiste non signé.
    internal static BigInteger ToInt(ReadOnlySpan<byte> bytes) => new(bytes, isUnsigned: true, isBigEndian: false);

    internal static Point Add(Point p, Point q)
    {
        var a = Mod((p.Y - p.X) * (q.Y - q.X));
        var b = Mod((p.Y + p.X) * (q.Y + q.X));
        var c = Mod(2 * p.T * q.T * D);
        var d = Mod(2 * p.Z * q.Z);
        BigInteger e = b - a, f = d - c, g = d + c, h = b + a;
        return new Point(Mod(e * f), Mod(g * h), Mod(f * g), Mod(e * h));
    }

    /// s·p (doublement et addition).
    internal static Point Mul(BigInteger s, Point p)
    {
        var q = new Point(0, 1, 1, 0); // neutre
        while (s > 0)
        {
            if (!s.IsEven) q = Add(q, p);
            p = Add(p, p);
            s >>= 1;
        }
        return q;
    }

    private static bool AreEqual(Point p, Point q) =>
        Mod(p.X * q.Z - q.X * p.Z).IsZero && Mod(p.Y * q.Z - q.Y * p.Z).IsZero;

    /// Décompression d'un point (RFC 8032 § 5.1.3) : y sur 255 bits, bit de poids fort = parité de x.
    internal static bool TryDecode(ReadOnlySpan<byte> s, out Point p)
    {
        p = default;
        bool sign = (s[31] & 0x80) != 0;
        var y = ToInt(s) & ((BigInteger.One << 255) - 1);
        if (!TryRecoverX(y, sign, out var x)) return false;
        p = new Point(x, y, 1, Mod(x * y));
        return true;
    }

    private static bool TryRecoverX(BigInteger y, bool sign, out BigInteger x)
    {
        x = BigInteger.Zero;
        if (y >= P) return false;
        var x2 = Mod((y * y - 1) * Inv(D * y * y + 1));
        if (x2.IsZero) return !sign;
        x = BigInteger.ModPow(x2, (P + 3) / 8, P);
        if (!Mod(x * x - x2).IsZero) x = Mod(x * SqrtM1);
        if (!Mod(x * x - x2).IsZero) return false;
        if (x.IsEven == sign) x = P - x; // parité de x ≠ bit de signe : on prend l'opposé
        return true;
    }

    private static Point MakeBase()
    {
        var y = Mod(4 * Inv(5));
        TryRecoverX(y, false, out var x);
        return new Point(x, y, 1, Mod(x * y));
    }
}
