using System.Collections.Generic;
using Il2CppMegagon.Downhill;
using Il2CppMegagon.Downhill.Tracks;
using Il2CppMegagon.Downhill.UI.Screens;
using LmdMulti.Core.Net;
using UnityEngine;

namespace LmdMulti.Game;

/// Lecture de l'état du jeu (voir docs/game-api.md) : piste en cours, nom affiché, compte Steam.
internal static class GameInfo
{
    private const int StateOnTrack = 100;
    private static readonly Dictionary<string, string> TrackNames = new();

    public static int State() => (int)GameStateManager.prop_EnumNPublicSealedva5vUnique_0;

    /// ID complet de la piste en cours (ex. CA_01_A2), ou null hors piste (menus, chargement).
    public static string CurrentTrackId()
    {
        try
        {
            if (State() != StateOnTrack) return null;
            string id = PlayManager.prop_TrackRoute_0?.m_trackRouteInfo?.m_fullTrackRouteID;
            return string.IsNullOrEmpty(id) ? null : id;
        }
        catch (Il2CppInterop.Runtime.Il2CppException) { return null; } // démarrage : gestionnaires pas encore créés
    }

    /// Nom localisé (« Sierra Rivera | Piste 2 »), mis en cache ; l'ID brut si indisponible.
    public static string TrackName(string id)
    {
        if (string.IsNullOrEmpty(id)) return "menu";
        if (TrackNames.TryGetValue(id, out var name)) return name;
        try { name = MountainManager.Method_Public_Static_String_String_0(id); }
        catch (System.Exception) { name = null; }
        if (string.IsNullOrEmpty(name)) return id; // pas de cache : réessai plus tard (localisation pas prête)
        return TrackNames[id] = name;
    }

    private static PauseScreen _pause;
    private static double _nextPauseSearch;

    /// Menu pause du jeu ouvert ? (écran PauseScreen de la scène actif ; recherché au plus toutes les 2 s)
    public static bool IsPaused()
    {
        if (_pause == null)
        {
            if (NetClock.NowMs < _nextPauseSearch) return false;
            _nextPauseSearch = NetClock.NowMs + 2000;
            foreach (var p in Resources.FindObjectsOfTypeAll<PauseScreen>())
                if (p.gameObject.scene.IsValid()) { _pause = p; break; } // pas le prefab
        }
        return _pause != null && _pause.isActiveAndEnabled;
    }

    /// Pseudo et SteamId si Steam est prêt.
    public static bool TryGetSteam(out string name, out ulong steamId)
    {
        name = null; steamId = 0;
        if (!Il2CppSteamworks.SteamClient.IsValid) return false;
        name = Il2CppSteamworks.SteamClient.Name;
        steamId = Il2CppSteamworks.SteamClient.SteamId.Value;
        return !string.IsNullOrEmpty(name);
    }
}
