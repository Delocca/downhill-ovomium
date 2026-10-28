using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using static Ovomium.Core.Update.JsonRead;

namespace Ovomium.Core.Update;

/// Une release publiée : version (tag vX.Y.Z), notes, fichiers joints (nom → URL de téléchargement).
public sealed class ReleaseInfo
{
    public ModVersion Version { get; init; }
    public string Body { get; init; }
    public IReadOnlyDictionary<string, string> Assets { get; init; }
}

/// Liste des releases de l'API GitHub (GET /repos/…/releases) : choix de la version à télécharger, notes cumulées.
public static class ReleaseFeed
{
    /// Releases publiées dont le tag est une version « vX.Y.Z » ; brouillons et préversions ignorés.
    public static List<ReleaseInfo> Parse(string json) => Parse(json, out _);

    /// Idem ; `seen` : nombre d'entrées de la liste, retenues ou non (journal : la requête a bien rapporté quelque chose).
    public static List<ReleaseInfo> Parse(string json, out int seen)
    {
        using var doc = JsonDocument.Parse(json);
        var list = new List<ReleaseInfo>();
        seen = 0;
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return list;
        foreach (var r in doc.RootElement.EnumerateArray())
        {
            seen++;
            if (r.ValueKind != JsonValueKind.Object || Flag(r, "draft") || Flag(r, "prerelease")) continue;
            if (!ModVersion.TryParseTag(Str(r, "tag_name"), out var v)) continue;
            list.Add(new ReleaseInfo { Version = v, Body = Str(r, "body") ?? "", Assets = AssetsOf(r) });
        }
        return list;
    }

    /// Release la plus récente, si elle est plus récente que `current` ; sinon null.
    public static ReleaseInfo Newest(IEnumerable<ReleaseInfo> releases, ModVersion current) =>
        releases.Where(r => r.Version > current).OrderByDescending(r => r.Version).FirstOrDefault();

    /// Notes des releases manquées (plus récentes que `current`, jusqu'à `upTo`), de la plus récente à la plus
    /// ancienne, en texte simple : une ligne d'en-tête par version, Markdown léger retiré, lignes coupées à `width`
    /// caractères, `maxLines` lignes au plus (puis « … »). Vide s'il n'y a rien à dire.
    public static string Notes(IEnumerable<ReleaseInfo> releases, ModVersion current, ModVersion upTo, int maxLines = 8, int width = 90)
    {
        var lines = new List<string>();
        foreach (var r in releases.Where(r => r.Version > current && !(r.Version > upTo)).OrderByDescending(r => r.Version))
        {
            var body = BodyLines(r.Body, width).ToList();
            if (body.Count == 0) continue;
            lines.Add($"{r.Version} :");
            lines.AddRange(body);
        }
        if (lines.Count > maxLines) lines = lines.Take(maxLines - 1).Append("…").ToList();
        return string.Join("\n", lines);
    }

    private static IEnumerable<string> BodyLines(string body, int width)
    {
        foreach (var raw in (body ?? "").Replace("\r", "").Split('\n'))
        {
            string line = raw.Trim().TrimStart('#').Trim().Replace("**", "").Replace("`", "");
            if (line.StartsWith("- ") || line.StartsWith("* ")) line = "· " + line[2..].Trim();
            if (line.Length == 0) continue;
            foreach (var part in Wrap(line, width)) yield return "  " + part;
        }
    }

    /// Coupe aux espaces ; un mot plus long que la largeur est coupé net.
    internal static IEnumerable<string> Wrap(string line, int width)
    {
        var sb = new StringBuilder();
        foreach (var word in line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            for (string w = word; w.Length > 0;)
            {
                if (sb.Length > 0 && sb.Length + 1 + w.Length > width) { yield return sb.ToString(); sb.Clear(); }
                string piece = w.Length > width ? w[..width] : w;
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(piece);
                w = w[piece.Length..];
            }
        }
        if (sb.Length > 0) yield return sb.ToString();
    }

    private static Dictionary<string, string> AssetsOf(JsonElement r)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!r.TryGetProperty("assets", out var arr) || arr.ValueKind != JsonValueKind.Array) return d;
        foreach (var a in arr.EnumerateArray())
        {
            string name = a.ValueKind == JsonValueKind.Object ? Str(a, "name") : null, url = name != null ? Str(a, "browser_download_url") : null;
            if (url != null) d[name] = url;
        }
        return d;
    }
}
