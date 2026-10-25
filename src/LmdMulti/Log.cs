using System;
using System.Collections.Generic;
using MelonLoader;

namespace LmdMulti;

/// Journal du mod (console MelonLoader + MelonLoader/Latest.log). Fil principal uniquement.
internal static class Log
{
    private static MelonLogger.Instance _log;
    private static readonly Dictionary<string, double> LastByKey = new();

    public static void Init(MelonLogger.Instance log) => _log = log;

    public static void Info(string msg) => _log.Msg(msg);
    public static void Warn(string msg) => _log.Warning(msg);
    public static void Error(string what, Exception e) => _log.Error($"{what}: {e.GetType().Name}: {e.Message}\n{e.StackTrace}");

    /// Erreur répétitive (chaque frame) : au plus une ligne toutes les 10 s par clé.
    public static void ErrorThrottled(string key, Exception e)
    {
        double now = Core.Net.NetClock.NowMs;
        if (LastByKey.TryGetValue(key, out var last) && now - last < 10000) return;
        LastByKey[key] = now;
        Error(key, e);
    }

    /// Exécute `action` en isolant ses exceptions (une partie cassée ne doit pas bloquer les autres).
    public static void Try(string key, Action action)
    {
        try { action(); }
        catch (Exception e) { ErrorThrottled(key, e); }
    }
}
