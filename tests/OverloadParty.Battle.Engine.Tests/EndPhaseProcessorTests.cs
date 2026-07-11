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

    [Trait("対象", "休止リソースの処理")]
    public class Dormant : Base
    {
        [Fact(DisplayName = "休止中のデータベースはイールドを生成せずインサイトプールが 0 のままになる")]
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

        [Fact(DisplayName = "until_next_turn_end の休止効果がエンドフェーズ処理で解除される")]
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

    [Trait("対象", "メインフェーズからバトルフェーズへの遷移")]
    public class MainToBattle : Base
    {
        [Fact(DisplayName = "メインフェーズでエンドフェーズ処理するとバトルフェーズへ進みゲームは終了しない")]
        public void AdvancesToBattle_NotToEnd()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main, activePlayer: 1);

            var result = EndPhaseProcessor.Process(state, _game, 1, _cc, new EffectRegistry());

            state.CurrentPhase.Should().Be(Phase.Battle);
            result.GameOver.Should().BeNull();
        }

        [Fact(DisplayName = "メインフェーズからバトルフェーズへ進むときはターンプレイヤーも現在ターンも変わらない")]
        public void DoesNotSwitchPlayer()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main, activePlayer: 1);

            EndPhaseProcessor.Process(state, _game, 1, _cc, new EffectRegistry());

            state.ActivePlayer.Should().Be(1);
            state.CurrentTurn.Should().Be(2);
        }

        [Fact(DisplayName = "メインフェーズのエンドフェーズ処理が previous=main・current=battle の PhaseChange イベントを 1 件発行する")]
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

    [Trait("対象", "初回ターンのフェーズ遷移")]
    public class MainToEndFirstTurn : Base
    {
        [Fact(DisplayName = "初回ターンのメインフェーズはバトルフェーズを飛ばし、ターンが相手へ移ってメインフェーズになる")]
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

    [Trait("対象", "バトルフェーズからエンドフェーズへの遷移")]
    public class BattleToEnd : Base
    {
        [Fact(DisplayName = "バトルフェーズでエンドフェーズ処理するとターンが相手へ移り、次ターンのメインフェーズになる")]
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

    [Trait("対象", "連続するエンドフェーズ処理")]
    public class TwoEndPhases : Base
    {
        [Fact(DisplayName = "エンドフェーズ処理を 2 回続けるとメインフェーズからバトルフェーズを経てターンが相手へ移る")]
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

    [Trait("対象", "手札上限による破棄要求")]
    public class DiscardRequired : Base
    {
        [Fact(DisplayName = "手札が 7 枚で上限 6 を超えると破棄が要求され、エンドフェーズで止まりターンが移らない")]
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

        [Fact(DisplayName = "手札が上限どおり 6 枚なら破棄は不要でターンが相手へ移る")]
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

    [Trait("対象", "維持コストの徴収")]
    public class MaintenanceCost : Base
    {
        [Fact(DisplayName = "表向きリソースの維持コスト 150 がエンドフェーズにバジェットから差し引かれる")]
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

    [Trait("対象", "エラスティックリソースの維持コスト")]
    public class ElasticMaintenanceCost : Base
    {
        [Fact(DisplayName = "エラスティックリソースの固有ステータスが free_tier と同じなら維持コストが 0 になる")]
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

        [Fact(DisplayName = "エラスティックボーナスで固有ステータスが free_tier を超えると維持コスト 23 がバジェットから引かれる")]
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

        [Fact(DisplayName = "維持コストの算出にランクとインスタンスファミリーの係数が反映され 96 が引かれる")]
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

        [Fact(DisplayName = "cost_per_request が 0 のサーバーレスは固有ステータスが free_tier を超えても維持コストが 0 になる")]
        public void ServerlessElastic_AlwaysFreeMaintenanceCost()
        {
            _cc.Add(TestFactory.ServerlessCard(cardId: "TST-0005"));
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle, activePlayer: 1, p1Budget: 5000);
            AddRepoCards(state, 2);

            // Serverless: cost_per_request=0 → 固有ステータスが free_tier を超えても維持コストは常に 0
            var resource = TestFactory.MakeResource(
                cardId: "TST-0005", instanceId: "res_1", faceUp: true,
                maxTP: 300, currentTP: 300, maxAV: 600, currentAV: 600, elasticBonus: 500);
            state.Player1Field.Frontend[0] = resource;

            EndPhaseProcessor.Process(state, _game, 1, _cc, new EffectRegistry());

            state.Player1Budget.Should().Be(5000);
        }
    }

    [Trait("対象", "バックエンドのインサイト生成")]
    public class InsightGeneration : Base
    {
        [Fact(DisplayName = "バックエンドの表向きData系リソースがエンドフェーズにイールド 400 分のインサイトを生成する")]
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

        [Fact(DisplayName = "バックエンドの裏向きData系リソースはインサイトを生成しない")]
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

        [Fact(DisplayName = "エラスティックなData系リソースはエンドフェーズにエラスティック増分 50 だけエラスティックボーナスが増える")]
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

    [Trait("対象", "this_turn 効果の解除")]
    public class TemporaryEffectsExpired : Base
    {
        [Fact(DisplayName = "this_turn の一時効果がエンドフェーズで解除される")]
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

    [Trait("対象", "ターン内フラグのリセット")]
    public class PerTurnFlagsReset : Base
    {
        [Fact(DisplayName = "エンドフェーズにリソースの攻撃済み・効果使用済み・収益化量とインシデント使用のターン内フラグがリセットされる")]
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

    [Trait("対象", "サポートの効果使用フラグのリセット")]
    public class SupportFlagsReset : Base
    {
        [Fact(DisplayName = "エンドフェーズにサポートカードの効果使用フラグがリセットされる")]
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

    [Trait("対象", "ローンチ失敗による敗北")]
    public class LaunchFailure : Base
    {
        // 手札破棄が不要な場合: EndPhase がその場でローンチ失敗判定を行い敗北する。
        [Fact(DisplayName = "稼働実績のないまま 3 ターン目のエンドフェーズを迎えるとローンチ失敗で敗北し相手が勝者になる")]
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

        // 手札破棄が必要な場合: EndPhase は判定を保留して破棄を要求し、破棄解決時に同じ判定が走り敗北する。
        [Fact(DisplayName = "手札超過のときローンチ失敗判定は破棄まで保留され、破棄を解決するとローンチ失敗で敗北する")]
        public void LaunchFailure_DeferredToDiscardWhenHandOverLimit()
        {
            var state = TestFactory.MakeGameState(turn: 5, phase: Phase.Battle, activePlayer: 1);
            state.SetHasOperated(1, false);
            foreach (var i in Enumerable.Range(0, 8))
            {
                state.Player1Hand.Add(new UndeployedCard { InstanceID = $"hand_{i}", CardID = "TST-0001" });
            }

            var endResult = EndPhaseProcessor.Process(state, _game, 1, _cc, new EffectRegistry());

            endResult.ShouldDiscard.Should().BeTrue("手札超過のため判定は保留され破棄が要求される");
            endResult.GameOver.Should().BeNull();

            var discardResult = DiscardProcessor.Process(
                state, _game, 1, new DiscardHandRequest { CardInstanceIDs = ["hand_6", "hand_7"] }, _cc, new EffectRegistry());

            discardResult.GameOver.Should().NotBeNull("破棄解決時にローンチ失敗判定が走る");
            discardResult.GameOver!.WinnerNum.Should().Be(2);
            discardResult.GameOver.Reason.Should().Be(WinReasons.LaunchFailure);
        }
    }

    [Trait("対象", "ターン終了イベントの発行")]
    public class TurnEndEvent : Base
    {
        [Fact(DisplayName = "エンドフェーズ処理で TurnEnd イベントが発行される")]
        public void EmitsTurnEndEvent()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle, activePlayer: 1);
            AddRepoCards(state, 2);

            var result = EndPhaseProcessor.Process(state, _game, 1, _cc, new EffectRegistry());

            result.Events.Should().Contain(e => e.EventType == EventTypes.TurnEnd);
        }
    }

    [Trait("対象", "デッキアウトによる敗北")]
    public class EmptyRepository : Base
    {
        [Fact(DisplayName = "ターン交代後のプレイヤーがドローできずデッキアウトで敗北し、もう一方が勝者になる")]
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

    [Trait("対象", "ターン開始イベントの生成")]
    public class TurnStartEvent : Base
    {
        [Fact(DisplayName = "MakeTurnStartEvent が現在ターン 3 とアクティブプレイヤー 2 を持つ TurnStart イベントを生成する")]
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

    /// <summary>エンドフェーズ用にコンピュート系リソースを登録したキャッシュを作る。</summary>
    /// <returns>TST-0001 を登録したキャッシュ。</returns>
    private static TestCardCache EndPhaseCc()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
        return cc;
    }

    /// <summary>指定プレイヤーのリポジトリにカードを 2 枚補充する。</summary>
    /// <param name="state">対象のゲーム状態。</param>
    /// <param name="playerNum">補充先のプレイヤー番号。</param>
    private static void AddRepo(BattleGameState state, long playerNum)
    {
        var repo = state.GetRepository(playerNum);
        repo.Add(new UndeployedCard { InstanceID = "repo_1", CardID = "TST-0001" });
        repo.Add(new UndeployedCard { InstanceID = "repo_2", CardID = "TST-0001" });
    }

    [Trait("対象", "エンドフェーズ効果とパッシブ効果の発動")]
    public class EndPhaseTriggers
    {
        [Fact(DisplayName = "エンドフェーズにリソースのエンドフェーズ効果ハンドラが 1 回発動する")]
        public void FiresOnEndPhaseHandler()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle, activePlayer: 1);
            AddRepo(state, 2);
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "r_1", faceUp: true);

            int fired = 0;
            var effects = new TestEffectRegistry();
            effects.Register("TST-0001", TriggerType.OnEndPhase, _ => { fired++; return new EffectResult(); });

            EndPhaseProcessor.Process(state, TestFactory.MakeGame(), 1, EndPhaseCc(), effects);

            fired.Should().Be(1, "エンドフェーズに エンドフェーズ効果 が発動する");
        }

        [Fact(DisplayName = "エンドフェーズにリソースのパッシブ効果ハンドラが 1 回発動する")]
        public void FiresPassiveHandlerAtEndPhase()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle, activePlayer: 1);
            AddRepo(state, 2);
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "r_1", faceUp: true);

            int fired = 0;
            var effects = new TestEffectRegistry();
            effects.Register("TST-0001", TriggerType.Passive, _ => { fired++; return new EffectResult(); });

            EndPhaseProcessor.Process(state, TestFactory.MakeGame(), 1, EndPhaseCc(), effects);

            fired.Should().Be(1, "エンドフェーズに パッシブ効果 が発動する");
        }
    }
}
