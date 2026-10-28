using System.Text;
using Il2CppMegagon.Downhill.Config;

namespace LmdExplorer;

/// Rapport « skin » : config locale et contenu des bases vélos / tenues (ce qu'un autre joueur peut porter).
internal static class SkinCatalog
{
    public static string Report()
    {
        var sb = new StringBuilder();
        Out.Try("config locale", () => sb.Append($"config locale : {SkinLook.Local()}\n"));
        Out.Try("vélos", () => AppendVehicles(sb));
        Out.Try("tenues", () => AppendRiders(sb));
        return sb.ToString();
    }

    private static void AppendVehicles(StringBuilder sb)
    {
        var db = GameConfig.ActiveConfig.vehicleDB;
        sb.Append($"\nvélos ({db.m_allVehicleConfigurations.Count}, défaut {db.defaultConfiguration?.ID}) :\n");
        foreach (var vc in db.m_allVehicleConfigurations)
        {
            sb.Append($"  {vc.ID} {vc.nameLocaKey} sélectionnable={vc.userSelectable} dlc={vc.isAdditionalContent} " +
                      $"possédé={vc.ownedByDefault} variante défaut={vc.defaultVariant?.ID} variantes :");
            foreach (var v in vc.m_vehicleCustomizationVariants)
                sb.Append($" {v.ID}({v.subMeshes?.Count} meshes{(v.m_connectedDLC != null ? ", dlc" : "")})");
            sb.Append('\n');
        }
    }

    private static void AppendRiders(StringBuilder sb)
    {
        var db = GameConfig.ActiveConfig.riderDB;
        sb.Append($"\ntenues ({db.m_configurations.Count} + add-ons {db.m_addonConfigurations?.Count}, défaut {db.defaultConfiguration?.ID}) :\n");
        foreach (var rs in db.m_configurations) AppendRider(sb, rs);
        if (db.m_addonConfigurations != null) foreach (var rs in db.m_addonConfigurations) AppendRider(sb, rs);
        sb.Append($"couleurs de peau : {db.m_skinColors.Count}, de cheveux : {db.m_hairColors.Count}\n");
        sb.Append($"corps : {db.m_bodyTypes.Count}, cheveux : {db.m_hairTypes.Count}, barbes : {db.m_facialHairTypes.Count}, " +
                  $"casques : {db.m_helmetTypes.Count}, dos : {db.m_backAccessoryTypes.Count}\n");
    }

    private static void AppendRider(StringBuilder sb, Il2CppMegagon.Downhill.Rider.RiderSkinConfiguration rs) =>
        sb.Append($"  {rs.ID} {rs.nameLocaKey} couleur={rs.primaryColor} cheveux={rs.allowHair} barbe={rs.allowFacialHair} " +
                  $"casque={rs.allowHelmet} dos={rs.allowBackAccessory} collections={rs.m_submeshCollections?.Count}\n");
}
