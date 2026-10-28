using System.Text;
using Ovomium.Core.Update;

namespace Ovomium.Tests.Update;

/// Installation par le plugin : paquet valide installé, sinon Mods/ intact et paquet en quarantaine.
public sealed class InstallerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ovomium-upd-" + Guid.NewGuid().ToString("N"));
    private readonly TestSigner _signer = TestSigner.FromSeed(42);
    private readonly List<string> _log = new();
    private string DataDir => Path.Combine(_root, "UserData", "Ovomium");
    private string ModsDir => Path.Combine(_root, "Mods");
    private string UpdateDir => Path.Combine(DataDir, UpdateFiles.ReadyDir);

    public InstallerTests()
    {
        Directory.CreateDirectory(ModsDir);
        Directory.CreateDirectory(DataDir);
        File.WriteAllText(Path.Combine(ModsDir, "Ovomium.dll"), "ancien mod");
        File.WriteAllText(Path.Combine(ModsDir, "LMD-Camera.dll"), "ancienne caméra");
        UpdateFiles.WriteInstalled(DataDir, new ModVersion(0, 4, 0));
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }

    /// Paquet signé dans update/ : fichiers donnés (nom → contenu), empreintes calculées sauf `badHash`.
    private void Prepare(string version, int minUpdater = 1, string badHash = null, params (string name, string content)[] files)
    {
        Directory.CreateDirectory(UpdateDir);
        var entries = new List<string>();
        foreach (var (name, content) in files)
        {
            var bytes = Encoding.UTF8.GetBytes(content);
            File.WriteAllBytes(Path.Combine(UpdateDir, name), bytes);
            string sha = name == badHash ? new string('0', 64) : Sha2.Sha256Hex(bytes);
            entries.Add($"{{\"name\":\"{name}\",\"dest\":\"Mods\",\"sha256\":\"{sha}\"}}");
        }
        var json = Encoding.UTF8.GetBytes($"{{\"version\":\"{version}\",\"minUpdater\":{minUpdater},\"files\":[{string.Join(",", entries)}]}}");
        File.WriteAllBytes(Path.Combine(UpdateDir, UpdatePackage.ManifestName), json);
        File.WriteAllBytes(Path.Combine(UpdateDir, UpdatePackage.SignatureName), _signer.Sign(json));
    }

    private static readonly (string, string)[] TwoFiles = { ("Ovomium.dll", "nouveau mod"), ("LMD-Camera.dll", "nouvelle caméra") };

    private UpdateOutcome Run(int updaterVersion = 1) => UpdateInstaller.Run(DataDir, ModsDir, updaterVersion, _signer.PublicKey, _log.Add);

    private string Mod(string name) => File.ReadAllText(Path.Combine(ModsDir, name));

    /// Refus : Mods/ intact, paquet en quarantaine, état écrit.
    private void AssertUntouched(UpdateOutcome o, string result, string reasonPart)
    {
        Assert.Equal(result, o.Result);
        Assert.Contains(reasonPart, o.Reason);
        Assert.Equal("ancien mod", Mod("Ovomium.dll"));
        Assert.Equal("ancienne caméra", Mod("LMD-Camera.dll"));
        Assert.Equal(new[] { "LMD-Camera.dll", "Ovomium.dll" }, Directory.GetFiles(ModsDir).Select(Path.GetFileName).Order());
        Assert.False(Directory.Exists(UpdateDir));
        Assert.True(Directory.Exists(Path.Combine(DataDir, UpdateFiles.QuarantineDir)));
        Assert.Equal(result, UpdateFiles.ReadState(DataDir).Result);
        Assert.Equal(new ModVersion(0, 4, 0), UpdateFiles.ReadInstalled(DataDir));
    }

    [Fact]
    public void SansPaquet_RienNestFait()
    {
        Assert.Null(Run());
        Assert.Null(UpdateFiles.ReadState(DataDir));
    }

    [Fact]
    public void PaquetValide_Installe()
    {
        Prepare("0.5.0", files: TwoFiles.Append(("Extra.dll", "nouveau fichier")).ToArray());
        var o = Run();
        Assert.Equal((UpdateOutcome.Installed, "0.5.0"), (o.Result, o.Version));
        Assert.Equal("nouveau mod", Mod("Ovomium.dll"));
        Assert.Equal("nouvelle caméra", Mod("LMD-Camera.dll"));
        Assert.Equal("nouveau fichier", Mod("Extra.dll"));
        Assert.Equal(3, Directory.GetFiles(ModsDir).Length); // ni .new ni .old
        Assert.False(Directory.Exists(UpdateDir));
        Assert.Equal(new ModVersion(0, 5, 0), UpdateFiles.ReadInstalled(DataDir));
        var state = UpdateFiles.ReadState(DataDir);
        Assert.Equal((UpdateOutcome.Installed, "0.5.0"), (state.Result, state.Version));
    }

    [Fact]
    public void SignatureAlteree_Refusee()
    {
        Prepare("0.5.0", files: TwoFiles);
        var path = Path.Combine(UpdateDir, UpdatePackage.ManifestName);
        File.WriteAllText(path, File.ReadAllText(path).Replace("0.5.0", "0.9.0"));
        AssertUntouched(Run(), UpdateOutcome.Rejected, "signature");
    }

    [Fact]
    public void AutreCle_Refusee()
    {
        Prepare("0.5.0", files: TwoFiles);
        var o = UpdateInstaller.Run(DataDir, ModsDir, 1, UpdatePackage.ReleaseKey, _log.Add);
        AssertUntouched(o, UpdateOutcome.Rejected, "signature");
    }

    [Fact]
    public void EmpreinteFausse_Refusee()
    {
        Prepare("0.5.0", badHash: "LMD-Camera.dll", files: TwoFiles);
        AssertUntouched(Run(), UpdateOutcome.Rejected, "empreinte fausse pour LMD-Camera.dll");
    }

    [Fact]
    public void FichierModifieApresSignature_Refuse()
    {
        Prepare("0.5.0", files: TwoFiles);
        File.WriteAllText(Path.Combine(UpdateDir, "Ovomium.dll"), "mod piégé");
        AssertUntouched(Run(), UpdateOutcome.Rejected, "empreinte fausse pour Ovomium.dll");
    }

    [Theory]
    [InlineData("0.4.0")]
    [InlineData("0.3.9")]
    public void RetourArriere_Refuse(string version)
    {
        Prepare(version, files: TwoFiles);
        AssertUntouched(Run(), UpdateOutcome.Rejected, "pas plus récente");
    }

    [Fact]
    public void MinUpdaterTropGrand_Refuse()
    {
        Prepare("0.5.0", minUpdater: 2, files: TwoFiles);
        AssertUntouched(Run(updaterVersion: 1), UpdateOutcome.Rejected, "plugin de mise à jour trop ancien");
    }

    [Fact]
    public void VersionInstalleeInconnue_Refusee()
    {
        File.Delete(Path.Combine(DataDir, UpdateFiles.InstalledFile));
        Prepare("0.5.0", files: TwoFiles);
        var o = Run();
        Assert.Equal(UpdateOutcome.Rejected, o.Result);
        Assert.Equal("ancien mod", Mod("Ovomium.dll"));
    }

    [Fact]
    public void VersionLueDansLaDll()
    {
        string asm = typeof(InstallerTests).Assembly.Location; // MelonInfo « Ovomium » 0.7.1 (MelonInfoStub)
        Assert.Equal(new ModVersion(0, 7, 1), InstalledVersion.FromMelonDll(asm));
        Assert.Null(InstalledVersion.FromMelonDll(Path.Combine(ModsDir, "Ovomium.dll"))); // pas une DLL
        Assert.Null(InstalledVersion.FromMelonDll(typeof(UpdatePackage).Assembly.Location)); // pas de MelonInfo
    }

    [Fact]
    public void InstalledJsonPerime_LaDllFaitFoi()
    {
        // Un installeur 0.7.1 est passé après la préparation d'un paquet 0.5.0 : installed.json dit encore 0.4.0.
        File.Copy(typeof(InstallerTests).Assembly.Location, Path.Combine(ModsDir, "Ovomium.dll"), overwrite: true);
        Prepare("0.5.0", files: ("Ovomium.dll", "mod 0.5.0"));
        var o = Run();
        Assert.Equal(UpdateOutcome.Rejected, o.Result);
        Assert.Contains("pas plus récente que 0.7.1", o.Reason);
        Assert.Equal(new ModVersion(0, 7, 1), InstalledVersion.FromMelonDll(Path.Combine(ModsDir, "Ovomium.dll")));
    }

    [Fact]
    public void FichierManquant_Refuse()
    {
        Prepare("0.5.0", files: TwoFiles);
        File.Delete(Path.Combine(UpdateDir, "LMD-Camera.dll"));
        AssertUntouched(Run(), UpdateOutcome.Rejected, "LMD-Camera.dll absent");
    }

    [Fact]
    public void RemplacementImpossible_ToutEstRemisEnPlace()
    {
        // Ordre du manifeste : Ovomium.dll remplacé d'abord, puis LMD-Camera.dll échoue (un dossier occupe
        // la place de sa sauvegarde) : Ovomium.dll doit retrouver son ancienne version.
        Prepare("0.5.0", files: TwoFiles);
        Directory.CreateDirectory(Path.Combine(ModsDir, "LMD-Camera.dll.old"));
        var o = Run();
        Assert.Equal(UpdateOutcome.Failed, o.Result);
        Assert.Equal("ancien mod", Mod("Ovomium.dll"));
        Assert.Equal("ancienne caméra", Mod("LMD-Camera.dll"));
        Assert.Equal(new[] { "LMD-Camera.dll", "Ovomium.dll" }, Directory.GetFiles(ModsDir).Select(Path.GetFileName).Order());
        Assert.False(Directory.Exists(UpdateDir));
        Assert.Equal(new ModVersion(0, 4, 0), UpdateFiles.ReadInstalled(DataDir));
    }

    [Fact]
    public void QuarantainePrecedente_Remplacee()
    {
        Directory.CreateDirectory(Path.Combine(DataDir, UpdateFiles.QuarantineDir, "vieux"));
        Prepare("0.4.0", files: TwoFiles);
        AssertUntouched(Run(), UpdateOutcome.Rejected, "pas plus récente");
        Assert.False(Directory.Exists(Path.Combine(DataDir, UpdateFiles.QuarantineDir, "vieux")));
    }
}
