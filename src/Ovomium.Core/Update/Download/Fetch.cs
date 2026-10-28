using System;
using System.IO;
using System.Net.Http;
using System.Threading;

namespace Ovomium.Core.Update;

/// Téléchargement synchrone (fil de la mise à jour, jamais le fil principal du jeu) : https://, ou file:// pour
/// les tests. Taille plafonnée. Toute erreur lève une exception, son message va au journal.
/// Pas compilé dans le plugin OvomiumUpdater (aucun accès réseau là-bas).
public sealed class Fetch : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = Timeout.InfiniteTimeSpan }; // délai par requête

    /// userAgent : obligatoire pour l'API GitHub, qui refuse les requêtes sans.
    public Fetch(string userAgent) => _http.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);

    public void Dispose() => _http.Dispose();

    /// accept : en-tête Accept (null = défaut). Redirections suivies (fichiers des releases servis par un autre hôte).
    public byte[] Bytes(string url, long maxBytes, TimeSpan timeout, string accept = null)
    {
        var uri = new Uri(url);
        if (uri.IsFile) return ReadFile(uri.LocalPath, maxBytes);
        if (uri.Scheme != Uri.UriSchemeHttps) throw new InvalidOperationException($"adresse refusée (https ou file seulement) : {url}");
        using var cts = new CancellationTokenSource(timeout);
        using var req = new HttpRequestMessage(HttpMethod.Get, uri);
        if (accept != null) req.Headers.Accept.ParseAdd(accept);
        using var resp = _http.Send(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        if (!resp.IsSuccessStatusCode) throw new HttpRequestException($"HTTP {(int)resp.StatusCode} pour {url}");
        if (resp.Content.Headers.ContentLength > maxBytes) throw new InvalidDataException($"trop gros : {url}");
        using var input = resp.Content.ReadAsStream(cts.Token);
        return ReadCapped(input, maxBytes, url, cts.Token);
    }

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
