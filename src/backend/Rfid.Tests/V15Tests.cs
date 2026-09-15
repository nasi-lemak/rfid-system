using Microsoft.EntityFrameworkCore;
using Rfid.Application.Contracts;
using Rfid.Application.Services;
using Rfid.Domain;
using Rfid.Domain.Entities;
using Xunit;

namespace Rfid.Tests;

public class DeviceHealthAndFirmwareTests
{
    [Fact]
    public async Task Health_follows_the_sla_and_raises_and_clears_offline_alerts()
    {
        var h = new TestHost(); var sender = new NullNotificationSender();
        var notif = new NotificationService(h.Db, sender);
        h.Db.NotificationChannels.Add(new NotificationChannel { TenantId = h.Ctx.TenantId, Name = "Ops Teams", Kind = NotificationKind.Teams, CatchAll = true, MinSeverity = Severity.Critical, Config = new() { ["url"] = "https://example/hook" } });
        var dev = new Device { TenantId = h.Ctx.TenantId, Name = "Dock portal", Kind = DeviceKind.Portal, HeartbeatSlaMinutes = 10 };
        h.Db.Devices.Add(dev); await h.SaveAsync();
        var svc = new DeviceHealthService(h.Db, h.Ctx, notif);
        var t0 = new DateTime(2026, 5, 1, 8, 0, 0, DateTimeKind.Utc);
        Assert.Equal(DeviceHealth.Unknown, svc.ComputeHealth(dev, t0));
        await svc.RecordAsync(dev.Id, new HeartbeatRequest { FirmwareVersion = "7.2.1", CpuPercent = 12, At = t0 });
        Assert.Equal(DeviceHealth.Online, dev.Health); Assert.Equal("7.2.1", dev.FirmwareVersion);
        Assert.Equal(DeviceHealth.Degraded, svc.ComputeHealth(dev, t0.AddMinutes(15)));
        Assert.Equal(1, await svc.EvaluateAsync(t0.AddMinutes(25)));
        Assert.Equal(DeviceHealth.Offline, dev.Health);
        var alert = await h.Db.Alerts.SingleAsync(a => a.Source == "device-health");
        Assert.Equal(Severity.Critical, alert.Severity); Assert.Equal(dev.Id, alert.DeviceId);
        Assert.Single(sender.Sent); Assert.Contains("Reader offline", sender.Sent[0].message.Subject);
        Assert.Equal(0, await svc.EvaluateAsync(t0.AddMinutes(30))); // no duplicate alert while still offline
        await svc.RecordAsync(dev.Id, new HeartbeatRequest { At = t0.AddMinutes(40) });
        Assert.Equal(DeviceHealth.Online, dev.Health);
        Assert.Equal(AlertStatus.Closed, (await h.Db.Alerts.SingleAsync(a => a.Source == "device-health")).Status);
        var up = (await svc.UptimeAsync(TimeSpan.FromHours(1), t0.AddMinutes(50))).Single();
        Assert.Equal(2, up.Heartbeats); Assert.Equal(6, up.Expected); Assert.InRange(up.UptimePercent, 33, 34);
    }

    [Fact]
    public async Task Firmware_rollout_is_polled_reported_and_applied()
    {
        var h = new TestHost();
        var svc = new DeviceHealthService(h.Db, h.Ctx);
        var a = new Device { TenantId = h.Ctx.TenantId, Name = "R1", Kind = DeviceKind.Fixed, FirmwareVersion = "7.1.0", LastSeenAt = DateTime.UtcNow, Health = DeviceHealth.Online };
        var b = new Device { TenantId = h.Ctx.TenantId, Name = "R2", Kind = DeviceKind.Fixed, FirmwareVersion = "7.2.0" };
        var rel = new FirmwareRelease { TenantId = h.Ctx.TenantId, Vendor = "Impinj", Model = "R700", Version = "7.2.0", Url = "https://fw/r700-7.2.0.upg" };
        h.Db.Devices.AddRange(a, b); h.Db.FirmwareReleases.Add(rel); await h.SaveAsync();
        var created = await svc.RolloutAsync(rel.Id, new[] { a.Id, b.Id });
        Assert.Single(created); Assert.Equal(a.Id, created[0].DeviceId); Assert.Equal("7.1.0", created[0].FromVersion); // b already on the version
        Assert.Null(await svc.PendingForDeviceAsync(b.Id));
        var pending = await svc.PendingForDeviceAsync(a.Id);
        Assert.NotNull(pending); Assert.Equal("7.2.0", pending!.Version); Assert.Equal(FirmwareRolloutStatus.Sent, (await h.Db.FirmwareRollouts.SingleAsync()).Status);
        await svc.ReportAsync(pending.RolloutId, FirmwareRolloutStatus.Installing);
        Assert.Equal(DeviceHealth.Updating, a.Health);
        await svc.ReportAsync(pending.RolloutId, FirmwareRolloutStatus.Done);
        Assert.Equal("7.2.0", a.FirmwareVersion); Assert.Equal(DeviceHealth.Online, a.Health);
        Assert.Null(await svc.PendingForDeviceAsync(a.Id));
        // A failed rollout raises a warning alert.
        var rel2 = new FirmwareRelease { TenantId = h.Ctx.TenantId, Vendor = "Impinj", Model = "R700", Version = "7.3.0" }; h.Db.FirmwareReleases.Add(rel2); await h.SaveAsync();
        var r2 = (await svc.RolloutAsync(rel2.Id, new[] { a.Id })).Single();
        await svc.ReportAsync(r2.Id, FirmwareRolloutStatus.Failed, "checksum mismatch");
        Assert.Equal("7.2.0", a.FirmwareVersion);
        Assert.Contains("checksum mismatch", (await h.Db.Alerts.SingleAsync(x => x.Source == "firmware")).Message);
    }
}

