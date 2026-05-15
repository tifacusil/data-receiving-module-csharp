using System.Buffers.Binary;
using System.Text;
using DataReceiving.Parsing;

namespace DataReceiving.IntegrationTests;

public static class FrameBuilder
{
    public static byte[] Build(byte typeCode, byte[] payload, bool corruptCrc = false)
    {
        int payloadLen = payload.Length;
        var frame = new byte[10 + payloadLen];
        frame[0] = 0xAA; frame[1] = 0x55;
        frame[2] = 0x01;
        frame[3] = typeCode;
        frame[4] = (byte)(payloadLen >> 8);
        frame[5] = (byte)(payloadLen & 0xFF);
        payload.CopyTo(frame, 6);
        ushort crc = Crc16Ccitt.Compute(frame.AsSpan(2, 4 + payloadLen));
        if (corruptCrc) crc ^= 0xFFFF;
        frame[6 + payloadLen] = (byte)(crc >> 8);
        frame[6 + payloadLen + 1] = (byte)(crc & 0xFF);
        frame[6 + payloadLen + 2] = 0x55;
        frame[6 + payloadLen + 3] = 0xAA;
        return frame;
    }

    public static byte[] BuildHeader(Guid messageId, string projectId, string sensorId, long timestampMs, uint samplingInterval)
    {
        var buf = new List<byte>();
        buf.AddRange(messageId.ToByteArray(bigEndian: true));
        byte[] proj = Encoding.ASCII.GetBytes(projectId);
        buf.Add((byte)proj.Length);
        buf.AddRange(proj);
        byte[] sens = Encoding.ASCII.GetBytes(sensorId);
        buf.Add((byte)sens.Length);
        buf.AddRange(sens);
        var ts = new byte[8];
        BinaryPrimitives.WriteInt64BigEndian(ts, timestampMs);
        buf.AddRange(ts);
        var si = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(si, samplingInterval);
        buf.AddRange(si);
        return [.. buf];
    }

    public static byte[] FloatBe(float v) { var b = new byte[4]; BinaryPrimitives.WriteSingleBigEndian(b, v); return b; }
    public static byte[] UInt16Be(ushort v) { var b = new byte[2]; BinaryPrimitives.WriteUInt16BigEndian(b, v); return b; }
}
