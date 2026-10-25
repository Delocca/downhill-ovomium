using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Ovomium.Core.Protocol;

namespace Ovomium.Core.Poses;

/// Fichier d'enregistrement des poses émises (.lmdrec), rejoué par le bot de test.
/// Format : "LMDREC" + version du protocole (1 octet) + piste (chaîne préfixée) + suite de
/// [longueur u16][paquet Pose complet tel qu'envoyé sur le réseau].
public sealed class PoseRecorder : IDisposable
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("LMDREC");
    private readonly BinaryWriter _w;
    public string Path { get; }
    public int Frames { get; private set; }

    public PoseRecorder(string path, string track)
    {
        Path = path;
        _w = new BinaryWriter(new BufferedStream(File.Create(path), 64 * 1024));
        _w.Write(Magic);
        _w.Write(ProtocolInfo.Version);
        _w.Write(track ?? "");
    }

    public void Write(byte[] packet, int length)
    {
        _w.Write((ushort)length);
        _w.Write(packet, 0, length);
        Frames++;
    }

    public void Dispose() => _w.Dispose();

    /// Lit un enregistrement : piste + poses dans l'ordre. Lève InvalidDataException si invalide.
    public static (string track, List<PoseFrame> frames) Read(string path)
    {
        using var r = new BinaryReader(File.OpenRead(path));
        if (Encoding.ASCII.GetString(r.ReadBytes(Magic.Length)) != "LMDREC")
            throw new InvalidDataException("pas un fichier .lmdrec");
        byte version = r.ReadByte();
        if (version != ProtocolInfo.Version)
            throw new InvalidDataException($"enregistrement en protocole v{version}, attendu v{ProtocolInfo.Version}");
        string track = r.ReadString();
        var frames = new List<PoseFrame>();
        while (r.BaseStream.Position + 2 <= r.BaseStream.Length)
        {
            int len = r.ReadUInt16();
            byte[] packet = r.ReadBytes(len);
            if (packet.Length < len) break; // fichier coupé (jeu fermé brutalement)
            if (Messages.ReadHeader(packet, len, out var h) == HeaderStatus.Ok && h.Type == MessageType.Pose)
                frames.Add(Messages.ReadPose(Messages.BodyReader(packet, len)));
        }
        return (track, frames);
    }
}