public class GeofenceTests
{
    [Fact]
    public void Geometry_distance_circle_and_polygon()
    {
        Assert.InRange(GeoService.DistanceM(51.5007, -0.1246, 51.5014, -0.1419), 1150, 1250); // Big Ben → Buckingham Palace ≈ 1.2 km
        var circle = new GeoFence { Kind = GeoFenceKind.Circle, CenterLat = 51.5, CenterLng = -0.12, RadiusM = 500 };
        Assert.True(GeoService.Contains(circle, 51.501, -0.121)); Assert.False(GeoService.Contains(circle, 51.51, -0.12));
        var poly = new GeoFence { Kind = GeoFenceKind.Polygon, Points = new() { new(0, 0), new(0, 1), new(1, 1), new(1, 0) } };
        Assert.True(GeoService.Contains(poly, 0.5, 0.5)); Assert.False(GeoService.Contains(poly, 1.5, 0.5)); Assert.False(GeoService.Contains(poly, -0.1, 0.5));
    }

    [Fact]
    public async Task Gps_fixes_drive_enter_exit_events_location_changes_alerts_and_dwell()
    {
        var h = new TestHost(); var sender = new NullNotificationSender();
        var notif = new NotificationService(h.Db, sender);
        var vehicle = await h.InstallTypeAsync("fleet-aviation", "VEHICLE");
        var depot = h.Loc(LocationKind.Yard, "Depot yard"); var customer = h.Loc(LocationKind.Customer, "Acme site");
        var (truck, tag) = h.Item(vehicle, "TRK-01", depot);
        var tracker = new Device { TenantId = h.Ctx.TenantId, Name = "Telematics 01", Kind = DeviceKind.Vehicle, TrackedItemId = truck.Id };
        h.Db.Devices.Add(tracker);
        h.Db.GeoFences.Add(new GeoFence { TenantId = h.Ctx.TenantId, Name = "Depot", Kind = GeoFenceKind.Circle, CenterLat = 51.50, CenterLng = -0.10, RadiusM = 300, LocationId = depot.Id, Trigger = GeoFenceTrigger.Exit, Severity = Severity.Info });
        h.Db.GeoFences.Add(new GeoFence { TenantId = h.Ctx.TenantId, Name = "Acme", Kind = GeoFenceKind.Circle, CenterLat = 51.60, CenterLng = -0.20, RadiusM = 300, LocationId = customer.Id, Trigger = GeoFenceTrigger.Enter, Severity = Severity.Warning, MaxDwellMinutes = 60 });
        await h.SaveAsync();
        var geo = new GeoService(h.Db, h.Ctx, h.Rules, new NullLivePublisher(), notif) { MinMoveM = 10 };
        var t0 = new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc);
        // At the depot (inside fence 1) – first sighting creates state, no transition.
        var r1 = await geo.IngestAsync(new GpsBatchRequest { DeviceId = tracker.Id, Fixes = { new() { Lat = 51.5001, Lng = -0.1001, At = t0, SpeedKph = 0 } } });
        Assert.Equal(1, r1.Resolved); Assert.Equal(0, r1.Transitions); Assert.Equal(1, r1.Fixes);
        // Stationary jitter → no new fix rows.
        var r1b = await geo.IngestAsync(new GpsBatchRequest { DeviceId = tracker.Id, Fixes = { new() { Lat = 51.50011, Lng = -0.10011, At = t0.AddSeconds(30) } } });
        Assert.Equal(0, r1b.Fixes);
        // Leaves the depot (exit alert), drives, arrives at Acme (enter alert + location change).
        var r2 = await geo.IngestAsync(new GpsBatchRequest { Fixes = { new() { Epc = tag.Epc, Lat = 51.52, Lng = -0.12, At = t0.AddMinutes(10), SpeedKph = 45 }, new() { Identifier = "TRK-01", Lat = 51.6002, Lng = -0.2001, At = t0.AddMinutes(40), SpeedKph = 5 } } });
        Assert.Equal(2, r2.Resolved); Assert.Equal(2, r2.Transitions); Assert.Equal(2, r2.Alerts);
        var fresh = await h.Db.Items.FirstAsync(i => i.Id == truck.Id);
        Assert.Equal(customer.Id, fresh.CurrentLocationId); Assert.Equal(51.6002, fresh.Latitude);
        var events = await h.Db.ItemEvents.Where(e => e.ItemId == truck.Id && (e.Type == ItemEventType.GeofenceEntered || e.Type == ItemEventType.GeofenceExited)).OrderBy(e => e.OccurredAt).ToListAsync();
        Assert.Equal(new[] { ItemEventType.GeofenceExited, ItemEventType.GeofenceEntered }, events.Select(e => e.Type));
        Assert.Equal("Acme site", events[1].Data["movedTo"]?.ToString());
        Assert.Equal(2, await h.Db.Alerts.CountAsync(a => a.Source == "geofence"));
        // Dwell: 61 minutes at Acme → one dwell alert, never twice.
        Assert.Equal(0, await geo.SweepDwellAsync(t0.AddMinutes(90)));
        Assert.Equal(1, await geo.SweepDwellAsync(t0.AddMinutes(101)));
        Assert.Equal(0, await geo.SweepDwellAsync(t0.AddMinutes(200)));
        Assert.Contains("has been in Acme", (await h.Db.Alerts.OrderByDescending(a => a.RaisedAt).FirstAsync()).Message);
        var map = await geo.MapAsync(TimeSpan.FromDays(365));
        Assert.Single(map.Items); Assert.Equal(new[] { "Acme" }, map.Items[0].Fences); Assert.Equal(1, map.Fences.Single(f => f.Name == "Acme").Inside);
        Assert.Equal(3, (await geo.TrackAsync(truck.Id, t0.AddHours(-1), t0.AddHours(2))).Count);
        // Unknown tracker → unknown.
        Assert.Equal(1, (await geo.IngestAsync(new GpsBatchRequest { Fixes = { new() { Epc = "DEADBEEF", Lat = 1, Lng = 1 } } })).Unknown);
    }
}

