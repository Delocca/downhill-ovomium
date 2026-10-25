using System;
using System.IO;
using MelonLoader;
using MelonLoader.Utils;

namespace LmdExplorer;

/// Sorties du mod : console MelonLoader (Latest.log) + fichiers dans UserData/LmdExplorer/.
internal static class Out
{
    private static MelonLogger.Instance _log;
    public static string Dir { get; private set; }

    public static void Init(MelonLogger.Instance log)
    {
        _log = log;
        Dir = Path.Combine(MelonEnvironment.UserDataDirectory, "LmdExplorer");
        Directory.CreateDirectory(Dir);
    }

    public static void Info(string msg) => _log.Msg(msg);

    public static void Error(string what, Exception e) => _log.Error($"{what}: {e.GetType().Name}: {e.Message}");

    /// Exécute une sonde en isolant ses exceptions (une sonde cassée ne doit pas tuer les autres).
    public static void Try(string what, Action action)
    {
        try { action(); }
        catch (Exception e) { Error(what, e); }
    }

    public static void WriteFile(string name, string text)
    {
        string path = Path.Combine(Dir, name);
        File.WriteAllText(path, text);
        Info($"fichier écrit : {path}");
    }

    public static bool Flag(string name) => File.Exists(Path.Combine(Dir, name));
}
