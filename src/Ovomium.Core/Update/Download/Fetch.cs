using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;

namespace Ovomium.Core.Update;

/// D'où une adresse peut venir. Feed : liste des releases (réglage UpdateApiUrl : https, ou file:// pour les tests).
/// GitHubAsset : fichier d'une release GitHub, https sur un hôte GitHub seulement, redirections comprises.
/// LocalAsset : fichier d'une liste de releases elle-même en file:// (tests) : file:// local accepté.
public enum FetchSource { Feed, GitHubAsset, LocalAsset }

/// Téléchargement synchrone (fil de la mise à jour, jamais le fil principal du jeu). Taille plafonnée, redirections
/// suivies à la main pour vérifier chaque étape. Toute erreur lève une exception, son message va au journal.
/// Pas compilé dans le plugin OvomiumUpdater (aucun accès réseau là-bas).
public sealed class Fetch : IDisposable
{
    private const int MaxRedirects = 5;
    private readonly HttpClient _http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan }; // délai par requête

    /// userAgent : obligatoire pour l'API GitHub, qui refuse les requêtes sans.
    public Fetch(string userAgent) => _http.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);

    public void Dispose() => _http.Dispose();

    /// Hôtes des fichiers de release GitHub : github.com/…/releases/download/… répond 302 vers
    /// release-assets.githubusercontent.com (mesuré le 2026-10-28 ; objects.githubusercontent.com auparavant).
    public static bool IsGitHubHost(string host)
    {
        host = host.ToLowerInvariant();
        return host is "github.com" or "api.github.com" || host.EndsWith(".githubusercontent.com", StringComparison.Ordinal);
    }

    /// Adresse refusée pour cette source : raison, sinon null. Jamais de file:// vers un autre poste (\\hôte\partage :
    /// Windows y enverrait l'identité NTLM du joueur).
    public static string Refusal(Uri uri, FetchSource source)
    {
        if (uri.IsFile)
            return source == FetchSource.GitHubAsset ? "file:// refusé pour un fichier de release"
                : uri.IsUnc || !string.IsNullOrEmpty(uri.Host) ? "file:// vers un autre poste refusé" : null;
        if (uri.Scheme != Uri.UriSchemeHttps) return "https seulement";
        if (source == FetchSource.LocalAsset) return "liste de releases locale : fichiers locaux seulement";
        if (source == FetchSource.GitHubAsset && !IsGitHubHost(uri.IdnHost)) return $"hôte {uri.IdnHost} hors de GitHub";
        return null;
    }

    /// accept : en-tête Accept (null = défaut).
    public byte[] Bytes(string url, long maxBytes, TimeSpan timeout, FetchSource source, string accept = null)
    {
        var uri = new Uri(url);
        string refusal = Refusal(uri, source);
        if (refusal != null) throw new InvalidOperationException($"adresse refusée ({refusal}) : {url}");
        if (uri.IsFile) return ReadFile(uri.LocalPath, maxBytes);
        using var cts = new CancellationTokenSource(timeout);
        for (int hop = 0; ; hop++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, uri);
            if (accept != null) req.Headers.Accept.ParseAdd(accept);
            using var resp = _http.Send(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            if (IsRedirect(resp.StatusCode) && resp.Headers.Location != null)
            {
                if (hop >= MaxRedirects) throw new HttpRequestException($"trop de redirections pour {url}");
                uri = new Uri(uri, resp.Headers.Location);
                refusal = Refusal(uri, source);
                if (refusal != null) throw new InvalidOperationException($"redirection refusée ({refusal}) : {uri.GetLeftPart(UriPartial.Path)}");
                continue;
            }
            if (!resp.IsSuccessStatusCode) throw new HttpRequestException($"HTTP {(int)resp.StatusCode} pour {url}");
            if (resp.Content.Headers.ContentLength > maxBytes) throw new InvalidDataException($"trop gros : {url}");
            using var input = resp.Content.ReadAsStream(cts.Token);
            return ReadCapped(input, maxBytes, url, cts.Token);
        }
    }

    private static bool IsRedirect(HttpStatusCode c) => (int)c is 301 or 302 or 303 or 307 or 308;

    private static byte[] ReadFile(string path, long maxBytes)
    {
        if (new FileInfo(path).Length > maxBytes) throw new InvalidDataException($"trop gros : {path}");
        return File.ReadAllBytes(path);
    }

    /// Lecture plafonnée (la taille annoncée peut manquer ou mentir), dans le délai de la requête.
    private static byte[] ReadCapped(Stream input, long maxBytes, string url, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        var buf = new byte[81920];
        int n;
        while ((n = input.ReadAsync(buf.AsMemory(), ct).AsTask().GetAwaiter().GetResult()) > 0)
        {
            if (ms.Length + n > maxBytes) throw new InvalidDataException($"trop gros : {url}");
            ms.Write(buf, 0, n);
        }
        return ms.ToArray();
    }
}
