using System;
using System.IO;
using System.Text.Json;
using static Ovomium.Core.Update.JsonRead;

namespace Ovomium.Core.Update;

/// Résultat d'une tentative d'installation, laissé au mod dans update-state.json.
public sealed class UpdateOutcome
{
    public const string Installed = "installed", Rejected = "rejected", Failed = "failed";

    public string Result { get; init; }
    public string Version { get; init; }
    public string Reason { get; init; }
}

/// Fichiers d'échange dans <jeu>/UserData/Ovomium/ entre le mod et le plugin OvomiumUpdater.
public static class UpdateFiles
{
    /// Paquet prêt à installer (vérifié par le mod, revérifié par le plugin).
    public const string ReadyDir = "update";
    /// Téléchargement en cours (renommé d'un coup en update/ une fois tout vérifié).
    public const string DownloadDir = "update.tmp";
    /// Dernier paquet refusé ou en échec, gardé pour examen (remplace le précédent).
    public const string QuarantineDir = "update.rejected";
    public const string StateFile = "update-state.json";
    /// Version installée, écrite par le mod à chaque démarrage et par le plugin après installation ; ne sert que si
    /// Mods/Ovomium.dll est illisible (InstalledVersion lit d'abord la DLL elle-même).
    public const string InstalledFile = "installed.json";
    /// Plus haute version refusée (par le mod ou par le plugin) : le mod ne la retélécharge pas, ni rien en dessous,
    /// tant qu'aucune version plus récente n'est publiée.
    public const string SkipFile = "update-skip.json";

    /// Version du paquet prêt dans update/ (vérifié au téléchargement, revérifié par le plugin), ou null.
    public static ModVersion? ReadyVersion(string dataDir)
    {
        string dir = Path.Combine(dataDir, ReadyDir);
        if (!UpdatePackage.HasManifest(dir)) return null;
        try { return UpdateManifest.Parse(File.ReadAllBytes(Path.Combine(dir, UpdatePackage.ManifestName)), out _)?.Version; }
        catch (IOException) { return null; }
    }

    public static ModVersion? ReadInstalled(string dataDir) => ReadVersion(Path.Combine(dataDir, InstalledFile));

    public static void WriteInstalled(string dataDir, ModVersion v) => WriteVersion(Path.Combine(dataDir, InstalledFile), v);

    public static ModVersion? ReadSkipped(string dataDir) => ReadVersion(Path.Combine(dataDir, SkipFile));

    /// Note `v` comme refusée (garde la plus haute des versions refusées).
    public static void Skip(string dataDir, ModVersion v)
    {
        var old = ReadSkipped(dataDir);
        if (old == null || v > old.Value) WriteVersion(Path.Combine(dataDir, SkipFile), v);
    }

    private static ModVersion? ReadVersion(string path)
    {
        var doc = ReadJson(path);
        if (doc == null) return null;
        using (doc)
            return ModVersion.TryParse(Str(doc.RootElement, "version"), out var v) ? v : null;
    }

    private static void WriteVersion(string path, ModVersion v) => WriteJson(path, w => w.WriteString("version", v.ToString()));

    /// Résultat laissé par le plugin, effacé à la lecture : chaque résultat n'est annoncé qu'une fois. null si aucun
    /// (ou s'il n'a pas pu être effacé : mieux vaut ne rien annoncer que l'annoncer à chaque lancement).
    public static UpdateOutcome TakeState(string dataDir)
    {
        var s = ReadState(dataDir);
        if (s == null) return null;
        try { ClearState(dataDir); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
        return s;
    }

    public static UpdateOutcome ReadState(string dataDir)
    {
        var doc = ReadJson(Path.Combine(dataDir, StateFile));
        if (doc == null) return null;
        using (doc)
        {
            var r = doc.RootElement;
            return new UpdateOutcome { Result = Str(r, "result"), Version = Str(r, "version"), Reason = Str(r, "reason") };
        }
    }

    public static void WriteState(string dataDir, UpdateOutcome o) =>
        WriteJson(Path.Combine(dataDir, StateFile), w =>
        {
            w.WriteString("result", o.Result);
            w.WriteString("version", o.Version ?? "");
            w.WriteString("reason", o.Reason ?? "");
        });

    public static void ClearState(string dataDir) => File.Delete(Path.Combine(dataDir, StateFile));

    /// Écriture d'un objet JSON par fichier temporaire puis remplacement : jamais de fichier tronqué.
    private static void WriteJson(string path, Action<Utf8JsonWriter> body)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string tmp = path + ".tmp";
        using (var fs = File.Create(tmp))
        using (var w = new Utf8JsonWriter(fs))
        {
            w.WriteStartObject();
            body(w);
            w.WriteEndObject();
        }
        File.Move(tmp, path, overwrite: true);
    }

    /// Objet JSON du fichier, ou null s'il est absent ou illisible.
    private static JsonDocument ReadJson(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var doc = JsonDocument.Parse(File.ReadAllBytes(path));
            if (doc.RootElement.ValueKind == JsonValueKind.Object) return doc;
            doc.Dispose();
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) { }
        return null;
    }
}
