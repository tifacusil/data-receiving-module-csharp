namespace DataReceiving.Parsing;

public interface IDtuPacketParser
{
    ParseResult Parse(ReadOnlySpan<byte> rawFrame);
}
