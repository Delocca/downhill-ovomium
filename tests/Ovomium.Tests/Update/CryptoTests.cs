using System.Security.Cryptography;
using System.Text;
using Ovomium.Core.Update;

namespace Ovomium.Tests.Update;

/// SHA-2 managés et vérification Ed25519 (RFC 8032, signatures produites par OpenSSL).
public class CryptoTests
{
    private static byte[] Hex(string s) => Convert.FromHexString(s);

    [Fact]
    public void Sha2_CommeLaBibliothequeDotnet()
    {
        var rnd = new Random(1);
        foreach (int len in Enumerable.Range(0, 300).Append(1 << 20))
        {
            var data = new byte[len];
            rnd.NextBytes(data);
            Assert.Equal(SHA256.HashData(data), Sha2.Sha256(data));
            Assert.Equal(SHA512.HashData(data), Sha2.Sha512(data));
        }
        Assert.Equal("2c26b46b68ffc68ff99b453c1d30413413422d706483bfa0f98a5e886266e7ae", Sha2.Sha256Hex(Encoding.ASCII.GetBytes("foo")));
    }

    // RFC 8032 § 7.1, TEST 1 (message vide) et TEST 3 (2 octets) ; TEST 3 recoupé avec openssl pkeyutl.
    public static IEnumerable<object[]> Rfc8032 => new[]
    {
        new object[] { "9d61b19deffd5a60ba844af492ec2cc44449c5697b326919703bac031cae7f60",
            "d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a", "",
            "e5564300c360ac729086e2cc806e828a84877f1eb8e5d974d873e065224901555fb8821590a33bacc61e39701cf9b46bd25bf5f0595bbe24655141438e7a100b" },
        new object[] { "c5aa8df43f9f837bedb7442f31dcb7b166d38535076f094b85ce3a2e0b4458f7",
            "fc51cd8e6218a1a38da47ed00230f0580816ed13ba3303ac5deb911548908025", "af82",
            "6291d657deec24024827e69c3abe01a30ce548a284743a445e3680d7db5ac3ac18ff9b538d16f290ae67f760984dc6594a7c15e9716ed28dc027beceea1ec40a" },
    };

    [Theory, MemberData(nameof(Rfc8032))]
    public void Ed25519_VecteursRfc8032(string secret, string pub, string msg, string sig)
    {
        Assert.True(Ed25519.Verify(Hex(pub), Hex(msg), Hex(sig)));
        // Le signataire des tests retrouve la même clé publique et la même signature.
        var signer = new TestSigner(Hex(secret));
        Assert.Equal(Hex(pub), signer.PublicKey);
        Assert.Equal(Hex(sig), signer.Sign(Hex(msg)));
    }

    // Clé jetable générée par `openssl genpkey -algorithm ed25519`, manifeste signé par
    // `openssl pkeyutl -sign -rawin` (la commande de publication), signature brute de 64 octets.
    private static readonly byte[] OpenSslKey = Convert.FromBase64String("Qj4ok29KRu7H77R8Yp6dZ/messXbDLuEHslCfFDRYvw=");
    private static readonly byte[] OpenSslSig = Convert.FromBase64String("k1F72TS4sBrDxfMrgH/QXxmv/kqWTyz+QUnY22pKx0fsYQ7XHFtpQnWjg7E3j5l9KKLUsw3fJLERsPzSB4r7Cw==");
    internal const string OpenSslManifest = "{\"version\":\"0.5.0\",\"minUpdater\":1,\"files\":[{\"name\":\"Ovomium.dll\",\"dest\":\"Mods\",\"sha256\":\"2c26b46b68ffc68ff99b453c1d30413413422d706483bfa0f98a5e886266e7ae\"}]}";

    [Fact]
    public void Ed25519_SignatureOpenSsl_Valide()
    {
        Assert.True(Ed25519.Verify(OpenSslKey, Encoding.UTF8.GetBytes(OpenSslManifest), OpenSslSig));
    }

    [Fact]
    public void Ed25519_Alteree_Refusee()
    {
        var msg = Encoding.UTF8.GetBytes(OpenSslManifest);
        var other = (byte[])msg.Clone();
        other[12] ^= 1; // « 0.5.0 » → « 1.5.0 »
        Assert.False(Ed25519.Verify(OpenSslKey, other, OpenSslSig));
        for (int i = 0; i < 64; i += 7)
        {
            var sig = (byte[])OpenSslSig.Clone();
            sig[i] ^= 0x10;
            Assert.False(Ed25519.Verify(OpenSslKey, msg, sig));
        }
        Assert.False(Ed25519.Verify(UpdatePackage.ReleaseKey, msg, OpenSslSig)); // autre clé
        Assert.False(Ed25519.Verify(OpenSslKey, msg, OpenSslSig[..63]));
        Assert.False(Ed25519.Verify(OpenSslKey[..31], msg, OpenSslSig));
    }

    [Fact]
    public void Ed25519_SNonReduit_Refuse()
    {
        // S + L donne la même équation, mais la RFC impose S < L (sinon signatures malléables).
        var msg = Encoding.UTF8.GetBytes(OpenSslManifest);
        var s = Ed25519.ToInt(OpenSslSig.AsSpan(32)) + Ed25519.L;
        var sig = OpenSslSig[..32].Concat(s.ToByteArray(isUnsigned: true).Concat(new byte[32]).Take(32)).ToArray();
        Assert.False(Ed25519.Verify(OpenSslKey, msg, sig));
    }

    [Fact]
    public void CleDesReleases_EstUnPointValide()
    {
        Assert.Equal(32, UpdatePackage.ReleaseKey.Length);
        Assert.True(Ed25519.TryDecode(UpdatePackage.ReleaseKey, out _));
        var signer = TestSigner.FromSeed(7);
        var msg = new byte[] { 1, 2, 3 };
        Assert.True(Ed25519.Verify(signer.PublicKey, msg, signer.Sign(msg)));
        Assert.False(Ed25519.Verify(UpdatePackage.ReleaseKey, msg, signer.Sign(msg)));
    }
}
