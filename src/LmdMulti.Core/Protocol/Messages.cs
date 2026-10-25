using System;
using System.Buffers.Binary;
using System.Numerics;
using LmdMulti.Core.Poses;

namespace LmdMulti.Core.Protocol;

/// En-tête commun, figé pour toutes les versions (permet de reconnaître un pair d'une autre version).
public readonly struct Header
{
    public readonly byte Version;
    public readonly MessageType Type;
    public readonly ulong SenderId;

    public Header(byte version, MessageType type, ulong senderId) { Version = version; Type = type; SenderId = senderId; }
}

public enum HeaderStatus { Ok, NotOurs, WrongVersion }

/// Annonce de présence : identité et état du joueur.
public sealed class Announce
{
    public ulong SteamId;
    public string Name = "";
    /// ID complet de la piste (ex. CA_01_A2), vide au menu.
    public string Track = "";
    public bool OnTrack;
    public string ModVersion = "";
}

/// Encodage / décodage des messages (format décrit dans docs/architecture.md).
public static class Messages
{
    public static HeaderStatus ReadHeader(byte[] buf, int len, out Header header)
    {
        header = default;
        if (len < ProtocolInfo.HeaderSize || buf[0] != ProtocolInfo.Magic0 || buf[1] != ProtocolInfo.Magic1)
            return HeaderStatus.NotOurs;
        header = new Header(buf[2], (MessageType)buf[3], BinaryPrimitives.ReadUInt64LittleEndian(buf.AsSpan(4)));
        return header.Version == ProtocolInfo.Version ? HeaderStatus.Ok : HeaderStatus.WrongVersion;
    }

    public static void WriteHeader(PacketWriter w, MessageType type, ulong senderId)
    {
        w.Reset();
        w.U8(ProtocolInfo.Magic0); w.U8(ProtocolInfo.Magic1);
        w.U8(ProtocolInfo.Version); w.U8((byte)type);
        w.U64(senderId);
    }

    public static PacketReader BodyReader(byte[] buf, int len) => new(buf, len, ProtocolInfo.HeaderSize);

    public static void WriteAnnounce(PacketWriter w, ulong senderId, Announce a)
    {
        WriteHeader(w, MessageType.Announce, senderId);
        w.U64(a.SteamId);
        w.Str(a.Name);
        w.Str(a.Track, 32);
        w.U8(a.OnTrack ? (byte)1 : (byte)0);
        w.Str(a.ModVersion, 16);
    }

    public static Announce ReadAnnounce(PacketReader r) => new()
    {
        SteamId = r.U64(), Name = r.Str(), Track = r.Str(), OnTrack = r.U8() != 0, ModVersion = r.Str(),
    };

    public static void WritePose(PacketWriter w, ulong senderId, PoseFrame f)
    {
        WriteHeader(w, MessageType.Pose, senderId);
        w.U32(f.TimeMs);
        w.U8((byte)f.Flags);
        w.Vec3(f.RootPos);
        w.Quat(f.RootRot);
        if (!f.HasBones) return;
        foreach (var q in f.BoneRot) w.Quat(q);
        w.U32(f.PosMask);
        for (int i = 0; i < Skeleton.Count; i++)
            if ((f.PosMask >> i & 1) != 0) w.Vec3Half(f.BonePos[i]);
    }

    public static PoseFrame ReadPose(PacketReader r)
    {
        var f = new PoseFrame { TimeMs = r.U32(), Flags = (PoseFlags)r.U8(), RootPos = r.Vec3(), RootRot = r.Quat() };
        if (!f.HasBones) return f;
        for (int i = 0; i < Skeleton.Count; i++) f.BoneRot[i] = r.Quat();
        f.PosMask = r.U32() & Skeleton.AllMask;
        for (int i = 0; i < Skeleton.Count; i++)
            if ((f.PosMask >> i & 1) != 0) f.BonePos[i] = r.Vec3Half();
        return f;
    }

    /// Taille d'une pose encodée (pour info / tests).
    public static int PoseSize(PoseFrame f) =>
        ProtocolInfo.HeaderSize + 4 + 1 + 12 + 4 +
        (f.HasBones ? Skeleton.Count * 4 + 4 + BitOperations.PopCount(f.PosMask) * 6 : 0);

    /// Ping et Pong portent l'horodatage de l'émetteur du ping (renvoyé tel quel).
    public static void WritePing(PacketWriter w, MessageType type, ulong senderId, uint timeMs)
    {
        WriteHeader(w, type, senderId);
        w.U32(timeMs);
    }
}
