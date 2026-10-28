using System;
using System.IO;
using MelonLoader;
using MelonLoader.Utils;
using Ovomium.Core.Update;

// Version affichée par MelonLoader : « UpdaterVersion.0.0 ».
[assembly: MelonInfo(typeof(Ovomium.Updater.UpdaterPlugin), Ovomium.Updater.UpdaterPlugin.MelonName, "1.0.0", "Edia")]
[assembly: MelonGame("Megagon Industries", "Lonely Mountains: Downhill")]

namespace Ovomium.Updater;

/// Installe la mise à jour d'Ovomium préparée par le mod (UserData/Ovomium/update/), avant que MelonLoader ne
/// charge Mods/. Tout est revérifié ici (signature, empreintes, versions) : voir Core/Update/UpdateInstaller.
///
/// Moment : OnPreInitialization, appelé par MelonLoader 0.7.3 (Core.Initialize) juste après le chargement de
/// Plugins/, avant la génération des assemblies Il2Cpp et bien avant LoadMelons(Mods) (Core.Start). Aucun fichier
/// de Mods/ n'est encore ouvert : on peut les remplacer, y compris sous Windows.
public sealed class UpdaterPlugin : MelonPlugin
{
    public const string MelonName = "OvomiumUpdater";

    /// Version du plugin, comparée au minUpdater du manifeste et lue par le mod (réflexion). Ne la changer que si
    /// le plugin change réellement (il n'est remplacé que par l'installeur, jamais par la mise à jour automatique).
    public const int UpdaterVersion = 1;

    public override void OnPreInitialization()
    {
        try
        {
            string dataDir = Path.Combine(MelonEnvironment.UserDataDirectory, "Ovomium");
            var o = UpdateInstaller.Run(dataDir, MelonEnvironment.ModsDirectory, UpdaterVersion, UpdatePackage.ReleaseKey, s => Say(0, s));
            if (o == null) return; // aucun paquet prêt : cas courant, rien à dire
            string what = $"mise à jour d'Ovomium {o.Version ?? "?"} : {o.Result}{(string.IsNullOrEmpty(o.Reason) ? "" : $" ({o.Reason})")}";
            if (o.Result == UpdateOutcome.Installed) Say(0, what);
            else Say(1, what + " ; paquet mis de côté dans UserData/Ovomium/" + UpdateFiles.QuarantineDir);
        }
        catch (Exception e)
        {
            // Jamais d'exception vers MelonLoader : le jeu doit démarrer quoi qu'il arrive.
            Say(2, $"mise à jour : {e.GetType().Name} : {e.Message}");
        }
    }

    /// Journal qui ne lève jamais (0 info, 1 avertissement, 2 erreur) : console si le journal de MelonLoader n'est pas
    /// prêt, rien si elle non plus.
    private void Say(int level, string text)
    {
        try
        {
            if (level == 0) LoggerInstance.Msg(text);
            else if (level == 1) LoggerInstance.Warning(text);
            else LoggerInstance.Error(text);
        }
        catch (Exception)
        {
            try { Console.Error.WriteLine($"[{MelonName}] {text}"); }
            catch (Exception) { }
        }
    }
}
