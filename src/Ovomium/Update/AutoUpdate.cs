using System.Reflection;
using System.Threading;
using MelonLoader;
using Ovomium.Core.Update;
using Ovomium.UI;
using UnityEngine;

namespace Ovomium.Update;

/// Mise à jour automatique côté mod (fil principal) : résultat de l'installation précédente, présence du plugin
/// OvomiumUpdater, puis une recherche par lancement après l'écran titre (UpdateDownloader, sur son fil).
/// L'installation elle-même est faite par le plugin au lancement suivant, avant le chargement des mods.
internal sealed class AutoUpdate
{
    public const string DefaultApiUrl = "https://api.github.com/repos/Delocca/downhill-ovomium/releases?per_page=50";
    private const string PluginName = "OvomiumUpdater";
    private static readonly Color Good = new(0.6f, 1f, 0.6f), Bad = new(1f, 0.45f, 0.45f), Info = new(1f, 0.85f, 0.5f);

    private readonly MenuNotices _notices;
    private readonly ModVersion _current;
    private string _dataDir;
    private int? _pluginVersion;
    private bool _started, _installerNoticeShown;
    private UpdateDownloader _downloader;

    public AutoUpdate(MenuNotices notices)
    {
        _notices = notices;
        ModVersion.TryParse(OvomiumMod.Version, out _current);
    }

    /// Une mise à jour téléchargée attend-elle un redémarrage du jeu ?
    public bool Ready => _dataDir != null && UpdateFiles.ReadyVersion(_dataDir) != null;

    /// Démarrage du mod (tous les plugins sont déjà chargés).
    public void Init()
    {
        _dataDir = Config.DataDir;
        // Version de référence pour le plugin (celle de la DLL vaut 0.0.0.0) : c'est nous qui la connaissons.
        if (UpdateFiles.ReadInstalled(_dataDir) != _current) UpdateFiles.WriteInstalled(_dataDir, _current);
        _pluginVersion = PluginVersion();
        Log.Info($"mise à jour automatique : {(Config.AutoUpdate.Value ? "active" : "coupée (AutoUpdate)")}, plugin {(_pluginVersion?.ToString() ?? "absent")}");
        ReportLastInstall();
    }

    /// Résultat laissé par le plugin au lancement (update-state.json), annoncé au menu puis effacé.
    private void ReportLastInstall()
    {
        var s = UpdateFiles.ReadState(_dataDir);
        if (s == null) return;
        if (s.Result == UpdateOutcome.Installed) _notices.Post($"Ovomium mis à jour en {s.Version}", Good);
        else
        {
            string what = s.Result == UpdateOutcome.Rejected ? "refusée" : "échouée";
            _notices.Post($"Mise à jour d'Ovomium {s.Version} {what} : {GuiKit.Esc(s.Reason)}", Bad, 20000);
            Log.Warn($"mise à jour {s.Version} {what} par le plugin : {s.Reason}");
        }
        UpdateFiles.ClearState(_dataDir);
    }

    /// Plugin installé ? Sa version (constante UpdaterVersion), lue par réflexion : 0 si illisible.
    private static int? PluginVersion()
    {
        foreach (var p in MelonTypeBase<MelonPlugin>.RegisteredMelons)
            if (p.Info?.Name == PluginName)
                return p.GetType().GetField("UpdaterVersion", BindingFlags.Public | BindingFlags.Static)?.GetRawConstantValue() is int v ? v : 0;
        return null;
    }

    /// À chaque image : lance la recherche une fois l'écran titre passé, relaie les messages du téléchargement.
    public void Update()
    {
        if (!_started && Tweaks.IntroSkip.TitlePassed && _dataDir != null && Config.AutoUpdate.Value)
        {
            _started = true;
            _downloader = new UpdateDownloader(new UpdateDownloader.Options(Config.UpdateApiUrl.Value?.Trim(), _dataDir, _current,
                _pluginVersion, UpdatePackage.ReleaseKey, $"Ovomium/{OvomiumMod.Version}"));
            new Thread(_downloader.Run) { IsBackground = true, Name = "Ovomium mise à jour" }.Start();
        }
        while (_downloader != null && _downloader.Outbox.TryDequeue(out var m)) Handle(m);
    }

    private void Handle(UpdateDownloader.Message m)
    {
        switch (m.Kind)
        {
            case UpdateDownloader.Kind.Log: Log.Info(m.Text); break;
            case UpdateDownloader.Kind.Warn: Log.Warn(m.Text); break;
            case UpdateDownloader.Kind.NeedInstaller:
                if (_installerNoticeShown) break;
                _installerNoticeShown = true;
                _notices.Post(m.Text, Info, 15000);
                break;
            case UpdateDownloader.Kind.Ready:
                _notices.Post($"Ovomium {m.Text} téléchargé : relance le jeu pour l'installer", Good, 15000);
                if (!string.IsNullOrEmpty(m.Notes)) _notices.Post(GuiKit.Esc(m.Notes), new Color(0.9f, 0.92f, 0.95f), 25000, block: true);
                break;
        }
    }
}
