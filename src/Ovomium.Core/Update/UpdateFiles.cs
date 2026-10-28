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

    /// Version du paquet prêt dans update/ (vérifié au téléchargement, revérifié par le plugin), ou null.
    public static ModVersion? ReadyVersion(string dataDir)
    {
        string dir = Path.Combine(dataDir, ReadyDir);
        if (!UpdatePackage.HasManifest(dir)) return null;
        try { return UpdateManifest.Parse(File.ReadAllBytes(Path.Combine(dir, UpdatePackage.ManifestName)), out _)?.Version; }
        catch (IOException) { return null; }
    }

    public static ModVersion? ReadInstalled(string dataDir)
    {
        var doc = ReadJson(Path.Combine(dataDir, InstalledFile));
        if (doc == null) return null;
        using (doc)
            return ModVersion.TryParse(Str(doc.RootElement, "version"), out var v) ? v : null;
    }

    public static void WriteInstalled(string dataDir, ModVersion v) =>
        WriteJson(Path.Combine(dataDir, InstalledFile), w => w.WriteString("version", v.ToString()));

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
