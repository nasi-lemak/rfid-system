using System.Numerics;

namespace Rfid.Domain.Epc;

/// <summary>
/// GS1 EPC 96-bit encoders/decoders beyond SGTIN: GRAI-96 (returnable assets: crates, kegs, totes,
/// cylinders) and GIAI-96 (individual assets: IT equipment, tools, furniture).
/// </summary>
public static class Gs1
{
    private static readonly int[] CompanyBits = { 40, 37, 34, 30, 27, 24, 20 };
    private static readonly int[] CompanyDigits = { 12, 11, 10, 9, 8, 7, 6 };
    // GRAI-96: asset type bits/digits per partition.
    private static readonly int[] GraiTypeBits = { 4, 7, 10, 14, 17, 20, 24 };
    // GIAI-96: individual asset reference bits per partition (fills 96 - 8 - 3 - 3 - company bits).
    private static readonly int[] GiaiRefBits = { 42, 45, 48, 52, 55, 58, 62 };
    private static readonly int[] GiaiRefDigits = { 13, 14, 15, 16, 17, 18, 19 };

    public static string EncodeGrai96(string companyPrefix, string assetType, ulong serial, int filter = 0)
    {
        var p = Array.IndexOf(CompanyDigits, companyPrefix.Length);
        if (p < 0) throw new ArgumentException("Company prefix must be 6-12 digits");
        var typeDigits = 12 - companyPrefix.Length;
        if (assetType.Length != typeDigits) throw new ArgumentException($"Asset type must be {typeDigits} digits");
        if (serial >= (1UL << 38)) throw new ArgumentException("Serial must fit in 38 bits");
        BigInteger v = 0x33;
        v = (v << 3) | (uint)filter;
        v = (v << 3) | (uint)p;
        v = (v << CompanyBits[p]) | BigInteger.Parse(companyPrefix);
        v = (v << GraiTypeBits[p]) | BigInteger.Parse(assetType);
        v = (v << 38) | serial;
        return v.ToString("X24").ToUpperInvariant();
    }

    public static (string companyPrefix, string assetType, ulong serial)? DecodeGrai96(string hex)
    {
        if (hex.Length != 24 || !Sgtin96.IsHex(hex)) return null;
        var v = BigInteger.Parse("0" + hex, System.Globalization.NumberStyles.HexNumber);
        if ((int)(v >> 88) != 0x33) return null;
        var p = (int)((v >> 82) & 7); if (p > 6) return null;
        var serial = (ulong)(v & ((BigInteger.One << 38) - 1));
        var type = (v >> 38) & ((BigInteger.One << GraiTypeBits[p]) - 1);
        var company = (v >> (38 + GraiTypeBits[p])) & ((BigInteger.One << CompanyBits[p]) - 1);
        return (company.ToString().PadLeft(CompanyDigits[p], '0'), type.ToString().PadLeft(12 - CompanyDigits[p], '0'), serial);
    }

    public static string EncodeGiai96(string companyPrefix, ulong assetReference, int filter = 0)
    {
        var p = Array.IndexOf(CompanyDigits, companyPrefix.Length);
        if (p < 0) throw new ArgumentException("Company prefix must be 6-12 digits");
        if (assetReference >= (1UL << Math.Min(63, GiaiRefBits[p]))) throw new ArgumentException($"Asset reference must fit in {GiaiRefBits[p]} bits");
        BigInteger v = 0x34;
        v = (v << 3) | (uint)filter;
        v = (v << 3) | (uint)p;
        v = (v << CompanyBits[p]) | BigInteger.Parse(companyPrefix);
        v = (v << GiaiRefBits[p]) | assetReference;
        return v.ToString("X24").ToUpperInvariant();
    }

    public static (string companyPrefix, ulong assetReference)? DecodeGiai96(string hex)
    {
        if (hex.Length != 24 || !Sgtin96.IsHex(hex)) return null;
        var v = BigInteger.Parse("0" + hex, System.Globalization.NumberStyles.HexNumber);
        if ((int)(v >> 88) != 0x34) return null;
        var p = (int)((v >> 82) & 7); if (p > 6) return null;
        var reference = v & ((BigInteger.One << GiaiRefBits[p]) - 1);
        var company = (v >> GiaiRefBits[p]) & ((BigInteger.One << CompanyBits[p]) - 1);
        return (company.ToString().PadLeft(CompanyDigits[p], '0'), (ulong)reference);
    }

    /// <summary>Identifies the scheme of a 96-bit EPC from its header.</summary>
    public static string Scheme(string hex)
    {
        if (hex.Length < 2 || !Sgtin96.IsHex(hex)) return "unknown";
        return hex[..2].ToUpperInvariant() switch { "30" => "SGTIN-96", "33" => "GRAI-96", "34" => "GIAI-96", "35" => "GID-96", "31" => "SSCC-96", "32" => "SGLN-96", _ => "proprietary" };
    }

    public static int GiaiMaxDigits(string companyPrefix) { var p = Array.IndexOf(CompanyDigits, companyPrefix.Length); return p < 0 ? 0 : GiaiRefDigits[p]; }
}
