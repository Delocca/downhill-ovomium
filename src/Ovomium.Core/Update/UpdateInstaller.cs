using System;
using System.Collections.Generic;
using System.IO;

namespace Ovomium.Core.Update;

/// Installation d'un paquet prêt (UserData/Ovomium/update/) dans Mods/, par le plugin OvomiumUpdater, avant le
/// chargement des mods. Tout est revérifié (signature, empreintes, versions) ; puis chaque fichier est copié en
/// « .new » à côté de sa cible et revérifié, et seulement alors les cibles sont remplacées (ancienne gardée en
/// « .old » jusqu'à la fin, remise en place si un remplacement échoue). En cas de refus ou d'échec, Mods/ reste
/// tel quel et le paquet part en quarantaine (update.rejected/). Résultat écrit dans update-state.json.
public static class UpdateInstaller
{
    private sealed class Staged
    {
        public string Source, Target, Fresh, Backup, Sha256;
        public bool HadOld, Placed;
    }

    /// null : aucun paquet prêt (rien n'est écrit). Sinon le résultat, aussi écrit dans update-state.json.
    public static UpdateOutcome Run(string dataDir, string modsDir, int updaterVersion, byte[] publicKey, Action<string> log)
    {
        string dir = Path.Combine(dataDir, UpdateFiles.ReadyDir);
        if (!Directory.Exists(dir)) return null;
        UpdateOutcome outcome;
        try { outcome = Install(dir, dataDir, modsDir, updaterVersion, publicKey, log); }
        catch (Exception e) { outcome = Outcome(UpdateOutcome.Failed, null, $"{e.GetType().Name} : {e.Message}"); }
        if (outcome.Result == UpdateOutcome.Installed) TryDelete(dir, log);
        else Quarantine(dir, dataDir, log);
        UpdateFiles.WriteState(dataDir, outcome);
        return outcome;
    }

    private static UpdateOutcome Install(string dir, string dataDir, string modsDir, int updaterVersion, byte[] key, Action<string> log)
    {
        var m = UpdatePackage.ReadSigned(dir, key, out string reason);
        if (m == null) return Outcome(UpdateOutcome.Rejected, null, reason);
        string v = m.Version.ToString();
        var installed = InstalledVersion.Read(modsDir, dataDir);
        if (installed == null) return Outcome(UpdateOutcome.Rejected, v, "version installée inconnue (ni Ovomium.dll lisible, ni installed.json)");
        if (!UpdatePackage.CheckVersions(m, installed.Value, updaterVersion, out reason)) return Outcome(UpdateOutcome.Rejected, v, reason);
        if (!UpdatePackage.CheckFiles(dir, m, out reason)) return Outcome(UpdateOutcome.Rejected, v, reason);

        var items = new List<Staged>();
        foreach (var f in m.Files)
        {
            string target = Path.Combine(modsDir, f.Name);
            items.Add(new Staged { Source = Path.Combine(dir, f.Name), Target = target, Fresh = target + ".new", Backup = target + ".old", Sha256 = f.Sha256 });
        }
        try
        {
            Stage(items);
            Swap(items);
        }
        catch (Exception e)
        {
            Rollback(items, log);
            return Outcome(UpdateOutcome.Failed, v, $"{e.GetType().Name} : {e.Message}");
        }
        foreach (var s in items) TryDeleteFile(s.Backup, log);
        // Les fichiers sont en place : une erreur ici ne doit pas faire croire à un échec (le mod réécrit ce
        // fichier à son démarrage de toute façon).
        try { UpdateFiles.WriteInstalled(dataDir, m.Version); }
        catch (Exception e) { log($"installed.json non écrit : {e.Message}"); }
        return Outcome(UpdateOutcome.Installed, v, null);
    }

    /// Copies « .new » à côté des cibles, revérifiées (une copie ratée ne doit rien remplacer).
    private static void Stage(List<Staged> items)
    {
        foreach (var s in items)
        {
            File.Copy(s.Source, s.Fresh, overwrite: true);
            if (!UpdatePackage.HasHash(s.Fresh, s.Sha256)) throw new IOException($"copie altérée : {Path.GetFileName(s.Fresh)}");
        }
    }

    private static void Swap(List<Staged> items)
    {
        foreach (var s in items)
        {
            if (File.Exists(s.Target)) { File.Move(s.Target, s.Backup, overwrite: true); s.HadOld = true; }
            File.Move(s.Fresh, s.Target);
            s.Placed = true;
        }
    }

    /// Remet chaque cible dans son état d'avant : ancienne version restaurée, fichier nouveau retiré, « .new » effacés.
    private static void Rollback(List<Staged> items, Action<string> log)
    {
        foreach (var s in items)
        {
            try
            {
                if (s.HadOld) File.Move(s.Backup, s.Target, overwrite: true);
                else if (s.Placed) File.Delete(s.Target);
                if (File.Exists(s.Fresh)) File.Delete(s.Fresh);
            }
            catch (Exception e) { log($"retour en arrière incomplet pour {s.Target} : {e.Message}"); }
        }
    }

    /// Paquet refusé : gardé dans update.rejected/ (le précédent est effacé), effacé si le déplacement échoue.
    private static void Quarantine(string dir, string dataDir, Action<string> log)
    {
        string q = Path.Combine(dataDir, UpdateFiles.QuarantineDir);
        try
        {
            if (Directory.Exists(q)) Directory.Delete(q, recursive: true);
            Directory.Move(dir, q);
        }
        catch (Exception e)
        {
            log($"quarantaine impossible ({e.Message}) : paquet effacé");
            TryDelete(dir, log);
        }
    }

    private static void TryDelete(string dir, Action<string> log)
    {
        try { Directory.Delete(dir, recursive: true); }
        catch (Exception e) { log($"impossible d'effacer {dir} : {e.Message}"); }
    }

    private static void TryDeleteFile(string path, Action<string> log)
    {
        try { File.Delete(path); }
        catch (Exception e) { log($"impossible d'effacer {path} : {e.Message}"); }
    }

    private static UpdateOutcome Outcome(string result, string version, string reason) =>
        new() { Result = result, Version = version, Reason = reason };
}
