namespace DataReceiving.Networking;

/// <summary>
/// Reads length-prefixed binary frames from a TCP stream.
/// Frame layout: SYNC(2) + VER(1) + TYPE(1) + LEN(2) + PAYLOAD(LEN) + CRC(2) + SYNC(2) = LEN + 10
/// </summary>
public static class FrameReader
{
    private const int HeaderSize = 6; // SYNC(2) + VER(1) + TYPE(1) + LEN(2)
    private const int TrailerSize = 4; // CRC(2) + SYNC_END(2)

    public static async Task<byte[]?> ReadFrameAsync(Stream stream, CancellationToken ct)
    {
        var header = new byte[HeaderSize];
        if (!await ReadExactAsync(stream, header, ct))
            return null;

        if (header[0] != 0xAA || header[1] != 0x55)
            throw new InvalidDataException($"Bad sync start: 0x{header[0]:X2} 0x{header[1]:X2}");

        int payloadLen = (header[4] << 8) | header[5];
        int totalLen = HeaderSize + payloadLen + TrailerSize;
        var frame = new byte[totalLen];
        header.CopyTo(frame, 0);

        if (!await ReadExactAsync(stream, frame.AsMemory(HeaderSize), ct))
            return null;

        return frame;
    }

    private static async Task<bool> ReadExactAsync(Stream stream, Memory<byte> buffer, CancellationToken ct)
    {
        int read = 0;
        while (read < buffer.Length)
        {
            int n = await stream.ReadAsync(buffer[read..], ct);
            if (n == 0) return false;
            read += n;
        }
        return true;
    }
}
