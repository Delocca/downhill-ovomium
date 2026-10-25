using System.Text;
using Il2Cpp;
using Il2CppMegagon.Downhill;
using Il2CppMegagon.Downhill.SaveGame.Parameter;
using Il2CppMegagon.Downhill.TrackElements;
using Il2CppMegagon.Downhill.Tracks;
using Il2CppMegagon.Downhill.UI.Screens;
using UnityEngine;

namespace LmdExplorer;

/// Piste courante, checkpoints, départ, état de jeu, chrono, profil, Steam.
internal static class TrackProbe
{
    public static int GameState() => (int)GameStateManager.prop_EnumNPublicSealedva5vUnique_0;

    /// Profil joueur (sauvegarde) : chemin repris de visual-gh/LMD-Custom-Rides.
    public static PlayerProfileParameter Profile() =>
        ObjectPublicOb1SePl1BoInInSePlUnique.field_Private_Static_ObjectPublicOb1SePl1BoInInSePlUnique_0?
            .field_Private_Object1PublicStObBo1UIObBoObBoObUnique_1_PlayerProfileParameter_0?.field_Private_T_0;

    public static string Report()
    {
        var sb = new StringBuilder();
        Out.Try("état", () => sb.Append($"GameState={GameState()} timeScale={Time.timeScale}\n"));
        Out.Try("profil", () => AppendProfile(sb));
        Out.Try("montagne", () => AppendMountain(sb));
        Out.Try("route", () => AppendRoute(sb, PlayManager.prop_TrackRoute_0));
        Out.Try("départs", () => AppendStarts(sb));
        Out.Try("chrono", () => sb.Append(TimerLine()).Append('\n'));
        Out.Try("steam", () => sb.Append(SteamLine()).Append('\n'));
        return sb.ToString();
    }

    private static void AppendProfile(StringBuilder sb)
    {
        var gp = Profile()?.GameProperties;
        if (gp == null) { sb.Append("profil: null\n"); return; }
        sb.Append($"profil: ActiveFullTrackRouteID={gp.ActiveFullTrackRouteID} ActiveVehicle={gp.ActiveVehicle} " +
                  $"GameModeID={gp.ActiveGameModeID} ToD={(int)gp.ActiveTimeOfDay} body={gp.ActiveBodyType} " +
                  $"skin={gp.ActiveRiderSkinID}/{gp.ActiveRiderSkinColor} helmet={gp.ActiveRiderHelmet} hair={gp.ActiveRiderHair}\n");
    }

    private static void AppendMountain(StringBuilder sb)
    {
        var sub = MountainManager.Method_Public_Static_SubMountain_0();
        var info = MountainManager.Method_Public_Static_ObjectPublicInm_StObm_InStUnique_0();
        sb.Append($"subMountain={(sub != null ? $"{sub.m_SubID} scene={sub.m_sceneName}" : "null")} ");
        sb.Append($"trackInfo={(info != null ? $"{info.m_fullTrackRouteID} cps={info.m_checkpointCount}" : "null")}\n");
        if (info != null)
            sb.Append($"nom affiché={MountainManager.Method_Public_Static_String_String_0(info.m_fullTrackRouteID)}\n");
    }

    private static void AppendRoute(StringBuilder sb, TrackRoute route)
    {
        if (route == null) { sb.Append("TrackRoute courante: null\n"); return; }
        sb.Append($"TrackRoute {route.name} id={route.m_trackRouteID} full={route.m_trackRouteInfo?.m_fullTrackRouteID}\n");
        var cps = route.m_checkpoints;
        for (int i = 0; i < cps.Length; i++)
        {
            var cp = cps[i];
            if (cp == null) { sb.Append($"  cp[{i}] null\n"); continue; }
            string spawn = cp.m_spawnTransform != null ? cp.m_spawnTransform.position.ToString() : "-";
            sb.Append($"  cp[{i}] {cp.name} pos={cp.transform.position} spawn={cp.m_hasSpawnPoint}:{spawn}\n");
        }
    }

