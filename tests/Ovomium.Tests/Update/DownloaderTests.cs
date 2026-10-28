using System.Text;
using Ovomium.Core.Update;

namespace Ovomium.Tests.Update;

/// Recherche et téléchargement par le mod, avec une « API GitHub » en file:// ; puis installation par le plugin.
public sealed class DownloaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ovomium-dl-" + Guid.NewGuid().ToString("N"));
    private readonly TestSigner _signer = TestSigner.FromSeed(9);
    private static readonly ModVersion Current = new(0, 4, 0);
    private string DataDir => Path.Combine(_root, "UserData", "Ovomium");
    private string ModsDir => Path.Combine(_root, "Mods");
    private string ApiFile => Path.Combine(_root, "releases.json");

    public DownloaderTests()
    {
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(ModsDir);
        File.WriteAllText(Path.Combine(ModsDir, "Ovomium.dll"), "ancien mod");
        UpdateFiles.WriteInstalled(DataDir, Current);
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }

    /// Release vX.Y.Z publiée : ses fichiers dans un dossier, listés (file://) dans releases.json avec une
    /// version plus ancienne et un brouillon. `tamper` modifie un fichier après signature.
    private void Publish(string version, int minUpdater = 1, Action<string> tamper = null)
    {
        string dir = Path.Combine(_root, "rel-" + version);
        Directory.CreateDirectory(dir);
        var dll = Encoding.UTF8.GetBytes("mod " + version);
        File.WriteAllBytes(Path.Combine(dir, "Ovomium.dll"), dll);
        var json = Encoding.UTF8.GetBytes($"{{\"version\":\"{version}\",\"minUpdater\":{minUpdater},\"files\":[{{\"name\":\"Ovomium.dll\",\"dest\":\"Mods\",\"sha256\":\"{Sha2.Sha256Hex(dll)}\"}}]}}");
        File.WriteAllBytes(Path.Combine(dir, UpdatePackage.ManifestName), json);
        File.WriteAllBytes(Path.Combine(dir, UpdatePackage.SignatureName), _signer.Sign(json));
        tamper?.Invoke(dir);
        string Asset(string name) => $"{{\"name\":\"{name}\",\"browser_download_url\":\"{new Uri(Path.Combine(dir, name)).AbsoluteUri}\"}}";
        string assets = string.Join(",", new[] { UpdatePackage.ManifestName, UpdatePackage.SignatureName, "Ovomium.dll" }.Select(Asset));
        File.WriteAllText(ApiFile, $$"""
            [{"tag_name":"v9.0.0","draft":true,"prerelease":false,"body":"brouillon","assets":[]},
             {"tag_name":"v{{version}}","draft":false,"prerelease":false,"body":"- nouveautés {{version}}","assets":[{{assets}}]},
             {"tag_name":"v0.4.0","draft":false,"prerelease":false,"body":"actuelle","assets":[]}]
            """);
    }

    private List<UpdateDownloader.Message> Check(int? plugin = 1, string api = null)
    {
        var d = new UpdateDownloader(new UpdateDownloader.Options(api ?? new Uri(ApiFile).AbsoluteUri, DataDir, Current, plugin, _signer.PublicKey, "Ovomium-tests"));
        d.Run();
        Assert.False(Directory.Exists(Path.Combine(DataDir, UpdateFiles.DownloadDir)));
        return d.Outbox.ToList();
    }

    private static UpdateDownloader.Kind[] Kinds(List<UpdateDownloader.Message> m) => m.Select(x => x.Kind).ToArray();

    [Fact]
    public void NouvelleVersion_TelechargeePuisInstallee()
    {
        Publish("0.5.0");
        var msgs = Check();
        var ready = Assert.Single(msgs, m => m.Kind == UpdateDownloader.Kind.Ready);
        Assert.Equal("0.5.0", ready.Text);
        Assert.Equal("0.5.0 :\n  · nouveautés 0.5.0", ready.Notes);
        Assert.Equal(new ModVersion(0, 5, 0), UpdateFiles.ReadyVersion(DataDir));

        var o = UpdateInstaller.Run(DataDir, ModsDir, 1, _signer.PublicKey, _ => { });
        Assert.Equal(UpdateOutcome.Installed, o.Result);
        Assert.Equal("mod 0.5.0", File.ReadAllText(Path.Combine(ModsDir, "Ovomium.dll")));
        Assert.Null(UpdateFiles.ReadyVersion(DataDir));
    }

    [Fact]
    public void DejaPrete_PasRetelechargee()
    {
        Publish("0.5.0");
        Check();
        File.Delete(Path.Combine(_root, "rel-0.5.0", "Ovomium.dll")); // un nouveau téléchargement échouerait
        Assert.Equal(new[] { UpdateDownloader.Kind.Log }, Kinds(Check()));
        Assert.Equal(new ModVersion(0, 5, 0), UpdateFiles.ReadyVersion(DataDir));
    }

    [Fact]
    public void ApiInaccessible_SimpleJournal()
    {
        Assert.Equal(new[] { UpdateDownloader.Kind.Log }, Kinds(Check(api: new Uri(Path.Combine(_root, "absent.json")).AbsoluteUri)));
        Assert.Equal(new[] { UpdateDownloader.Kind.Log }, Kinds(Check(api: "http://example.invalid/releases")));
        Assert.Null(UpdateFiles.ReadyVersion(DataDir));
    }

    [Fact]
    public void PluginAbsent_RienPrepare()
    {
        Publish("0.5.0");
        var m = Assert.Single(Check(plugin: null));
        Assert.Equal(UpdateDownloader.Kind.NeedInstaller, m.Kind);
        Assert.Contains("relance l'installeur", m.Text);
        Assert.Null(UpdateFiles.ReadyVersion(DataDir));
    }

    [Fact]
    public void MinUpdaterTropGrand_DemandeLInstalleur()
    {
        Publish("0.5.0", minUpdater: 2);
        var m = Assert.Single(Check(plugin: 1));
        Assert.Equal(UpdateDownloader.Kind.NeedInstaller, m.Kind);
        Assert.Contains("relance l'installeur d'Ovomium pour cette version", m.Text);
        Assert.Null(UpdateFiles.ReadyVersion(DataDir));
    }

    [Fact]
    public void SignatureAlteree_Refusee()
    {
        Publish("0.5.0", tamper: dir => File.AppendAllText(Path.Combine(dir, UpdatePackage.ManifestName), " "));
        var m = Assert.Single(Check());
        Assert.Equal(UpdateDownloader.Kind.Warn, m.Kind);
        Assert.Contains("signature invalide", m.Text);
        Assert.Null(UpdateFiles.ReadyVersion(DataDir));
    }

    [Fact]
    public void EmpreinteFausse_Refusee()
    {
        Publish("0.5.0", tamper: dir => File.WriteAllText(Path.Combine(dir, "Ovomium.dll"), "piégé"));
        var m = Assert.Single(Check());
        Assert.Contains("empreinte fausse", m.Text);
        Assert.Null(UpdateFiles.ReadyVersion(DataDir));
    }

    [Fact]
    public void RienDePlusRecent_RienAFaire()
    {
        File.WriteAllText(ApiFile, """[{"tag_name":"v0.4.0","draft":false,"prerelease":false,"body":"","assets":[]}]""");
        Assert.Equal(new[] { UpdateDownloader.Kind.Log }, Kinds(Check()));
    }
}
