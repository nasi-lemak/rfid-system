using System.Numerics;
using System.Text.RegularExpressions;

namespace Rfid.Domain.Epc;

/// <summary>GS1 SGTIN-96 encoder/decoder plus hex helpers. Enough for commissioning retail/apparel tags.</summary>
public static class Sgtin96
{
    private static readonly int[] PartitionCompanyBits = { 40, 37, 34, 30, 27, 24, 20 };
    private static readonly int[] PartitionCompanyDigits = { 12, 11, 10, 9, 8, 7, 6 };
    private static readonly int[] PartitionItemBits = { 4, 7, 10, 14, 17, 20, 24 };

    public static bool IsHex(string s) => Regex.IsMatch(s, "^[0-9A-Fa-f]+$") && s.Length % 2 == 0;

    public static string Encode(string companyPrefix, string itemRef, ulong serial, int filter = 1)
    {
        var partition = Array.IndexOf(PartitionCompanyDigits, companyPrefix.Length);
        if (partition < 0) throw new ArgumentException("Company prefix must be 6-12 digits");
        var itemDigits = 13 - companyPrefix.Length;
        if (itemRef.Length != itemDigits) throw new ArgumentException($"Item reference must be {itemDigits} digits");
        BigInteger v = 0x30; // header
        v = (v << 3) | (uint)filter;
        v = (v << 3) | (uint)partition;
        v = (v << PartitionCompanyBits[partition]) | BigInteger.Parse(companyPrefix);
        v = (v << PartitionItemBits[partition]) | BigInteger.Parse(itemRef);
        v = (v << 38) | serial;
        return v.ToString("X24").ToUpperInvariant();
    }

    public static (string companyPrefix, string itemRef, ulong serial)? Decode(string epcHex)
    {
        if (epcHex.Length != 24 || !IsHex(epcHex)) return null;
        var v = BigInteger.Parse("0" + epcHex, System.Globalization.NumberStyles.HexNumber);
        var header = (int)(v >> 88);
        if (header != 0x30) return null;
        var partition = (int)((v >> 82) & 7);
        if (partition > 6) return null;
        var cBits = PartitionCompanyBits[partition];
        var iBits = PartitionItemBits[partition];
        var serial = (ulong)(v & ((BigInteger.One << 38) - 1));
        var item = (v >> 38) & ((BigInteger.One << iBits) - 1);
        var company = (v >> (38 + iBits)) & ((BigInteger.One << cBits) - 1);
        return (company.ToString().PadLeft(PartitionCompanyDigits[partition], '0'),
                item.ToString().PadLeft(13 - PartitionCompanyDigits[partition], '0'), serial);
    }
}
