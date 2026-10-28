using Ovomium.Core.Protocol;

namespace Ovomium.Core.Appearance;

/// Apparence d'un joueur, telle que la stocke son profil (vélo, variante, tenue, couleurs, attaches).
/// Valeurs brutes du jeu (identifiants et enums en entiers) : validées seulement par la construction
/// du modèle, qui se replie sur le vélo local si l'une est inconnue. Égalité par valeur = clé de cache.
public readonly record struct RiderLook(
    int Vehicle, int Variant, int Skin, int SkinColor, int HairColor, int BeardColor,
    int Body, int Hair, int Beard, int Helmet, int Back)
{
    /// Vélo 0 = apparence inconnue (pair qui n'a pas encore lu son profil) : copie du vélo local.
    public bool IsKnown => Vehicle != 0;

    /// Encodage : 11 entiers 32 bits (44 octets ; les identifiants du jeu dépassent 255).
    public void Write(PacketWriter w)
    {
        foreach (int v in new[] { Vehicle, Variant, Skin, SkinColor, HairColor, BeardColor, Body, Hair, Beard, Helmet, Back })
            w.U32((uint)v);
    }

    public static RiderLook Read(PacketReader r)
    {
        int I() => (int)r.U32();
        return new RiderLook(I(), I(), I(), I(), I(), I(), I(), I(), I(), I(), I());
    }

    public override string ToString() =>
        $"vhl={Vehicle} var={Variant} sk={Skin} sc={SkinColor} hc={HairColor} bc={BeardColor} " +
        $"body={Body} hair={Hair} beard={Beard} helmet={Helmet} back={Back}";
}
