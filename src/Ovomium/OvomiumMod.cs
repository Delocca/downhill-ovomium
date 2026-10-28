using System;
using Ovomium.Core.Net;
using Ovomium.Diagnostics;
using Ovomium.Game;
using Ovomium.Ghosts;
using Ovomium.Race;
using Ovomium.UI;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(Ovomium.OvomiumMod), "Ovomium", Ovomium.OvomiumMod.Version, "Edia")]
[assembly: MelonGame("Megagon Industries", "Lonely Mountains: Downhill")]

namespace Ovomium;

/// Point d'entrée : relie réseau (Core.Net), fantômes (Ghosts), course (Race) et interface (UI).
/// Tout ce qui touche à Unity tourne ici, dans le fil principal ; le réseau a son propre fil.
public class OvomiumMod : MelonMod
{
    public const string Version = "0.4.0";

    private UdpNode _node;
    private RaceController _race;
    private readonly GhostManager _ghosts = new();
    private readonly PoseCapture _capture = new();
    private LocalSender _sender;
    private readonly PlayersPanel _panel = new();
    private readonly Toasts _toasts = new();
    private readonly SessionTools _tools = new();
    private readonly View.CameraToggle _camera = new();
    private readonly Tweaks.KeyboardRemap _keyboard = new();
    private string _myName = "?", _myTrack;
    private double _nextLocalRefresh;
    private readonly MenuNotices _menuNotices = new();
    private Update.AutoUpdate _update;
    private readonly System.Diagnostics.Stopwatch _cost = new(); // temps passé dans le mod (DebugLog)

    public override void OnInitializeMelon()
    {
        Log.Init(LoggerInstance);
        Config.Init();
        _sender = new LocalSender(_capture);
        _panel.Visible = Config.ShowPanel.Value;
        var options = new NodeOptions
        {
            Port = Config.Port.Value, DiscoveryPort = Config.Port.Value, ModVersion = Version,
            ManualPeers = Endpoints.ParsePeers(Config.ManualPeers.Value, Config.Port.Value, Log.Warn),
        };
        _node = new UdpNode(options);
        if (!_node.Start()) _toasts.Show($"Ovomium : réseau indisponible ({_node.Status})", Color.red);
        _node.Echo = Config.EchoMode.Value;
        _race = new RaceController(_node, _toasts);
        _update = new Update.AutoUpdate(_menuNotices);
        Log.Try("mise à jour", _update.Init);
        Log.Info($"prêt : {_node.Status}, protocole v{Core.Protocol.ProtocolInfo.Version}, {options.ManualPeers.Count} IP manuelle(s), écho={_node.Echo}, config {Config.FilePath}");
    }

    /// Tous les mods sont enregistrés : on peut chercher LMD-VR et signaler les touches que le joueur lui a prises.
    public override void OnLateInitializeMelon() =>
        Log.Try("touches VR", () => _menuNotices.Post(View.LmdVrBridge.KeysNotice(), new Color(1f, 0.75f, 0.4f)));

    public override void OnUpdate()
    {
        if (_node == null) return;
        _tools.Frame(_cost.Elapsed.TotalMilliseconds); // coût de l'image précédente (Update + tous les OnGUI)
        _cost.Restart();
        Log.Try("intro", Tweaks.IntroSkip.Update);
        double now = NetClock.NowMs;
        Log.Try("état local", () => RefreshLocal(now));
        Log.Try("mise à jour", _update.Update);
        Log.Try("avis", () => _menuNotices.Flush(_toasts));
        Log.Try("événements réseau", DrainEvents);
        Log.Try("touches", HandleKeys);
        Log.Try("touches de course", () => _race.HandleKeys(now, _myName));
        Log.Try("vue", () => _camera.Update(_myTrack != null, _toasts));
        Log.Try("clavier", () => _keyboard.Update(now));
        Log.Try("course", () => _race.Update(now)); // séparés : une touche en erreur ne fige pas la course
        // Capture et application dans Update : les contraintes Unity (cheveux, phares) suivent dans la même image.
        Log.Try("envoi pose", () => _sender.Tick(_node, _myTrack, now, _tools.PositionMask));
        Log.Try("fantômes", () => _ghosts.Update(_myTrack, _capture.BikeObject, now));
        Log.Try("diagnostic", () =>
        {
            _tools.Survey(_sender.LastFrame);
            _tools.Tick(_node, _ghosts.VisibleCount, _ghosts.TotalHolds(), _myTrack, _ghosts.Describe);
            _panel.ExtraHint = _camera.Hint;
            _panel.Refresh(_node, _myName, _myTrack, _ghosts.IsVisible);
        });
        _cost.Stop();
    }

    public override void OnGUI()
    {
        // Rien d'interactif : on ne dessine qu'à l'événement Repaint (OnGUI est appelé plusieurs fois par image).
        if (_node == null || Event.current.type != EventType.Repaint) return;
        _cost.Start();
        Log.Try("affichage", () =>
        {
            Vector3? me = _myTrack != null && _sender.LastFrame != null ? _sender.LastFrame.RootPos.ToUnity() : null;
            // Menu pause du jeu : les pseudos passeraient par-dessus ses boutons.
            if (_myTrack != null && !GameInfo.IsPaused()) NameLabels.Draw(_ghosts.Labels(), me);
            _panel.Draw();
            RaceHud.Draw(_race, NetClock.NowMs);
            _toasts.Draw(RaceHud.BannerBottom);
        });
        _cost.Stop();
    }