public class NotificationAndEscalationTests
{
    [Fact]
    public async Task Rule_channels_catch_all_and_escalation_policy_deliver_in_order()
    {
        var h = new TestHost(); var sender = new NullNotificationSender();
        var notif = new NotificationService(h.Db, sender);
        var email = new NotificationChannel { TenantId = h.Ctx.TenantId, Name = "Ops e-mail", Kind = NotificationKind.Email, Config = new() { ["host"] = "smtp", ["to"] = "ops@example.com" } };
        var sms = new NotificationChannel { TenantId = h.Ctx.TenantId, Name = "On-call SMS", Kind = NotificationKind.Sms, Config = new() { ["to"] = "+441234" } };
        var slack = new NotificationChannel { TenantId = h.Ctx.TenantId, Name = "All critical → Slack", Kind = NotificationKind.Slack, CatchAll = true, MinSeverity = Severity.Critical, Config = new() { ["url"] = "https://hooks.slack" } };
        var disabled = new NotificationChannel { TenantId = h.Ctx.TenantId, Name = "Old", Kind = NotificationKind.Webhook, Enabled = false, CatchAll = true, MinSeverity = Severity.Info };
        h.Db.NotificationChannels.AddRange(email, sms, slack, disabled);
        var policy = new EscalationPolicy { TenantId = h.Ctx.TenantId, Name = "Critical on-call", Steps = new() { new() { AfterMinutes = 0, ChannelIds = { email.Id } }, new() { AfterMinutes = 15, ChannelIds = { sms.Id }, Message = "Still unacknowledged" } }, RepeatLastStep = true };
        h.Db.EscalationPolicies.Add(policy); await h.SaveAsync();
        var raised = new DateTime(2026, 7, 1, 10, 0, 0, DateTimeKind.Utc);
        var alert = new Alert { TenantId = h.Ctx.TenantId, Severity = Severity.Critical, Message = "Cabinet door open", RaisedAt = raised };
        h.Db.Alerts.Add(alert);
        var rule = new Rule { Name = "Door", NotifyChannelIds = { email.Id }, EscalationPolicyId = policy.Id };
        var sent = await notif.OnAlertRaisedAsync(alert, rule);
        await h.SaveAsync();
        Assert.Equal(2, sent); // e-mail (rule + step 0, deduplicated) and catch-all Slack; disabled channel skipped
        Assert.Equal(new[] { "All critical → Slack", "Ops e-mail" }, sender.Sent.Select(s => s.channel.Name).OrderBy(n => n));
        Assert.Equal(1, alert.EscalationLevel); Assert.Equal(raised.AddMinutes(15), alert.NextEscalationAt);
        Assert.Equal(0, await notif.EscalateDueAsync(raised.AddMinutes(10)));
        Assert.Equal(1, await notif.EscalateDueAsync(raised.AddMinutes(16)));
        Assert.Equal("On-call SMS", sender.Sent[^1].channel.Name); Assert.Contains("Escalation #2", sender.Sent[^1].message.Subject); Assert.Contains("Still unacknowledged", sender.Sent[^1].message.Body);
        Assert.Equal(2, alert.EscalationLevel); Assert.Equal(raised.AddMinutes(31), alert.NextEscalationAt); // repeats the last step
        alert.Status = AlertStatus.Acknowledged; alert.NextEscalationAt = null; await h.SaveAsync();
        Assert.Equal(0, await notif.EscalateDueAsync(raised.AddHours(2)));
        var logs = await h.Db.NotificationLogs.OrderBy(l => l.SentAt).ToListAsync();
        Assert.Equal(3, logs.Count); Assert.All(logs, l => Assert.Equal(NotificationStatus.Sent, l.Status));
        // A warning alert with no rule: default policy at Warning applies, catch-all Slack (Critical) does not.
        h.Db.EscalationPolicies.Add(new EscalationPolicy { TenantId = h.Ctx.TenantId, Name = "Default", IsDefault = true, MinSeverity = Severity.Warning, Steps = new() { new() { AfterMinutes = 30, ChannelIds = { email.Id } } } }); await h.SaveAsync();
        var warn = new Alert { TenantId = h.Ctx.TenantId, Severity = Severity.Warning, Message = "Low stock", RaisedAt = raised }; h.Db.Alerts.Add(warn);
        Assert.Equal(0, await notif.OnAlertRaisedAsync(warn, null));
        Assert.Equal(raised.AddMinutes(30), warn.NextEscalationAt); Assert.NotNull(warn.EscalationPolicyId);
    }

