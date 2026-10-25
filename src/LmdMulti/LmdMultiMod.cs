using System;
using LmdMulti.Core.Net;
using LmdMulti.Diagnostics;
using LmdMulti.Game;
using LmdMulti.Ghosts;
using LmdMulti.UI;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(LmdMulti.LmdMultiMod), "LmdMulti", LmdMulti.LmdMultiMod.Version, "Edia")]
[assembly: MelonGame("Megagon Industries", "Lonely Mountains: Downhill")]

namespace LmdMulti;

/// Point d'entrée : relie réseau (Core.Net), fantômes (Ghosts) et interface (UI).
/// Tout ce qui touche à Unity tourne ici, dans le fil principal ; le réseau a son propre fil.
public class LmdMultiMod : MelonMod
{
    public const string Version = "0.1.0";

    private UdpNode _node;
    private readonly GhostManager _ghosts = new();
    private readonly PoseCapture _capture = new();
    private LocalSender _sender;
    private readonly PlayersPanel _panel = new();
    private readonly Toasts _toasts = new();
    private readonly SessionTools _tools = new();
    private string _myName = "?", _myTrack;
    private double _nextLocalRefresh;

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
        if (!_node.Start()) _toasts.Show($"LmdMulti : réseau indisponible ({_node.Status})", Color.red);
        _node.Echo = Config.EchoMode.Value;
        Log.Info($"prêt : {_node.Status}, protocole v{Core.Protocol.ProtocolInfo.Version}, {options.ManualPeers.Count} IP manuelle(s), écho={_node.Echo}, config {Config.FilePath}");
    }

    public override void OnUpdate()
    {
        if (_node == null) return;
        double now = NetClock.NowMs;
        Log.Try("état local", () => RefreshLocal(now));
        Log.Try("événements réseau", DrainEvents);
        Log.Try("touches", HandleKeys);
        // Capture et application dans Update : les contraintes Unity (cheveux, phares) suivent dans la même image.
        Log.Try("envoi pose", () => _sender.Tick(_node, _myTrack, now, _tools.PositionMask));
        Log.Try("fantômes", () => _ghosts.Update(_myTrack, _capture.BikeObject, now));
        Log.Try("diagnostic", () =>
        {
            _tools.Survey(_sender.LastFrame);
            _tools.Tick(_node, _ghosts.VisibleCount, _ghosts.TotalHolds(), _myTrack, _ghosts.Describe);
            _panel.Refresh(_node, _myName, _myTrack, _ghosts.IsVisible);
        });
    }

    public override void OnGUI()
    {
        if (_node == null) return;
        Log.Try("affichage", () =>
        {
            NameLabels.Draw(_ghosts.Labels());
            _panel.Draw();
            _toasts.Draw();
        });
    }

    public override void OnApplicationQuit() => Shutdown();
    public override void OnDeinitializeMelon() => Shutdown();

    private void Shutdown()
    {
        _sender?.Dispose();
        _node?.Dispose(); // envoie « Bye » : les autres retirent notre fantôme tout de suite
    }

    /// Pseudo et piste locale, transmis aux annonces (2×/s).
    private void RefreshLocal(double now)
    {
        if (now < _nextLocalRefresh) return;
        _nextLocalRefresh = now + 500;
        string track = GameInfo.CurrentTrackId();
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
                case NetEventKind.PeerJoined: _ghosts.OnPeer(e.Peer); AnnounceArrival(e.Peer); break;
                case NetEventKind.PeerChanged: _ghosts.OnPeer(e.Peer); Log.Info($"{e.Peer.Name} : {(e.Peer.OnTrack ? e.Peer.Track : "menu")}"); break;
                case NetEventKind.PeerLeft:
                    _ghosts.OnLeft(e.PeerId);
                    if (!e.Peer.IsEcho) _toasts.Show($"{e.Peer.Name} est parti", new Color(0.8f, 0.8f, 0.8f));
                    break;
                case NetEventKind.Log: Log.Warn(e.Text); break;
            }
        }
    }

    private void AnnounceArrival(PeerInfo p)
    {
        Log.Info($"pair détecté : {p.Name} {p.EndPoint} v{(p.IsCompatible ? Core.Protocol.ProtocolInfo.Version : p.IncompatibleVersion)} mod {p.ModVersion} piste={p.Track}");
        if (!p.IsCompatible)
            _toasts.Show($"{p.Name} : version de LmdMulti incompatible, mettez-vous à jour", new Color(1f, 0.45f, 0.45f));
        else if (!p.IsEcho)
            _toasts.Show($"{p.Name} a rejoint ({GameInfo.TrackName(p.OnTrack ? p.Track : null)})", new Color(0.6f, 1f, 0.6f));
    }

    private void HandleKeys()
    {
        if (Input.GetKeyDown(Config.Key(Config.PanelKey))) _panel.Visible = !_panel.Visible;
        if (Input.GetKeyDown(Config.Key(Config.EchoKey)))
        {
            _node.Echo = !_node.Echo;
            _toasts.Show(_node.Echo ? $"Écho activé : ton fantôme te suit à {Config.EchoDelaySeconds.Value:0.#} s" : "Écho coupé", new Color(1f, 0.85f, 0.4f));
        }
        if (Input.GetKeyDown(Config.Key(Config.CaptureKey))) _tools.Capture();
    }
}
