using System;
using System.Linq;
using System.Reflection;
using MelonLoader;
using Ovomium.Core.Keys;

namespace Ovomium.View;

/// Compatibilité avec le mod LMD-VR 1.0.0 (visual-gh, licence MIT, installé à part par le joueur) :
/// - installé → KeysNotice signale au menu nos touches réglées sur les siennes (codées en dur : F5-F7, F9-F11…) ;
///   nos défauts F1-F4 et V n'en croisent aucune ;
/// - VR démarrée → CameraToggle coupe notre bascule V / R3 (R3 et L3+R3 sont à lui, et c'est lui qui pilote la caméra).
/// « Démarrée » se lit par réflexion dans ses membres privés : Mod._vr (VrManager) → _session (VrSession) → State,
/// démarrée hors Off et Failed (comme sa touche F9). Une autre version peut casser cette lecture : on considère alors
/// la VR démarrée dès que le mod est là (journalisé), ce qui ne coupe que notre bascule de vue.
internal static class LmdVrBridge
{
    public const string MelonName = "LMD VR";
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static bool _searched, _readFailed;
    private static MelonBase _melon;

    /// Mod LMD-VR chargé.
    public static bool Installed
    {
        get
        {
            if (_searched) return _melon != null;
            _searched = true;
            _melon = MelonBase.RegisteredMelons.FirstOrDefault(m => m.Info.Name == MelonName);
            Log.Info(_melon == null ? "VR : mod LMD-VR absent"
                : $"VR : LMD-VR {_melon.Info.Version} trouvé, bascule de vue coupée pendant la VR");
            return _melon != null;
        }
    }

    /// Avis pour le joueur (null sans LMD-VR ou sans conflit) : nos touches qui tombent sur les siennes (journalisées).
    public static string KeysNotice()
    {
        if (!Installed) return null;
        var clashes = Config.AllKeys.Where(k => KeyChoice.VrConflicts(new[] { k.Name }).Any()).ToList();
        if (clashes.Count == 0) return null;
        string list = string.Join(" · ", clashes.Select(k => $"{k.Name} {k.Label}"));
        Log.Warn($"touches aussi utilisées par LMD-VR : {list} ; à changer dans {Config.FilePath}");
        return $"Touches d'Ovomium aussi utilisées par LMD-VR : {list} (à changer dans Ovomium.cfg)";
    }

    /// Session VR démarrée (en attente du casque, au repos ou en cours).
    public static bool Active
    {
        get
        {
            if (!Installed) return false;
            if (_readFailed) return true;
            try
            {
                object vr = _melon.GetType().GetField("_vr", Any)?.GetValue(_melon) ?? throw new MissingMemberException("Mod._vr");
                var sessionField = vr.GetType().GetField("_session", Any) ?? throw new MissingMemberException("VrManager._session");
                object session = sessionField.GetValue(vr);
                if (session == null) return false; // jamais démarrée
                object state = session.GetType().GetProperty("State", Any)?.GetValue(session) ?? throw new MissingMemberException("VrSession.State");
                return state.ToString() is not ("Off" or "Failed");
            }
            catch (Exception e)
            {
                _readFailed = true;
                Log.Warn($"VR : état de LMD-VR {_melon.Info.Version} illisible ({e.Message}) ; bascule de vue coupée tant qu'il est installé");
                return true;
            }
        }
    }
}
