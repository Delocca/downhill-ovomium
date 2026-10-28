using System.Text;

namespace Ovomium.Tests.Update;

/// Fausse « Ovomium.dll » lisible par InstalledVersion : l'assembly de tests (MelonInfo « Ovomium » 0.7.1, voir
/// MelonInfoStub) dont la version est changée dans les octets de l'attribut, suivie d'une étiquette (données après
/// la fin du PE, ignorées par PEReader) qui distingue les contenus.
internal static class FakeMelonDll
{
    private const string Marker = "#ETIQUETTE:";
    private static readonly byte[] Original = File.ReadAllBytes(typeof(FakeMelonDll).Assembly.Location);

    /// `version` : 5 caractères (« X.Y.Z » à un chiffre), comme 0.7.1, pour ne pas décaler l'attribut.
    public static byte[] Bytes(string version, string label)
    {
        if (version.Length != 5) throw new ArgumentException("version à 5 caractères attendue", nameof(version));
        var needle = Encoding.ASCII.GetBytes("\u0007Ovomium\u00050.7.1");
        var bytes = (byte[])Original.Clone();
        int i = bytes.AsSpan().IndexOf(needle);
        if (i < 0) throw new InvalidOperationException("attribut MelonInfo introuvable");
        Encoding.ASCII.GetBytes(version).CopyTo(bytes, i + needle.Length - 5);
        return bytes.Concat(Encoding.UTF8.GetBytes(Marker + label)).ToArray();
    }

    /// Étiquette d'une fausse DLL, sinon le texte entier du fichier.
    public static string Label(string path)
    {
        string text = Encoding.UTF8.GetString(File.ReadAllBytes(path));
        int i = text.LastIndexOf(Marker, StringComparison.Ordinal);
        return i < 0 ? text : text[(i + Marker.Length)..];
    }
}
