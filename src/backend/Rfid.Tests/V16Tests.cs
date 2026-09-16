using Microsoft.EntityFrameworkCore;
using Rfid.Application.Contracts;
using Rfid.Application.Services;
using Rfid.Domain;
using Rfid.Domain.Entities;
using Rfid.Domain.Epc;
using Xunit;

namespace Rfid.Tests;

public class Sscc96AndEncodingTests
{
    [Fact]
    public void Sscc96_round_trips_and_validates()
    {
        var epc = Sscc96.Encode("0614141", 1, 234567890, filter: 2);
        Assert.Equal(24, epc.Length); Assert.StartsWith("31", epc);
        var d = Sscc96.Decode(epc)!.Value;
        Assert.Equal("0614141", d.companyPrefix); Assert.Equal(1, d.extensionDigit); Assert.Equal(234567890UL, d.serial);
        Assert.Equal(10, Sscc96.SerialDigitsFor("0614141"));
        Assert.Throws<ArgumentException>(() => Sscc96.Encode("0614141", 1, 1_000_000_000)); // 10 digits: 9 left after the extension digit
        Assert.Throws<ArgumentException>(() => Sscc96.Encode("12345", 1, 1));
        Assert.Equal("SSCC-96", Gs1.Scheme(epc));
        var dec = EncodingService.Decode(epc); Assert.Contains("SSCC-96", System.Text.Json.JsonSerializer.Serialize(dec));
    }

    [Fact]
    public async Task Pools_allocate_contiguous_serials_and_the_wizard_creates_binds_and_previews()
    {
        var h = new TestHost();
        var tote = await h.InstallTypeAsync("returnable-assets", "RTI");
        var store = h.Loc(LocationKind.Room, "Store");
        var untagged = new List<Item>(); for (var i = 0; i < 3; i++) { var it = new Item { TenantId = h.Ctx.TenantId, ItemTypeId = tote.Id, Identifier = $"T-{i}", Name = $"Tote {i}", CurrentLocationId = store.Id }; h.Db.Items.Add(it); untagged.Add(it); }
        var pool = new SerialPool { TenantId = h.Ctx.TenantId, Name = "Totes", Scheme = EpcScheme.Grai96, CompanyPrefix = "0614141", Reference = "12345", Filter = 0, NextSerial = 100, MaxSerial = 199 };
        h.Db.SerialPools.Add(pool); await h.SaveAsync();
        var svc = new EncodingService(h.Db, h.Ctx);
        var (p, first, last) = await svc.AllocateAsync(pool.Id, 5);
        Assert.Equal(100UL, first); Assert.Equal(104UL, last); Assert.Equal(105UL, p.NextSerial);
        await Assert.ThrowsAsync<DomainException>(() => svc.AllocateAsync(pool.Id, 1000)); // exceeds MaxSerial
        // Manual preview: duplicates are detected against existing tags.
        h.Db.Tags.Add(new Tag { TenantId = h.Ctx.TenantId, Epc = Sgtin96.Encode("0614141", "812345", 7, 1) }); await h.SaveAsync();
        var prev = await svc.PreviewAsync(new EncodingRequest { Scheme = EpcScheme.Sgtin96, CompanyPrefix = "0614141", Reference = "812345", Filter = 1, FirstSerial = 5, Count = 5, Mode = "tags" });
        Assert.Equal(5, prev.Count); Assert.Equal(1, prev.Duplicates); Assert.Contains(prev.Warnings, w => w.Contains("already exist"));
        await Assert.ThrowsAsync<DomainException>(() => svc.CommitAsync(new EncodingRequest { Scheme = EpcScheme.Sgtin96, CompanyPrefix = "0614141", Reference = "812345", Filter = 1, FirstSerial = 5, Count = 5, Mode = "tags" }));
        // Commit from the pool, binding to the untagged totes: count is reduced to the targets, tags become Active, events written.
        var batch = await svc.CommitAsync(new EncodingRequest { PoolId = pool.Id, Count = 10, Mode = "bind", ItemTypeId = tote.Id, Name = "Tote roll 1" });
        Assert.Equal(3, batch.Count); Assert.Equal(105UL, batch.FirstSerial); Assert.Equal(107UL, batch.LastSerial); Assert.Equal(3, batch.ItemsBound);
        Assert.Equal(3, await h.Db.Tags.CountAsync(t => t.EncodingBatchId == batch.Id && t.Status == TagStatus.Active));
        Assert.Equal(108UL, (await h.Db.SerialPools.FirstAsync()).NextSerial);
        Assert.Equal(3, await h.Db.ItemEvents.CountAsync(e => e.Type == ItemEventType.Commissioned));
        var grai = Gs1.DecodeGrai96(batch.FirstEpc!)!.Value; Assert.Equal("12345", grai.assetType); Assert.Equal(105UL, grai.serial);
        // Create items + tags.
        var b2 = await svc.CommitAsync(new EncodingRequest { PoolId = pool.Id, Count = 4, Mode = "items", ItemTypeId = tote.Id, IdentifierPrefix = "TOTE-" });
        Assert.Equal(4, b2.ItemsBound); Assert.Equal(1, await h.Db.Items.CountAsync(i => i.Identifier == "TOTE-108"));
        // Unassigned tags.
        var b3 = await svc.CommitAsync(new EncodingRequest { Scheme = EpcScheme.Sscc96, CompanyPrefix = "0614141", Reference = "3", Filter = 0, FirstSerial = 1, Count = 2, Mode = "tags" });
        Assert.Equal(2, await h.Db.Tags.CountAsync(t => t.EncodingBatchId == b3.Id && t.Status == TagStatus.Unassigned));
    }
}

