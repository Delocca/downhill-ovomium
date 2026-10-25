using System;
using System.Buffers.Binary;
using System.Text;

namespace LmdMulti.Core.Protocol;

/// Écriture binaire petit-boutiste dans un tampon fixe.
public sealed class PacketWriter
{
    public readonly byte[] Buffer;
    public int Length { get; private set; }

    public PacketWriter(int capacity = ProtocolInfo.MaxPacketSize) => Buffer = new byte[capacity];

    public void Reset() => Length = 0;

    public void U8(byte v) => Buffer[Length++] = v;
    public void U16(ushort v) { BinaryPrimitives.WriteUInt16LittleEndian(Buffer.AsSpan(Length), v); Length += 2; }
    public void U32(uint v) { BinaryPrimitives.WriteUInt32LittleEndian(Buffer.AsSpan(Length), v); Length += 4; }
    public void U64(ulong v) { BinaryPrimitives.WriteUInt64LittleEndian(Buffer.AsSpan(Length), v); Length += 8; }
    public void F32(float v) { BinaryPrimitives.WriteSingleLittleEndian(Buffer.AsSpan(Length), v); Length += 4; }
    public void Half(float v) => U16((ushort)BitConverter.HalfToInt16Bits((System.Half)v));

    public void Vec3(Vec3 v) { F32(v.X); F32(v.Y); F32(v.Z); }
    public void Vec3Half(Vec3 v) { Half(v.X); Half(v.Y); Half(v.Z); }
    public void Quat(Quat q) => U32(QuatCodec.Pack(q));

    /// Chaîne UTF-8 préfixée par sa longueur (octet), tronquée à maxBytes sans couper un caractère.
    public void Str(string s, int maxBytes = ProtocolInfo.MaxNameBytes)
    {
        s ??= "";
        byte[] bytes = Encoding.UTF8.GetBytes(s);
        int n = Math.Min(bytes.Length, Math.Min(maxBytes, 255));
        while (n > 0 && n < bytes.Length && (bytes[n] & 0xC0) == 0x80) n--; // octet de continuation
        U8((byte)n);
        bytes.AsSpan(0, n).CopyTo(Buffer.AsSpan(Length));
        Length += n;
    }

    public void Bytes(byte[] data)
    {
        data.AsSpan().CopyTo(Buffer.AsSpan(Length));
        Length += data.Length;
    }

    public byte[] ToArray() => Buffer.AsSpan(0, Length).ToArray();
}

/// Lecture binaire petit-boutiste ; lève FormatException si le paquet est trop court.
public sealed class PacketReader
{
    private readonly byte[] _buf;
    private readonly int _end;
    public int Pos { get; private set; }

    public PacketReader(byte[] buffer, int length, int start = 0) { _buf = buffer; _end = length; Pos = start; }

    private ReadOnlySpan<byte> Take(int n)
    {
        if (Pos + n > _end) throw new FormatException("paquet tronqué");
        var span = _buf.AsSpan(Pos, n);
        Pos += n;
        return span;
    }

    public byte U8() => Take(1)[0];
    public ushort U16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
    public uint U32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
    public ulong U64() => BinaryPrimitives.ReadUInt64LittleEndian(Take(8));
    public float F32() => BinaryPrimitives.ReadSingleLittleEndian(Take(4));
    public float Half() => (float)BitConverter.Int16BitsToHalf((short)U16());

    public Vec3 Vec3() => new(F32(), F32(), F32());
    public Vec3 Vec3Half() => new(Half(), Half(), Half());
    public Quat Quat() => QuatCodec.Unpack(U32());

    public string Str()
    {
        int n = U8();
        return Encoding.UTF8.GetString(Take(n));
    }

    /// Tout ce qui reste du paquet.
    public byte[] Rest() => Take(_end - Pos).ToArray();
}