    private static void AppendStarts(StringBuilder sb)
    {
        foreach (var s in Object.FindObjectsOfType<TrackStartPosition>())
            sb.Append($"TrackStartPosition {s.name} pos={s.transform.position} rot={s.transform.rotation.eulerAngles}\n");
        var finish = GameObject.Find("GameObject_FinishLine");
        sb.Append($"GameObject_FinishLine={(finish != null ? finish.transform.position.ToString() : "absent")}\n");
    }

    /// Catalogue : montagnes (index de liste) -> sous-montagnes -> pistes, avec l'index que
    /// Mountain.Method_Public_Virtual_Final_New_Int32_String_Boolean_0(id) renvoie pour chaque piste.
    public static string Catalogue()
    {
        var sb = new StringBuilder();
        var list = Il2CppMegagon.Downhill.Managers.Manager<MountainManager>.prop_T_0.field_Private_List_1_Mountain_0;
        for (int m = 0; m < list.Count; m++)
        {
            var mt = list[m];
            sb.Append($"[{m}] {mt.m_mountainID} playable={mt.IsPlayable} owned={mt.IsOwned}\n");
            foreach (var sub in mt.m_subMountains)
                foreach (var info in sub.m_trackRouteInfos)
                {
                    string id = info.m_fullTrackRouteID;
                    sb.Append($"    sub {sub.m_SubID} scene={sub.m_sceneName} {id} idx={mt.Method_Public_Virtual_Final_New_Int32_String_Boolean_0(id, false)}" +
                              $" cps={info.m_checkpointCount} nom={MountainManager.Method_Public_Static_String_String_0(id)}\n");
                }
        }
        return sb.ToString();
    }

    /// Sélectionne une piste par ID complet (ex. AL_01_A1) : index de montagne puis index de piste
    /// dans la montagne. Vérifié en jeu : « start » depuis le menu charge ensuite cette piste
    /// (et le profil ActiveFullTrackRouteID est mis à jour par le jeu).
    public static void Select(string fullTrackId)
    {
        var mm = Il2CppMegagon.Downhill.Managers.Manager<MountainManager>.prop_T_0;
        var list = mm.field_Private_List_1_Mountain_0;
        for (int m = 0; m < list.Count; m++)
        {
            int idx = list[m].Method_Public_Virtual_Final_New_Int32_String_Boolean_0(fullTrackId, false);
            if (!fullTrackId.StartsWith(list[m].m_mountainID + "_") || idx < 0) continue;
            mm.Method_Private_Void_Int32_0(m);   // choisit la montagne
            mm.Method_Private_Void_Int32_1(idx); // choisit la piste
            Out.Info(SelectionLine());
            return;
        }
        Out.Info($"piste inconnue : {fullTrackId}");
    }

    /// Sélection courante côté MountainManager (celle que « start » lance) vs profil.
    public static string SelectionLine()
    {
        var info = MountainManager.Method_Public_Static_ObjectPublicInm_StObm_InStUnique_0();
        return $"sélection MountainManager={info?.m_fullTrackRouteID} idx={MountainManager.prop_Int32_0}/{MountainManager.prop_Int32_1}/{MountainManager.prop_Int32_2} " +
               $"profil={Profile()?.GameProperties?.ActiveFullTrackRouteID}";
    }

    public static string TimerLine()
    {
        var tracker = HighscoreManager.prop_ObjectPublicBoGaObInSiStObDi2LiUnique_0;
        string t = "tracker=null";
        if (tracker != null)
        {
            var ms = tracker.field_Private_List_1_UInt32_0;
            string first = ms != null && ms.Count > 0 ? ms[0].ToString() : "-";
            t = $"tracker: sections={tracker.field_Private_Int32_0} dernierTemps={tracker.field_Private_Single_0:F2} " +
                $"ms[0]={first} fini={tracker.prop_Boolean_0}";
        }
        var ps = Object.FindObjectOfType<PlayScreen>();
        string hud = ps != null && ps.m_timePanelText != null ? ps.m_timePanelText.text : "-";
        return $"{t} HUD='{hud}'";
    }

    public static string SteamLine()
    {
        string name = Il2CppSteamworks.SteamClient.Name;
        ulong id = Il2CppSteamworks.SteamClient.SteamId.Value;
        return $"Steam: Name='{name}' SteamId={id} IsValid={Il2CppSteamworks.SteamClient.IsValid}";
    }
}
