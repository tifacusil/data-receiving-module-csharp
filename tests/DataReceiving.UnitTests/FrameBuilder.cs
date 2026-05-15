using System.Buffers.Binary;
using System.Text;
using DataReceiving.Parsing;

namespace DataReceiving.UnitTests;

/// <summary>
/// Builds valid binary frames for testing.
/// </summary>
public static class FrameBuilder
{
    public static byte[] Build(
        byte typeCode,
        byte[] payload,
        byte ver = 0x01,
        bool corruptCrc = false,
        byte[]? syncStart = null,
        byte[]? syncEnd = null)
    {
        syncStart ??= [0xAA, 0x55];
        syncEnd ??= [0x55, 0xAA];

        int payloadLen = payload.Length;
        var frame = new byte[2 + 1 + 1 + 2 + payloadLen + 2 + 2];

        frame[0] = syncStart[0];
        frame[1] = syncStart[1];
        frame[2] = ver;
        frame[3] = typeCode;
        frame[4] = (byte)(payloadLen >> 8);
        frame[5] = (byte)(payloadLen & 0xFF);
        payload.CopyTo(frame, 6);

        // CRC over VER + TYPE + LEN + PAYLOAD
        ushort crc = Crc16Ccitt.Compute(frame.AsSpan(2, 4 + payloadLen));
        if (corruptCrc) crc ^= 0xFFFF;
        frame[6 + payloadLen] = (byte)(crc >> 8);
        frame[6 + payloadLen + 1] = (byte)(crc & 0xFF);

        frame[6 + payloadLen + 2] = syncEnd[0];
        frame[6 + payloadLen + 3] = syncEnd[1];

        return frame;
    }

    /// <summary>Builds the common header bytes placed at the start of every payload.</summary>
    public static byte[] BuildHeader(
        Guid messageId,
        string projectId,
        string sensorId,
        long timestampMs,
        uint samplingInterval)
    {
        var buf = new List<byte>();

        // MSG_ID: 16 bytes in RFC 4122 byte order
        buf.AddRange(messageId.ToByteArray(bigEndian: true));

        // PROJ_ID
        byte[] projBytes = Encoding.ASCII.GetBytes(projectId);
        buf.Add((byte)projBytes.Length);
        buf.AddRange(projBytes);

        // SENS_ID
        byte[] sensBytes = Encoding.ASCII.GetBytes(sensorId);
        buf.Add((byte)sensBytes.Length);
        buf.AddRange(sensBytes);

        // TIMESTAMP: int64 BE
        var ts = new byte[8];
        BinaryPrimitives.WriteInt64BigEndian(ts, timestampMs);
        buf.AddRange(ts);

        // SAMPLE_INT: uint32 BE
        var si = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(si, samplingInterval);
        buf.AddRange(si);

        return [.. buf];
    }

    public static byte[] FloatBe(float v)
    {
        var b = new byte[4];
        BinaryPrimitives.WriteSingleBigEndian(b, v);
        return b;
    }

    public static byte[] Int16Be(short v)
    {
        var b = new byte[2];
        BinaryPrimitives.WriteInt16BigEndian(b, v);
        return b;
    }

    public static byte[] UInt16Be(ushort v)
    {
        var b = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(b, v);
        return b;
    }
}
