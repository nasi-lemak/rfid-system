using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Rfid.Application.Contracts;
using Rfid.Application.Inventory;
using Rfid.Application.Security;
using Rfid.Application.Templates;
using Rfid.Domain;
using Rfid.Domain.Entities;
using Xunit;

namespace Rfid.Tests;

public class StockAndPackagesTests
{
    [Fact]
    public async Task MoveQuantity_splits_and_merges_lot_rows_and_refuses_overdraw()
    {
        var h = new TestHost();
        var lot = await h.InstallTypeAsync("inventory", "STOCK-LOT");
        var binA = h.Loc(LocationKind.Bin, "A-01"); var binB = h.Loc(LocationKind.Bin, "B-01");
        var (src, tag) = h.Item(lot, "SKU1-L1", binA); src.Quantity = 100; src.LotNumber = "L1"; src.ExpiryDate = new DateOnly(2027, 1, 1);
        await h.SaveAsync();

        var r1 = await h.Ops.ProcessAsync(new OperationRequest { Type = OperationType.MoveQuantity, ToLocationId = binB.Id, Lines = { new() { Epc = tag.Epc, Quantity = 30 } } });
        Assert.Equal(1, r1.Ok);
        var source = await h.Db.Items.FirstAsync(i => i.Id == src.Id);
        var target = await h.Db.Items.FirstAsync(i => i.Id != src.Id && i.ItemTypeId == lot.Id);
        Assert.Equal(70, source.Quantity); Assert.Equal(binA.Id, source.CurrentLocationId);
        Assert.Equal(30, target.Quantity); Assert.Equal(binB.Id, target.CurrentLocationId); Assert.Equal("L1", target.LotNumber); Assert.Equal(src.ExpiryDate, target.ExpiryDate); Assert.Equal("SKU1-L1@A-01".Replace("A-01", "B-01"), target.Identifier);
        Assert.Equal(2, await h.Db.ItemEvents.CountAsync(e => e.Type == ItemEventType.QuantityChanged && e.OperationId == r1.OperationId));

        var r2 = await h.Ops.ProcessAsync(new OperationRequest { Type = OperationType.MoveQuantity, ToLocationId = binB.Id, Lines = { new() { Epc = tag.Epc, Quantity = 20 } } });
        Assert.Equal(1, r2.Ok);
        Assert.Equal(50, (await h.Db.Items.FirstAsync(i => i.Id == target.Id)).Quantity);                        // merged into the existing lot row
        Assert.Equal(2, await h.Db.Items.CountAsync(i => i.ItemTypeId == lot.Id));

        var over = await h.Ops.ProcessAsync(new OperationRequest { Type = OperationType.MoveQuantity, ToLocationId = binB.Id, Lines = { new() { Epc = tag.Epc, Quantity = 500 } } });
        Assert.Equal(1, over.Rejected); Assert.Contains("available", over.Lines[0].Message);
        var same = await h.Ops.ProcessAsync(new OperationRequest { Type = OperationType.MoveQuantity, ToLocationId = binA.Id, Lines = { new() { Epc = tag.Epc, Quantity = 1 } } });
        Assert.Equal(1, same.Rejected);
        Assert.Equal(50, (await h.Db.Items.FirstAsync(i => i.Id == src.Id)).Quantity);
    }

