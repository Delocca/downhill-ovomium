using System;
using System.Collections.Generic;
using Il2Cpp;
using Il2CppSystem.Threading.Tasks;
using Il2CppMegagon.Downhill;
using Il2CppMegagon.Downhill.Managers;
using Il2CppMegagon.Downhill.Scoring;

namespace LmdExplorer;

/// Commande « online <piste> » : LIT mon score en ligne (total et sections) sur le serveur de classement de
/// Megagon, par le même chemin que l'écran de classement du jeu (docs/game-api.md § 5 « Lire les
/// classements en ligne »). Lecture seule : aucune méthode d'envoi, de compte ou de profil n'est appelée.
internal static class OnlineScores
{
    private const int GlobalAllTime = 0x3F, OwnAllTime = 0xF; // filtres du jeu (EnumPublicSealedva22Unique)

    public static void Read(string trackId)
    {
        if (string.IsNullOrWhiteSpace(trackId)) { Out.Info("usage : online <piste>, ex. online RO_01_A4"); return; }
        var lm = Manager<LeaderboardManager>.prop_T_0;
        int mode = (int)lm.field_Private_EnumNPublicSealedva3vUnique_0;
        Out.Info($"classement en ligne : mode du jeu {mode} ({(mode == 0 ? "connecté" : "HORS LIGNE : le jeu répondra « non connecté »")})");
        var gm = MountainManager.prop_GameMode_0;
        var tod = MountainManager.prop_EnumPublicSealedva5v2_0;
        for (int section = 0; section <= 9; section++) ReadOne(trackId.Trim(), section, gm, tod);
    }

    private static void ReadOne(string track, int section, GameMode gm, EnumPublicSealedva5v2 tod)
    {
        string id = HighscoreManager.Method_Public_Static_String_String_Int32_GameMode_EnumPublicSealedva5v2_0(track, section, gm, tod);
        var lb = HighscoreManager
            .Method_Private_Static_InterfacePublicAbstractStObVoStObAc1UIObVoUnique_String_Int32_GameMode_EnumPublicSealedva5v2_EnumPublicSealedva22Unique_0(
                track, section, gm, tod, (EnumPublicSealedva22Unique)GlobalAllTime)
            ?.TryCast<ObjectPublicAbstractStInObBoObStInObInStUnique>();
        if (lb == null) { Out.Info($"{id} : classement introuvable"); return; }
        // Mon rang (filtre « moi »), par l'objet classement du jeu (vérifie la connexion, comme son écran).
        // Lire sa ligne en cache depuis le rappel échoue (NullReferenceException, 2026-10-25) : voir OnRank.
        Action<ValueTypePublicSealedBoUIVoBoUI1> done = r => Out.Try($"lecture {id}", () => OnRank(id, r));
        lb.Method_Public_Virtual_Final_New_Void_EnumPublicSealedva22Unique_Action_1_ValueTypePublicSealedBoUIVoBoUI1_0(
            (EnumPublicSealedva22Unique)GlobalAllTime, done);
    }

    private static void OnRank(string id, ValueTypePublicSealedBoUIVoBoUI1 r)
    {
        if (!r.field_Public_Boolean_0) { Out.Info($"EN LIGNE {id} : échec de la requête"); return; }
        if (r.field_Public_UInt32_0 == 0) { Out.Info($"EN LIGNE {id} : aucun score"); return; }
        Out.Info($"EN LIGNE {id} : rang {r.field_Public_UInt32_0}, lecture de la ligne…");
        // Ma ligne (temps, etc.) : même requête GET que le jeu, par l'API (une Task surveillée dans Poll).
        var api = ObjectPublicStInObLeCoObObUnique.prop_ObjectPublicStInObLeCoObObUnique_0;
        var range = new ValueTypePublicSealedInObInInUnique { field_Public_Int32_0 = 0, field_Public_Int32_1 = 0 };
        var task = api.Method_Public_Task_1_ValueTypePublicSealedBoLi1ObUnique_String_ValueTypePublicSealedInObInInUnique_EnumPublicSealedva22Unique_EnumPublicSealedva22Unique_Int32_Func_1_List_1_UInt32_0(
            id, range, (EnumPublicSealedva22Unique)OwnAllTime, (EnumPublicSealedva22Unique)GlobalAllTime, -1, null);
        Pending.Add((id, task));
    }

    private static readonly List<(string id, Task<ValueTypePublicSealedBoLi1ObUnique> task)> Pending = new();

    /// À chaque image : affiche les lignes reçues.
    public static void Poll()
    {
        for (int i = Pending.Count - 1; i >= 0; i--)
        {
            var (id, task) = Pending[i];
            if (!task.IsCompleted) continue;
            Pending.RemoveAt(i);
            Out.Try($"ligne {id}", () => ShowRows(id, task));
        }
    }

    private static void ShowRows(string id, Task<ValueTypePublicSealedBoLi1ObUnique> task)
    {
        if (task.IsFaulted) { Out.Info($"EN LIGNE {id} : requête en échec"); return; }
        var page = task.Result;
        var list = page?.field_Public_List_1_InterfacePublicAbstractUIInUIVoInUIInInUIUIUnique_0;
        if (page == null || !page.field_Public_Boolean_0 || list == null) { Out.Info($"EN LIGNE {id} : réponse vide ou en échec"); return; }
        if (list.Count == 0) Out.Info($"EN LIGNE {id} : aucune ligne");
        foreach (var e in list)
            Out.Info($"EN LIGNE {id} : rang {e.prop_UInt32_0}, temps {e.prop_UInt32_1} ms ({e.prop_UInt32_1 / 1000.0:F3} s), " +
                     $"joueur {e.prop_UInt32_2}, véhicule {e.prop_Int32_0}, chutes {e.prop_Int32_1}, autres {e.prop_Int32_2}/{e.prop_Int32_3}");
    }
}
