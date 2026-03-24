using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Models;
using OverloadParty.Battle.Npc;

namespace OverloadParty.Battle.Tests.Tests.Npc;

public class StandardAiTests
{
    private readonly TestCardCache _cc = new();
    private readonly Game _game = TestFactory.MakeGame();
    private readonly NullEffectRegistry _effects = new();

    public StandardAiTests()
    {
        _cc.Add(TestFactory.ComputeCard(cardId: "SH-0001", tp: 600, av: 1400, mc: 150));
        _cc.Add(TestFactory.DataCard(cardId: "NT-0009", cardType: CardTypes.Database, yield: 400, av: 800, mc: 100));
        _cc.Add(TestFactory.ComputeCard(cardId: "TEST-0002", tp: 300, av: 600, mc: 50, name: "CheapCompute"));
        _cc.Add(TestFactory.DataCard(cardId: "NT-0010", cardType: CardTypes.ObjectStorage, yield: 200, av: 500, mc: 30, name: "CheapStorage"));
    }

    private StandardAi CreateAi() => new(_cc, _effects);

    // ─── DecideMainPhaseActions ──────────────────────────────

    [Fact]
    public void DecideMainPhaseActions_WithPlayCardActions_ReturnsNpcActionsWithCorrectKeys()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main, activePlayer: 2);
        state.Player2Hand.Add(new HandCard { InstanceID = "hand_1", CardID = "SH-0001" });

        var available = new List<AvailableAction>
        {
            new()
            {
                Type = WireActionTypes.PlayCard,
                HandInstanceID = "hand_1",
                CardID = "SH-0001",
                ValidZones = ["frontend_0", "backend_1"],
            },
        };

        var ai = CreateAi();
        var actions = ai.DecideMainPhaseActions(state, _game, 2, available);

        // Should contain at least one play_card action and an end_phase action
        actions.Should().Contain(a => a.ActionType == WireActionTypes.PlayCard);
        actions.Should().Contain(a => a.ActionType == WireActionTypes.EndPhase);

        var playAction = actions.First(a => a.ActionType == WireActionTypes.PlayCard);
        playAction.Data.Should().ContainKey("cardInstanceId");
        playAction.Data.Should().ContainKey("position");
        playAction.Data["cardInstanceId"].Should().Be("hand_1");

        // Position should be a SlotPosition
        playAction.Data["position"].Should().BeOfType<SlotPosition>();
    }

    [Fact]
    public void DecideMainPhaseActions_ComputeCard_PrefersFrontendZone()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main, activePlayer: 2);
        state.Player2Hand.Add(new HandCard { InstanceID = "hand_1", CardID = "SH-0001" });

        var available = new List<AvailableAction>
        {
            new()
            {
                Type = WireActionTypes.PlayCard,
                HandInstanceID = "hand_1",
                CardID = "SH-0001",
                ValidZones = ["backend_0", "frontend_1"],
            },
        };

        var ai = CreateAi();
        var actions = ai.DecideMainPhaseActions(state, _game, 2, available);

        var playAction = actions.First(a => a.ActionType == WireActionTypes.PlayCard);
        var position = (SlotPosition)playAction.Data["position"];
        position.Zone.Should().Be("frontend");
        position.Index.Should().Be(1);
    }

    [Fact]
    public void DecideMainPhaseActions_NoAvailableActions_ReturnsOnlyEndPhase()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main, activePlayer: 2);

        var ai = CreateAi();
        var actions = ai.DecideMainPhaseActions(state, _game, 2, []);

        actions.Should().HaveCount(1);
        actions[0].ActionType.Should().Be(WireActionTypes.EndPhase);
    }

    [Fact]
    public void DecideMainPhaseActions_ScaleUpActions_ContainCorrectPayloadKeys()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main, activePlayer: 2);
        state.Player2Field.Frontend[0] = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "res_1");

        var available = new List<AvailableAction>
        {
            new()
            {
                Type = WireActionTypes.ScaleUp,
                SourceInstanceID = "res_1",
                TargetRank = "medium",
                NeedsFamily = true,
            },
        };

        var ai = CreateAi();
        var actions = ai.DecideMainPhaseActions(state, _game, 2, available);

        var scaleAction = actions.FirstOrDefault(a => a.ActionType == WireActionTypes.ScaleUp);
        scaleAction.Should().NotBeNull();
        scaleAction!.Data.Should().ContainKey("componentInstanceId");
        scaleAction.Data["componentInstanceId"].Should().Be("res_1");
        scaleAction.Data.Should().ContainKey("targetRank");
        scaleAction.Data.Should().ContainKey("instanceFamily");
    }

    [Fact]
    public void DecideMainPhaseActions_MonetizeActions_ContainDistributionsKey()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main, activePlayer: 2);
        state.Player2InsightPool = 500;

        var available = new List<AvailableAction>
        {
            new()
            {
                Type = WireActionTypes.Monetize,
                SourceInstanceID = "res_be_1",
                RemainingCapacity = 300,
            },
        };

        var ai = CreateAi();
        var actions = ai.DecideMainPhaseActions(state, _game, 2, available);

        var monetizeAction = actions.FirstOrDefault(a => a.ActionType == WireActionTypes.Monetize);
        monetizeAction.Should().NotBeNull();
        monetizeAction!.Data.Should().ContainKey("distributions");

        var distributions = monetizeAction.Data["distributions"] as List<Dictionary<string, object>>;
        distributions.Should().NotBeNull();
        distributions!.Should().NotBeEmpty();
        distributions[0].Should().ContainKey("componentInstanceId");
        distributions[0]["componentInstanceId"].Should().Be("res_be_1");
        distributions[0].Should().ContainKey("amount");
    }

    // ─── DecideBattlePhaseActions ────────────────────────────

    [Fact]
    public void DecideBattlePhaseActions_WithAttackers_ReturnsAttackActionsWithCorrectKeys()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle, activePlayer: 2);
        state.Player1Field.Frontend[0] = TestFactory.MakeResource(
            cardId: "SH-0001", instanceId: "target_1", maxAV: 1400);

        var available = new List<AvailableAction>
        {
            new()
            {
                Type = WireActionTypes.Attack,
                SourceInstanceID = "atk_1",
                ValidTargets = ["target_1"],
            },
        };

        var ai = CreateAi();
        var actions = ai.DecideBattlePhaseActions(state, _game, 2, available);

        var attackAction = actions.FirstOrDefault(a => a.ActionType == WireActionTypes.Attack);
        attackAction.Should().NotBeNull();
        attackAction!.Data.Should().ContainKey("attackerInstanceId");
        attackAction.Data.Should().ContainKey("targetInstanceId");
        attackAction.Data["attackerInstanceId"].Should().Be("atk_1");
        attackAction.Data["targetInstanceId"].Should().Be("target_1");
    }

    [Fact]
    public void DecideBattlePhaseActions_MultipleAttackers_SelectsLowestAVTarget()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle, activePlayer: 2);
        state.Player1Field.Frontend[0] = TestFactory.MakeResource(
            cardId: "SH-0001", instanceId: "t_strong", maxAV: 2000, damage: 0);
        state.Player1Field.Frontend[1] = TestFactory.MakeResource(
            cardId: "SH-0001", instanceId: "t_weak", maxAV: 600, damage: 0);

        var available = new List<AvailableAction>
        {
            new()
            {
                Type = WireActionTypes.Attack,
                SourceInstanceID = "atk_1",
                ValidTargets = ["t_strong", "t_weak"],
            },
        };

        var ai = CreateAi();
        var actions = ai.DecideBattlePhaseActions(state, _game, 2, available);

        var attackAction = actions.First(a => a.ActionType == WireActionTypes.Attack);
        attackAction.Data["targetInstanceId"].Should().Be("t_weak");
    }

    [Fact]
    public void DecideBattlePhaseActions_NoAttackActions_ReturnsOnlyEndPhase()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle, activePlayer: 2);

        var ai = CreateAi();
        var actions = ai.DecideBattlePhaseActions(state, _game, 2, []);

        actions.Should().HaveCount(1);
        actions[0].ActionType.Should().Be(WireActionTypes.EndPhase);
    }

    [Fact]
    public void DecideBattlePhaseActions_AlwaysEndsWithEndPhase()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle, activePlayer: 2);
        state.Player1Field.Frontend[0] = TestFactory.MakeResource(
            cardId: "SH-0001", instanceId: "target_1");

        var available = new List<AvailableAction>
        {
            new()
            {
                Type = WireActionTypes.Attack,
                SourceInstanceID = "atk_1",
                ValidTargets = ["target_1"],
            },
        };

        var ai = CreateAi();
        var actions = ai.DecideBattlePhaseActions(state, _game, 2, available);

        actions.Last().ActionType.Should().Be(WireActionTypes.EndPhase);
    }

    // ─── DecideDiscard ───────────────────────────────────────

    [Fact]
    public void DecideDiscard_HandWithinLimit_ReturnsEmptyList()
    {
        var state = TestFactory.MakeGameState();
        // HandLimit is 6, add fewer cards
        foreach (var i in Enumerable.Range(0, GameConstants.HandLimit))
        {
            state.Player1Hand.Add(new HandCard { InstanceID = $"h_{i}", CardID = "SH-0001" });
        }

        var ai = CreateAi();
        var result = ai.DecideDiscard(state, 1);

        result.Should().BeEmpty();
    }

    [Fact]
    public void DecideDiscard_HandExceedsLimit_DiscardsCorrectCount()
    {
        var state = TestFactory.MakeGameState();
        var excess = 2;
        foreach (var i in Enumerable.Range(0, GameConstants.HandLimit + excess))
        {
            state.Player1Hand.Add(new HandCard { InstanceID = $"h_{i}", CardID = "SH-0001" });
        }

        var ai = CreateAi();
        var result = ai.DecideDiscard(state, 1);

        result.Should().HaveCount(excess);
    }

    [Fact]
    public void DecideDiscard_DiscardsCheapestCardsFirst()
    {
        var state = TestFactory.MakeGameState();
        // CardId 1 has MC=150, CardId 2 has MC=50, CardId 100 has MC=100
        // Add 8 cards (HandLimit=6, so discard 2)
        state.Player1Hand.Add(new HandCard { InstanceID = "expensive_1", CardID = "SH-0001" });    // MC=150
        state.Player1Hand.Add(new HandCard { InstanceID = "cheap_1", CardID = "TEST-0002" });        // MC=50
        state.Player1Hand.Add(new HandCard { InstanceID = "medium_1", CardID = "NT-0009" });     // MC=100
        state.Player1Hand.Add(new HandCard { InstanceID = "expensive_2", CardID = "SH-0001" });    // MC=150
        state.Player1Hand.Add(new HandCard { InstanceID = "cheap_2", CardID = "TEST-0002" });        // MC=50
        state.Player1Hand.Add(new HandCard { InstanceID = "medium_2", CardID = "NT-0009" });     // MC=100
        state.Player1Hand.Add(new HandCard { InstanceID = "cheap_3", CardID = "NT-0010" });      // MC=30
        state.Player1Hand.Add(new HandCard { InstanceID = "expensive_3", CardID = "SH-0001" });    // MC=150

        var ai = CreateAi();
        var result = ai.DecideDiscard(state, 1);

        // Should discard 2 cheapest: MC=30 (cheap_3) and MC=50 (cheap_1 or cheap_2)
        result.Should().HaveCount(2);
        result.Should().Contain("cheap_3");  // MC=30
        // Second discard should be one of the MC=50 cards
        result.Should().Contain(id => id == "cheap_1" || id == "cheap_2");
    }

    [Fact]
    public void DecideDiscard_EmptyHand_ReturnsEmpty()
    {
        var state = TestFactory.MakeGameState();
        // No cards in hand

        var ai = CreateAi();
        ai.DecideDiscard(state, 1).Should().BeEmpty();
    }

    // ─── Dictionary key verification ─────────────────────────

    [Fact]
    public void PlayCardPayload_UsesCamelCaseKeys()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main, activePlayer: 1);
        state.Player1Hand.Add(new HandCard { InstanceID = "h1", CardID = "SH-0001" });

        var available = new List<AvailableAction>
        {
            new()
            {
                Type = WireActionTypes.PlayCard,
                HandInstanceID = "h1",
                CardID = "SH-0001",
                ValidZones = ["frontend_0"],
            },
        };

        var ai = CreateAi();
        var actions = ai.DecideMainPhaseActions(state, _game, 1, available);

        var playAction = actions.First(a => a.ActionType == WireActionTypes.PlayCard);
        playAction.Data.Keys.Should().Contain("cardInstanceId");
        playAction.Data.Keys.Should().Contain("position");
    }

    [Fact]
    public void AttackPayload_UsesCamelCaseKeys()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle, activePlayer: 1);
        state.Player2Field.Frontend[0] = TestFactory.MakeResource(instanceId: "t1");

        var available = new List<AvailableAction>
        {
            new()
            {
                Type = WireActionTypes.Attack,
                SourceInstanceID = "a1",
                ValidTargets = ["t1"],
            },
        };

        var ai = CreateAi();
        var actions = ai.DecideBattlePhaseActions(state, _game, 1, available);

        var attackAction = actions.First(a => a.ActionType == WireActionTypes.Attack);
        attackAction.Data.Keys.Should().Contain("attackerInstanceId");
        attackAction.Data.Keys.Should().Contain("targetInstanceId");
    }

    [Fact]
    public void ScaleUpPayload_UsesCamelCaseKeys()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main, activePlayer: 1);

        var available = new List<AvailableAction>
        {
            new()
            {
                Type = WireActionTypes.ScaleUp,
                SourceInstanceID = "r1",
                TargetRank = "medium",
                NeedsFamily = true,
            },
        };

        var ai = CreateAi();
        var actions = ai.DecideMainPhaseActions(state, _game, 1, available);

        var scaleAction = actions.First(a => a.ActionType == WireActionTypes.ScaleUp);
        scaleAction.Data.Keys.Should().Contain("componentInstanceId");
        scaleAction.Data.Keys.Should().Contain("targetRank");
        scaleAction.Data.Keys.Should().Contain("instanceFamily");
    }

    [Fact]
    public void MonetizePayload_DistributionUsesCamelCaseKeys()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main, activePlayer: 1);
        state.Player1InsightPool = 100;

        var available = new List<AvailableAction>
        {
            new()
            {
                Type = WireActionTypes.Monetize,
                SourceInstanceID = "be_1",
                RemainingCapacity = 200,
            },
        };

        var ai = CreateAi();
        var actions = ai.DecideMainPhaseActions(state, _game, 1, available);

        var monetizeAction = actions.First(a => a.ActionType == WireActionTypes.Monetize);
        var dists = (List<Dictionary<string, object>>)monetizeAction.Data["distributions"];
        dists[0].Keys.Should().Contain("componentInstanceId");
        dists[0].Keys.Should().Contain("amount");
    }

    // ─── Null/empty effect registry (no-op stub) ─────────────

    /// <summary>
    /// Minimal IEffectRegistry that returns null for everything.
    /// Used to test StandardAi without real effect evaluation.
    /// </summary>
    private class NullEffectRegistry : IEffectRegistry
    {
        public EffectHandler? Get(string cardId, TriggerType trigger) => null;
        public bool Has(string cardId, TriggerType trigger) => false;
        public BudgetRequirement? GetBudgetRequirement(string cardId, TriggerType trigger) => null;
        public EffectInfo? GetEffectInfo(string cardId, TriggerType trigger) => null;
        public List<string>? GetChoiceOptions(string cardId, TriggerType trigger) => null;
    }
}
