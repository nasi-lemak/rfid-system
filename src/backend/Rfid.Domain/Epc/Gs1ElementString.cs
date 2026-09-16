using System.Text.RegularExpressions;

namespace Rfid.Domain.Epc;

/// <summary>
/// Parses GS1 barcode content: element strings with parenthesised AIs "(01)09506000134352(21)ABC123",
/// FNC1/GS-separated GS1-128 or GS1 DataMatrix payloads ("]d2" + "01…" + GS + "21…"), and
/// GS1 Digital Link URLs "https://id.gs1.org/01/09506000134352/21/ABC123".
/// </summary>
public static class Gs1ElementString
{
    /// <summary>ASCII group separator used between variable-length elements.</summary>
    public const char Gs = (char)29;
    private static readonly Dictionary<string, int> Fixed = new() { ["00"] = 18, ["01"] = 14, ["02"] = 14, ["11"] = 6, ["13"] = 6, ["15"] = 6, ["17"] = 6, ["20"] = 2, ["410"] = 13, ["414"] = 13 };
    private static readonly string[] Variable = { "10", "21", "22", "37", "240", "241", "250", "251", "253", "254", "400", "401", "402", "403", "420", "8003", "8004", "8005", "8006", "8017", "8018", "8200", "90", "91", "92", "93", "94", "95", "96", "97", "98", "99" };

    public static bool LooksLikeGs1(string s) => s.StartsWith("(") || s.StartsWith("]") || s.Contains("id.gs1.org") || s.Contains("/01/") || Regex.IsMatch(s, "^(01|00|8003|8004)\\d{13,}");

    public static Dictionary<string, string> Parse(string input)
    {
        var result = new Dictionary<string, string>();
        var s = input.Trim();
        if (s.Contains("/01/") || s.Contains("/00/") || s.Contains("/8003/") || s.Contains("/8004/"))
        {
            var path = s.Contains("://") ? s[(s.IndexOf("://", StringComparison.Ordinal) + 3)..] : s;
            var parts = path.Split(new[] { '/', '?' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            for (var i = 0; i + 1 < parts.Count; i++) if (Regex.IsMatch(parts[i], "^\\d{2,4}$") && (Fixed.ContainsKey(parts[i]) || Variable.Contains(parts[i]))) { result[parts[i]] = Uri.UnescapeDataString(parts[i + 1]); i++; }
            return result;
        }
        if (s.StartsWith("]")) s = s.Length > 3 ? s[3..] : "";             // symbology identifier ]C1 / ]d2 / ]Q3
        if (s.StartsWith("("))                                             // human-readable form
        {
            foreach (Match m in Regex.Matches(s, "\\((\\d{2,4})\\)([^(]*)")) result[m.Groups[1].Value] = m.Groups[2].Value.Trim();
            return result;
        }
        var pos = 0;
        while (pos < s.Length)
        {
            var ai = Fixed.Keys.Concat(Variable).OrderByDescending(k => k.Length).FirstOrDefault(k => s.AsSpan(pos).StartsWith(k));
            if (ai == null) break;
            pos += ai.Length;
            if (Fixed.TryGetValue(ai, out var len)) { if (pos + len > s.Length) break; result[ai] = s.Substring(pos, len); pos += len; }
            else { var end = s.IndexOf(Gs, pos); if (end < 0) end = s.Length; result[ai] = s[pos..end]; pos = end + 1; }
        }
        return result;
    }

    /// <summary>Candidate 96-bit EPCs for the parsed GS1 data, one per possible company-prefix length (the tag table decides which is real).</summary>
    public static IEnumerable<string> CandidateEpcs(Dictionary<string, string> ai)
    {
        var list = new List<string>();
        void Try(Func<string> f) { try { list.Add(f()); } catch { /* prefix length not valid for this scheme */ } }
        if (ai.TryGetValue("01", out var gtin) && gtin.Length == 14 && ai.TryGetValue("21", out var serial) && ulong.TryParse(serial, out var sn))
        {
            var indicator = gtin[0]; var body = gtin[1..13];
            for (var cp = 6; cp <= 12; cp++) { var prefix = body[..cp]; var itemRef = indicator + body[cp..]; Try(() => Sgtin96.Encode(prefix, itemRef, sn, 1)); }
        }
        if (ai.TryGetValue("00", out var sscc) && sscc.Length == 18)
        {
            var ext = sscc[0] - '0'; var body = sscc[1..17];
            for (var cp = 6; cp <= 12; cp++) { var prefix = body[..cp]; if (ulong.TryParse(body[cp..], out var sn2)) Try(() => Sscc96.Encode(prefix, ext, sn2, 0)); }
        }
        if (ai.TryGetValue("8003", out var grai) && grai.Length >= 15)
        {
            var body = grai[1..13]; var serial3 = grai[14..];
            if (ulong.TryParse(serial3, out var sn3)) for (var cp = 6; cp <= 12; cp++) { var prefix = body[..cp]; var type = body[cp..]; Try(() => Gs1.EncodeGrai96(prefix, type, sn3, 0)); }
        }
        if (ai.TryGetValue("8004", out var giai))
            for (var cp = 6; cp <= 12 && cp < giai.Length; cp++) { var prefix = giai[..cp]; if (ulong.TryParse(giai[cp..], out var sn4)) Try(() => Gs1.EncodeGiai96(prefix, sn4, 0)); }
        return list.Distinct();
    }
}
