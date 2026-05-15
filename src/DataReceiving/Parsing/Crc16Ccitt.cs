namespace DataReceiving.Parsing;

/// <summary>
/// CRC-16/CCITT-FALSE: poly=0x1021, init=0xFFFF, refIn=false, refOut=false, xorOut=0x0000.
/// Test vector: Compute("123456789") == 0x29B1.
/// </summary>
public static class Crc16Ccitt
{
    private static readonly ushort[] Table = BuildTable();

    private static ushort[] BuildTable()
    {
        var table = new ushort[256];
        for (int i = 0; i < 256; i++)
        {
            ushort crc = (ushort)(i << 8);
            for (int j = 0; j < 8; j++)
                crc = (ushort)((crc & 0x8000) != 0 ? (crc << 1) ^ 0x1021 : crc << 1);
            table[i] = crc;
        }
        return table;
    }

    public static ushort Compute(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (var b in data)
            crc = (ushort)((crc << 8) ^ Table[(crc >> 8) ^ b]);
        return crc;
    }
}
