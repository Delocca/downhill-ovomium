using Ovomium.Core.Poses;
using Ovomium.Core.Protocol;

namespace Ovomium.Tests;

public class RecordingTests
{
    [Fact]
    public void Recording_RoundTrip_SurvivesTruncatedTail()
    {
        string path = Path.Combine(Path.GetTempPath(), $"ovomium-test-{Guid.NewGuid():N}.lmdrec");
        try
        {
            var r = new Random(5);
            var w = new PacketWriter();
            using (var rec = new PoseRecorder(path, "AL_01_A1"))
                for (uint t = 0; t < 10; t++)
                {
                    Messages.WritePose(w, 1, ProtocolTests.RandomPose(r, t * 40));
                    rec.Write(w.Buffer, w.Length);
                }
            // Fin coupée (jeu fermé en pleine écriture) : la dernière pose partielle est ignorée.
            using (var f = File.Open(path, FileMode.Open)) f.SetLength(f.Length - 10);
            var (track, frames) = PoseRecorder.Read(path);
            Assert.Equal("AL_01_A1", track);
            Assert.Equal(9, frames.Count);
            Assert.Equal(320u, frames[8].TimeMs);
        }
        finally { File.Delete(path); }
    }
}