    [Fact]
    public async Task Rule_engine_notifies_when_an_alert_is_created()
    {
        var h = new TestHost(); var sender = new NullNotificationSender();
        var notif = new NotificationService(h.Db, sender);
        var ch = new NotificationChannel { TenantId = h.Ctx.TenantId, Name = "Teams", Kind = NotificationKind.Teams, Config = new() { ["url"] = "https://x" } };
        h.Db.NotificationChannels.Add(ch);
        var asset = await h.InstallTypeAsync("asset-management", "ASSET");
        var store = h.Loc(LocationKind.Room, "Store"); var gate = h.Loc(LocationKind.Gate, "Exit gate");
        var (item, _) = h.Item(asset, "A-1", store);
        h.Db.Rules.Add(new Rule { TenantId = h.Ctx.TenantId, Name = "Left building", Trigger = ItemEventType.Moved, Action = RuleAction.Notify, Severity = Severity.Critical, Conditions = { new() { Field = "toLocation.kind", Op = "eq", Value = "Gate" } }, NotifyChannelIds = { ch.Id } });
        await h.SaveAsync();
        var engine = new RuleEngine(h.Db, h.Ctx, new NullLivePublisher(), new NullWebhookDispatcher(), notif);
        var ev = new ItemEvent { TenantId = h.Ctx.TenantId, ItemId = item.Id, Type = ItemEventType.Moved, FromLocationId = store.Id, ToLocationId = gate.Id };
        var alerts = await engine.EvaluateAsync(new RuleContext { Event = ev, Item = item, ItemType = asset, FromLocation = store, ToLocation = gate });
        Assert.Single(alerts); Assert.Single(sender.Sent); Assert.Contains("Left building", sender.Sent[0].message.Subject);
    }
}

