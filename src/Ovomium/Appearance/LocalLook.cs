using Il2Cpp;
using Ovomium.Core.Appearance;

namespace Ovomium.Appearance;

/// Apparence du joueur local, lue dans son profil (GamePropertiesSettings) : jamais écrite.
internal static class LocalLook
{
    private static bool _warned;

    /// Apparence courante, ou inconnue (vélo 0) si le profil n'est pas encore chargé.
    public static RiderLook Read()
    {
        try
        {
            var gp = ObjectPublicOb1SePl1BoInInSePlUnique.field_Private_Static_ObjectPublicOb1SePl1BoInInSePlUnique_0?
                .field_Private_Object1PublicStObBo1UIObBoObBoObUnique_1_PlayerProfileParameter_0?.field_Private_T_0?.GameProperties;
            if (gp == null) return default;
            int vhl = gp.ActiveVehicle;
            return new RiderLook(vhl, gp.Method_Public_Int32_Int32_0(vhl), gp.ActiveRiderSkinID,
                gp.ActiveRiderSkinColor, gp.ActiveRiderHairColor, gp.ActiveRiderFacialHairColor,
                (int)gp.ActiveBodyType, (int)gp.ActiveRiderHair, (int)gp.ActiveRiderFacialHair,
                (int)gp.ActiveRiderHelmet, (int)gp.ActiveRiderBackAccessory);
        }
        catch (System.Exception e)
        {
            if (!_warned) { _warned = true; Log.Error("lecture de l'apparence locale", e); }
            return default;
        }
    }
}
