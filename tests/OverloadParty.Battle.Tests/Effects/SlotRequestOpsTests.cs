using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Effects.Ops;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Effects;

public class SlotRequestOpsTests
{
    private readonly TestCardCache _cc = new();
    private readonly Game _game = TestFactory.MakeGame();

    public SlotRequestOpsTests()
    {
        _cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
        _cc.Add(TestFactory.DataCard(cardId: "TST-DB01", cardType: "Database"));
    }

    private EffectContext MakeContext(GameState? state = null, DeployedResource? target = null,
        Dictionary<string, object>? choiceData = null)
    {
        state ??= TestFactory.MakeGameState();
        return new EffectContext
        {
            State = state,
            Game = _game,
            PlayerNum = 1,
            Target = target,
            CardCache = _cc,
            ChoiceData = choiceData,
        };
    }

    // ─── RequestSlotFromRepoOp ─────────────────────────────

    [Fact]
    public void RequestSlotFromRepo_SetsAwaitingSlotSelect()
    {
        var state = TestFactory.MakeGameState();
        state.Player1Repository = [new UndeployedCard { InstanceID = "r_1", CardID = "TST-0001" }];
        var op = new RequestSlotFromRepoOp();

        var handler = EffectComposer.Compose(op);
        handler(MakeContext(state));

        state.AwaitingSlotSelect.Should().NotBeNull();
        state.AwaitingSlotSelect!.PlayerNum.Should().Be(1);
        state.AwaitingSlotSelect.Resource.CardID.Should().Be("TST-0001");
        state.AwaitingSlotSelect.ValidZones.Should().NotBeEmpty();
    }

    [Fact]
    public void RequestSlotFromRepo_RemovesCardFromRepo()
    {
        var state = TestFactory.MakeGameState();
        state.Player1Repository = [new UndeployedCard { InstanceID = "r_1", CardID = "TST-0001" }];
        var op = new RequestSlotFromRepoOp();

        var handler = EffectComposer.Compose(op);
        handler(MakeContext(state));

        state.Player1Repository.Should().BeEmpty();
    }

    [Fact]
    public void RequestSlotFromRepo_OverrideAV_Applied()
    {
        var state = TestFactory.MakeGameState();
        state.Player1Repository = [new UndeployedCard { InstanceID = "r_1", CardID = "TST-0001" }];
        var op = new RequestSlotFromRepoOp { OverrideAV = 200 };

        var handler = EffectComposer.Compose(op);
        handler(MakeContext(state));

        state.AwaitingSlotSelect!.Resource.MaxAV.Should().Be(200);
    }

    [Fact]
    public void RequestSlotFromRepo_NoMatchingCard_DoesNothing()
    {
        var state = TestFactory.MakeGameState();
        state.Player1Repository = [new UndeployedCard { InstanceID = "r_1", CardID = "TST-0001" }];
        var op = new RequestSlotFromRepoOp { Filter = card => card.CardId == "NONEXISTENT" };

        var handler = EffectComposer.Compose(op);
        handler(MakeContext(state));

        state.AwaitingSlotSelect.Should().BeNull();
        state.Player1Repository.Should().HaveCount(1);
    }

    [Fact]
    public void RequestSlotFromRepo_ComputeCard_HasFrontendAndBackendZones()
    {
        var state = TestFactory.MakeGameState();
        state.Player1Repository = [new UndeployedCard { InstanceID = "r_1", CardID = "TST-0001" }];
        var op = new RequestSlotFromRepoOp();

        var handler = EffectComposer.Compose(op);
        handler(MakeContext(state));

        var zones = state.AwaitingSlotSelect!.ValidZones;
        zones.Should().Contain(z => z.StartsWith("frontend_"));
        zones.Should().Contain(z => z.StartsWith("backend_"));
    }

    [Fact]
    public void RequestSlotFromRepo_DatabaseCard_HasOnlyBackendZones()
    {
        var state = TestFactory.MakeGameState();
        state.Player1Repository = [new UndeployedCard { InstanceID = "r_1", CardID = "TST-DB01" }];
        var op = new RequestSlotFromRepoOp();

        var handler = EffectComposer.Compose(op);
        handler(MakeContext(state));

        var zones = state.AwaitingSlotSelect!.ValidZones;
        zones.Should().AllSatisfy(z => z.Should().StartWith("backend_"));
    }

