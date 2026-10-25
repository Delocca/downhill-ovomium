using System;
using System.IO;
using LmdMulti.Core.Net;
using LmdMulti.Core.Poses;
using LmdMulti.Core.Protocol;

namespace LmdMulti.Ghosts;

/// Capture la pose locale à cadence fixe quand on est en piste, l'encode et la confie au réseau ;
/// l'enregistre aussi dans un .lmdrec si RecordPoses est actif (un fichier par passage sur une piste).
internal sealed class LocalSender : IDisposable
{
    private readonly PoseCapture _capture;
    private readonly PacketWriter _w = new();
    private double _next;
    private PoseRecorder _rec;

    /// Dernière pose capturée (diagnostic), null avant la première.
    public PoseFrame LastFrame { get; private set; }

    public LocalSender(PoseCapture capture) => _capture = capture;

    public void Tick(UdpNode node, string track, double nowMs, uint posMask)
    {
        if (track == null) { StopRecording(); return; }
        if (nowMs < _next) return;
        _next = Math.Max(_next + 1000.0 / Config.ClampedSendRate, nowMs - 100); // cadence stable, sans rattrapage en rafale
        var f = new PoseFrame();
        if (!_capture.Capture(f, posMask)) return;
        f.TimeMs = NetClock.NowMsU32;
        Messages.WritePose(_w, node.Id, f);
        node.SendPose(_w.ToArray());
        LastFrame = f;
        if (Config.RecordPoses.Value) Record(track);
        else StopRecording();
    }

    private void Record(string track)
    {
        if (_rec == null)
        {
            string dir = Path.Combine(Config.DataDir, "recordings");
            Directory.CreateDirectory(dir);
            _rec = new PoseRecorder(Path.Combine(dir, $"{track}-{DateTime.Now:yyyyMMdd-HHmmss}.lmdrec"), track);
            Log.Info($"enregistrement des poses : {_rec.Path}");
        }
        _rec.Write(_w.Buffer, _w.Length);
    }

    private void StopRecording()
    {
        if (_rec == null) return;
        Log.Info($"enregistrement terminé : {_rec.Frames} poses dans {_rec.Path}");
        _rec.Dispose();
        _rec = null;
    }

    public void Dispose() => StopRecording();
}
