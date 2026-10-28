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
    /// Ovomium.dll : fausse DLL de MelonInfo `dllVersion` (par défaut celle du paquet), étiquetée par le contenu.
    private void Prepare(string version, int minUpdater = 1, string badHash = null, string dllVersion = null, params (string name, string content)[] files)
    {
        Directory.CreateDirectory(UpdateDir);
        var entries = new List<string>();
        foreach (var (name, content) in files)
        {
            var bytes = PackageBytes(name, content, dllVersion ?? version);
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

    private static byte[] PackageBytes(string name, string content, string version) =>
        name == "Ovomium.dll" ? FakeMelonDll.Bytes(version, content) : Encoding.UTF8.GetBytes(content);

    private string Mod(string name) => FakeMelonDll.Label(Path.Combine(ModsDir, name));

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

    [Fact]
    public void RetourArriere_Refuse()
    {
        Prepare("0.3.9", files: TwoFiles);
        AssertUntouched(Run(), UpdateOutcome.Rejected, "pas plus récente");
    }

    /// Mods/ en 0.4.0 « authentique » : mêmes fichiers que le paquet 0.4.0 de `camera`.
    private void Install040(string camera)
    {
        File.WriteAllBytes(Path.Combine(ModsDir, "Ovomium.dll"), FakeMelonDll.Bytes("0.4.0", "mod 0.4.0"));
        File.WriteAllText(Path.Combine(ModsDir, "LMD-Camera.dll"), camera);
    }

    [Fact]
    public void MemeVersion_FichiersIdentiques_Refusee()
    {
        Install040("caméra 0.4.0");
        Prepare("0.4.0", files: new[] { ("Ovomium.dll", "mod 0.4.0"), ("LMD-Camera.dll", "caméra 0.4.0") });
        var o = Run();
        Assert.Equal(UpdateOutcome.Rejected, o.Result);
        Assert.Contains("pas plus récente", o.Reason);
    }

    [Fact]
    public void MemeVersion_FichierDifferent_Reparee()
    {
        Install040("caméra abîmée");
        Prepare("0.4.0", files: new[] { ("Ovomium.dll", "mod 0.4.0"), ("LMD-Camera.dll", "caméra 0.4.0") });
        var o = Run();
        Assert.Equal((UpdateOutcome.Installed, "0.4.0", "réparation"), (o.Result, o.Version, o.Reason));
        Assert.Equal("caméra 0.4.0", Mod("LMD-Camera.dll"));
    }

    [Fact]
    public void ArretPendantLeRemplacement_AncienRemisPuisReinstalle()
    {
        // Arrêt brutal après « Ovomium.dll → .old, .new → Ovomium.dll » et « LMD-Camera.dll → .old », avant que la
        // nouvelle caméra soit en place. Le paquet est encore dans update/.
        Prepare("0.5.0", files: TwoFiles);
        File.Move(Path.Combine(ModsDir, "Ovomium.dll"), Path.Combine(ModsDir, "Ovomium.dll.old"));
        File.Copy(Path.Combine(UpdateDir, "Ovomium.dll"), Path.Combine(ModsDir, "Ovomium.dll"));
        File.Move(Path.Combine(ModsDir, "LMD-Camera.dll"), Path.Combine(ModsDir, "LMD-Camera.dll.old"));
        var o = Run(); // caméra remise, puis réparation : la DLL se dit déjà 0.5.0
        Assert.Equal((UpdateOutcome.Installed, "réparation"), (o.Result, o.Reason));
        Assert.Equal("nouveau mod", Mod("Ovomium.dll"));
        Assert.Equal("nouvelle caméra", Mod("LMD-Camera.dll"));
        Assert.Equal(new[] { "LMD-Camera.dll", "Ovomium.dll" }, Directory.GetFiles(ModsDir).Select(Path.GetFileName).Order());
    }

    [Fact]
    public void SansPaquet_SauvegardeOrphelineRemiseEnPlace()
    {
        File.Move(Path.Combine(ModsDir, "Ovomium.dll"), Path.Combine(ModsDir, "Ovomium.dll.old"));
        File.WriteAllText(Path.Combine(ModsDir, "LMD-Camera.dll.old"), "sauvegarde périmée"); // cible présente : gardée telle quelle
        Assert.Null(Run());
        Assert.Equal("ancien mod", Mod("Ovomium.dll"));
        Assert.Equal("ancienne caméra", Mod("LMD-Camera.dll"));
        Assert.Contains(_log, l => l.Contains("Ovomium.dll remis en place"));
    }

    [Fact]
    public void DllQuiNeSeDitPasDeLaVersionDuPaquet_Refusee()
    {
        // release.sh --no-build après un changement de version sans recompiler : installée, la DLL se dirait 0.4.9 et
        // le mod retéléchargerait 0.5.0 à chaque lancement.
        Prepare("0.5.0", dllVersion: "0.4.9", files: TwoFiles);
        AssertUntouched(Run(), UpdateOutcome.Rejected, "Ovomium.dll se déclare 0.4.9 au lieu de 0.5.0");
        Assert.Equal(new ModVersion(0, 5, 0), UpdateFiles.ReadSkipped(DataDir));
    }

    [Fact]
    public void SansOvomiumDll_Refuse()
    {
        Prepare("0.5.0", files: ("LMD-Camera.dll", "nouvelle caméra"));
        AssertUntouched(Run(), UpdateOutcome.Rejected, "Ovomium.dll absent du manifeste");
    }

    [Fact]
    public void Refus_VersionNotee_MemeSansSignatureValide()
    {
        Prepare("0.5.0", files: TwoFiles);
        var path = Path.Combine(UpdateDir, UpdatePackage.ManifestName);
        File.WriteAllText(path, File.ReadAllText(path).Replace("0.5.0", "0.6.0"));
        Run();
        Assert.Equal(new ModVersion(0, 6, 0), UpdateFiles.ReadSkipped(DataDir));
        UpdateFiles.Skip(DataDir, new ModVersion(0, 5, 5)); // plus ancienne : la plus haute reste
        Assert.Equal(new ModVersion(0, 6, 0), UpdateFiles.ReadSkipped(DataDir));
    }

    [Fact]
    public void EtatLuUneSeuleFois()
    {
        Prepare("0.3.9", files: TwoFiles);
        Run();
        Assert.Equal(UpdateOutcome.Rejected, UpdateFiles.TakeState(DataDir).Result);
        Assert.Null(UpdateFiles.TakeState(DataDir));
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
        Prepare("0.3.9", files: TwoFiles);
        AssertUntouched(Run(), UpdateOutcome.Rejected, "pas plus récente");
        Assert.False(Directory.Exists(Path.Combine(DataDir, UpdateFiles.QuarantineDir, "vieux")));
    }
}
