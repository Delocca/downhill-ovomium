using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using static Ovomium.Core.Update.JsonRead;

namespace Ovomium.Core.Update;

/// Un fichier de la mise à jour : nom (sans chemin), dossier de destination, empreinte SHA-256 (hex minuscule).
public sealed class UpdateFile
{
    public string Name { get; init; }
    public string Dest { get; init; }
    public string Sha256 { get; init; }
}

/// Manifeste `ovomium-update.json`, à ne lire qu'après vérification de sa signature (UpdatePackage.ReadSigned) :
/// {"version":"0.5.0","minUpdater":1,"files":[{"name":"Ovomium.dll","dest":"Mods","sha256":"…"}]}
public sealed class UpdateManifest
{
    /// Seul dossier de destination accepté pour l'instant (relatif à la racine du jeu).
    public const string ModsDest = "Mods";
    public const int MaxBytes = 64 * 1024;

    // Nom de fichier simple : ni séparateur, ni « .. », ni « : » (flux NTFS, lecteur), ni caractère exotique.
    // \z et non $ : $ accepterait un saut de ligne final.
    private static readonly Regex SafeName = new(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,99}\z");
    private static readonly Regex Hex64 = new(@"^[0-9a-f]{64}\z");

    public ModVersion Version { get; private init; }
    /// Version minimale du plugin de mise à jour (OvomiumUpdater.UpdaterVersion) capable d'installer ce paquet.
    public int MinUpdater { get; private init; }
    public IReadOnlyList<UpdateFile> Files { get; private init; }

    /// Manifeste lu et validé, ou null avec la raison du refus.
    public static UpdateManifest Parse(byte[] json, out string error)
    {
        error = null;
        if (json == null || json.Length == 0 || json.Length > MaxBytes) { error = "manifeste vide ou trop gros"; return null; }
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) { error = "manifeste : objet attendu"; return null; }
            if (!ModVersion.TryParse(Str(root, "version"), out var version)) { error = "manifeste : version illisible"; return null; }
            if (!root.TryGetProperty("minUpdater", out var mu) || mu.ValueKind != JsonValueKind.Number || !mu.TryGetInt32(out int minUpdater) || minUpdater < 1)
            { error = "manifeste : minUpdater illisible"; return null; }
            var files = ParseFiles(root, out error);
            return files == null ? null : new UpdateManifest { Version = version, MinUpdater = minUpdater, Files = files };
        }
        catch (JsonException e) { error = $"manifeste : JSON invalide ({e.Message})"; return null; }
    }

    private static List<UpdateFile> ParseFiles(JsonElement root, out string error)
    {
        error = null;
        if (!root.TryGetProperty("files", out var arr) || arr.ValueKind != JsonValueKind.Array || arr.GetArrayLength() == 0)
        { error = "manifeste : liste de fichiers absente"; return null; }
        var files = new List<UpdateFile>();
        foreach (var e in arr.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.Object) { error = "manifeste : fichier mal formé"; return null; }
            var f = new UpdateFile { Name = Str(e, "name"), Dest = Str(e, "dest"), Sha256 = Str(e, "sha256") };
            error = CheckFile(f);
            if (error != null) return null;
            files.Add(f);
        }
        if (files.Select(f => f.Name.ToLowerInvariant()).Distinct().Count() != files.Count) { error = "manifeste : fichier en double"; return null; }
        return files;
    }

    private static string CheckFile(UpdateFile f)
    {
        if (f.Name == null || !SafeName.IsMatch(f.Name) || f.Name.Contains("..")) return $"manifeste : nom de fichier refusé ({f.Name})";
        if (f.Dest != ModsDest) return $"manifeste : destination refusée ({f.Dest}) pour {f.Name}";
        if (f.Sha256 == null || !Hex64.IsMatch(f.Sha256)) return $"manifeste : empreinte mal formée pour {f.Name}";
        return null;
    }
}