public class DashboardAndAnalyticsTests
{
    [Fact]
    public async Task Widgets_evaluate_against_live_data()
    {
        var h = new TestHost();
        var asset = await h.InstallTypeAsync("asset-management", "ASSET");
        var store = h.Loc(LocationKind.Room, "Store"); var lab = h.Loc(LocationKind.Room, "Lab");
        h.Item(asset, "A-1", store); h.Item(asset, "A-2", store); var (a3, _) = h.Item(asset, "A-3", lab); a3.DueBackAt = DateTime.UtcNow.AddDays(-1); a3.CustodianPartyId = h.Party("Ann").Id;
        h.Db.Devices.Add(new Device { TenantId = h.Ctx.TenantId, Name = "R", Kind = DeviceKind.Fixed, Health = DeviceHealth.Offline });
        h.Db.Alerts.Add(new Alert { TenantId = h.Ctx.TenantId, Severity = Severity.Critical, Message = "x" });
        await h.SaveAsync();
        var svc = new DashboardService(h.Db);
        var widgets = new List<DashboardWidget>
        {
            new() { Id = "a", Type = "stat", Config = new() { ["filter"] = "items" } },
            new() { Id = "b", Type = "stat", Config = new() { ["filter"] = "overdue" } },
            new() { Id = "c", Type = "stat", Config = new() { ["filter"] = "items", ["locationId"] = store.Id.ToString() } },
            new() { Id = "d", Type = "breakdown", Config = new() { ["by"] = "location" } },
            new() { Id = "e", Type = "devices" },
            new() { Id = "f", Type = "list", Config = new() { ["source"] = "alerts" } },
            new() { Id = "g", Type = "trend", Config = new() { ["metric"] = "events", ["days"] = 7 } },
            new() { Id = "h", Type = "stat", Config = new() { ["filter"] = "bogus" } },
        };
        var res = await svc.EvaluateAsync(widgets);
        object? Data(string id) => res.Single(r => r.Id == id).Data;
        long Value(string id) => (long)Data(id)!.GetType().GetProperty("value")!.GetValue(Data(id))!;
        Assert.Equal(3, Value("a")); Assert.Equal(1, Value("b")); Assert.Equal(2, Value("c"));
        Assert.Equal(2, ((IEnumerable<object>)Data("d")!).Count());
        Assert.Null(res.Single(r => r.Id == "e").Error); Assert.Null(res.Single(r => r.Id == "f").Error); Assert.Null(res.Single(r => r.Id == "g").Error);
        Assert.Contains("Unknown stat filter", res.Single(r => r.Id == "h").Error);
        Assert.True(DashboardService.DefaultWidgets().Count >= 12);
    }

