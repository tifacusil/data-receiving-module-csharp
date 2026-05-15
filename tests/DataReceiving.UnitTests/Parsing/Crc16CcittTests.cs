using System.Text;
using DataReceiving.Parsing;

namespace DataReceiving.UnitTests.Parsing;

public class Crc16CcittTests
{
    [Fact]
    public void test_standard_vector()
    {
        ushort result = Crc16Ccitt.Compute(Encoding.ASCII.GetBytes("123456789"));
        Assert.Equal((ushort)0x29B1, result);
    }

    [Fact]
    public void test_empty_bytes()
    {
        ushort result = Crc16Ccitt.Compute([]);
        Assert.Equal((ushort)0xFFFF, result);
    }

    [Fact]
    public void test_single_byte()
    {
        // Known fixture: CRC-16/CCITT-FALSE of [0x31] ('1') = 0xC782
        ushort result = Crc16Ccitt.Compute([0x31]);
        Assert.Equal((ushort)0xC782, result);
    }

    [Fact]
    public void test_two_identical_bytes_differ_from_one()
    {
        ushort one = Crc16Ccitt.Compute([0x31]);
        ushort two = Crc16Ccitt.Compute([0x31, 0x31]);
        Assert.NotEqual(one, two);
    }
}
