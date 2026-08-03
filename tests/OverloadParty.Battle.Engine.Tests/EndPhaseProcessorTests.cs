using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects.Ops;
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

            EndPhaseProcessor.Process(state, _game, 1, _cc, new EffectRegistry(), new FakeClock());

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

            EndPhaseProcessor.Process(state, _game, 1, _cc, new EffectRegistry(), new FakeClock());

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

            var result = EndPhaseProcessor.Process(state, _game, 1, _cc, new EffectRegistry(), new FakeClock());

            state.CurrentPhase.Should().Be(Phase.Battle);
            result.GameOver.Should().BeNull();
        }

        [Fact(DisplayName = "メインフェーズからバトルフェーズへ進むときはターンプレイヤーも現在ターンも変わらない")]
        public void DoesNotSwitchPlayer()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main, activePlayer: 1);

            EndPhaseProcessor.Process(state, _game, 1, _cc, new EffectRegistry(), new FakeClock());

            state.ActivePlayer.Should().Be(1);
            state.CurrentTurn.Should().Be(2);
        }

        [Fact(DisplayName = "メインフェーズのエンドフェーズ処理が previous=main・current=battle のフェーズ変更イベントを 1 件発行する")]
        public void EmitsPhaseChangeEvent()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main, activePlayer: 1);

            var result = EndPhaseProcessor.Process(state, _game, 1, _cc, new EffectRegistry(), new FakeClock());

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

            var result = EndPhaseProcessor.Process(state, _game, 1, _cc, new EffectRegistry(), new FakeClock());

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

            var result = EndPhaseProcessor.Process(state, _game, 1, _cc, new EffectRegistry(), new FakeClock());

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
            var result1 = EndPhaseProcessor.Process(state, _game, 1, _cc, new EffectRegistry(), new FakeClock());
            state.CurrentPhase.Should().Be(Phase.Battle);
            state.ActivePlayer.Should().Be(1);
            state.CurrentTurn.Should().Be(2);

            // Second end_phase: Battle → End → turn switch → draw → main
            var result2 = EndPhaseProcessor.Process(state, _game, 1, _cc, new EffectRegistry(), new FakeClock());
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

            var result = EndPhaseProcessor.Process(state, _game, 1, _cc, new EffectRegistry(), new FakeClock());

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

            var result = EndPhaseProcessor.Process(state, _game, 1, _cc, new EffectRegistry(), new FakeClock());

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

            EndPhaseProcessor.Process(state, _game, 1, _cc, new EffectRegistry(), new FakeClock());

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

            EndPhaseProcessor.Process(state, _game, 1, _cc, new EffectRegistry(), new FakeClock());

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

            EndPhaseProcessor.Process(state, _game, 1, _cc, new EffectRegistry(), new FakeClock());

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

            EndPhaseProcessor.Process(state, _game, 1, _cc, new EffectRegistry(), new FakeClock());

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

            EndPhaseProcessor.Process(state, _game, 1, _cc, new EffectRegistry(), new FakeClock());

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

            EndPhaseProcessor.Process(state, _game, 1, _cc, new EffectRegistry(), new FakeClock());

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

            EndPhaseProcessor.Process(state, _game, 1, _cc, new EffectRegistry(), new FakeClock());

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

            EndPhaseProcessor.Process(state, _game, 1, _cc, new EffectRegistry(), new FakeClock());

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

            EndPhaseProcessor.Process(state, _game, 1, _cc, new EffectRegistry(), new FakeClock());

            resource.TemporaryEffects.Should().BeEmpty();
        }
    }

    [Trait("対象", "ターン内フラグのリセット")]
    public class PerTurnFlagsReset : Base
    {
        [Fact(DisplayName = "エンドフェーズにリソースの攻撃済み・効果使用済み・収益化使用済みとインシデント使用のターン内フラグがリセットされる")]
        public void ResetsPerTurnFlags()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle, activePlayer: 1);
            AddRepoCards(state, 2);

            var resource = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "res_1", faceUp: true);
            resource.HasAttacked = true;
            resource.EffectUsedThisTurn = true;
            resource.MonetizedThisTurn = true;
            state.Player1Field.Frontend[0] = resource;

            state.SetIncidentPlayedThisTurn(1, true);

            EndPhaseProcessor.Process(state, _game, 1, _cc, new EffectRegistry(), new FakeClock());

            resource.HasAttacked.Should().BeFalse();
            resource.EffectUsedThisTurn.Should().BeFalse();
            resource.MonetizedThisTurn.Should().BeFalse();
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

            EndPhaseProcessor.Process(state, _game, 1, _cc, new EffectRegistry(), new FakeClock());

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

            var result = EndPhaseProcessor.Process(state, _game, 1, _cc, new EffectRegistry(), new FakeClock());

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

            var endResult = EndPhaseProcessor.Process(state, _game, 1, _cc, new EffectRegistry(), new FakeClock());

            endResult.ShouldDiscard.Should().BeTrue("手札超過のため判定は保留され破棄が要求される");
            endResult.GameOver.Should().BeNull();

            var discardResult = DiscardProcessor.Process(
                state, _game, 1, new DiscardHandRequest { CardInstanceIDs = ["hand_6", "hand_7"] }, _cc,
                new EffectRegistry(), new FakeClock());

            discardResult.GameOver.Should().NotBeNull("破棄解決時にローンチ失敗判定が走る");
            discardResult.GameOver!.WinnerNum.Should().Be(2);
            discardResult.GameOver.Reason.Should().Be(WinReasons.LaunchFailure);
        }
    }

    [Trait("対象", "ターン終了イベントの発行")]
    public class TurnEndEvent : Base
    {
        [Fact(DisplayName = "エンドフェーズ処理でターン終了イベントが発行される")]
        public void EmitsTurnEndEvent()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle, activePlayer: 1);
            AddRepoCards(state, 2);

            var result = EndPhaseProcessor.Process(state, _game, 1, _cc, new EffectRegistry(), new FakeClock());

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
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(
                cardId: "TST-0001", instanceId: "res_1", faceUp: true);
            // Do NOT add repo cards for player 2 (next draw will fail)

            var result = EndPhaseProcessor.Process(state, _game, 1, _cc, new EffectRegistry(), new FakeClock());

            result.GameOver.Should().NotBeNull();
            result.GameOver!.WinnerNum.Should().Be(1);
            result.GameOver.Reason.Should().Be(WinReasons.DeckOut);
        }
    }

    [Trait("対象", "ターン開始イベントの生成")]
    public class TurnStartEvent : Base
    {
        [Fact(DisplayName = "ターン開始イベントの生成は、現在ターン 3 とアクティブプレイヤー 2 を持つイベントになる")]
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
        cc.Add(TestFactory.ComputeCard(cardId: "TST-0002", name: "SecondEndPhase"));
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

    [Trait("対象", "エンドフェーズの段階別勝敗判定")]
    public class StagedWinConditions
    {
        private const string HighMaintenanceCardId = "TST-0011";
        private const string SupportCardId = "TST-0210";

        /// <summary>維持コスト 300 のリソースと、稼働実績を伴わないサポートを登録したキャッシュを作る。</summary>
        /// <returns>段階判定テスト用のキャッシュ。</returns>
        private static TestCardCache StagedCc()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
            cc.Add(TestFactory.ComputeCard(cardId: HighMaintenanceCardId, mc: 300));
            cc.Add(TestFactory.PlatformCard(cardId: SupportCardId));
            return cc;
        }

        /// <summary>プレイヤー 1 のフロントエンドに維持コスト 300 の表向きリソースを置く。</summary>
        /// <param name="state">対象のゲーム状態。</param>
        private static void PlaceHighMaintenanceResource(BattleGameState state)
        {
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(
                cardId: HighMaintenanceCardId, instanceId: "res_1", faceUp: true);
        }

        /// <summary>プレイヤー 1 のサポートゾーンに、稼働実績を伴わない表向きサポートを置く。</summary>
        /// <param name="state">対象のゲーム状態。</param>
        private static void PlaceSupport(BattleGameState state)
        {
            state.Player1Field.Support[0] = new DeployedSupport
            {
                InstanceID = "sup_1",
                CardID = SupportCardId,
                FaceUp = true,
                DeployingTurnsLeft = 0,
            };
        }

        /// <summary>指定プレイヤーのバジェットを 0 にするエンドフェーズ効果を登録したレジストリを作る。</summary>
        /// <param name="cardId">効果を持つカードの ID。</param>
        /// <param name="targetPlayerNum">バジェットを 0 にする対象のプレイヤー番号。</param>
        /// <returns>エンドフェーズ効果を 1 件だけ持つレジストリ。</returns>
        private static TestEffectRegistry BudgetDrainingEffect(string cardId, long targetPlayerNum)
        {
            var effects = new TestEffectRegistry();
            effects.Register(cardId, TriggerType.OnEndPhase, ctx =>
            {
                ctx.State.SetBudget(targetPlayerNum, 0);
                return new EffectResult();
            });
            return effects;
        }

        /// <summary>プレイヤー 1 のエンドフェーズを終了させる。</summary>
        /// <param name="state">対象のゲーム状態。</param>
        /// <param name="effects">効果ハンドラのレジストリ。</param>
        /// <returns>アクション結果。</returns>
        private static ActionResult EndPhase(BattleGameState state, IEffectRegistry effects) =>
            EndPhaseProcessor.Process(
                state, TestFactory.MakeGame(), 1, StagedCc(), effects, new FakeClock());

        [Fact(DisplayName = "維持コスト 300 のリソースを持ちバジェットが 300 のとき、エンドフェーズを終えると、バジェット 0 で自分がバジェットゼロ敗北になる")]
        public void MaintenanceCostReachesZero_LosesByBudgetZero()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle, activePlayer: 1, p1Budget: 300);
            AddRepo(state, 2);
            PlaceHighMaintenanceResource(state);

            var result = EndPhase(state, new EffectRegistry());

            state.Player1Budget.Should().Be(0);
            result.GameOver.Should().NotBeNull();
            result.GameOver!.WinnerNum.Should().Be(2);
            result.GameOver.Reason.Should().Be(WinReasons.BudgetZero);
        }

        [Fact(DisplayName = "維持コスト 300 のリソースを持ちバジェットが 301 のとき、エンドフェーズを終えると、バジェット 1 でゲームが続行しターンが相手へ移る")]
        public void MaintenanceCostLeavesBudget_ContinuesGame()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle, activePlayer: 1, p1Budget: 301);
            AddRepo(state, 2);
            PlaceHighMaintenanceResource(state);

            var result = EndPhase(state, new EffectRegistry());

            state.Player1Budget.Should().Be(1);
            result.GameOver.Should().BeNull();
            state.ActivePlayer.Should().Be(2);
        }

        [Fact(DisplayName = "維持コストで自分のバジェットが 0 になり相手のデッキが 0 枚のとき、エンドフェーズを終えると、デッキアウトではなく自分のバジェットゼロ敗北になる")]
        public void BudgetZeroPrecedesOpponentDeckOut()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle, activePlayer: 1, p1Budget: 300);
            PlaceHighMaintenanceResource(state);

            var result = EndPhase(state, new EffectRegistry());

            result.GameOver.Should().NotBeNull();
            result.GameOver!.WinnerNum.Should().Be(2);
            result.GameOver.Reason.Should().Be(WinReasons.BudgetZero);
        }

        [Fact(DisplayName = "エンドフェーズ効果で相手のバジェットが 0 になり維持コストで自分のバジェットも 0 になるとき、エンドフェーズを終えると、引き分けになる")]
        public void BothBudgetsReachZero_Draw()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle, activePlayer: 1, p1Budget: 300);
            AddRepo(state, 2);
            PlaceHighMaintenanceResource(state);

            var result = EndPhase(state, BudgetDrainingEffect(HighMaintenanceCardId, targetPlayerNum: 2));

            result.GameOver.Should().NotBeNull();
            result.GameOver!.WinnerNum.Should().Be(0);
            result.GameOver.Reason.Should().Be(WinReasons.Draw);
        }

        [Fact(DisplayName = "稼働実績があり表向きリソースが 0 体で相手のデッキが 0 枚のとき、エンドフェーズを終えると、デッキアウトではなく自分のシステムダウン敗北になる")]
        public void SystemDownPrecedesOpponentDeckOut()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle, activePlayer: 1);
            state.SetHasOperated(1, true);

            var result = EndPhase(state, new EffectRegistry());

            result.GameOver.Should().NotBeNull();
            result.GameOver!.WinnerNum.Should().Be(2);
            result.GameOver.Reason.Should().Be(WinReasons.SystemDown);
        }

        [Fact(DisplayName = "自分の 3 ターン目で稼働実績がないプレイヤーのエンドフェーズ効果で相手のバジェットが 0 になるとき、エンドフェーズを終えると、ローンチ失敗ではなく相手のバジェットゼロ敗北になる")]
        public void OpponentBudgetZeroPrecedesOwnLaunchFailure()
        {
            var state = TestFactory.MakeGameState(turn: 5, phase: Phase.Battle, activePlayer: 1);
            AddRepo(state, 2);
            state.SetHasOperated(1, false);
            PlaceSupport(state);

            var result = EndPhase(state, BudgetDrainingEffect(SupportCardId, targetPlayerNum: 2));

            result.GameOver.Should().NotBeNull();
            result.GameOver!.WinnerNum.Should().Be(1);
            result.GameOver.Reason.Should().Be(WinReasons.BudgetZero);
        }

        [Fact(DisplayName = "自分の 3 ターン目で稼働実績がなくバジェットに変化がないとき、エンドフェーズを終えると、ローンチ失敗で敗北する")]
        public void NoBudgetChange_LosesByLaunchFailure()
        {
            var state = TestFactory.MakeGameState(turn: 5, phase: Phase.Battle, activePlayer: 1);
            AddRepo(state, 2);
            state.SetHasOperated(1, false);
            PlaceSupport(state);

            var result = EndPhase(state, new EffectRegistry());

            result.GameOver.Should().NotBeNull();
            result.GameOver!.WinnerNum.Should().Be(2);
            result.GameOver.Reason.Should().Be(WinReasons.LaunchFailure);
        }

        [Fact(DisplayName = "手札が 8 枚あり維持コストでバジェットが 0 になるとき、エンドフェーズを終えると、破棄を要求せずバジェットゼロ敗北になる")]
        public void BudgetZeroWithHandOverLimit_SkipsDiscard()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle, activePlayer: 1, p1Budget: 300);
            AddRepo(state, 2);
            PlaceHighMaintenanceResource(state);
            foreach (var i in Enumerable.Range(0, 8))
            {
                state.Player1Hand.Add(new UndeployedCard { InstanceID = $"hand_{i}", CardID = "TST-0001" });
            }

            var result = EndPhase(state, new EffectRegistry());

            result.ShouldDiscard.Should().BeFalse();
            result.GameOver.Should().NotBeNull();
            result.GameOver!.WinnerNum.Should().Be(2);
            result.GameOver.Reason.Should().Be(WinReasons.BudgetZero);
        }
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

            EndPhaseProcessor.Process(state, TestFactory.MakeGame(), 1, EndPhaseCc(), effects, new FakeClock());

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

            EndPhaseProcessor.Process(state, TestFactory.MakeGame(), 1, EndPhaseCc(), effects, new FakeClock());

            fired.Should().Be(1, "エンドフェーズに パッシブ効果 が発動する");
        }

        [Fact(DisplayName = "リソースに装着したアタッチメントのエンドフェーズ効果は、1 ターンに 1 回だけ発動する")]
        public void AttachmentEndPhaseEffect_FiresOncePerTurn()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle, activePlayer: 1);
            AddRepo(state, 2);
            state.Player1Field.Frontend[0] = OrderedResource("TST-0001", "r_1", 1);
            state.Player1Field.Support[0] = new DeployedSupport
            {
                InstanceID = "att_1",
                CardID = "TST-0002",
                FaceUp = true,
                TargetInstanceID = "r_1",
            };

            int fired = 0;
            var effects = new TestEffectRegistry();
            effects.Register("TST-0002", TriggerType.OnEndPhase, _ => { fired++; return new EffectResult(); });

            EndPhaseProcessor.Process(state, TestFactory.MakeGame(), 1, EndPhaseCc(), effects, new FakeClock());

            fired.Should().Be(1);
        }

        [Fact(DisplayName = "エンドフェーズ効果が選択を要求すると、選択待ちへ遷移する")]
        public void EndPhaseEffectRequestingChoice_Suspends()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle, activePlayer: 1, p1Budget: 5000);
            AddRepo(state, 2);
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "r_1", faceUp: true);
            AddRepo(state, 1);

            var peekMeta = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(
                """{"peek":2}""");
            var effects = new EffectRegistry();
            effects.RegisterComposed("TST-0001", TriggerType.OnEndPhase,
                new GainBudgetOp(PlayerRef.Myself, new StaticAmount(300)),
                new CustomFnOp(new CustomEffectRegistry().Build(CustomEffects.KeepOneFromDeckTop, peekMeta)!));

            EndPhaseProcessor.Process(state, TestFactory.MakeGame(), 1, EndPhaseCc(), effects, new FakeClock());

            state.PendingEffectChoice.Should().NotBeNull();
            state.PendingEffectChoice!.ChoiceKind.Should().Be(ChoiceKinds.DeckTop);
        }

        [Fact(DisplayName = "エンドフェーズ効果が選択待ちに入るまでに実行した手順の結果は、盤面に残る")]
        public void EndPhaseEffectRequestingChoice_KeepsAlreadyAppliedOps()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle, activePlayer: 1, p1Budget: 5000);
            AddRepo(state, 2);
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "r_1", faceUp: true);
            AddRepo(state, 1);

            var peekMeta = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(
                """{"peek":2}""");
            var effects = new EffectRegistry();
            effects.RegisterComposed("TST-0001", TriggerType.OnEndPhase,
                new GainBudgetOp(PlayerRef.Myself, new StaticAmount(300)),
                new CustomFnOp(new CustomEffectRegistry().Build(CustomEffects.KeepOneFromDeckTop, peekMeta)!));

            EndPhaseProcessor.Process(state, TestFactory.MakeGame(), 1, EndPhaseCc(), effects, new FakeClock());

            state.GetBudget(1).Should().Be(5300);
        }

        /// <summary>エンドフェーズ効果の発動順を決めるため、DeployOrder を指定してリソースを作る。</summary>
        /// <param name="cardId">カード ID。</param>
        /// <param name="instanceId">インスタンス ID。</param>
        /// <param name="deployOrder">発動順。小さいほど先に発動する。</param>
        /// <returns>DeployOrder を設定した表向きのリソース。</returns>
        private static DeployedResource OrderedResource(string cardId, string instanceId, long deployOrder)
        {
            var resource = TestFactory.MakeResource(cardId: cardId, instanceId: instanceId, faceUp: true);
            resource.DeployOrder = deployOrder;
            return resource;
        }

        /// <summary>指定回数だけ選択待ちを返し、それ以降は成立するハンドラ。</summary>
        /// <param name="instanceId">効果を持つリソースのインスタンス ID。</param>
        /// <param name="suspendCount">選択待ちに入る回数。</param>
        /// <returns>選択待ちを指定回数だけ要求するハンドラ。</returns>
        private static EffectHandler SuspendingHandler(string instanceId, int suspendCount)
        {
            int remaining = suspendCount;
            return _ =>
            {
                if (remaining <= 0) { return new EffectResult(); }

                remaining--;
                return new EffectResult
                {
                    PendingChoice = new PendingEffectChoice
                    {
                        ChooserPlayerNum = 1,
                        OwnerPlayerNum = 1,
                        EffectCardId = "TST-0001",
                        EffectInstanceId = instanceId,
                        Trigger = TriggerType.OnEndPhase,
                        ChoiceKey = "instanceId",
                        Candidates = ["choice_1"],
                        ChoiceKind = ChoiceKinds.FieldTarget,
                    },
                };
            };
        }

        private static ResolvePendingChoiceRequest ChoiceReq() => new() { ChosenId = "choice_1" };

        [Fact(DisplayName = "エンドフェーズ効果が選択待ちに入ったとき、後続の効果は発動せずターンも交代しない")]
        public void EndPhaseEffectRequestingChoice_HoldsTurnProgression()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle, activePlayer: 1);
            AddRepo(state, 2);
            state.Player1Field.Frontend[0] = OrderedResource("TST-0001", "r_1", 1);
            state.Player1Field.Frontend[1] = OrderedResource("TST-0002", "r_2", 2);

            int laterFired = 0;
            var effects = new TestEffectRegistry();
            effects.Register("TST-0001", TriggerType.OnEndPhase, SuspendingHandler("r_1", 1));
            effects.Register("TST-0002", TriggerType.OnEndPhase, _ => { laterFired++; return new EffectResult(); });

            EndPhaseProcessor.Process(state, TestFactory.MakeGame(), 1, EndPhaseCc(), effects, new FakeClock());

            state.PendingEffectChoice.Should().NotBeNull();
            laterFired.Should().Be(0);
            state.ActivePlayer.Should().Be(1);
            state.CurrentTurn.Should().Be(2);
        }

        [Fact(DisplayName = "選択を解決すると、残りのエンドフェーズ効果が発動しターンが交代する")]
        public void ResolvingEndPhaseChoice_RunsRemainingEffectsAndAdvancesTurn()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle, activePlayer: 1);
            AddRepo(state, 2);
            state.Player1Field.Frontend[0] = OrderedResource("TST-0001", "r_1", 1);
            state.Player1Field.Frontend[1] = OrderedResource("TST-0002", "r_2", 2);

            int laterFired = 0;
            var effects = new TestEffectRegistry();
            effects.Register("TST-0001", TriggerType.OnEndPhase, SuspendingHandler("r_1", 1));
            effects.Register("TST-0002", TriggerType.OnEndPhase, _ => { laterFired++; return new EffectResult(); });

            var game = TestFactory.MakeGame();
            EndPhaseProcessor.Process(state, game, 1, EndPhaseCc(), effects, new FakeClock());

            ResolvePendingChoiceProcessor.Process(
                state, game, 1, ChoiceReq(), EndPhaseCc(), effects, new FakeClock());

            state.PendingEffectChoice.Should().BeNull();
            laterFired.Should().Be(1);
            state.ActivePlayer.Should().Be(2);
        }

        [Fact(DisplayName = "選択を解決したとき、中断前に発動を終えた効果は二重に発動しない")]
        public void ResolvingEndPhaseChoice_DoesNotRefireAlreadyFiredEffects()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle, activePlayer: 1);
            AddRepo(state, 2);
            state.Player1Field.Frontend[0] = OrderedResource("TST-0002", "r_2", 1);
            state.Player1Field.Frontend[1] = OrderedResource("TST-0001", "r_1", 2);

            int earlierFired = 0;
            var effects = new TestEffectRegistry();
            effects.Register("TST-0002", TriggerType.OnEndPhase, _ => { earlierFired++; return new EffectResult(); });
            effects.Register("TST-0001", TriggerType.OnEndPhase, SuspendingHandler("r_1", 1));

            var game = TestFactory.MakeGame();
            EndPhaseProcessor.Process(state, game, 1, EndPhaseCc(), effects, new FakeClock());

            ResolvePendingChoiceProcessor.Process(
                state, game, 1, ChoiceReq(), EndPhaseCc(), effects, new FakeClock());

            earlierFired.Should().Be(1);
        }

        [Fact(DisplayName = "エンドフェーズ効果が二段階の選択を要求したとき、二度目を解決すると残りの効果が発動しターンが交代する")]
        public void MultiStageEndPhaseChoice_ResumesAfterFinalResolution()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle, activePlayer: 1);
            AddRepo(state, 2);
            state.Player1Field.Frontend[0] = OrderedResource("TST-0001", "r_1", 1);
            state.Player1Field.Frontend[1] = OrderedResource("TST-0002", "r_2", 2);

            int laterFired = 0;
            var effects = new TestEffectRegistry();
            effects.Register("TST-0001", TriggerType.OnEndPhase, SuspendingHandler("r_1", 2));
            effects.Register("TST-0002", TriggerType.OnEndPhase, _ => { laterFired++; return new EffectResult(); });

            var game = TestFactory.MakeGame();
            EndPhaseProcessor.Process(state, game, 1, EndPhaseCc(), effects, new FakeClock());

            ResolvePendingChoiceProcessor.Process(
                state, game, 1, ChoiceReq(), EndPhaseCc(), effects, new FakeClock());

            state.PendingEffectChoice.Should().NotBeNull("二段階目の選択が残る");
            laterFired.Should().Be(0);
            state.ActivePlayer.Should().Be(1);

            ResolvePendingChoiceProcessor.Process(
                state, game, 1, ChoiceReq(), EndPhaseCc(), effects, new FakeClock());

            state.PendingEffectChoice.Should().BeNull();
            laterFired.Should().Be(1);
            state.ActivePlayer.Should().Be(2);
        }
    }
}
