using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Engine;

public class EndPhaseProcessorTests
{
    /// <summary>EndPhaseProcessor.Process テストの共有 setup (カードキャッシュ・ゲーム・リポジトリ補充)。</summary>
    public abstract class Base
    {
        protected readonly TestCardCache _cc = new();
        protected readonly Game _game = TestFactory.MakeGame();

        protected Base()
        {
            _cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
            _cc.Add(TestFactory.DataCard(cardId: "TST-0002", subtype: "Database", yield: 400));
        }

        /// <summary>指定プレイヤーのリポジトリにカードを 2 枚補充する。</summary>
        /// <param name="state">対象のゲーム状態。</param>
        /// <param name="playerNum">補充先のプレイヤー番号。</param>
        protected static void AddRepoCards(BattleGameState state, long playerNum)
        {
            var repo = state.GetRepository(playerNum);
            repo.Add(new UndeployedCard { InstanceID = "repo_1", CardID = "TST-0001" });
            repo.Add(new UndeployedCard { InstanceID = "repo_2", CardID = "TST-0001" });
        }
    }

    /// <summary>Tests for EndPhaseProcessor.Process — dormant resource behavior at end phase.</summary>
    public class Dormant : Base
    {
        [Fact]
        public void DormantDb_SkipsYieldGeneration()
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
        public void UntilNextTurnEndDormant_Expires()
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
    }

    /// <summary>Tests for EndPhaseProcessor.Process — Main phase advances one step to Battle.</summary>
    public class MainToBattle : Base
    {
        [Fact]
        public void AdvancesToBattle_NotToEnd()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main, activePlayer: 1);

            var result = EndPhaseProcessor.Process(state, _game, 1, _cc, new EffectRegistry());

