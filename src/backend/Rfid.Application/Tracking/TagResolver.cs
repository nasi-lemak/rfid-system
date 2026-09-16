using Microsoft.EntityFrameworkCore;
using Rfid.Application.Contracts;
using Rfid.Domain;
using Rfid.Domain.Entities;

namespace Rfid.Application.Services;

public class TagResolver
{
    private readonly IAppDb _db;
    public TagResolver(IAppDb db) => _db = db;

    /// <summary>Hex EPCs are upper-cased; barcode payloads keep their case (they are matched as scanned, trimmed).</summary>
    public static string Normalize(string epc)
    {
        var t = epc.Trim();
        return IsHexEpc(t) ? t.ToUpperInvariant() : t;
    }
    public static bool IsHexEpc(string s) => s.Length >= 8 && s.Length % 2 == 0 && s.All(Uri.IsHexDigit);

    /// <summary>Batch-resolve EPCs to tags (with item + item type). Missing EPCs are absent from the result.</summary>
    public async Task<Dictionary<string, Tag>> ResolveAsync(IEnumerable<string> epcs, CancellationToken ct = default)
    {
        var set = epcs.Select(Normalize).Distinct().ToList();
        if (set.Count == 0) return new();
        var tags = await _db.Tags
            .Include(t => t.Item!).ThenInclude(i => i.ItemType)
            .Include(t => t.Item!).ThenInclude(i => i.CurrentLocation)
            .Where(t => set.Contains(t.Epc))
            .ToListAsync(ct);
        var result = tags.GroupBy(t => t.Epc).ToDictionary(g => g.Key, g => g.First());
        // Barcode fallback: codes that are not tags may be GS1 element strings / Digital Links (→ candidate EPCs) or item identifiers.
        var unresolved = set.Where(c => !result.ContainsKey(c)).ToList();
        if (unresolved.Count == 0) return result;
        var candidates = new Dictionary<string, List<string>>();
        foreach (var code in unresolved)
        {
            if (!Rfid.Domain.Epc.Gs1ElementString.LooksLikeGs1(code)) continue;
            var ai = Rfid.Domain.Epc.Gs1ElementString.Parse(code);
            var list = Rfid.Domain.Epc.Gs1ElementString.CandidateEpcs(ai).ToList();
            if (ai.TryGetValue("21", out var serial)) list.Add("IDENT:" + serial);
            if (list.Count > 0) candidates[code] = list;
        }
        var epcCandidates = candidates.Values.SelectMany(v => v).Where(v => !v.StartsWith("IDENT:")).Distinct().ToList();
        var found = epcCandidates.Count == 0 ? new List<Tag>() : await _db.Tags.Include(t => t.Item!).ThenInclude(i => i.ItemType).Include(t => t.Item!).ThenInclude(i => i.CurrentLocation).Where(t => epcCandidates.Contains(t.Epc)).ToListAsync(ct);
        foreach (var (code, list) in candidates) { var hit = found.FirstOrDefault(t => list.Contains(t.Epc)); if (hit != null) result[code] = hit; }
        // Plain identifiers (asset numbers printed as barcodes) resolve to the item's first active tag, or a virtual barcode tag.
        var idents = set.Where(c => !result.ContainsKey(c)).Select(c => candidates.TryGetValue(c, out var l) && l.FirstOrDefault(x => x.StartsWith("IDENT:")) is { } id ? id[6..] : c).Distinct().ToList();
        if (idents.Count > 0)
        {
            var items = await _db.Items.Include(i => i.ItemType).Include(i => i.CurrentLocation).Include(i => i.Tags).Where(i => idents.Contains(i.Identifier)).ToListAsync(ct);
            foreach (var code in set.Where(c => !result.ContainsKey(c)))
            {
                var ident = candidates.TryGetValue(code, out var l) && l.FirstOrDefault(x => x.StartsWith("IDENT:")) is { } id ? id[6..] : code;
                var item = items.FirstOrDefault(i => i.Identifier == ident);
                if (item != null) result[code] = item.Tags.FirstOrDefault(t => t.Status == TagStatus.Active) ?? new Tag { TenantId = item.TenantId, Epc = code, ItemId = item.Id, Item = item, Technology = TagTechnology.Barcode, Status = TagStatus.Active, Symbology = "identifier" };
            }
        }
        return result;
    }

    /// <summary>Describes what a scanned code is (for the UI): hex EPC, GS1 element string (with parsed AIs), or a plain code.</summary>
    public static object Describe(string code)
    {
        var n = Normalize(code);
        if (IsHexEpc(n)) return new { kind = "epc", code = n, scheme = Rfid.Domain.Epc.Gs1.Scheme(n) };
        if (Rfid.Domain.Epc.Gs1ElementString.LooksLikeGs1(n)) { var ai = Rfid.Domain.Epc.Gs1ElementString.Parse(n); return new { kind = "gs1", code = n, ai, candidateEpcs = Rfid.Domain.Epc.Gs1ElementString.CandidateEpcs(ai).Take(3) }; }
        return new { kind = "code", code = n };
    }
}
