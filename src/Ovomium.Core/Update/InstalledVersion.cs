using System;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Ovomium.Core.Update;

/// Version réellement installée d'Ovomium, lue dans Mods/Ovomium.dll sans la charger : argument « version » de
/// son attribut [MelonInfo(type, "Ovomium", version, auteur)]. La version de fichier de la DLL vaut 0.0.0.0
/// (GenerateAssemblyInfo=false). Plus sûr que installed.json, qu'un installeur lancé entre-temps laisserait périmé.
public static class InstalledVersion
{
    public const string ModFile = "Ovomium.dll", MelonName = "Ovomium";

    /// Version de Mods/Ovomium.dll, sinon celle d'installed.json, sinon null.
    public static ModVersion? Read(string modsDir, string dataDir) =>
        FromMelonDll(Path.Combine(modsDir, ModFile)) ?? UpdateFiles.ReadInstalled(dataDir);

    /// Version déclarée par l'attribut MelonInfo de la DLL (melon nommé « Ovomium »), ou null.
    public static ModVersion? FromMelonDll(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var fs = File.OpenRead(path);
            using var pe = new PEReader(fs);
            if (!pe.HasMetadata) return null;
            var md = pe.GetMetadataReader();
            foreach (var h in md.GetAssemblyDefinition().GetCustomAttributes())
            {
                var ca = md.GetCustomAttribute(h);
                if (AttributeName(md, ca) != "MelonInfoAttribute") continue;
                var blob = md.GetBlobReader(ca.Value);
                if (blob.ReadUInt16() != 1) continue; // prologue des arguments
                blob.ReadSerializedString(); // type du melon
                string name = blob.ReadSerializedString(), version = blob.ReadSerializedString();
                if (name == MelonName && ModVersion.TryParse(version, out var v)) return v;
            }
        }
        catch (Exception e) when (e is IOException or BadImageFormatException or UnauthorizedAccessException or InvalidOperationException) { }
        return null;
    }

    private static string AttributeName(MetadataReader md, CustomAttribute ca)
    {
        EntityHandle type = ca.Constructor.Kind switch
        {
            HandleKind.MemberReference => md.GetMemberReference((MemberReferenceHandle)ca.Constructor).Parent,
            HandleKind.MethodDefinition => md.GetMethodDefinition((MethodDefinitionHandle)ca.Constructor).GetDeclaringType(),
            _ => default,
        };
        return type.Kind switch
        {
            HandleKind.TypeReference => md.GetString(md.GetTypeReference((TypeReferenceHandle)type).Name),
            HandleKind.TypeDefinition => md.GetString(md.GetTypeDefinition((TypeDefinitionHandle)type).Name),
            _ => null,
        };
    }
}