public class AnomalyDetectionTests
{
    private static async Task<(TestHost h, Device reader, Device quiet, Location zone)> SeedAsync()
    {
        var h = new TestHost();
        var asset = await h.InstallTypeAsync("asset-management", "ASSET");
        var zone = h.Loc(LocationKind.Zone, "Dock");
        var reader = new Device { TenantId = h.Ctx.TenantId, Name = "Dock portal", Kind = DeviceKind.Portal };
        var quiet = new Device { TenantId = h.Ctx.TenantId, Name = "Night gate", Kind = DeviceKind.Gate };
        h.Db.Devices.AddRange(reader, quiet);
        var (item, _) = h.Item(asset, "A-1", zone);
        await h.SaveAsync();
        return (h, reader, quiet, zone);
    }

    [Fact]
    public async Task Detects_spike_drop_off_hours_and_unknown_surge_but_not_normal_traffic()
    {
        var (h, reader, quiet, zone) = await SeedAsync();
        var now = new DateTime(2026, 9, 10, 15, 30, 0, DateTimeKind.Utc); // window = 14:00–15:00
        var rnd = new Random(1);
        // Baseline: 14 days, business hours 08–17 → ~40 reads/hour on the dock portal, none at night.
        for (var d = 1; d <= 14; d++) for (var hr = 8; hr < 17; hr++) for (var k = 0; k < 38 + rnd.Next(5); k++)
            h.Db.TagReads.Add(new TagRead { TenantId = h.Ctx.TenantId, Epc = "E1", DeviceId = reader.Id, LocationId = zone.Id, ItemId = Guid.NewGuid(), ReadAt = now.Date.AddDays(-d).AddHours(hr).AddMinutes(rnd.Next(60)) });
        await h.SaveAsync();
        var svc = new AnomalyService(h.Db, h.Ctx);
        // Normal current hour → nothing.
        for (var k = 0; k < 41; k++) h.Db.TagReads.Add(new TagRead { TenantId = h.Ctx.TenantId, Epc = "E1", DeviceId = reader.Id, LocationId = zone.Id, ItemId = Guid.NewGuid(), ReadAt = now.Date.AddHours(14).AddMinutes(rnd.Next(60)) });
        await h.SaveAsync();
        Assert.Empty(await svc.DetectAsync(now));
        // Spike: 400 reads this hour.
        for (var k = 0; k < 360; k++) h.Db.TagReads.Add(new TagRead { TenantId = h.Ctx.TenantId, Epc = "E1", DeviceId = reader.Id, LocationId = zone.Id, ItemId = Guid.NewGuid(), ReadAt = now.Date.AddHours(14).AddMinutes(rnd.Next(60)) });
        await h.SaveAsync();
        var f = await svc.DetectAsync(now);
        var spike = Assert.Single(f, x => x.Kind == AnomalyKind.ReadRateSpike); Assert.Equal(reader.Id, spike.DeviceId); Assert.True(spike.Score > 4, $"score {spike.Score}"); Assert.Equal(401, spike.Observed);
        // Drop: an hour with 0 reads where ~40 are expected (17:00–18:00 has no baseline, so test 10:00 on the next day).
        var later = now.Date.AddDays(1).AddHours(11).AddMinutes(5);
        var drop = Assert.Single(await svc.DetectAsync(later), x => x.Kind == AnomalyKind.ReadRateDrop); Assert.Equal(0, drop.Observed); Assert.InRange(drop.Expected, 33, 43);
        // Off-hours: 30 reads at 02:00 on a location that is silent at night.
        var night = now.Date.AddDays(1).AddHours(3).AddMinutes(1);
        for (var k = 0; k < 30; k++) h.Db.TagReads.Add(new TagRead { TenantId = h.Ctx.TenantId, Epc = "E1", DeviceId = quiet.Id, LocationId = zone.Id, ItemId = Guid.NewGuid(), ReadAt = night.Date.AddHours(2).AddMinutes(rnd.Next(60)) });
        // Unknown surge on the quiet gate: 25 unknown-tag reads in the same hour, none before.
        for (var k = 0; k < 25; k++) h.Db.TagReads.Add(new TagRead { TenantId = h.Ctx.TenantId, Epc = "FFFF", DeviceId = quiet.Id, LocationId = zone.Id, ItemId = null, ReadAt = night.Date.AddHours(2).AddMinutes(rnd.Next(60)) });
        await h.SaveAsync();
        var nf = await svc.DetectAsync(night);
        Assert.Contains(nf, x => x.Kind == AnomalyKind.OffHoursActivity && x.LocationId == zone.Id);
        Assert.Contains(nf, x => x.Kind == AnomalyKind.UnknownTagSurge && x.DeviceId == quiet.Id);
        // Persisting: alerts for strong findings, dedup on the second run.
        var (created, updated) = await svc.RunAsync(night);
        Assert.True(created >= 2); Assert.Equal(0, updated);
        Assert.True(await h.Db.Alerts.AnyAsync(a => a.Source == "anomaly"));
        var (c2, u2) = await svc.RunAsync(night.AddMinutes(15));
        Assert.Equal(0, c2); Assert.True(u2 >= 2);
        var profile = await svc.ProfileAsync(reader.Id, null, now);
        Assert.Equal(24, profile.Count); Assert.InRange(profile[10].Baseline, 35, 45); Assert.Equal(0, profile[3].Baseline);
    }

