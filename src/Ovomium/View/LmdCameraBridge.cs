using System;
using System.Linq;
using System.Reflection;
using MelonLoader;

namespace Ovomium.View;

/// Pont vers le mod LMD-Camera 1.1.0 (flo-st, licence MIT, livré tel quel à côté d'Ovomium : vue à la
/// première personne). Ce mod n'a que des touches clavier (1, 2, 3) ; on lui ajoute une bascule (touche et
/// bouton de manette) en appelant ses membres privés par réflexion : CameraMod._cameraInputHandler →
/// CameraInputHandler._view / _rig / _cameraSettings / ApplyView(ViewMode, RiderRig), CameraSettings.LastView
/// (préférence enregistrée dans UserData/LMD-Camera.cfg). Une autre version de LMD-Camera peut casser le pont :
/// il se désactive alors (journalisé) et les touches du mod restent utilisables.
internal sealed class LmdCameraBridge
{
    public const string MelonName = "LMD-Camera";
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private readonly object _handler;
    private readonly FieldInfo _view, _rig, _settings;
    private readonly MethodInfo _apply;

    private LmdCameraBridge(object handler, FieldInfo view, FieldInfo rig, FieldInfo settings, MethodInfo apply)
    {
        _handler = handler;
        _view = view;
        _rig = rig;
        _settings = settings;
        _apply = apply;
    }

    public string Version { get; private set; }

    /// Mod LMD-Camera chargé et compatible, sinon null (raison journalisée).
    public static LmdCameraBridge Find()
    {
        var melon = MelonBase.RegisteredMelons.FirstOrDefault(m => m.Info.Name == MelonName);
        if (melon == null) { Log.Info("vue : mod LMD-Camera absent (bascule 1re personne indisponible)"); return null; }
        try
        {
            object handler = melon.GetType().GetField("_cameraInputHandler", Any)?.GetValue(melon);
            var t = handler?.GetType();
            var bridge = new LmdCameraBridge(handler, t?.GetField("_view", Any), t?.GetField("_rig", Any),
                t?.GetField("_cameraSettings", Any), t?.GetMethod("ApplyView", Any));
            if (handler == null || bridge._view == null || bridge._rig == null || bridge._settings == null || bridge._apply == null)
                throw new MissingMemberException("membres attendus introuvables");
            bridge.Version = melon.Info.Version;
            Log.Info($"vue : LMD-Camera {melon.Info.Version} trouvé, bascule 1re personne active");
            return bridge;
        }
        catch (Exception e)
        {
            Log.Warn($"vue : LMD-Camera {melon.Info.Version} non pris en charge ({e.Message}) ; ses touches 1/2/3 restent utilisables");
            return null;
        }
    }

    /// Vue courante : "None" (pas en piste), "Default", "ThirdPerson" ou "FirstPerson".
    public string View => _view.GetValue(_handler)?.ToString() ?? "None";

    /// Vélo trouvé par LMD-Camera, et pas en pleine chute (LMD-Camera refuse alors de changer de vue).
    public bool CanSwitch(out bool ragdoll)
    {
        ragdoll = false;
        object rig = _rig.GetValue(_handler);
        if (rig == null || View == "None") return false;
        var rt = rig.GetType();
        if (rt.GetProperty("IsAlive")?.GetValue(rig) is false) return false;
        ragdoll = rt.GetProperty("IsRagdolling")?.GetValue(rig) is true;
        return !ragdoll;
    }

    /// Applique la vue et l'enregistre comme préférence (comme les touches de LMD-Camera).
    public void SetView(string view)
    {
        object value = Enum.Parse(_view.FieldType, view);
        _apply.Invoke(_handler, new[] { value, _rig.GetValue(_handler) });
        object settings = _settings.GetValue(_handler);
        settings?.GetType().GetProperty("LastView", Any)?.SetValue(settings, value);
    }
}