    public override void OnApplicationQuit() => Shutdown();
    public override void OnDeinitializeMelon() => Shutdown();

    /// Fermeture du jeu (appelée deux fois : chaque étape est sans effet la seconde). Chaque étape isolée :
    /// le réseau doit toujours se fermer, avec « Bye » (les autres retirent notre fantôme tout de suite).
    private void Shutdown()
    {
        if (_race != null) Log.Try("fermeture : course", () => _race.Shutdown(NetClock.NowMs));
        if (_sender != null) Log.Try("fermeture : enregistrement", _sender.Dispose);
        if (_node != null) Log.Try("fermeture : réseau", _node.Dispose);
    }

    /// Pseudo et piste locale, transmis aux annonces (2×/s).
    private void RefreshLocal(double now)
    {
        if (now < _nextLocalRefresh) return;
        _nextLocalRefresh = now + 500;
        // Pendant un chargement, la piste lue peut être l'ancienne, et le vélo est en cours de mise en place :
        // ni annonce sur une piste, ni capture de pose, ni marionnette avant la fin de l'écran de chargement.
        string track = GameInfo.CurrentTrackId();
        if (track != null && GameInfo.LoadingVisible()) track = null;
        if (track != _myTrack) Log.Info($"piste locale : {track ?? "(hors piste)"}");
        _myTrack = track;
        ulong steamId = 0;
        string name = Config.Nickname.Value;
        if (GameInfo.TryGetSteam(out var steamName, out steamId) && string.IsNullOrWhiteSpace(name)) name = steamName;
        if (string.IsNullOrWhiteSpace(name))
        {
            if (now < 20000) return; // Steam pas encore prêt : on attend avant de s'annoncer
            name = $"Joueur-{_node.Id % 10000:D4}";
        }
        _myName = name.Trim();
        _node.SetLocal(_myName, steamId, track, track != null);
    }

    private void DrainEvents()
    {
        for (int n = 0; n < 5000 && _node.TryGetEvent(out var e); n++)
        {
            switch (e.Kind)
            {
                case NetEventKind.Pose: _ghosts.OnPose(e.PeerId, e.Pose, e.RecvMs); break;
                case NetEventKind.PeerJoined:
                    _ghosts.OnPeer(e.Peer);
                    _race.OnPeerJoined(e.PeerId, NetClock.NowMs);
                    AnnounceArrival(e.Peer);
                    break;
                case NetEventKind.PeerChanged: _ghosts.OnPeer(e.Peer); Log.Info($"{e.Peer.Name} : {(e.Peer.OnTrack ? e.Peer.Track : "menu")}"); break;
                case NetEventKind.PeerLeft:
                    _ghosts.OnLeft(e.PeerId);
                    _race.OnPeerLeft(e.PeerId, NetClock.NowMs);
                    if (!e.Peer.IsEcho) _toasts.Show($"{e.Peer.Name} est parti", new Color(0.8f, 0.8f, 0.8f));
                    break;
                case NetEventKind.Message: _race.OnMessage(e.PeerId, e.Data, NetClock.NowMs, _myName); break;
                case NetEventKind.Log: Log.Warn(e.Text); break;
            }
        }
    }

    private void AnnounceArrival(PeerInfo p)
    {
        Log.Info($"pair détecté : {p.Name} {p.EndPoint} v{(p.IsCompatible ? Core.Protocol.ProtocolInfo.Version : p.IncompatibleVersion)} mod {p.ModVersion} piste={p.Track}");
        if (!p.IsCompatible)
            _toasts.Show($"{p.Name} : version d'Ovomium incompatible, " + (_update.Ready ? "mise à jour prête : relance le jeu" : "mettez-vous à jour"),
                new Color(1f, 0.45f, 0.45f));
        else if (!p.IsEcho)
            _toasts.Show($"{p.Name} a rejoint ({GameInfo.TrackName(p.OnTrack ? p.Track : null)})", new Color(0.6f, 1f, 0.6f));
    }

    private void HandleKeys()
    {
        if (Input.GetKeyDown(Config.PanelKey.Code)) _panel.Visible = !_panel.Visible;
        if (Input.GetKeyDown(Config.EchoKey.Code))
        {
            _node.Echo = !_node.Echo;
            _toasts.Show(_node.Echo ? $"Écho activé : ton fantôme te suit à {Config.EchoDelaySeconds.Value:0.#} s" : "Écho coupé", new Color(1f, 0.85f, 0.4f), "echo");
        }
        if (Input.GetKeyDown(Config.StyleKey.Code)) CycleGhostStyle();
    }

    /// Aspect suivant, enregistré dans la config ; les marionnettes sont recréées à l'image suivante.
    private void CycleGhostStyle()
    {
        var next = (GhostLook.Style)(((int)GhostLook.Parse(Config.GhostStyle.Value) + 1) % 3);
        Config.GhostStyle.Value = next.ToString();
        Config.GhostStyle.Category.SaveToFile(false); // notre fichier seulement (Save() global crée MelonPreferences.cfg)
        _ghosts.RebuildPuppets();
        string label = next switch { GhostLook.Style.Tint => "teinté", GhostLook.Style.Translucent => "translucide", _ => "normal" };
        _toasts.Show($"Aspect des fantômes : {label}", new Color(0.8f, 0.9f, 1f), "style");
    }
}
