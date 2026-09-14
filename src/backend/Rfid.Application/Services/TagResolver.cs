using Microsoft.EntityFrameworkCore;
using Rfid.Application.Contracts;
using Rfid.Domain.Entities;

namespace Rfid.Application.Services;

public class TagResolver
{
    private readonly IAppDb _db;
    public TagResolver(IAppDb db) => _db = db;

    public static string Normalize(string epc) => epc.Trim().ToUpperInvariant();

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
        return tags.GroupBy(t => t.Epc).ToDictionary(g => g.Key, g => g.First());
    }
}
