using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Engine;

public class EndPhaseProcessorTests
{
    private readonly TestCardCache _cc = new();
    private readonly Game _game = TestFactory.MakeGame();

    public EndPhaseProcessorTests()
    {
        _cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
        _cc.Add(TestFactory.DataCard(cardId: "TST-0002", subtype: "Database", yield: 400));
    }

    // ─── 休止 ────────────────────────────────────────────────

    [Fact]
    public void Process_EndPhase_DormantDb_SkipsYieldGeneration()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle, activePlayer: 1);
        AddRepoCards(state, 2);
        var db = TestFactory.MakeResource(cardId: "TST-0002", instanceId: "db_1", faceUp: true,
            maxYield: 400, currentYield: 400);
        db.TemporaryEffects.Add(new TemporaryEffect { EffectType = BuffTypes.Dormant, Duration = "this_turn" });
        state.Player1Field.Backend[0] = db;
        state.SetInsightPool(1, 0);

        EndPhaseProcessor.Process(state, _game, 1, _cc, new EffectRegistry());

        state.Player1InsightPool.Should().Be(0, "dormant DB does not generate yield");
    }

    [Fact]
    public void Process_EndPhase_UntilNextTurnEndDormant_Expires()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle, activePlayer: 1);
        AddRepoCards(state, 2);
        var res = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "r_1", faceUp: true);
        res.TemporaryEffects.Add(new TemporaryEffect
        {
            EffectType = BuffTypes.Dormant,
            Duration = "until_next_turn_end",
        });
        state.Player1Field.Frontend[0] = res;

        EndPhaseProcessor.Process(state, _game, 1, _cc, new EffectRegistry());

        FieldHelpers.HasTemporaryEffect(res, BuffTypes.Dormant).Should().BeFalse();
    }

    // ─── Phase transition: Main → Battle (one step) ──────────

    [Fact]
    public void Process_MainPhase_AdvancesToBattle_NotToEnd()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main, activePlayer: 1);

        var result = EndPhaseProcessor.Process(state, _game, 1, _cc, new EffectRegistry());

        state.CurrentPhase.Should().Be(Phase.Battle);
        result.GameOver.Should().BeNull();
        result.StateUpdated.Should().BeTrue();
    }

    [Fact]
    public void Process_MainPhase_DoesNotSwitchPlayer()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main, activePlayer: 1);

        EndPhaseProcessor.Process(state, _game, 1, _cc, new EffectRegistry());

        state.ActivePlayer.Should().Be(1);
        state.CurrentTurn.Should().Be(2);
    }

    [Fact]
    public void Process_MainPhase_EmitsPhaseChangeEvent()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main, activePlayer: 1);

        var result = EndPhaseProcessor.Process(state, _game, 1, _cc, new EffectRegistry());

        result.Events.Should().ContainSingle();
        result.Events[0].EventType.Should().Be(EventTypes.PhaseChange);
        var data = result.Events[0].EventData.Should().BeOfType<PhaseChangeEventData>().Subject;
        data.PreviousPhase.Should().Be("main");
        data.CurrentPhase.Should().Be("battle");
    }

    // ─── Phase transition: Main → End (first turn skip) ──────

    [Fact]
    public void Process_MainPhase_FirstTurn_SkipsBattleGoesToEnd()
    {
        var state = TestFactory.MakeGameState(turn: 1, phase: Phase.Main, activePlayer: 1);
        AddRepoCards(state, 2);

        var result = EndPhaseProcessor.Process(state, _game, 1, _cc, new EffectRegistry());

        // End phase processing → turn switch → draw → main
        state.CurrentPhase.Should().Be(Phase.Main);
        state.ActivePlayer.Should().Be(2);
        state.CurrentTurn.Should().Be(2);
    }

    // ─── Phase transition: Battle → End ──────────────────────

    [Fact]
    public void Process_BattlePhase_AdvancesToEnd_ThenSwitchesTurn()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle, activePlayer: 1);
        AddRepoCards(state, 2);

        var result = EndPhaseProcessor.Process(state, _game, 1, _cc, new EffectRegistry());

        // End phase processing → turn switch → draw phase → advance to main
        state.ActivePlayer.Should().Be(2);
        state.CurrentTurn.Should().Be(3);
        state.CurrentPhase.Should().Be(Phase.Main);
    }

    // ─── Two end_phase calls: Main → Battle → End ────────────

    [Fact]
    public void TwoEndPhases_MainToBattleToEnd_FullSequence()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main, activePlayer: 1);
        AddRepoCards(state, 2);

        // First end_phase: Main → Battle
        var result1 = EndPhaseProcessor.Process(state, _game, 1, _cc, new EffectRegistry());
        state.CurrentPhase.Should().Be(Phase.Battle);
        state.ActivePlayer.Should().Be(1);
        state.CurrentTurn.Should().Be(2);

        // Second end_phase: Battle → End → turn switch → draw → main
        var result2 = EndPhaseProcessor.Process(state, _game, 1, _cc, new EffectRegistry());
        state.ActivePlayer.Should().Be(2);
        state.CurrentTurn.Should().Be(3);
        state.CurrentPhase.Should().Be(Phase.Main);
    }

    // ─── Discard required ────────────────────────────────────

    [Fact]
    public void Process_EndPhase_HandOverLimit_NeedsDiscard()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle, activePlayer: 1);
        // Add 7 cards to hand (limit is 6)
        foreach (var i in Enumerable.Range(0, 7))
        {
            state.Player1Hand.Add(new UndeployedCard
            {
                InstanceID = $"hand_{i}",
                CardID = "TST-0001",
            });
        }

        var result = EndPhaseProcessor.Process(state, _game, 1, _cc, new EffectRegistry());

        result.NeedsDiscard.Should().BeTrue();
        // Phase stays at End, no turn switch yet
        state.CurrentPhase.Should().Be(Phase.End);
        state.ActivePlayer.Should().Be(1);
        state.CurrentTurn.Should().Be(2);
    }

    [Fact]
    public void Process_EndPhase_HandWithinLimit_NoDiscard()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle, activePlayer: 1);
        AddRepoCards(state, 2);
        // 6 cards = at limit, no discard needed
        foreach (var i in Enumerable.Range(0, 6))
        {
            state.Player1Hand.Add(new UndeployedCard
            {
                InstanceID = $"hand_{i}",
                CardID = "TST-0001",
            });
        }

        var result = EndPhaseProcessor.Process(state, _game, 1, _cc, new EffectRegistry());

        result.NeedsDiscard.Should().BeFalse();
        // Turn switches normally
        state.ActivePlayer.Should().Be(2);
    }

    // ─── Maintenance cost collection ─────────────────────────

    [Fact]
    public void Process_EndPhase_CollectsMaintenanceCost()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle, activePlayer: 1, p1Budget: 5000);
        AddRepoCards(state, 2);

        // Place a face-up compute resource (MC=150 at small rank)
        var resource = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "res_1", faceUp: true);
        state.Player1Field.Frontend[0] = resource;

        EndPhaseProcessor.Process(state, _game, 1, _cc, new EffectRegistry());

        // Budget should be reduced by maintenance cost (150)
        state.Player1Budget.Should().Be(5000 - 150);
    }

    // ─── Elastic maintenance cost ─────────────────────────────

    [Fact]
    public void Process_EndPhase_ElasticResource_CostPerRequest()
    {
        _cc.Add(TestFactory.ElasticContainerCard(cardId: "TST-0002"));
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle, activePlayer: 1, p1Budget: 5000);
        AddRepoCards(state, 2);

        // Elastic container: base TP=500, free_tier=500, cost_per_request=10
        // MC = max(0, 500 - 500) * 10 / 100 = 0
        var resource = TestFactory.MakeResource(
            cardId: "TST-0002", instanceId: "res_1", faceUp: true,
            maxTP: 500, currentTP: 500, maxAV: 1200, currentAV: 1200);
        state.Player1Field.Frontend[0] = resource;

        EndPhaseProcessor.Process(state, _game, 1, _cc, new EffectRegistry());

        // With no elastic bonus beyond base, MC should be 0
        state.Player1Budget.Should().Be(5000);
    }

    [Fact]
    public void Process_EndPhase_ElasticResource_WithBonus_CostsMore()
    {
        _cc.Add(TestFactory.ElasticContainerCard(cardId: "TST-0002"));
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle, activePlayer: 1, p1Budget: 5000);
        AddRepoCards(state, 2);

        // Elastic container with elastic bonus pushing stat above free tier
        // baseStat = 500, rank small = x1, elasticBonus = 300
        // scaledStat = 500 * 1 + 300 = 800
        // MC = max(0, 800 - 500) * 10 / 100 = 300 * 10 / 100 = 30
        var resource = TestFactory.MakeResource(
            cardId: "TST-0002", instanceId: "res_1", faceUp: true,
            maxTP: 500, currentTP: 500, maxAV: 1200, currentAV: 1200, elasticBonus: 300);
        state.Player1Field.Frontend[0] = resource;

        EndPhaseProcessor.Process(state, _game, 1, _cc, new EffectRegistry());

        state.Player1Budget.Should().Be(5000 - 30);
    }

    // ─── Insight generation ─────────────────────────────────

    [Fact]
    public void Process_EndPhase_GeneratesInsightFromBackendData()
    {
        _cc.Add(TestFactory.DataCard(cardId: "TST-0003", yield: 400));
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle, activePlayer: 1);
        AddRepoCards(state, 2);

        // Place a face-up data resource in backend
        var resource = TestFactory.MakeResource(
            cardId: "TST-0003", instanceId: "db_1", faceUp: true,
            maxAV: 800, currentAV: 800, maxYield: 400, currentYield: 400, maxTP: null, currentTP: null);
        state.Player1Field.Backend[0] = resource;

        long insightBefore = state.Player1InsightPool;

        EndPhaseProcessor.Process(state, _game, 1, _cc, new EffectRegistry());

        state.Player1InsightPool.Should().Be(insightBefore + 400);
    }

    [Fact]
    public void Process_EndPhase_FaceDownBackendData_NoInsight()
    {
        _cc.Add(TestFactory.DataCard(cardId: "TST-0003", yield: 400));
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle, activePlayer: 1);
        AddRepoCards(state, 2);

        // Place a face-down data resource in backend
        var resource = TestFactory.MakeResource(
            cardId: "TST-0003", instanceId: "db_1", faceUp: false, deployLeft: 1,
            maxAV: 800, currentAV: 800, maxYield: 400, currentYield: 400, maxTP: null, currentTP: null);
        state.Player1Field.Backend[0] = resource;

        long insightBefore = state.Player1InsightPool;

        EndPhaseProcessor.Process(state, _game, 1, _cc, new EffectRegistry());

        state.Player1InsightPool.Should().Be(insightBefore);
    }

    [Fact]
    public void Process_EndPhase_ElasticDataResource_GainsElasticIncrement()
    {
        _cc.Add(TestFactory.DataCard(
            cardId: "TST-0004", yield: 300, elastic: true, elasticIncrement: 50, freeTier: 300));
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle, activePlayer: 1);
        AddRepoCards(state, 2);

        var resource = TestFactory.MakeResource(
            cardId: "TST-0004", instanceId: "db_1", faceUp: true,
            maxAV: 800, currentAV: 800, maxYield: 300, currentYield: 300, maxTP: null, currentTP: null);
        state.Player1Field.Backend[0] = resource;

        EndPhaseProcessor.Process(state, _game, 1, _cc, new EffectRegistry());

        resource.ElasticBonus.Should().Be(50);
    }

    // ─── Temporary effects expired ──────────────────────────

    [Fact]
    public void Process_EndPhase_ExpiresThisTurnTemporaryEffects()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle, activePlayer: 1);
        AddRepoCards(state, 2);

        var resource = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "res_1", faceUp: true);
        resource.TemporaryEffects.Add(new TemporaryEffect
        {
            EffectType = EffectTypes.BuffTP,
            Value = 200,
            Duration = "this_turn",
            SourceID = "test"
        });
        state.Player1Field.Frontend[0] = resource;

        EndPhaseProcessor.Process(state, _game, 1, _cc, new EffectRegistry());

        resource.TemporaryEffects.Should().BeEmpty();
    }

    // ─── Per-turn flags reset ───────────────────────────────

    [Fact]
    public void Process_EndPhase_ResetsPerTurnFlags()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle, activePlayer: 1);
        AddRepoCards(state, 2);

        var resource = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "res_1", faceUp: true);
        resource.HasAttacked = true;
        resource.EffectUsedThisTurn = true;
        resource.MonetizedAmount = 100;
        state.Player1Field.Frontend[0] = resource;

        state.SetIncidentPlayedThisTurn(1, true);

        EndPhaseProcessor.Process(state, _game, 1, _cc, new EffectRegistry());

        resource.HasAttacked.Should().BeFalse();
        resource.EffectUsedThisTurn.Should().BeFalse();
        resource.MonetizedAmount.Should().Be(0);
        state.GetIncidentPlayedThisTurn(1).Should().BeFalse();
    }

    // ─── Support flags reset ────────────────────────────────

    [Fact]
    public void Process_EndPhase_ResetsSupportEffectUsedFlag()
    {
        _cc.Add(TestFactory.PlatformCard(cardId: "TEST-0200"));
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle, activePlayer: 1);
        AddRepoCards(state, 2);

        state.Player1Field.Support[0] = new DeployedSupport
        {
            InstanceID = "sup_1",
            CardID = "TEST-0200",
            FaceUp = true,
            EffectUsedThisTurn = true
        };

        EndPhaseProcessor.Process(state, _game, 1, _cc, new EffectRegistry());

        state.Player1Field.Support[0]!.EffectUsedThisTurn.Should().BeFalse();
    }

    // ─── Launch failure ─────────────────────────────────────

    [Fact]
    public void Process_EndPhase_LaunchFailure_GameOver()
    {
        // Turn 5 → personalTurn = (5+1)/2 = 3, which >= LaunchFailureTurn=3
        var state = TestFactory.MakeGameState(turn: 5, phase: Phase.Battle, activePlayer: 1);
        // Player has never deployed (HasOperated = false)
        state.SetHasOperated(1, false);

        var result = EndPhaseProcessor.Process(state, _game, 1, _cc, new EffectRegistry());

        result.GameOver.Should().NotBeNull();
        result.GameOver!.WinnerNum.Should().Be(2);
    }

    // ─── TurnEnd event emitted ──────────────────────────────

    [Fact]
    public void Process_EndPhase_EmitsTurnEndEvent()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle, activePlayer: 1);
        AddRepoCards(state, 2);

        var result = EndPhaseProcessor.Process(state, _game, 1, _cc, new EffectRegistry());

        result.Events.Should().Contain(e => e.EventType == EventTypes.TurnEnd);
    }

    // ─── Repository empty → game over ───────────────────────

    [Fact]
    public void Process_EndPhase_EmptyRepository_GameOver()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle, activePlayer: 1);
        state.SetHasOperated(1, true);
        // Do NOT add repo cards for player 2 (next draw will fail)
        // But make sure player 1's end-phase logic works
        // After turn switch, player 2 (active) has empty repo

        var result = EndPhaseProcessor.Process(state, _game, 1, _cc, new EffectRegistry());

        // Player 2 can't draw → game over, player 1 wins
        result.GameOver.Should().NotBeNull();
        result.GameOver!.WinnerNum.Should().Be(1);
    }

    // ─── TurnStart event data (internal format) ───────────────

    [Fact]
    public void MakeTurnStartEvent_ContainsActivePlayerForViewMapping()
    {
        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main, activePlayer: 2);

        var evt = EndPhaseProcessor.MakeTurnStartEvent("test-game", state);

        evt.EventType.Should().Be(EventTypes.TurnStart);
        evt.PlayerNum.Should().BeNull();
        var data = evt.EventData.Should().BeOfType<TurnStartInternalEventData>().Subject;
        data.Turn.Should().Be(3L);
        data.ActivePlayer.Should().Be(2L);
    }

    // ─── helpers ─────────────────────────────────────────────

    private static void AddRepoCards(BattleGameState state, long playerNum)
    {
        var repo = state.GetRepository(playerNum);
        repo.Add(new UndeployedCard { InstanceID = "repo_1", CardID = "TST-0001" });
        repo.Add(new UndeployedCard { InstanceID = "repo_2", CardID = "TST-0001" });
    }
}
