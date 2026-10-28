// Harnais bout en bout : charge les DLL installées dans un faux jeu, chacune dans son AssemblyLoadContext (les deux
// contiennent Ovomium.Core.Update), et appelle leur code par réflexion, comme le jeu le ferait.
//   mod <jeu> <apiUrl>   comme AutoUpdate.Init puis Update : installed.json, UpdateDownloader.Run, messages Outbox
//   plugin <jeu>         comme UpdaterPlugin.OnPreInitialization : UpdateInstaller.Run
//   version <dll>        InstalledVersion.FromMelonDll (argument version de MelonInfo)
//   plugin-hors-melonloader <jeu>   OnPreInitialization sans MelonLoader initialisé : aucune exception ne doit sortir
// Variable OVOMIUM_REFS : dossier refs/ (MelonLoader.dll, pour charger le type UpdaterPlugin).
using System.Collections;
using System.Reflection;
using System.Runtime.Loader;

static class Harness
{
    const string Ns = "Ovomium.Core.Update.";

    static int Main(string[] a) => a[0] switch
    {
        "mod" => Mod(a[1], a[2]),
        "plugin" => Plugin(a[1]),
        "version" => PrintVersion(a[1]),
        "plugin-hors-melonloader" => PluginWithoutMelonLoader(a[1]),
        _ => 2,
    };

    /// OnPreInitialization appelé sur un plugin que MelonLoader n'a pas initialisé (ni environnement, ni journal) :
    /// rien ne doit en sortir. Code 0 si aucune exception ne s'échappe.
    static int PluginWithoutMelonLoader(string game)
    {
        var type = T(Load(Path.Combine(game, "Plugins", "OvomiumUpdater.dll")), "Ovomium.Updater.UpdaterPlugin");
        var plugin = Activator.CreateInstance(type);
        try { type.GetMethod("OnPreInitialization").Invoke(plugin, null); }
        catch (TargetInvocationException e) { Console.WriteLine($"exception sortie : {e.InnerException?.GetType().Name}"); return 1; }
        Console.WriteLine("aucune exception sortie");
        return 0;
    }

    /// Copie chargée (la DLL d'origine peut être remplacée ensuite), dépendances cherchées dans refs/.
    static Assembly Load(string dll)
    {
        string copy = Path.Combine(Path.GetTempPath(), $"ovomium-e2e-{Guid.NewGuid():N}.dll");
        File.Copy(dll, copy);
        string refs = Environment.GetEnvironmentVariable("OVOMIUM_REFS") ?? "";
        var alc = new AssemblyLoadContext(copy);
        alc.Resolving += (c, n) => File.Exists(Path.Combine(refs, n.Name + ".dll")) ? c.LoadFromAssemblyPath(Path.Combine(refs, n.Name + ".dll")) : null;
        return alc.LoadFromAssemblyPath(copy);
    }

    static Type T(Assembly a, string name) => a.GetType(name, throwOnError: true);
    static object Prop(object o, string name) => o.GetType().GetProperty(name).GetValue(o);
    static byte[] ReleaseKey(Assembly a) => (byte[])T(a, Ns + "UpdatePackage").GetField("ReleaseKey").GetValue(null);

    static object MelonVersion(Assembly a, string dll) =>
        T(a, Ns + "InstalledVersion").GetMethod("FromMelonDll").Invoke(null, new object[] { dll }) ?? throw new Exception($"version illisible : {dll}");

    static int PrintVersion(string dll)
    {
        Console.WriteLine(MelonVersion(Load(dll), dll));
        return 0;
    }

    /// Constante UpdaterVersion du plugin, lue comme AutoUpdate.PluginVersion ; null si le plugin est absent.
    static int? PluginVersion(string game)
    {
        string dll = Path.Combine(game, "Plugins", "OvomiumUpdater.dll");
        if (!File.Exists(dll)) return null;
        var f = T(Load(dll), "Ovomium.Updater.UpdaterPlugin").GetField("UpdaterVersion", BindingFlags.Public | BindingFlags.Static);
        return f?.GetRawConstantValue() is int v ? v : 0;
    }

    static int Mod(string game, string apiUrl)
    {
        string dll = Path.Combine(game, "Mods", "Ovomium.dll"), data = Path.Combine(game, "UserData", "Ovomium");
        var asm = Load(dll);
        object current = MelonVersion(asm, dll); // = OvomiumMod.Version, argument de son MelonInfo
        Directory.CreateDirectory(data); // Config.Init
        T(asm, Ns + "UpdateFiles").GetMethod("WriteInstalled").Invoke(null, new[] { data, current });
        int? plugin = PluginVersion(game);
        var type = T(asm, Ns + "UpdateDownloader");
        var options = Activator.CreateInstance(type.GetNestedType("Options"), apiUrl, data, current, plugin, ReleaseKey(asm), $"Ovomium/{current}");
        var downloader = Activator.CreateInstance(type, options);
        Console.WriteLine($"mod {current}, plugin {plugin?.ToString() ?? "absent"}");
        type.GetMethod("Run").Invoke(downloader, null);
        foreach (var m in (IEnumerable)type.GetField("Outbox").GetValue(downloader))
            Console.WriteLine($"  {Prop(m, "Kind")} : {Prop(m, "Text")}");
        return 0;
    }

    static int Plugin(string game)
    {
        var asm = Load(Path.Combine(game, "Plugins", "OvomiumUpdater.dll"));
        int updaterVersion = (int)T(asm, "Ovomium.Updater.UpdaterPlugin").GetField("UpdaterVersion").GetRawConstantValue();
        Action<string> log = s => Console.WriteLine("  journal : " + s);
        // OnPreInitialization : UserData/Ovomium et Mods/ de MelonEnvironment.
        var o = T(asm, Ns + "UpdateInstaller").GetMethod("Run").Invoke(null,
            new object[] { Path.Combine(game, "UserData", "Ovomium"), Path.Combine(game, "Mods"), updaterVersion, ReleaseKey(asm), log });
        Console.WriteLine(o == null ? "plugin : aucun paquet prêt" : $"plugin {updaterVersion} : {Prop(o, "Result")} {Prop(o, "Version")} {Prop(o, "Reason")}");
        return 0;
    }
}
