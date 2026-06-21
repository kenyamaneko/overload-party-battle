using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Npc;
using GD = OverloadParty.GameState;

namespace OverloadParty.Battle.Tests.Npc;

public class ActionFilterTests
{
    /// <summary>Tests for ActionFilter.ParseZoneStr.</summary>
    public class ParseZoneStr
    {
        [Theory]
        [InlineData("frontend_0", "frontend", 0)]
        [InlineData("frontend_2", "frontend", 2)]
        [InlineData("backend_0", "backend", 0)]
        [InlineData("backend_2", "backend", 2)]
        [InlineData("support_1", "support", 1)]
        public void ValidInput_ReturnsCorrectSlotPosition(string input, string expectedZone, int expectedIndex)
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
        public void InvalidInput_ReturnsNull(string input)
        {
            ActionFilter.ParseZoneStr(input).Should().BeNull();
        }

        [Fact]
        public void MultipleUnderscores_UsesLastSegment()
        {
            var result = ActionFilter.ParseZoneStr("some_zone_3");

            result.Should().NotBeNull();
            result!.Zone.Should().Be("some_zone");
            result.Index.Should().Be(3);
        }
    }

    /// <summary>Tests for ActionFilter.FilterByType.</summary>
    public class FilterByType
    {
        [Fact]
        public void ReturnsOnlyMatchingActions()
        {
            var actions = new List<GD.AvailableAction>
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
        public void NoMatches_ReturnsEmptyList()
        {
            var actions = new List<GD.AvailableAction>
            {
                new() { Type = ActionTypes.Attack },
            };

            ActionFilter.FilterByType(actions, ActionTypes.PlayCard).Should().BeEmpty();
        }
    }

    /// <summary>Tests for ActionFilter.PickBestZone.</summary>
    public class PickBestZone
    {
        [Fact]
        public void NullValidZones_ReturnsNull()
        {
            var card = TestFactory.ComputeCard();
            ActionFilter.PickBestZone(null, card, []).Should().BeNull();
        }

        [Fact]
        public void ComputeCard_PrefersFrontend()
        {
            var card = TestFactory.ComputeCard();
            var zones = new List<string> { "backend_0", "frontend_1" };

            var result = ActionFilter.PickBestZone(zones, card, []);

            result.Should().Be("frontend_1");
        }

        [Fact]
        public void ComputeCard_FallsBackToBackend()
        {
            var card = TestFactory.ComputeCard();
            var zones = new List<string> { "backend_0" };

            var result = ActionFilter.PickBestZone(zones, card, []);

            result.Should().Be("backend_0");
        }

        [Fact]
        public void ObjectStorageCard_PrefersBackend()
        {
            var card = TestFactory.DataCard(subtype: "ObjectStorage");
            var zones = new List<string> { "frontend_0", "backend_1" };

            var result = ActionFilter.PickBestZone(zones, card, []);

            result.Should().Be("backend_1");
        }

        [Fact]
        public void ComputeCard_NoMatchingZone_Throws()
        {
            var card = TestFactory.ComputeCard();
            var zones = new List<string> { "support_0" };

            var act = () => ActionFilter.PickBestZone(zones, card, []);

            act.Should().Throw<InvalidOperationException>();
        }

        [Fact]
        public void ObjectStorageCard_NoMatchingZone_Throws()
        {
            var card = TestFactory.DataCard(subtype: "ObjectStorage");
            var zones = new List<string> { "support_0" };

            var act = () => ActionFilter.PickBestZone(zones, card, []);

            act.Should().Throw<InvalidOperationException>();
        }

        [Fact]
        public void SkipsUsedZones()
        {
            var card = TestFactory.ComputeCard();
            var zones = new List<string> { "frontend_0", "frontend_1" };
            var used = new HashSet<string> { "frontend_0" };

            var result = ActionFilter.PickBestZone(zones, card, used);

            result.Should().Be("frontend_1");
        }

        [Fact]
        public void AllZonesUsed_ReturnsNull()
        {
            var card = TestFactory.ComputeCard();
            var zones = new List<string> { "frontend_0" };
            var used = new HashSet<string> { "frontend_0" };

            ActionFilter.PickBestZone(zones, card, used).Should().BeNull();
        }
    }

    /// <summary>Tests for ActionFilter.PickSupportZone.</summary>
    public class PickSupportZone
    {
        [Fact]
        public void NullValidZones_ReturnsNull()
        {
            ActionFilter.PickSupportZone(null, []).Should().BeNull();
        }

        [Fact]
        public void ReturnsSupportZoneNotUsed()
        {
            var zones = new List<string> { "support_0", "support_1" };
            var used = new HashSet<string> { "support_0" };

            ActionFilter.PickSupportZone(zones, used).Should().Be("support_1");
        }

        [Fact]
        public void SkipsNonSupportZones()
        {
            var zones = new List<string> { "frontend_0", "backend_1" };

            ActionFilter.PickSupportZone(zones, []).Should().BeNull();
        }
    }

    /// <summary>Tests for ActionFilter.ResolveCardIdForInstance.</summary>
    public class ResolveCardIdForInstance
    {
        [Fact]
        public void FindsResourceInFrontend()
        {
            var field = TestFactory.MakeWireField();
            field.Frontend[0] = TestFactory.MakeWireResource(cardId: "TST-0001", instanceId: "inst_42");

            ActionFilter.ResolveCardIdForInstance("inst_42", field).Should().Be("TST-0001");
        }

        [Fact]
        public void FindsDeployedSupport()
        {
            var field = TestFactory.MakeWireField();
            field.Support[0] = TestFactory.MakeWireSupport(instanceId: "sup_1", cardId: "TST-0002");

            ActionFilter.ResolveCardIdForInstance("sup_1", field).Should().Be("TST-0002");
        }

        [Fact]
        public void NotFound_Throws()
        {
            var field = TestFactory.MakeWireField();
            var act = () => ActionFilter.ResolveCardIdForInstance("missing", field);
            act.Should().Throw<InvalidOperationException>();
        }
    }
}