    [Fact]
    public async Task Stock_balances_summary_movements_and_fefo_allocation()
    {
        var h = new TestHost();
        var lot = await h.InstallTypeAsync("inventory", "STOCK-LOT");            // reorder point 10
        var binA = h.Loc(LocationKind.Bin, "A-01"); var binB = h.Loc(LocationKind.Bin, "B-01");
        var (l1, t1) = h.Item(lot, "SKU1-L1", binA); l1.Quantity = 4; l1.LotNumber = "L1"; l1.ExpiryDate = new DateOnly(2026, 12, 1);
        var (l2, t2) = h.Item(lot, "SKU1-L2", binB); l2.Quantity = 3; l2.LotNumber = "L2"; l2.ExpiryDate = new DateOnly(2026, 10, 1);   // expires first
        await h.SaveAsync();
        await h.Ops.ProcessAsync(new OperationRequest { Type = OperationType.Adjust, Lines = { new() { Epc = t1.Epc, Quantity = -2 } } });   // consumption

        var stock = new StockService(h.Db);
        var balances = await stock.BalancesAsync(SiteScope.All);
        Assert.Equal(2, balances.Count); Assert.Equal(2, balances.Single(b => b.LotNumber == "L1").Quantity);
        var summary = Assert.Single(await stock.SummaryAsync(SiteScope.All));
        Assert.Equal(5, summary.OnHand); Assert.Equal(10, summary.ReorderPoint); Assert.Equal(5, summary.Shortfall); Assert.Equal(2, summary.Consumed30d); Assert.Equal(2, summary.Locations); Assert.Equal(new DateOnly(2026, 10, 1), summary.EarliestExpiry);
        var moves = await stock.MovementsAsync(SiteScope.All);
        Assert.Single(moves); Assert.Equal(-2, moves[0].Delta); Assert.Equal("consumption", moves[0].Kind);

        var alloc = await stock.AllocateAsync(new StockService.AllocateRequest(new() { new("STOCK-LOT", null, 4, null) }, null), SiteScope.All);
        var line = Assert.Single(alloc);
        Assert.Equal(4, line.Allocated); Assert.Equal(0, line.Shortfall);
        Assert.Equal(new[] { "SKU1-L2", "SKU1-L1" }, line.Picks.Select(p => p.Identifier));                          // FEFO: L2 first
        Assert.Equal(new[] { 3m, 1m }, line.Picks.Select(p => p.Take));
        var short_ = Assert.Single(await stock.AllocateAsync(new StockService.AllocateRequest(new() { new("STOCK-LOT", null, 9, null) }, binA.Id), SiteScope.All));
        Assert.Equal(2, short_.Allocated); Assert.Equal(7, short_.Shortfall);
    }

    [Fact]
    public void Template_packages_are_signed_verified_and_tamper_evident()
    {
        using var publisher = RSA.Create(2048); using var stranger = RSA.Create(2048);
        var opts = new TemplateSigningOptions { TrustedPublishers = { new() { KeyId = "acme-2026", Name = "ACME Verticals", PublicKeyPem = publisher.ExportSubjectPublicKeyInfoPem() } } };
        var svc = new TemplatePackageService(opts);
        var pkg = new TemplatePackage { Code = "cold-chain-pro", Name = "Cold chain pro", Vertical = "Pharma", Version = "2.1.0", Author = "ACME", Definition = new() { ItemTypes = { new ItemType { Name = "Vaccine lot", Code = "VAX", Category = ItemCategory.Quantity, TracksExpiry = true } } } };

        Assert.Equal(SignatureStatus.Unsigned, svc.Verify(pkg).Status); Assert.True(svc.Accept(svc.Verify(pkg)));
        TemplatePackageService.Sign(pkg, publisher, "acme-2026");
        var ok = svc.Verify(pkg); Assert.Equal(SignatureStatus.Valid, ok.Status); Assert.Equal("ACME Verticals", ok.Publisher);

        var json = System.Text.Json.JsonSerializer.Serialize(pkg, TemplatePackageService.Json);
        var roundTrip = System.Text.Json.JsonSerializer.Deserialize<TemplatePackage>(json, TemplatePackageService.Json)!;
        Assert.Equal(SignatureStatus.Valid, svc.Verify(roundTrip).Status);                                          // canonical form survives JSON round-trip

        roundTrip.Definition.ItemTypes[0].TracksExpiry = false;                                                        // tampered content
        Assert.Equal(SignatureStatus.Invalid, svc.Verify(roundTrip).Status); Assert.False(svc.Accept(svc.Verify(roundTrip)));

        var foreign = TemplatePackageService.Sign(new TemplatePackage { Code = "x", Name = "x", Definition = new() }, stranger, "unknown-key");
        Assert.Equal(SignatureStatus.Untrusted, svc.Verify(foreign).Status); Assert.True(svc.Accept(svc.Verify(foreign)));
        var strict = new TemplatePackageService(new TemplateSigningOptions { RequireSignature = true, TrustedPublishers = opts.TrustedPublishers });
        Assert.False(strict.Accept(strict.Verify(foreign))); Assert.False(strict.Accept(strict.Verify(new TemplatePackage()))); Assert.True(strict.Accept(strict.Verify(pkg)));

        var signer = new TemplatePackageService(new TemplateSigningOptions { Signing = new() { KeyId = "acme-2026", PrivateKeyPem = publisher.ExportRSAPrivateKeyPem() }, TrustedPublishers = opts.TrustedPublishers });
        Assert.True(signer.CanSign); Assert.Equal(SignatureStatus.Valid, signer.Verify(signer.Sign(new TemplatePackage { Code = "y", Name = "y" })).Status);
    }
}
