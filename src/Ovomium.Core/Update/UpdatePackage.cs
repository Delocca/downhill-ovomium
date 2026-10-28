using System;
using System.IO;
using System.Linq;

namespace Ovomium.Core.Update;

/// Vérification d'un paquet de mise à jour posé dans un dossier : manifeste signé, empreintes, versions.
/// Même code pour le mod (après téléchargement) et pour le plugin OvomiumUpdater (avant installation).
public static class UpdatePackage
{
    public const string ManifestName = "ovomium-update.json";
    public const string SignatureName = ManifestName + ".sig";

    /// Clé publique Ed25519 des releases d'Ovomium (32 octets bruts). Clé privée : ~/.config/ovomium/release-key.pem
    /// chez Edia, voir docs/distribution.md.
    public static readonly byte[] ReleaseKey = Convert.FromBase64String("dseNI/klJpWEjY/3pE+HWOROQNvyUqUIdLyUld81pF8=");

    /// Le manifeste et sa signature sont-ils là ?
    public static bool HasManifest(string dir) =>
        File.Exists(Path.Combine(dir, ManifestName)) && File.Exists(Path.Combine(dir, SignatureName));

    /// Manifeste dont la signature (64 octets bruts, sur les octets exacts du fichier) est valide, ou null + raison.
    public static UpdateManifest ReadSigned(string dir, byte[] publicKey, out string reason)
    {
        reason = null;
        if (!HasManifest(dir)) { reason = "manifeste ou signature absent"; return null; }
        var json = File.ReadAllBytes(Path.Combine(dir, ManifestName));
        var sig = File.ReadAllBytes(Path.Combine(dir, SignatureName));
        if (json.Length > UpdateManifest.MaxBytes) { reason = "manifeste trop gros"; return null; }
        if (!Ed25519.Verify(publicKey, json, sig)) { reason = "signature invalide"; return null; }
        return UpdateManifest.Parse(json, out reason);
    }

    /// Chaque fichier listé est présent dans `dir` avec la bonne empreinte, et Ovomium.dll déclare la version du
    /// manifeste (CheckModDll).
    public static bool CheckFiles(string dir, UpdateManifest m, out string reason)
    {
        foreach (var f in m.Files)
        {
            string path = Path.Combine(dir, f.Name);
            if (!File.Exists(path)) { reason = $"{f.Name} absent"; return false; }
            if (!HasHash(path, f.Sha256)) { reason = $"empreinte fausse pour {f.Name}"; return false; }
        }
        return CheckModDll(dir, m, out reason);
    }

    /// Ovomium.dll fait partie du paquet et son MelonInfo annonce la version du manifeste. Sinon, une fois
    /// installée, elle se dirait plus ancienne et chaque lancement retéléchargerait la même mise à jour.
    public static bool CheckModDll(string dir, UpdateManifest m, out string reason)
    {
        reason = null;
        var mod = m.Files.FirstOrDefault(f => string.Equals(f.Name, InstalledVersion.ModFile, StringComparison.OrdinalIgnoreCase));
        if (mod == null) { reason = $"{InstalledVersion.ModFile} absent du manifeste"; return false; }
        var declared = InstalledVersion.FromMelonDll(Path.Combine(dir, mod.Name));
        if (declared != m.Version)
            reason = $"{InstalledVersion.ModFile} se déclare {declared?.ToString() ?? "sans version lisible"} au lieu de {m.Version}";
        return reason == null;
    }

    public static bool HasHash(string path, string sha256) => Sha2.Sha256Hex(File.ReadAllBytes(path)) == sha256;

    /// Une cible de Mods/ manque ou diffère du paquet ?
    public static bool DiffersFrom(string modsDir, UpdateManifest m) =>
        m.Files.Any(f => { string p = Path.Combine(modsDir, f.Name); return !File.Exists(p) || !HasHash(p, f.Sha256); });

    /// Version strictement plus récente que celle installée (pas de retour arrière), et plugin assez récent pour
    /// ce paquet. `repair` : la même version est acceptée (réinstallation de fichiers de Mods/ abîmés).
    public static bool CheckVersions(UpdateManifest m, ModVersion installed, int updaterVersion, out string reason, bool repair = false)
    {
        reason = null;
        if (!(m.Version > installed || (repair && m.Version == installed))) reason = $"version {m.Version} pas plus récente que {installed}";
        else if (m.MinUpdater > updaterVersion) reason = $"plugin de mise à jour trop ancien ({updaterVersion} < {m.MinUpdater}) : relancer l'installeur";
        return reason == null;
    }
}