    [Fact]
    public async Task Detects_items_flapping_between_two_zones()
    {
        var h = new TestHost();
        var asset = await h.InstallTypeAsync("asset-management", "ASSET");
        var a = h.Loc(LocationKind.Zone, "A"); var b = h.Loc(LocationKind.Zone, "B");
        var (item, _) = h.Item(asset, "A-1", a); await h.SaveAsync();
        var now = new DateTime(2026, 9, 10, 15, 30, 0, DateTimeKind.Utc);
        for (var i = 0; i < 8; i++) h.Db.ItemEvents.Add(new ItemEvent { TenantId = h.Ctx.TenantId, ItemId = item.Id, Type = ItemEventType.Moved, FromLocationId = i % 2 == 0 ? a.Id : b.Id, ToLocationId = i % 2 == 0 ? b.Id : a.Id, OccurredAt = now.AddHours(-i - 1) }); // detection windows are hour-aligned
        await h.SaveAsync();
        var f = await new AnomalyService(h.Db, h.Ctx).DetectAsync(now);
        var flap = Assert.Single(f, x => x.Kind == AnomalyKind.ItemFlapping); Assert.Equal(item.Id, flap.ItemId); Assert.Equal(8, flap.Observed);
    }
}

public class AuditAndRetentionTests
{
    [Fact]
    public void Audit_redaction_masks_secrets_and_truncates()
    {
        var body = Rfid.Api.Auth.AuditMiddleware.Redact("{\"email\":\"a@b.c\",\"password\":\"hunter2\",\"config\":{\"authToken\":\"x\",\"url\":\"https://x\"},\"list\":[{\"token\":\"t\"}]}")!;
        Assert.DoesNotContain("hunter2", body); Assert.Contains("\"password\":\"***\"", body); Assert.Contains("\"authToken\":\"***\"", body); Assert.Contains("\"token\":\"***\"", body); Assert.Contains("https://x", body);
        Assert.Null(Rfid.Api.Auth.AuditMiddleware.Redact("  "));
        var big = Rfid.Api.Auth.AuditMiddleware.Redact("{\"x\":\"" + new string('a', 10000) + "\"}")!;
        Assert.True(big.Length < 4100); Assert.EndsWith("…", big);
    }

