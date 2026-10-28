using System;
using System.Globalization;
using Il2CppMegagon.Downhill.Rider;

namespace LmdExplorer;

/// Apparence complète d'un joueur, telle que la stocke le profil (GamePropertiesSettings) : lue, jamais écrite.
internal sealed class SkinLook
{
    public int Vehicle, Variant, Skin, SkinColor, HairColor, BeardColor;
    public BodyType Body;
    public HairType Hair;
    public FacialHairType Beard;
    public HelmetType Helmet;
    public BackAccessoryType Back;

    public static SkinLook Local()
    {
        var gp = TrackProbe.Profile().GameProperties;
        return new SkinLook
        {
            Vehicle = gp.ActiveVehicle,
            Variant = gp.Method_Public_Int32_Int32_0(gp.ActiveVehicle), // variante choisie pour ce vélo (supposé)
            Skin = gp.ActiveRiderSkinID,
            SkinColor = gp.ActiveRiderSkinColor,
            HairColor = gp.ActiveRiderHairColor,
            BeardColor = gp.ActiveRiderFacialHairColor,
            Body = gp.ActiveBodyType,
            Hair = gp.ActiveRiderHair,
            Beard = gp.ActiveRiderFacialHair,
            Helmet = gp.ActiveRiderHelmet,
            Back = gp.ActiveRiderBackAccessory,
        };
    }

    /// clé=valeur (valeurs d'enum en nombre ou en nom).
    public void Set(string kv)
    {
        string[] p = kv.Split('=', 2);
        string v = p[1];
        int I() => int.Parse(v, CultureInfo.InvariantCulture);
        T E<T>() where T : struct, Enum => Enum.Parse<T>(v, true);
        switch (p[0])
        {
            case "vhl": Vehicle = I(); break;
            case "var": Variant = I(); break;
            case "sk": Skin = I(); break;
            case "sc": SkinColor = I(); break;
            case "hc": HairColor = I(); break;
            case "bc": BeardColor = I(); break;
            case "body": Body = E<BodyType>(); break;
            case "hair": Hair = E<HairType>(); break;
            case "beard": Beard = E<FacialHairType>(); break;
            case "helmet": Helmet = E<HelmetType>(); break;
            case "back": Back = E<BackAccessoryType>(); break;
            default: throw new ArgumentException($"clé inconnue : {p[0]}");
        }
    }

    public override string ToString() =>
        $"vhl={Vehicle} var={Variant} sk={Skin} sc={SkinColor} hc={HairColor} bc={BeardColor} " +
        $"body={Body} hair={Hair} beard={Beard} helmet={Helmet} back={Back}";
}
