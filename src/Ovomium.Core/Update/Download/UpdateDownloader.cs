using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text;

namespace Ovomium.Core.Update;

/// Recherche et téléchargement d'une nouvelle version par le mod, sur son propre fil (une fois par lancement).
/// Ne touche ni à Unity ni au journal : tout ce qu'il a à dire part dans `Outbox`, vidée par le fil principal.
/// Le paquet est préparé dans update.tmp/, entièrement vérifié (même code que le plugin), puis renommé d'un
/// coup en update/ : le plugin ne voit jamais un paquet à moitié téléchargé.
/// Pas compilé dans le plugin OvomiumUpdater (aucun accès réseau là-bas).
public sealed class UpdateDownloader
{
    /// Réglages d'une recherche : liste des releases (API GitHub ou file://), UserData/Ovomium, version du mod,
    /// version du plugin (null = absent), clé de signature, User-Agent.
    public sealed record Options(string ApiUrl, string DataDir, ModVersion Current, int? PluginVersion, byte[] PublicKey, string UserAgent);

    public enum Kind { Log, Warn, Ready, NeedInstaller }

    /// Message pour le fil principal ; Ready : Text = version, Notes = notes des releases manquées.
    public sealed record Message(Kind Kind, string Text, string Notes = null);

    private const long MaxApiBytes = 4 << 20, MaxFileBytes = 64 << 20;
    private static readonly TimeSpan ApiTimeout = TimeSpan.FromSeconds(20), FileTimeout = TimeSpan.FromSeconds(120);

    public readonly ConcurrentQueue<Message> Outbox = new();
    private readonly string _apiUrl, _dataDir;
    private readonly ModVersion _current;
    private readonly int? _pluginVersion;
    private readonly byte[] _key;
    private readonly Fetch _fetch;

    public UpdateDownloader(Options o)
    {
        _apiUrl = o.ApiUrl;
        _dataDir = o.DataDir;
        _current = o.Current;
        _pluginVersion = o.PluginVersion;
        _key = o.PublicKey;
        _fetch = new Fetch(o.UserAgent);
    }

    /// Toute la recherche, d'un bloc (bloquant : à lancer sur un fil à part dans le jeu).
    public void Run()
    {
        try { CheckAndPrepare(); }
        catch (Exception e) { Say(Kind.Warn, $"mise à jour : {e.GetType().Name} : {e.Message}"); }
        finally
        {
            TryDeleteDir(Path.Combine(_dataDir, UpdateFiles.DownloadDir));
            _fetch.Dispose();
        }
    }

    private void Say(Kind kind, string text, string notes = null) => Outbox.Enqueue(new Message(kind, text, notes));

    private void CheckAndPrepare()
    {
        string json;
        // Dépôt privé la plupart du temps : 404 (ou pas de réseau) = rien à faire, on réessaiera au prochain lancement.
        try { json = Encoding.UTF8.GetString(_fetch.Bytes(_apiUrl, MaxApiBytes, ApiTimeout, "application/vnd.github+json")); }
        catch (Exception e) { Say(Kind.Log, $"mise à jour : releases inaccessibles ({e.Message})"); return; }
        var releases = ReleaseFeed.Parse(json);
        var target = ReleaseFeed.Newest(releases, _current);
        if (target == null) { Say(Kind.Log, $"mise à jour : {_current} est la dernière version"); return; }
        string v = target.Version.ToString();
        if (_pluginVersion == null)
        {
            Say(Kind.NeedInstaller, $"Ovomium {v} est sorti : relance l'installeur d'Ovomium pour l'avoir (mise à jour automatique absente de cette installation)");
            return;
        }
        if (UpdateFiles.ReadyVersion(_dataDir) == target.Version) { Say(Kind.Log, $"mise à jour : {v} déjà prête, en attente d'un redémarrage"); return; }
        if (Prepare(target)) Say(Kind.Ready, v, ReleaseFeed.Notes(releases, _current, target.Version));
    }

    /// Télécharge et vérifie la release dans update.tmp/, puis la met en place dans update/. Faux si refusée.
    private bool Prepare(ReleaseInfo r)
    {
        string tmp = Path.Combine(_dataDir, UpdateFiles.DownloadDir), v = r.Version.ToString();
        TryDeleteDir(tmp);
        Directory.CreateDirectory(tmp);
        if (!Download(r, UpdatePackage.ManifestName, tmp, 64 * 1024) || !Download(r, UpdatePackage.SignatureName, tmp, 64)) return false;
        var m = UpdatePackage.ReadSigned(tmp, _key, out string reason);
        if (m == null) { Say(Kind.Warn, $"mise à jour {v} refusée : {reason}"); return false; }
        if (m.Version != r.Version) { Say(Kind.Warn, $"mise à jour {v} refusée : le manifeste annonce {m.Version}"); return false; }
        if (!UpdatePackage.CheckVersions(m, _current, _pluginVersion.Value, out reason))
        {
            if (m.MinUpdater > _pluginVersion.Value) Say(Kind.NeedInstaller, $"Ovomium {v} est sorti : relance l'installeur d'Ovomium pour cette version");
            else Say(Kind.Warn, $"mise à jour {v} refusée : {reason}");
            return false;
        }
        if (m.Files.Any(f => !Download(r, f.Name, tmp, MaxFileBytes))) return false;
        if (!UpdatePackage.CheckFiles(tmp, m, out reason)) { Say(Kind.Warn, $"mise à jour {v} refusée : {reason}"); return false; }
        string ready = Path.Combine(_dataDir, UpdateFiles.ReadyDir);
        TryDeleteDir(ready); // paquet plus ancien jamais installé
        Directory.Move(tmp, ready);
        Say(Kind.Log, $"mise à jour : {v} prête dans {ready}");
        return true;
    }

    private bool Download(ReleaseInfo r, string name, string dir, long maxBytes)
    {
        if (!r.Assets.TryGetValue(name, out var url)) { Say(Kind.Warn, $"mise à jour {r.Version} : fichier {name} absent de la release"); return false; }
        File.WriteAllBytes(Path.Combine(dir, name), _fetch.Bytes(url, maxBytes, FileTimeout));
        return true;
    }

    private static void TryDeleteDir(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