    [Fact]
    public async Task Retention_defaults_counts_and_deletes_expired_rows_only()
    {
        var h = new TestHost();
        var asset = await h.InstallTypeAsync("asset-management", "ASSET");
        var store = h.Loc(LocationKind.Room, "Store"); var (item, _) = h.Item(asset, "A-1", store);
        var now = new DateTime(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc);
        for (var d = 0; d < 120; d += 10) h.Db.TagReads.Add(new TagRead { TenantId = h.Ctx.TenantId, Epc = "E", ItemId = item.Id, ReadAt = now.AddDays(-d) });
        for (var d = 0; d < 120; d += 10) h.Db.ItemEvents.Add(new ItemEvent { TenantId = h.Ctx.TenantId, ItemId = item.Id, Type = ItemEventType.Seen, OccurredAt = now.AddDays(-d) });
        h.Db.Alerts.Add(new Alert { TenantId = h.Ctx.TenantId, Message = "old closed", Status = AlertStatus.Closed, RaisedAt = now.AddDays(-400), ClosedAt = now.AddDays(-400) });
        h.Db.Alerts.Add(new Alert { TenantId = h.Ctx.TenantId, Message = "old open", Status = AlertStatus.Open, RaisedAt = now.AddDays(-400) });
        await h.SaveAsync();
        var svc = new RetentionService(h.Db, h.Ctx) { BatchSize = 2 };
        var policies = await svc.EnsureDefaultsAsync();
        Assert.Equal(RetentionService.Datasets.Length, policies.Count);
        Assert.Equal(90, policies.Single(p => p.Dataset == "tag_reads").RetainDays);
        Assert.Null(policies.Single(p => p.Dataset == "item_events").RetainDays); Assert.False(policies.Single(p => p.Dataset == "item_events").Enabled);
        var counts = await svc.CountsAsync(now);
        Assert.Equal((12, 2), counts["tag_reads"]); // only the 100- and 110-day-old reads are past 90 d (the 90-day one is exactly on the cutoff)
        Assert.Equal(0, counts["item_events"].expired);
        Assert.Equal(1, counts["alerts"].expired);
        var deleted = await svc.ApplyAsync(now);
        Assert.Equal(2, deleted["tag_reads"]); Assert.Equal(10, await h.Db.TagReads.CountAsync());
        Assert.Equal(12, await h.Db.ItemEvents.CountAsync()); // protected by default
        Assert.Equal(1, deleted["alerts"]); Assert.Equal(1, await h.Db.Alerts.CountAsync()); Assert.Equal("old open", (await h.Db.Alerts.SingleAsync()).Message);
        Assert.Equal(2, policies.Single(p => p.Dataset == "tag_reads").LastDeleted);
    }
}
