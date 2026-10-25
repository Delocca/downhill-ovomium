using System.Collections.Generic;
using Il2Cpp;
using Il2CppMegagon.Downhill;
using Il2CppMegagon.Downhill.Managers;
using LmdMulti.Game;

namespace LmdMulti.Race;

/// Sélection d'une piste par son ID (ex. AL_01_A1), y compris verrouillée chez ce joueur (docs/game-api.md § 4).
/// Verrou du jeu : masque d'heures du jour débloquées porté par l'info de piste, en mémoire seulement
/// (recalculé depuis la progression sauvegardée au chargement du profil). On le force le temps de la
/// course, puis on le remet une fois revenu au menu. Tant qu'elle est forcée, ni temps ni progression
/// n'y sont enregistrés (Diagnostics/TestMode.cs, AllowResult).
internal static class TrackAccess
{
    private sealed class Forced { public ObjectPublicInm_StObm_InStUnique Info; public EnumPublicSealedva5v2 Flags; public SubMountain Sub; public bool WasPlayable; }

    private static readonly Dictionary<string, Forced> ForcedTracks = new();
    /// Sélection du joueur avant qu'une course ne sélectionne une piste forcée (rendue une fois en piste).
    private static string _selectionBefore;

    public static bool IsForced(string trackId) => trackId != null && ForcedTracks.ContainsKey(trackId);
    public static bool HasForced => ForcedTracks.Count > 0;

    /// Résultat concernant une piste forcée : la piste en cours, ou l'ID passé (piste « CA_01_A2 » ou
    /// classement « CA_01_A2_00_N_D »).
    public static bool IsForcedFor(string currentTrack, string id)
    {
        if (IsForced(currentTrack)) return true;
        if (string.IsNullOrEmpty(id)) return false;
        foreach (var track in ForcedTracks.Keys)
            if (id.StartsWith(track, System.StringComparison.Ordinal)) return true;
        return false;
    }

    /// Choisit montagne puis piste dans le MountainManager (comme les menus). Faux + erreur si impossible.
    public static bool Select(string trackId, out string error)
    {
        error = null;
        var mm = Manager<MountainManager>.prop_T_0;
        var list = mm?.field_Private_List_1_Mountain_0;
        if (list == null) { error = "catalogue des pistes indisponible"; return false; }
        for (int m = 0; m < list.Count; m++)
        {
            var mountain = list[m];
            if (!trackId.StartsWith(mountain.m_mountainID + "_", System.StringComparison.Ordinal)) continue;
            int idx = mountain.Method_Public_Virtual_Final_New_Int32_String_Boolean_0(trackId, false);
            if (idx < 0) break;
            string before = GameInfo.SelectedTrackId();
            if (!Unlock(mountain, trackId, out error)) return false;
            if (IsForced(trackId) && before != null && !IsForced(before)) _selectionBefore ??= before;
            mm.Method_Private_Void_Int32_0(m);   // montagne
            mm.Method_Private_Void_Int32_1(idx); // piste
            return true;
        }
        error = $"piste inconnue : {trackId}";
        return false;
    }

    private static bool Unlock(Mountain mountain, string trackId, out string error)
    {
        error = null;
        foreach (var sub in mountain.m_subMountains)
            foreach (var info in sub.m_trackRouteInfos)
            {
                if (info.m_fullTrackRouteID != trackId) continue;
                if (!sub.IsOwned) { error = "montagne non possédée (DLC)"; return false; }
                var tod = MountainManager.prop_EnumPublicSealedva5v2_0;
                if (info.Method_Public_Boolean_EnumPublicSealedva5v2_0(tod) || ForcedTracks.ContainsKey(trackId)) return true;
                ForcedTracks[trackId] = new Forced
                {
                    Info = info, Flags = info.field_Private_ValueTypePublicSealedObUnique_0.field_Public_EnumPublicSealedva5v2_0,
                    Sub = sub, WasPlayable = sub.IsPlayable,
                };
                info.Method_Public_Void_Boolean_0(true); // jour et nuit, en mémoire seulement
                if (!sub.IsPlayable) sub.IsPlayable = true;
                Log.Info($"course : {trackId} verrouillée chez toi, accès ouvert le temps de la course (heure du jour {(int)tod}, sous-montagne jouable : {ForcedTracks[trackId].WasPlayable})");
                return true;
            }
        error = $"piste introuvable : {trackId}";
        return false;
    }

    /// Une fois sur la piste forcée (déjà chargée) : rend au menu la sélection d'avant la course.
    /// Revenir au menu avec une montagne verrouillée sélectionnée laisse le vélo du jeu dans son état de
    /// piste (entrées, ragdoll) : le chargement suivant se fige alors (NullReferenceException dans
    /// BikeLocomotion, caméra ; docs/game-api.md § 4). Rend aussi au profil sa piste active d'origine.
    public static void RestoreSelection()
    {
        if (_selectionBefore == null) return;
        string id = _selectionBefore;
        _selectionBefore = null;
        if (Select(id, out string error)) Log.Info($"course : sélection du menu rendue ({id})");
        else Log.Warn($"course : sélection du menu non rendue ({id}) : {error}");
    }

    /// Remet les verrous forcés (au menu installé : RaceController).
    public static void RestoreAll()
    {
        foreach (var id in new List<string>(ForcedTracks.Keys))
        {
            var f = ForcedTracks[id];
            ForcedTracks.Remove(id);
            Log.Try("course : verrou remis", () =>
            {
                f.Info.Method_Public_Void_Boolean_0(false);
                if (f.Flags != 0) f.Info.Method_Public_Void_Boolean_EnumPublicSealedva5v2_0(true, f.Flags);
                if (!f.WasPlayable) f.Sub.IsPlayable = false;
            });
            Log.Info($"course : verrou de {id} remis comme avant");
        }
    }
}
