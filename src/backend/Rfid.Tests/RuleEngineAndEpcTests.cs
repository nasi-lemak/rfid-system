using System.Text.Json;
using Rfid.Application.Services;
using Rfid.Domain;
using Rfid.Domain.Entities;
using Rfid.Domain.Epc;
using Xunit;

namespace Rfid.Tests;

public class RuleEngineAndEpcTests
{
    private static RuleContext Ctx(Item item, ItemType? type = null, Location? to = null, Dictionary<string, object?>? data = null) =>
        new() { Event = new ItemEvent { ItemId = item.Id, Type = ItemEventType.Moved, Data = data ?? new() }, Item = item, ItemType = type, ToLocation = to };

    [Theory]
    [InlineData("item.cycleCount", "gte", 10, true)]
    [InlineData("item.cycleCount", "lt", 10, false)]
    [InlineData("item.state", "eq", "Soiled", true)]
    [InlineData("item.state", "in", "Clean,Soiled", true)]
    [InlineData("item.state", "nin", "Clean,Soiled", false)]
    [InlineData("item.attributes.size", "eq", "L", true)]
    [InlineData("item.nonexistent", "exists", null, false)]
    public void Conditions_match_item_fields(string field, string op, object? value, bool expected)
    {
        var item = new Item { State = "Soiled", CycleCount = 10, Attributes = new() { ["size"] = "L" } };
        Assert.Equal(expected, RuleEngine.Matches(new RuleCondition { Field = field, Op = op, Value = value }, Ctx(item)));
    }

    [Fact]
    public void Conditions_accept_json_element_values_as_stored_in_jsonb()
    {
        var item = new Item { State = "OnShelf", CycleCount = 3 };
        var strVal = JsonSerializer.Deserialize<JsonElement>("\"OnShelf\"");
        var numVal = JsonSerializer.Deserialize<JsonElement>("5");
        var arrVal = JsonSerializer.Deserialize<JsonElement>("[\"OnShelf\",\"Lost\"]");
        var boolVal = JsonSerializer.Deserialize<JsonElement>("true");
        Assert.True(RuleEngine.Matches(new RuleCondition { Field = "item.state", Op = "eq", Value = strVal }, Ctx(item)));
        Assert.True(RuleEngine.Matches(new RuleCondition { Field = "item.cycleCount", Op = "lt", Value = numVal }, Ctx(item)));
        Assert.True(RuleEngine.Matches(new RuleCondition { Field = "item.state", Op = "in", Value = arrVal }, Ctx(item)));
        Assert.False(RuleEngine.Matches(new RuleCondition { Field = "item.hasCustodian", Op = "eq", Value = boolVal }, Ctx(item)));
    }

    [Fact]
    public void Location_attributes_and_event_data_are_addressable()
    {
        var item = new Item { State = "Contaminated" };
        var clean = new Location { Name = "Clean store", Kind = LocationKind.Room, Attributes = new() { ["clean"] = true } };
        var c = Ctx(item, to: clean, data: new() { ["direction"] = "In" });
        Assert.True(RuleEngine.Matches(new RuleCondition { Field = "toLocation.clean", Op = "eq", Value = true }, c));
        Assert.True(RuleEngine.Matches(new RuleCondition { Field = "toLocation.kind", Op = "eq", Value = "Room" }, c));
        Assert.True(RuleEngine.Matches(new RuleCondition { Field = "data.direction", Op = "eq", Value = "in" }, c));
        Assert.False(RuleEngine.Matches(new RuleCondition { Field = "data.direction", Op = "eq", Value = "Out" }, c));
    }

    [Fact]
    public void Derived_fields_compute_expiry_and_cycles_remaining()
    {
        var type = new ItemType { MaxCycles = 100 };
        var item = new Item { CycleCount = 95, ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)) };
        var c = Ctx(item, type);
        Assert.Equal(5, RuleEngine.Resolve("item.cyclesRemaining", c));
        var days = (double)RuleEngine.Resolve("item.daysUntilExpiry", c)!;
        Assert.InRange(days, 8.9, 10.1);
        Assert.True(RuleEngine.Matches(new RuleCondition { Field = "item.daysUntilExpiry", Op = "lt", Value = 30 }, c));
    }

    [Fact]
    public void Sgtin96_round_trips()
    {
        var epc = Sgtin96.Encode("0614141", "812345", 6789);
        Assert.Equal(24, epc.Length);
        Assert.StartsWith("30", epc);
        var d = Sgtin96.Decode(epc)!.Value;
        Assert.Equal("0614141", d.companyPrefix);
        Assert.Equal("812345", d.itemRef);
        Assert.Equal(6789UL, d.serial);
        Assert.Null(Sgtin96.Decode("E2801160600002000000ABCD"));
    }

    [Fact]
    public void Lifecycle_prefers_exact_transition_over_wildcard()
    {
        var lc = new Rfid.Domain.Lifecycle.LifecycleDefinition
        {
            States = { "A", "B", "C" },
            Transitions = { new() { From = "*", To = "C", On = "Return" }, new() { From = "A", To = "B", On = "Return" } }
        };
        Assert.Equal("B", lc.FindTransition("A", OperationType.Return)!.To);
        Assert.Equal("C", lc.FindTransition("B", OperationType.Return)!.To);
        Assert.Null(lc.FindTransition("A", OperationType.Issue));
        Assert.Equal("C", lc.FindTransition("A", OperationType.Return, "C")!.To);
    }
}