    [Fact]
    public void RequestSlotFromRepo_NoEmptySlots_Throws()
    {
        var state = TestFactory.MakeGameState();
        state.Player1Repository = [new UndeployedCard { InstanceID = "r_1", CardID = "TST-DB01" }];
        // Fill all backend slots
        for (int i = 0; i < BattleConstants.SlotsPerZone; i++)
        {
            state.Player1Field.Backend[i] = TestFactory.MakeResource(instanceId: $"occ_{i}");
        }
        var op = new RequestSlotFromRepoOp();

        var handler = EffectComposer.Compose(op);
        var result = handler(MakeContext(state));

        // Guard failure — EffectComposer catches GameRuleException and sets GuardFailed
        result.GuardFailed.Should().BeTrue();
    }

    // ─── RequestSlotFromHandOp ─────────────────────────────

    [Fact]
    public void RequestSlotFromHand_SetsAwaitingSlotSelect()
    {
        var state = TestFactory.MakeGameState();
        state.Player1Hand = [new UndeployedCard { InstanceID = "h_1", CardID = "TST-0001" }];
        var op = new RequestSlotFromHandOp();
        var choiceData = new Dictionary<string, object> { ["cardId"] = "TST-0001" };

        var handler = EffectComposer.Compose(op);
        handler(MakeContext(state, choiceData: choiceData));

        state.AwaitingSlotSelect.Should().NotBeNull();
        state.AwaitingSlotSelect!.Resource.CardID.Should().Be("TST-0001");
    }

    [Fact]
    public void RequestSlotFromHand_RemovesCardFromHand()
    {
        var state = TestFactory.MakeGameState();
        state.Player1Hand = [new UndeployedCard { InstanceID = "h_1", CardID = "TST-0001" }];
        var op = new RequestSlotFromHandOp();
        var choiceData = new Dictionary<string, object> { ["cardId"] = "TST-0001" };

        var handler = EffectComposer.Compose(op);
        handler(MakeContext(state, choiceData: choiceData));

        state.Player1Hand.Should().BeEmpty();
    }

    [Fact]
    public void RequestSlotFromHand_NoChoice_GuardFails()
    {
        var state = TestFactory.MakeGameState();
        state.Player1Hand = [new UndeployedCard { InstanceID = "h_1", CardID = "TST-0001" }];
        var op = new RequestSlotFromHandOp();

        var handler = EffectComposer.Compose(op);
        var result = handler(MakeContext(state));

        result.GuardFailed.Should().BeTrue();
    }

    // ─── RequestSlotFromRepoSameCardOp ─────────────────────

    [Fact]
    public void RequestSlotFromRepoSameCard_MatchesTargetCardId()
    {
        var state = TestFactory.MakeGameState();
        state.Player1Repository =
        [
            new UndeployedCard { InstanceID = "r_1", CardID = "TST-0001" },
            new UndeployedCard { InstanceID = "r_2", CardID = "TST-DB01" },
        ];
        var target = TestFactory.MakeResource(cardId: "TST-DB01", instanceId: "destroyed");
        var op = new RequestSlotFromRepoSameCardOp();

        var handler = EffectComposer.Compose(op);
        handler(MakeContext(state, target: target));

        state.AwaitingSlotSelect.Should().NotBeNull();
        state.AwaitingSlotSelect!.Resource.CardID.Should().Be("TST-DB01");
        state.Player1Repository.Should().HaveCount(1);
        state.Player1Repository[0].CardID.Should().Be("TST-0001");
    }

    [Fact]
    public void RequestSlotFromRepoSameCard_NoTarget_GuardFails()
    {
        var state = TestFactory.MakeGameState();
        state.Player1Repository = [new UndeployedCard { InstanceID = "r_1", CardID = "TST-0001" }];
        var op = new RequestSlotFromRepoSameCardOp();

        var handler = EffectComposer.Compose(op);
        var result = handler(MakeContext(state));

        result.GuardFailed.Should().BeTrue();
    }
}
