using System.Numerics;

namespace Rfid.Domain.Epc;

/// <summary>GS1 SSCC-96 (Serial Shipping Container Code) encoder/decoder: pallets, cartons, unit loads.</summary>
public static class Sscc96
{
    private static readonly int[] CompanyBits = { 40, 37, 34, 30, 27, 24, 20 };
    private static readonly int[] CompanyDigits = { 12, 11, 10, 9, 8, 7, 6 };
    private static readonly int[] SerialBits = { 18, 21, 24, 28, 31, 34, 38 };
    private static readonly int[] SerialDigits = { 5, 6, 7, 8, 9, 10, 11 };

    /// <summary>Serial reference digits available for the company prefix (the extension digit is the first of them).</summary>
    public static int SerialDigitsFor(string companyPrefix) { var p = Array.IndexOf(CompanyDigits, companyPrefix.Length); return p < 0 ? 0 : SerialDigits[p]; }

    /// <summary>Encodes an SSCC-96. `extensionDigit` (0-9) becomes the leading digit of the serial reference; `serial` fills the rest.</summary>
    public static string Encode(string companyPrefix, int extensionDigit, ulong serial, int filter = 0)
    {
        var p = Array.IndexOf(CompanyDigits, companyPrefix.Length);
        if (p < 0) throw new ArgumentException("Company prefix must be 6-12 digits");
        if (extensionDigit is < 0 or > 9) throw new ArgumentException("Extension digit must be 0-9");
        var restDigits = SerialDigits[p] - 1;
        if (serial >= (ulong)Math.Pow(10, restDigits)) throw new ArgumentException($"Serial must have at most {restDigits} digits for this prefix");
        var serialRef = (ulong)extensionDigit * (ulong)Math.Pow(10, restDigits) + serial;
        BigInteger v = 0x31;
        v = (v << 3) | (uint)filter;
        v = (v << 3) | (uint)p;
        v = (v << CompanyBits[p]) | BigInteger.Parse(companyPrefix);
        v = (v << SerialBits[p]) | serialRef;
        v <<= 24; // reserved
        return v.ToString("X24").ToUpperInvariant();
    }

    public static (string companyPrefix, int extensionDigit, ulong serial)? Decode(string hex)
    {
        if (hex.Length != 24 || !Sgtin96.IsHex(hex)) return null;
        var v = BigInteger.Parse("0" + hex, System.Globalization.NumberStyles.HexNumber);
        if ((int)(v >> 88) != 0x31) return null;
        var p = (int)((v >> 82) & 7); if (p > 6) return null;
        var serialRef = (ulong)((v >> 24) & ((BigInteger.One << SerialBits[p]) - 1));
        var company = (v >> (24 + SerialBits[p])) & ((BigInteger.One << CompanyBits[p]) - 1);
        var restDigits = SerialDigits[p] - 1; var pow = (ulong)Math.Pow(10, restDigits);
        return (company.ToString().PadLeft(CompanyDigits[p], '0'), (int)(serialRef / pow), serialRef % pow);
    }
}