    [Fact]
    public async Task Analytics_trend_accuracy_and_alert_response()
    {
        var h = new TestHost();
        var asset = await h.InstallTypeAsync("asset-management", "ASSET");
        var store = h.Loc(LocationKind.Room, "Store"); h.Item(asset, "A-1", store); h.Item(asset, "A-2", store);
        var now = new DateTime(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc);
        h.Db.Stocktakes.Add(new Stocktake { TenantId = h.Ctx.TenantId, Name = "W1", LocationId = store.Id, Status = StocktakeStatus.Reconciled, ExpectedCount = 10, FoundCount = 9, MissingCount = 1, StartedAt = now.AddDays(-20), CompletedAt = now.AddDays(-20) });
        h.Db.Stocktakes.Add(new Stocktake { TenantId = h.Ctx.TenantId, Name = "W2", LocationId = store.Id, Status = StocktakeStatus.Applied, ExpectedCount = 10, FoundCount = 10, StartedAt = now.AddDays(-5), CompletedAt = now.AddDays(-5) });
        h.Db.Alerts.Add(new Alert { TenantId = h.Ctx.TenantId, Severity = Severity.Critical, Message = "a", RaisedAt = now.AddDays(-3), AcknowledgedAt = now.AddDays(-3).AddMinutes(20), ClosedAt = now.AddDays(-3).AddMinutes(60), Status = AlertStatus.Closed, EscalationLevel = 1 });
        h.Db.Alerts.Add(new Alert { TenantId = h.Ctx.TenantId, Severity = Severity.Critical, Message = "b", RaisedAt = now.AddDays(-1) });
        for (var i = 0; i < 5; i++) h.Db.ItemEvents.Add(new ItemEvent { TenantId = h.Ctx.TenantId, ItemId = Guid.NewGuid(), Type = ItemEventType.Moved, OccurredAt = now.AddDays(-i * 2) });
        await h.SaveAsync();
        var svc = new AnalyticsService(h.Db);
        var trend = await svc.TrendAsync("events", 14, "day", now);
        Assert.Equal(14, trend.Buckets.Count); Assert.Equal(5, trend.Total); Assert.Equal(1, trend.Buckets.Last().Count);
        var weekly = await svc.TrendAsync("events", 14, "week", now);
        Assert.Equal(5, weekly.Total); Assert.InRange(weekly.Buckets.Count, 2, 3);
        var acc = await svc.AccuracyAsync(365, now);
        Assert.Equal(2, acc.Stocktakes); Assert.Equal(95.0, acc.OverallAccuracy); Assert.Equal(90.0, acc.Rows[0].Accuracy);
        var resp = await svc.AlertResponseAsync(30, now);
        var crit = resp.Single(r => r.Severity == "Critical");
        Assert.Equal(2, crit.Raised); Assert.Equal(1, crit.Acknowledged); Assert.Equal(20, crit.AvgMinutesToAck); Assert.Equal(60, crit.AvgMinutesToClose); Assert.Equal(1, crit.OpenNow); Assert.Equal(1, crit.Escalated);
        var util = await svc.UtilizationAsync(30, now);
        Assert.Single(util); Assert.Equal(2, util[0].Items);
    }
}

public class WarehouseExportTests
{
    [Fact]
    public async Task Exports_parquet_and_csv_and_runs_incrementally()
    {
        var h = new TestHost();
        var asset = await h.InstallTypeAsync("asset-management", "ASSET");
        var store = h.Loc(LocationKind.Room, "Store"); var (item, _) = h.Item(asset, "A-1", store);
        var now = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
        for (var i = 1; i <= 3; i++) h.Db.ItemEvents.Add(new ItemEvent { TenantId = h.Ctx.TenantId, ItemId = item.Id, Type = ItemEventType.Seen, OccurredAt = now.AddHours(-i), Data = new() { ["rssi"] = -50 } });
        await h.SaveAsync();
        var root = Path.Combine(Path.GetTempPath(), "rfid-wh-" + Guid.NewGuid().ToString("N")[..8]);
        var svc = new WarehouseExportService(h.Db, h.Ctx) { RootPath = root, InitialWindow = TimeSpan.FromDays(1) };
        var run = await svc.ExportAsync("events", now.AddDays(-1), now.AddMinutes(1), "parquet", true);
        Assert.Null(run.Error); Assert.Equal(3, run.Rows); Assert.True(File.Exists(run.Path)); Assert.EndsWith(".parquet", run.Path); Assert.True(run.Bytes > 100);
        // Parquet magic bytes at both ends.
        var bytes = await File.ReadAllBytesAsync(run.Path);
        Assert.Equal("PAR1", System.Text.Encoding.ASCII.GetString(bytes[..4])); Assert.Equal("PAR1", System.Text.Encoding.ASCII.GetString(bytes[^4..]));
        var (type, rows) = await svc.QueryAsync("items", now.AddDays(-1), now);
        var csv = System.Text.Encoding.UTF8.GetString(await WarehouseExportService.SerializeAsync(type, rows, "csv"));
        Assert.StartsWith("Id,TenantId,Identifier,Name", csv); Assert.Contains("A-1", csv); Assert.Equal(2, csv.TrimEnd().Split('\n').Length);
        // Incremental: first run covers the initial window; the second run only the new minute and skips the daily snapshot.
        var first = await svc.RunIncrementalAsync(now);
        Assert.Equal(WarehouseExportService.Datasets.Length, first.Count); Assert.All(first, r => Assert.Null(r.Error));
        Assert.Equal(3, first.Single(r => r.Dataset == "events").Rows); Assert.Equal(1, first.Single(r => r.Dataset == "items").Rows);
        var second = await svc.RunIncrementalAsync(now.AddMinutes(5));
        Assert.DoesNotContain(second, r => r.Dataset == "items");
        Assert.Equal(now, second.Single(r => r.Dataset == "events").From); Assert.Equal(0, second.Single(r => r.Dataset == "events").Rows);
        Directory.Delete(root, true);
    }
}
