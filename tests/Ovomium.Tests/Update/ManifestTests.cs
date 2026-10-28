using System.Text;
using Ovomium.Core.Update;

namespace Ovomium.Tests.Update;

/// Manifeste, versions et liste des releases GitHub.
public class ManifestTests
{
    private const string Hash = "2c26b46b68ffc68ff99b453c1d30413413422d706483bfa0f98a5e886266e7ae";

    private static UpdateManifest Parse(string json, out string error) => UpdateManifest.Parse(Encoding.UTF8.GetBytes(json), out error);

    private static string WithFile(string name, string dest = "Mods", string sha = Hash) =>
        $"{{\"version\":\"0.5.0\",\"minUpdater\":1,\"files\":[{{\"name\":\"{name}\",\"dest\":\"{dest}\",\"sha256\":\"{sha}\"}}]}}";

    [Fact]
    public void Manifeste_DuContrat_Lu()
    {
        var m = Parse(CryptoTests.OpenSslManifest, out var error);
        Assert.Null(error);
        Assert.Equal(new ModVersion(0, 5, 0), m.Version);
        Assert.Equal(1, m.MinUpdater);
        var f = Assert.Single(m.Files);
        Assert.Equal(("Ovomium.dll", "Mods", Hash), (f.Name, f.Dest, f.Sha256));
    }

    [Theory]
    [InlineData("../Ovomium.dll")]
    [InlineData("..")]
    [InlineData("a..dll")]
    [InlineData("sub/Ovomium.dll")]
    [InlineData("sub\\\\Ovomium.dll")]
    [InlineData("C:Ovomium.dll")]
    [InlineData(".hidden")]
    [InlineData("Ovomium.dll\\n")]
    [InlineData("")]
    public void Manifeste_NomDangereux_Refuse(string name)
    {
        Assert.Null(Parse(WithFile(name), out var error));
        Assert.Contains("nom de fichier", error);
    }

    [Theory]
    [InlineData("UserLibs")]
    [InlineData("mods")]
    [InlineData("Mods/..")]
    public void Manifeste_DestinationAutreQueMods_Refusee(string dest) => Assert.Null(Parse(WithFile("a.dll", dest), out _));

    [Theory]
    [InlineData("2C26B46B68FFC68FF99B453C1D30413413422D706483BFA0F98A5E886266E7AE")] // majuscules
    [InlineData("2c26b4")]
    public void Manifeste_EmpreinteMalFormee_Refusee(string sha) => Assert.Null(Parse(WithFile("a.dll", sha: sha), out _));

    [Theory]
    [InlineData("{\"version\":\"0.5\",\"minUpdater\":1,\"files\":[]}")]
    [InlineData("{\"version\":\"0.5.0\",\"minUpdater\":0,\"files\":[{\"name\":\"a.dll\",\"dest\":\"Mods\",\"sha256\":\"" + Hash + "\"}]}")]
    [InlineData("{\"version\":\"0.5.0\",\"minUpdater\":1,\"files\":[]}")]
    [InlineData("{\"version\":\"0.5.0\",\"minUpdater\":1,\"files\":[{\"name\":\"a.dll\",\"dest\":\"Mods\",\"sha256\":\"" + Hash + "\"},{\"name\":\"A.DLL\",\"dest\":\"Mods\",\"sha256\":\"" + Hash + "\"}]}")]
    [InlineData("[1,2]")]
    [InlineData("pas du json")]
    public void Manifeste_Invalide_Refuse(string json) => Assert.Null(Parse(json, out _));

    [Fact]
    public void Versions_Comparees()
    {
        Assert.True(ModVersion.TryParse("0.10.0", out var a));
        Assert.True(ModVersion.TryParseTag("v0.9.12", out var b));
        Assert.True(a > b);
        Assert.Equal("0.10.0", a.ToString());
        foreach (var bad in new[] { "0.5", "0.5.0.1", "0.5.0-beta", " 0.5.0", "+1.0.0", "a.b.c", null })
            Assert.False(ModVersion.TryParse(bad, out _));
        Assert.False(ModVersion.TryParseTag("0.5.0", out _));
    }

    private const string Releases = """
    [
      {"tag_name":"v0.7.0","draft":true,"prerelease":false,"body":"brouillon","assets":[]},
      {"tag_name":"v0.6.0","draft":false,"prerelease":true,"body":"préversion","assets":[]},
      {"tag_name":"v0.5.1","draft":false,"prerelease":false,"body":"## Corrections\r\n- **chargement** figé\r\n","assets":[
        {"name":"ovomium-update.json","browser_download_url":"https://example/0.5.1/ovomium-update.json"},
        {"name":"Ovomium.dll","browser_download_url":"https://example/0.5.1/Ovomium.dll"}]},
      {"tag_name":"v0.5.0","draft":false,"prerelease":false,"body":"Mise à jour automatique","assets":[]},
      {"tag_name":"v0.4.0","draft":false,"prerelease":false,"body":"ancienne","assets":[]},
      {"tag_name":"nightly","draft":false,"prerelease":false,"body":"","assets":[]}
    ]
    """;

    [Fact]
    public void Releases_PlusRecentePubliee_Choisie()
    {
        var list = ReleaseFeed.Parse(Releases, out int seen);
        Assert.Equal(3, list.Count); // brouillon, préversion et tag non numéroté ignorés
        Assert.Equal(6, seen);
        var current = new ModVersion(0, 4, 0);
        var target = ReleaseFeed.Newest(list, current);
        Assert.Equal(new ModVersion(0, 5, 1), target.Version);
        Assert.Equal("https://example/0.5.1/Ovomium.dll", target.Assets["Ovomium.dll"]);
        Assert.Null(ReleaseFeed.Newest(list, new ModVersion(0, 5, 1)));
        Assert.Equal("0.5.1 :\n  Corrections\n  · chargement figé\n0.5.0 :\n  Mise à jour automatique",
            ReleaseFeed.Notes(list, current, target.Version));
    }

    [Fact]
    public void Notes_TronqueesEtCoupees()
    {
        var list = new List<ReleaseInfo> { new() { Version = new ModVersion(1, 0, 0), Body = string.Join("\n", Enumerable.Repeat("ligne", 20)) } };
        var notes = ReleaseFeed.Notes(list, new ModVersion(0, 1, 0), new ModVersion(1, 0, 0), maxLines: 4).Split('\n');
        Assert.Equal(new[] { "1.0.0 :", "  ligne", "  ligne", "…" }, notes);
        Assert.Equal(new[] { "aaaa bb", "cccccccc", "cc" }, ReleaseFeed.Wrap("aaaa bb cccccccccc", 8));
        Assert.Equal("", ReleaseFeed.Notes(list, new ModVersion(1, 0, 0), new ModVersion(1, 0, 0)));
    }
}