            state.CurrentPhase.Should().Be(Phase.Battle);
            result.GameOver.Should().BeNull();
        }

        [Fact]
        public void DoesNotSwitchPlayer()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main, activePlayer: 1);

            EndPhaseProcessor.Process(state, _game, 1, _cc, new EffectRegistry());

            state.ActivePlayer.Should().Be(1);
            state.CurrentTurn.Should().Be(2);
        }

        [Fact]
        public void EmitsPhaseChangeEvent()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main, activePlayer: 1);

            var result = EndPhaseProcessor.Process(state, _game, 1, _cc, new EffectRegistry());

            result.Events.Should().ContainSingle();
            result.Events[0].EventType.Should().Be(EventTypes.PhaseChange);
            var data = result.Events[0].EventData.Should().BeOfType<PhaseChangeEventData>().Subject;
            data.PreviousPhase.Should().Be("main");
            data.CurrentPhase.Should().Be("battle");
        }
    }

    /// <summary>Tests for EndPhaseProcessor.Process — first turn skips Battle and goes to End.</summary>
    public class MainToEndFirstTurn : Base
    {
        [Fact]
        public void FirstTurn_SkipsBattleGoesToEnd()
        {
            var state = TestFactory.MakeGameState(turn: 1, phase: Phase.Main, activePlayer: 1);
            AddRepoCards(state, 2);

            var result = EndPhaseProcessor.Process(state, _game, 1, _cc, new EffectRegistry());

            // End phase processing → turn switch → draw → main
            state.CurrentPhase.Should().Be(Phase.Main);
            state.ActivePlayer.Should().Be(2);
            state.CurrentTurn.Should().Be(2);
        }
    }

    /// <summary>Tests for EndPhaseProcessor.Process — Battle phase advances to End then switches turn.</summary>
    public class BattleToEnd : Base
    {
        [Fact]
        public void AdvancesToEnd_ThenSwitchesTurn()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle, activePlayer: 1);
            AddRepoCards(state, 2);

            var result = EndPhaseProcessor.Process(state, _game, 1, _cc, new EffectRegistry());

            // End phase processing → turn switch → draw phase → advance to main
            state.ActivePlayer.Should().Be(2);
            state.CurrentTurn.Should().Be(3);
            state.CurrentPhase.Should().Be(Phase.Main);
        }
    }

    /// <summary>Tests for EndPhaseProcessor.Process — two consecutive end phases run the full sequence.</summary>
    public class TwoEndPhases : Base
    {
        [Fact]
        public void MainToBattleToEnd_FullSequence()
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
    }

    /// <summary>Tests for EndPhaseProcessor.Process — hand size relative to limit drives discard.</summary>
    public class DiscardRequired : Base
    {
        [Fact]
        public void HandOverLimit_NeedsDiscard()
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

            result.ShouldDiscard.Should().BeTrue();
            // Phase stays at End, no turn switch yet
            state.CurrentPhase.Should().Be(Phase.End);
            state.ActivePlayer.Should().Be(1);
            state.CurrentTurn.Should().Be(2);
        }

        [Fact]
        public void HandWithinLimit_NoDiscard()
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

            result.ShouldDiscard.Should().BeFalse();
            // Turn switches normally
            state.ActivePlayer.Should().Be(2);
        }
    }

    /// <summary>Tests for EndPhaseProcessor.Process — maintenance cost collection at end phase.</summary>
    public class MaintenanceCost : Base
    {
        [Fact]
        public void CollectsMaintenanceCost()
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
    }

    /// <summary>Tests for EndPhaseProcessor.Process — elastic resource maintenance cost scales per request.</summary>
    public class ElasticMaintenanceCost : Base
    {
        [Fact]
        public void ElasticResource_CostPerRequest()
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
        public void ElasticResource_WithBonus_CostsMore()
        {
            _cc.Add(TestFactory.ElasticContainerCard(cardId: "TST-0002"));
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle, activePlayer: 1, p1Budget: 5000);
            AddRepoCards(state, 2);

            // Elastic container with elastic bonus pushing stat above free tier
            // baseStat = 500, rank small ×1, family ×1, elasticBonus(raw) = 300
            // effectiveElasticBonus = 500 × ln(1 + 300/500) ≈ 235
            // intrinsicStat = 500 + 235 = 735
            // MC = max(0, 735 - 500) × 10 / 100 = 23
            var resource = TestFactory.MakeResource(
                cardId: "TST-0002", instanceId: "res_1", faceUp: true,
                maxTP: 500, currentTP: 500, maxAV: 1200, currentAV: 1200, elasticBonus: 300);
            state.Player1Field.Frontend[0] = resource;

            EndPhaseProcessor.Process(state, _game, 1, _cc, new EffectRegistry());

            state.Player1Budget.Should().Be(5000 - 23);
        }

        [Fact]
        public void ElasticResource_FamilyMultiplierIncludedInMC()
        {
            _cc.Add(TestFactory.OrchestratorCard(cardId: "TST-0004"));
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle, activePlayer: 1, p1Budget: 5000);
            AddRepoCards(state, 2);

            // R+E Orchestrator: base TP=600, free_tier=600, cost_per_request=10
            // medium ×2, family C ×1.3 → intrinsicStat = trunc(600 × 2 × 1.3) = 1560
            // MC = max(0, 1560 - 600) × 10 / 100 = 96
            var resource = TestFactory.MakeResource(
                cardId: "TST-0004", instanceId: "res_1", faceUp: true,
                rank: Rank.Medium, family: InstanceFamily.C,
                maxTP: 1560, currentTP: 1560, maxAV: 3600, currentAV: 3600);
            state.Player1Field.Frontend[0] = resource;

            EndPhaseProcessor.Process(state, _game, 1, _cc, new EffectRegistry());

            state.Player1Budget.Should().Be(5000 - 96);
        }
    }

    /// <summary>Tests for EndPhaseProcessor.Process — insight generation from backend data resources.</summary>
    public class InsightGeneration : Base
    {
        [Fact]
        public void GeneratesInsightFromBackendData()
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
        public void FaceDownBackendData_NoInsight()
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
        public void ElasticDataResource_GainsElasticIncrement()
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
    }

    /// <summary>Tests for EndPhaseProcessor.Process — this_turn temporary effects expire.</summary>
    public class TemporaryEffectsExpired : Base
    {
        [Fact]
        public void ExpiresThisTurnTemporaryEffects()
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
    }

    /// <summary>Tests for EndPhaseProcessor.Process — per-turn flags reset on resources.</summary>
    public class PerTurnFlagsReset : Base
    {
        [Fact]
        public void ResetsPerTurnFlags()
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
    }

    /// <summary>Tests for EndPhaseProcessor.Process — support effect-used flag resets.</summary>
    public class SupportFlagsReset : Base
    {
        [Fact]
        public void ResetsSupportEffectUsedFlag()
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
    }

    /// <summary>Tests for EndPhaseProcessor.Process — launch failure ends the game.</summary>
    public class LaunchFailure : Base
    {
        [Fact]
        public void LaunchFailure_GameOver()
        {
            // Turn 5 → personalTurn = (5+1)/2 = 3, which >= LaunchFailureTurn=3
            var state = TestFactory.MakeGameState(turn: 5, phase: Phase.Battle, activePlayer: 1);
            // Player has never deployed (HasOperated = false)
            state.SetHasOperated(1, false);

            var result = EndPhaseProcessor.Process(state, _game, 1, _cc, new EffectRegistry());

            result.GameOver.Should().NotBeNull();
            result.GameOver!.WinnerNum.Should().Be(2);
        }
    }

    /// <summary>Tests for EndPhaseProcessor.Process — TurnEnd event emitted.</summary>
    public class TurnEndEvent : Base
    {
        [Fact]
        public void EmitsTurnEndEvent()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle, activePlayer: 1);
            AddRepoCards(state, 2);

            var result = EndPhaseProcessor.Process(state, _game, 1, _cc, new EffectRegistry());

            result.Events.Should().Contain(e => e.EventType == EventTypes.TurnEnd);
        }
    }

    /// <summary>Tests for EndPhaseProcessor.Process — empty repository on next draw ends the game.</summary>
    public class EmptyRepository : Base
    {
        [Fact]
        public void EmptyRepository_GameOver()
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
    }

    /// <summary>Tests for EndPhaseProcessor.MakeTurnStartEvent — internal TurnStart event data.</summary>
    public class TurnStartEvent : Base
    {
        [Fact]
        public void ContainsActivePlayerForViewMapping()
        {
            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main, activePlayer: 2);

            var evt = EndPhaseProcessor.MakeTurnStartEvent("test-game", state);

            evt.EventType.Should().Be(EventTypes.TurnStart);
            evt.PlayerNum.Should().BeNull();
            var data = evt.EventData.Should().BeOfType<TurnStartInternalEventData>().Subject;
            data.Turn.Should().Be(3L);
            data.ActivePlayer.Should().Be(2L);
        }
    }
}
