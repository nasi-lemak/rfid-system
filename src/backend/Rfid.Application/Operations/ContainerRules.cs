using Microsoft.EntityFrameworkCore;
using Rfid.Application.Contracts;
using Rfid.Domain.Entities;

namespace Rfid.Application.Operations;

/// <summary>
/// Container-hierarchy invariants shared by every code path that can re-parent an item
/// (Pack effect, manual item edits, imports). Items form a forest via <see cref="Item.ParentItemId"/>;
/// nesting is bounded and cycles are refused at write time rather than trusted to be absent.
/// </summary>
public static class ContainerRules
{
    public const int MaxDepth = 32;

    /// <summary>True when <paramref name="candidateAncestorId"/> is at or above <paramref name="node"/> in the container tree (bounded walk).</summary>
    public static async Task<bool> IsDescendantAsync(IAppDb db, Item node, Guid candidateAncestorId, CancellationToken ct = default)
    {
        if (node.Id == candidateAncestorId) return true;
        var current = node.ParentItemId; var depth = 0;
        while (current.HasValue && depth++ < MaxDepth)
        {
            if (current == candidateAncestorId) return true;
            current = await db.Items.Where(i => i.Id == current).Select(i => i.ParentItemId).FirstOrDefaultAsync(ct);
        }
        return false;
    }

    /// <summary>Overload by id: is <paramref name="nodeId"/> inside <paramref name="candidateAncestorId"/>?</summary>
    public static async Task<bool> IsDescendantAsync(IAppDb db, Guid nodeId, Guid candidateAncestorId, CancellationToken ct = default)
    {
        var node = await db.Items.Where(i => i.Id == nodeId).Select(i => new Item { Id = i.Id, ParentItemId = i.ParentItemId }).FirstOrDefaultAsync(ct);
        return node != null && await IsDescendantAsync(db, node, candidateAncestorId, ct);
    }
}
