// Faux attribut MelonInfo (même nom, mêmes arguments que celui de MelonLoader) posé sur l'assembly de tests :
// elle sert de « Mods/Ovomium.dll » en version 0.7.1 pour InstalledVersion.
[assembly: MelonLoader.MelonInfo(typeof(Ovomium.Tests.Update.InstallerTests), "Ovomium", "0.7.1", "Edia")]

namespace MelonLoader
{
    [System.AttributeUsage(System.AttributeTargets.Assembly)]
    internal sealed class MelonInfoAttribute : System.Attribute
    {
        public MelonInfoAttribute(System.Type type, string name, string version, string author, string downloadLink = null) { }
    }
}
