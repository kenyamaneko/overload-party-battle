using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Models;
using OverloadParty.Battle.Npc;

namespace OverloadParty.Battle.Tests.Npc;

public class ActionFilterTests
{
    // ─── ParseZoneStr ────────────────────────────────────────

    [Theory]
    [InlineData("frontend_0", "frontend", 0)]
    [InlineData("frontend_2", "frontend", 2)]
    [InlineData("backend_0", "backend", 0)]
    [InlineData("backend_2", "backend", 2)]
    [InlineData("support_1", "support", 1)]
    public void ParseZoneStr_ValidInput_ReturnsCorrectSlotPosition(string input, string expectedZone, int expectedIndex)
    {
        var result = ActionFilter.ParseZoneStr(input);

        result.Should().NotBeNull();
        result!.Zone.Should().Be(expectedZone);
        result.Index.Should().Be(expectedIndex);
    }

    [Theory]
    [InlineData("")]
    [InlineData("frontend")]
    [InlineData("abc")]
    [InlineData("no_number_here_x")]
    public void ParseZoneStr_InvalidInput_ReturnsNull(string input)
    {
        ActionFilter.ParseZoneStr(input).Should().BeNull();
    }

    [Fact]
    public void ParseZoneStr_MultipleUnderscores_UsesLastSegment()
    {
        // "some_zone_3" → Zone="some_zone", Index=3
        var result = ActionFilter.ParseZoneStr("some_zone_3");

        result.Should().NotBeNull();
        result!.Zone.Should().Be("some_zone");
        result.Index.Should().Be(3);
    }

    // ─── FilterByType ────────────────────────────────────────

    [Fact]
    public void FilterByType_ReturnsOnlyMatchingActions()
    {
        var actions = new List<AvailableAction>
        {
            new() { Type = ActionTypes.PlayCard, HandInstanceID = "h1" },
            new() { Type = ActionTypes.Attack, SourceInstanceID = "a1" },
            new() { Type = ActionTypes.PlayCard, HandInstanceID = "h2" },
            new() { Type = ActionTypes.ScaleUp, SourceInstanceID = "s1" },
        };

        var result = ActionFilter.FilterByType(actions, ActionTypes.PlayCard);

        result.Should().HaveCount(2);
        result.Select(a => a.HandInstanceID).Should().Equal("h1", "h2");
    }

    [Fact]
    public void FilterByType_NoMatches_ReturnsEmptyList()
    {
        var actions = new List<AvailableAction>
        {
            new() { Type = ActionTypes.Attack },
        };

        ActionFilter.FilterByType(actions, ActionTypes.PlayCard).Should().BeEmpty();
    }

    // ─── PickBestZone ────────────────────────────────────────

    [Fact]
    public void PickBestZone_NullValidZones_ReturnsNull()
    {
        var card = TestFactory.ComputeCard();
        ActionFilter.PickBestZone(null, card, []).Should().BeNull();
    }

    [Fact]
    public void PickBestZone_ComputeCard_PrefersFrontend()
    {
        var card = TestFactory.ComputeCard();
        var zones = new List<string> { "backend_0", "frontend_1" };

        var result = ActionFilter.PickBestZone(zones, card, []);

        result.Should().Be("frontend_1");
    }

    [Fact]
    public void PickBestZone_ComputeCard_FallsBackToBackend()
    {
        var card = TestFactory.ComputeCard();
        var zones = new List<string> { "backend_0" };

        var result = ActionFilter.PickBestZone(zones, card, []);

        result.Should().Be("backend_0");
    }

    [Fact]
    public void PickBestZone_ObjectStorageCard_PrefersBackend()
    {
        var card = TestFactory.DataCard(subtype: "ObjectStorage");
        var zones = new List<string> { "frontend_0", "backend_1" };

        var result = ActionFilter.PickBestZone(zones, card, []);

        result.Should().Be("backend_1");
    }

    [Fact]
    public void PickBestZone_ComputeCard_NoMatchingZone_Throws()
    {
        var card = TestFactory.ComputeCard();
        var zones = new List<string> { "support_0" };

        var act = () => ActionFilter.PickBestZone(zones, card, []);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void PickBestZone_ObjectStorageCard_NoMatchingZone_Throws()
    {
        var card = TestFactory.DataCard(subtype: "ObjectStorage");
        var zones = new List<string> { "support_0" };

        var act = () => ActionFilter.PickBestZone(zones, card, []);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void PickBestZone_SkipsUsedZones()
    {
        var card = TestFactory.ComputeCard();
        var zones = new List<string> { "frontend_0", "frontend_1" };
        var used = new HashSet<string> { "frontend_0" };

        var result = ActionFilter.PickBestZone(zones, card, used);

        result.Should().Be("frontend_1");
    }

    [Fact]
    public void PickBestZone_AllZonesUsed_ReturnsNull()
    {
        var card = TestFactory.ComputeCard();
        var zones = new List<string> { "frontend_0" };
        var used = new HashSet<string> { "frontend_0" };

        ActionFilter.PickBestZone(zones, card, used).Should().BeNull();
    }

    // ─── PickSupportZone ─────────────────────────────────────

    [Fact]
    public void PickSupportZone_NullValidZones_ReturnsNull()
    {
        ActionFilter.PickSupportZone(null, []).Should().BeNull();
    }

    [Fact]
    public void PickSupportZone_ReturnsSupportZoneNotUsed()
    {
        var zones = new List<string> { "support_0", "support_1" };
        var used = new HashSet<string> { "support_0" };

        ActionFilter.PickSupportZone(zones, used).Should().Be("support_1");
    }

    [Fact]
    public void PickSupportZone_SkipsNonSupportZones()
    {
        var zones = new List<string> { "frontend_0", "backend_1" };

        ActionFilter.PickSupportZone(zones, []).Should().BeNull();
    }

    // ─── ResolveCardIdForInstance ─────────────────────────────

    [Fact]
    public void ResolveCardIdForInstance_FindsResourceInFrontend()
    {
        var field = TestFactory.MakeField();
        field.Frontend[0] = TestFactory.MakeResource(cardId: "TK-0020", instanceId: "inst_42");

        ActionFilter.ResolveCardIdForInstance("inst_42", field).Should().Be("TK-0020");
    }

    [Fact]
    public void ResolveCardIdForInstance_FindsDeployedSupport()
    {
        var field = TestFactory.MakeField();
        field.Support[0] = new DeployedSupport { InstanceID = "sup_1", CardID = "NT-0008" };

        ActionFilter.ResolveCardIdForInstance("sup_1", field).Should().Be("NT-0008");
    }

    [Fact]
    public void ResolveCardIdForInstance_NotFound_Throws()
    {
        var field = TestFactory.MakeField();
        var act = () => ActionFilter.ResolveCardIdForInstance("missing", field);
        act.Should().Throw<InvalidOperationException>();
    }
}
