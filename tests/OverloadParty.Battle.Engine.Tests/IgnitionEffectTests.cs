using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Effects.Ops;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Engine;

/// <summary>
/// 起動効果として登録した op / カスタム効果を use_ignition 経由で発動し、効果が state を変えることを検証する。
/// op を直接叩かず、プレイヤーのアクション (起動効果の使用) 越しに振る舞いを確認する。
/// </summary>
public class IgnitionEffectTests
{
    /// <summary>指定カードに op 列を起動効果として登録したキャッシュとレジストリを作る。</summary>
    /// <param name="card">起動効果を持たせるカード定義。</param>
    /// <param name="ops">起動効果として登録する op 列。</param>
    /// <returns>カードキャッシュと効果レジストリ。</returns>
    private static (TestCardCache Cc, EffectRegistry Effects) Env(CardDefinition card, params IEffectOp[] ops)
    {
        var cc = new TestCardCache();
        cc.Add(card);
        var effects = new EffectRegistry();
        effects.RegisterComposed(card.CardId, TriggerType.Ignition, ops);
        return (cc, effects);
    }

    /// <summary>指定カスタム効果を 1 つの op として包む。</summary>
    /// <param name="name">カスタム効果名。</param>
    /// <returns>カスタム効果を実行する op。</returns>
    private static IEffectOp Custom(string name) =>
        new CustomFnOp(new CustomEffectRegistry().Build(name, null)!);

    /// <summary>起動効果を使用するリクエストを作る。</summary>
    /// <param name="instanceId">発動元インスタンス ID。</param>
    /// <param name="targetInstanceId">任意の対象インスタンス ID。</param>
    /// <param name="choiceData">任意の選択データ。</param>
    /// <returns>起動効果使用リクエスト。</returns>
    private static UseIgnitionRequest Use(
        string instanceId, string? targetInstanceId = null, Dictionary<string, object>? choiceData = null) =>
        new() { InstanceID = instanceId, TargetInstanceID = targetInstanceId, ChoiceData = choiceData };

    [Trait("対象", "deal_damage の起動効果")]
    public class DealDamage
    {
        [Fact(DisplayName = "deal_damage を起動効果で発動すると対象が 300 のダメージを受ける")]
        public void Ignition_DealsDamageToTarget()
        {
            var (cc, effects) = Env(TestFactory.ComputeCard(cardId: "TST-0001"),
                new DealDamageOp(TargetSelector.Instance, new StaticAmount(300)));
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "src", faceUp: true);
            var target = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "tgt", faceUp: true, maxAV: 1000, damage: 0);
            state.Player2Field.Frontend[0] = target;

            UseIgnitionProcessor.Process(state, TestFactory.MakeGame(), 1, Use("src", "tgt"), cc, effects);

            target.Damage.Should().Be(300);
        }
    }

    [Trait("対象", "gain_insight の起動効果")]
    public class GainInsight
    {
        [Fact(DisplayName = "gain_insight を起動効果で発動すると自分のインサイトプールが 300 増える")]
        public void Ignition_AddsToOwnPool()
        {
            var (cc, effects) = Env(TestFactory.ComputeCard(cardId: "TST-0001"), new GainInsightOp(new StaticAmount(300)));
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "src", faceUp: true);
            state.SetInsightPool(1, 100);

            UseIgnitionProcessor.Process(state, TestFactory.MakeGame(), 1, Use("src"), cc, effects);

            state.GetInsightPool(1).Should().Be(400);
        }
    }

    [Trait("対象", "absorb_insight の起動効果")]
    public class AbsorbInsight
    {
        [Theory(DisplayName = "absorb_insight を起動効果で発動すると相手のインサイトを吸収量と相手の保有量の小さい方まで吸収する")]
        [InlineData(120, 120, 0)]
        [InlineData(500, 300, 200)]
        public void Ignition_TransfersClampedToOpponentPool(long oppPool, long expectedGained, long expectedOppLeft)
        {
            var (cc, effects) = Env(TestFactory.ComputeCard(cardId: "TST-0001"), new AbsorbInsightOp(new StaticAmount(300)));
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "src", faceUp: true);
            state.SetInsightPool(1, 0);
            state.SetInsightPool(2, oppPool);

            UseIgnitionProcessor.Process(state, TestFactory.MakeGame(), 1, Use("src"), cc, effects);

            state.GetInsightPool(1).Should().Be(expectedGained);
            state.GetInsightPool(2).Should().Be(expectedOppLeft);
        }
    }

    [Trait("対象", "destroy_check の起動効果")]
    public class DestroyCheck
    {
        [Fact(DisplayName = "destroy_check を起動効果で発動すると実効可用性が 0 以下のリソースが破壊され、SLA ペナルティ減算・on_destroy 発火・トラッシュ移動を伴う")]
        public void Ignition_DestroysZeroedResource()
        {
            var (cc, effects) = Env(TestFactory.ComputeCard(cardId: "TST-0001", slaPenalty: 400), new DestroyCheckOp());
            effects.Register("TST-0001", TriggerType.OnDestroy, ctx => new EffectResult
            {
                Events = [new GameEvent { EventType = "on_destroy_triggered", GameID = ctx.Game.GameID }]
            });

            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "src", faceUp: true);
            state.Player1Field.Frontend[1] = TestFactory.MakeResource(
                cardId: "TST-0001", instanceId: "dead", maxAV: 1000, damage: 1000, faceUp: true);
            long budgetBefore = state.Player1Budget;

            var result = UseIgnitionProcessor.Process(state, TestFactory.MakeGame(), 1, Use("src"), cc, effects);

            FieldHelpers.FindResourceByID(state.Player1Field, "dead").Should().BeNull();
            FieldHelpers.FindResourceByID(state.Player1Field, "src").Should().NotBeNull();
            state.Player1Trash.Should().Contain(c => c.InstanceID == "dead");
            state.Player1Budget.Should().Be(budgetBefore - 400);
            result.Events.Should().Contain(e => e.EventType == "on_destroy_triggered");
        }
    }

    [Trait("対象", "reveal_reactive の起動効果")]
    public class RevealReactive
    {
        [Fact(DisplayName = "reveal_reactive を起動効果で発動すると相手の伏せたリアクティブが表向きに開示される")]
        public void Ignition_RevealsHiddenReactive()
        {
            var (cc, effects) = Env(TestFactory.ComputeCard(cardId: "TST-0001"), new RevealReactiveOp());
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "src", faceUp: true);
            state.Player2Field.Support[0] = new DeployedSupport { InstanceID = "sup_1", CardID = "TST-0400", FaceUp = false };

            UseIgnitionProcessor.Process(state, TestFactory.MakeGame(), 1, Use("src"), cc, effects);

            state.Player2Field.Support[0]!.FaceUp.Should().BeTrue();
        }

        /// <summary>発動元と、相手の伏せリアクティブを指定枚数だけ並べた状態を作る。</summary>
        /// <param name="faceDownCount">相手のサポートゾーンに伏せる枚数。</param>
        /// <returns>ゲーム状態。</returns>
        private static BattleGameState StateWithFaceDownReactives(int faceDownCount)
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(
                cardId: "TST-0001", instanceId: "src", faceUp: true);
            for (int i = 0; i < faceDownCount; i++)
            {
                state.Player2Field.Support[i] = new DeployedSupport
                {
                    InstanceID = $"sup_{i + 1}",
                    CardID = "TST-0400",
                    FaceUp = false,
                };
            }
            return state;
        }

        [Fact(DisplayName = "相手の伏せリアクティブが 2 枚あるとき、どちらを確認するかの選択待ちへ遷移し、まだ開示されない")]
        public void Ignition_MultipleFaceDown_SuspendsForChoice()
        {
            var (cc, effects) = Env(TestFactory.ComputeCard(cardId: "TST-0001"), new RevealReactiveOp());
            var state = StateWithFaceDownReactives(2);

            UseIgnitionProcessor.Process(state, TestFactory.MakeGame(), 1, Use("src"), cc, effects);

            state.PendingEffectChoice.Should().NotBeNull();
            state.PendingEffectChoice!.ChoiceKind.Should().Be(ChoiceKinds.FaceDownReactive);
            state.PendingEffectChoice.Candidates.Should().Equal("sup_1", "sup_2");
            state.Player2Field.Support.Where(s => s is not null).Should().OnlyContain(s => !s.FaceUp);
        }

        [Fact(DisplayName = "伏せリアクティブが 2 枚あるとき、選んだ 2 枚目が開示される")]
        public void Ignition_MultipleFaceDown_RevealsChosenCard()
        {
            var (cc, effects) = Env(TestFactory.ComputeCard(cardId: "TST-0001"), new RevealReactiveOp());
            var state = StateWithFaceDownReactives(2);

            var game = TestFactory.MakeGame();
            UseIgnitionProcessor.Process(state, game, 1, Use("src"), cc, effects);
            ResolvePendingChoiceProcessor.Process(
                state, game, 1, new ResolvePendingChoiceRequest { ChosenId = "sup_2" },
                cc, effects, new FakeClock());

            state.Player2Field.Support[1]!.FaceUp.Should().BeTrue();
            state.Player2Field.Support[0]!.FaceUp.Should().BeFalse();
            state.PendingEffectChoice.Should().BeNull();
        }

        [Fact(DisplayName = "相手に伏せリアクティブが無いとき、選択待ちにならず起動効果は使用済みになって終わる")]
        public void Ignition_NoFaceDown_CompletesWithoutSuspending()
        {
            var (cc, effects) = Env(TestFactory.ComputeCard(cardId: "TST-0001"), new RevealReactiveOp());
            var state = StateWithFaceDownReactives(0);

            var result = UseIgnitionProcessor.Process(state, TestFactory.MakeGame(), 1, Use("src"), cc, effects);

            state.PendingEffectChoice.Should().BeNull();
            result.Events.Should().Contain(e => e.EventType == ActionTypes.UseIgnition);
            state.Player1Field.Frontend[0]!.EffectUsedThisTurn.Should().BeTrue();
        }
    }

    [Trait("対象", "destroy_platform の起動効果")]
    public class DestroyPlatform
    {
        [Fact(DisplayName = "destroy_platform を起動効果で発動すると相手のプラットフォームが破壊される")]
        public void Ignition_DestroysOpponentPlatform()
        {
            var (cc, effects) = Env(TestFactory.ComputeCard(cardId: "TST-0001"), new DestroyPlatformOp());
            cc.Add(TestFactory.PlatformCard(cardId: "TST-0200"));
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "src", faceUp: true);
            state.Player2Field.Support[0] = new DeployedSupport { InstanceID = "plat_1", CardID = "TST-0200", FaceUp = true };

            UseIgnitionProcessor.Process(state, TestFactory.MakeGame(), 1, Use("src"), cc, effects);

            state.Player2Field.Support.Select(s => s.InstanceID).Should().NotContain("plat_1");
        }

        [Fact(DisplayName = "destroy_platform でプラットフォームが破壊されたとき、盤面変化を条件とする常時効果が再計算される")]
        public void Ignition_DestroyingPlatform_FiresOnFieldChange()
        {
            var (cc, effects) = Env(TestFactory.ComputeCard(cardId: "TST-0001"), new DestroyPlatformOp());
            cc.Add(TestFactory.PlatformCard(cardId: "TST-0200"));
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0005", name: "Watcher"));
            effects.RegisterPassive("TST-0005", new PassiveEffectDef
            {
                Guards = [new ResourceCountGuard("opponent", Zones.Support, null, null, null, ["TST-0200"], 1, null)],
                Applications =
                [
                    new PassiveBuffApplication
                    {
                        Selector = SourceSelector.Instance,
                        EffectType = EffectTypes.BuffTP,
                        Amount = new StaticAmount(200),
                        Mode = "",
                    },
                ],
            });

            var game = TestFactory.MakeGame();
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "src", faceUp: true);
            var watcher = TestFactory.MakeResource(cardId: "TST-0005", instanceId: "watcher", faceUp: true);
            state.Player1Field.Frontend[1] = watcher;
            state.Player2Field.Support[0] = new DeployedSupport { InstanceID = "plat_1", CardID = "TST-0200", FaceUp = true };
            PassiveRecalculator.Recalculate(state, game, cc, effects);
            StatCalculator.CalculateEffectiveTP(watcher, state.Player1Field, cc).Should().Be(
                800, "相手プラットフォームが場にいる間はパッシブ効果で+200される");

            UseIgnitionProcessor.Process(state, game, 1, Use("src"), cc, effects);

            StatCalculator.CalculateEffectiveTP(watcher, state.Player1Field, cc).Should().Be(
                600, "プラットフォーム破壊で発動条件を失い基礎値に戻る");
        }
    }

    [Trait("対象", "scale_to_zero の起動効果")]
    public class ScaleToZero
    {
        /// <summary>エンドフェーズまで進めて維持コストを徴収させる。</summary>
        /// <param name="state">対象のゲーム状態。</param>
        /// <param name="cc">カード定義キャッシュ。</param>
        /// <param name="effects">効果ハンドラのレジストリ。</param>
        private static void CollectMaintenanceCost(BattleGameState state, TestCardCache cc, EffectRegistry effects)
        {
            state.CurrentPhase = Phase.Battle;
            state.Player2Repository.Add(new UndeployedCard { InstanceID = "repo_1", CardID = "TST-0001" });
            EndPhaseProcessor.Process(state, TestFactory.MakeGame(), 1, cc, effects, new FakeClock());
        }

        [Fact(DisplayName = "エラスティックリソースを置いたままターンを終えると、維持コスト 39 がバジェットから引かれる")]
        public void WithoutIgnition_CollectsMaintenanceCost()
        {
            var (cc, effects) = Env(TestFactory.ElasticContainerCard(cardId: "TST-0003"), Custom(CustomEffects.ScaleToZero));
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
            var state = TestFactory.MakeGameState(turn: 5, phase: Phase.Main, p1Budget: 5000);
            var src = TestFactory.MakeResource(cardId: "TST-0003", instanceId: "src", faceUp: true, elasticBonus: 600);
            src.DeployedOnTurn = 1;
            src.LastAttackTurn = 0;
            state.Player1Field.Frontend[0] = src;

            CollectMaintenanceCost(state, cc, effects);

            state.GetBudget(1).Should().Be(4961);
        }

        [Fact(DisplayName = "エラスティックリソースに scale_to_zero を使うと、そのターンの維持コストが引かれない")]
        public void Ignition_ZeroesMaintenanceCost()
        {
            var (cc, effects) = Env(TestFactory.ElasticContainerCard(cardId: "TST-0003"), Custom(CustomEffects.ScaleToZero));
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
            var state = TestFactory.MakeGameState(turn: 5, phase: Phase.Main, p1Budget: 5000);
            var src = TestFactory.MakeResource(cardId: "TST-0003", instanceId: "src", faceUp: true, elasticBonus: 600);
            src.DeployedOnTurn = 1;
            src.LastAttackTurn = 0;
            state.Player1Field.Frontend[0] = src;

            UseIgnitionProcessor.Process(state, TestFactory.MakeGame(), 1, Use("src"), cc, effects);
            CollectMaintenanceCost(state, cc, effects);

            state.GetBudget(1).Should().Be(5000);
        }

        [Fact(DisplayName = "ランクとインスタンスファミリーで倍率のかかるリソースに scale_to_zero を使っても、そのターンの維持コストが引かれない")]
        public void Ignition_ZeroesMaintenanceCost_WithRankAndFamilyMultipliers()
        {
            var scalable = TestFactory.ComputeCard(
                cardId: "TST-0003", tp: 600, mc: 0, deployTurns: 0,
                resizable: true, elastic: true, elasticIncrement: 100, freeTier: 600, costPerRequest: 10);
            var (cc, effects) = Env(scalable, Custom(CustomEffects.ScaleToZero));
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
            var state = TestFactory.MakeGameState(turn: 5, phase: Phase.Main, p1Budget: 5000);
            var src = TestFactory.MakeResource(
                cardId: "TST-0003", instanceId: "src", rank: Rank.Medium, family: InstanceFamily.C, faceUp: true);
            src.DeployedOnTurn = 1;
            src.LastAttackTurn = 0;
            state.Player1Field.Frontend[0] = src;

            UseIgnitionProcessor.Process(state, TestFactory.MakeGame(), 1, Use("src"), cc, effects);
            CollectMaintenanceCost(state, cc, effects);

            state.GetBudget(1).Should().Be(5000);
        }

        [Fact(DisplayName = "維持コストが 0 のリソースに scale_to_zero を使うと、維持コスト軽減は付与されない")]
        public void Ignition_NoMaintenanceCost_AddsNoReduction()
        {
            var (cc, effects) = Env(TestFactory.ServerlessCard(cardId: "TST-0003"), Custom(CustomEffects.ScaleToZero));
            var state = TestFactory.MakeGameState(turn: 5, phase: Phase.Main);
            var src = TestFactory.MakeResource(cardId: "TST-0003", instanceId: "src", faceUp: true);
            src.DeployedOnTurn = 1;
            src.LastAttackTurn = 0;
            state.Player1Field.Frontend[0] = src;

            UseIgnitionProcessor.Process(state, TestFactory.MakeGame(), 1, Use("src"), cc, effects);

            src.TemporaryEffects.Should().BeEmpty();
        }
    }

    [Trait("対象", "reattach の起動効果")]
    public class Reattach
    {
        [Fact(DisplayName = "reattach を起動効果で発動するとアタッチメントが選んだ対象へ付け替わる")]
        public void Ignition_MovesAttachmentToChosenTarget()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
            cc.Add(TestFactory.AttachmentCard(cardId: "TST-0301"));
            var effects = new EffectRegistry();
            effects.RegisterComposed("TST-0301", TriggerType.Ignition, Custom(CustomEffects.Reattach));

            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "old_host", faceUp: true);
            state.Player1Field.Frontend[1] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "new_host", faceUp: true);
            var att = new DeployedSupport { InstanceID = "att", CardID = "TST-0301", TargetInstanceID = "old_host", FaceUp = true };
            state.Player1Field.Support[0] = att;

            UseIgnitionProcessor.Process(state, TestFactory.MakeGame(), 1,
                Use("att", choiceData: new Dictionary<string, object> { ["instanceId"] = "new_host" }), cc, effects);

            att.TargetInstanceID.Should().Be("new_host");
        }
    }

    [Trait("対象", "cloud_shift の起動効果")]
    public class CloudShift
    {
        [Fact(DisplayName = "cloud_shift を起動効果で発動すると手札からデプロイ要求が出て発動元が自壊する")]
        public void Ignition_DeploysFromHandAndSelfDestructs()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
            cc.Add(TestFactory.PlatformCard(cardId: "TST-0201"));
            var meta = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(
                """{"faction":"SHE","deploy_discount":300}""");
            var effects = new EffectRegistry();
            effects.RegisterComposed("TST-0201", TriggerType.Ignition,
                new CustomFnOp(new CustomEffectRegistry().Build(CustomEffects.CloudShift, meta)!));

            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main, p1Budget: 5000);
            state.Player1Hand = [new UndeployedCard { InstanceID = "h_1", CardID = "TST-0001" }];
            state.Player1Field.Support[0] = new DeployedSupport { InstanceID = "sup_1", CardID = "TST-0201", FaceUp = true };

            UseIgnitionProcessor.Process(state, TestFactory.MakeGame(), 1,
                Use("sup_1", choiceData: new Dictionary<string, object> { ["cardId"] = "TST-0001" }), cc, effects);

            state.GetBudget(1).Should().Be(5300);
            state.PendingSlotSelects.Should().ContainSingle();
            state.Player1Field.Support.Select(s => s.InstanceID).Should().NotContain("sup_1");
        }

        [Fact(DisplayName = "cloud_shift をリソースの起動効果で発動すると手札からデプロイ要求が出て発動元リソースがトラッシュへ送られる")]
        public void Ignition_FromResource_DeploysFromHandAndSelfTrashes()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0004", name: "ShiftableCompute"));
            var meta = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(
                """{"faction":"SHE","deploy_discount":300}""");
            var effects = new EffectRegistry();
            effects.RegisterComposed("TST-0004", TriggerType.Ignition,
                new CustomFnOp(new CustomEffectRegistry().Build(CustomEffects.CloudShift, meta)!));

            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main, p1Budget: 5000);
            state.Player1Hand = [new UndeployedCard { InstanceID = "h_1", CardID = "TST-0001" }];
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(
                cardId: "TST-0004", instanceId: "src", faceUp: true);

            UseIgnitionProcessor.Process(state, TestFactory.MakeGame(), 1,
                Use("src", choiceData: new Dictionary<string, object> { ["cardId"] = "TST-0001" }), cc, effects);

            state.GetBudget(1).Should().Be(5300);
            state.PendingSlotSelects.Should().ContainSingle();
            state.Player1Field.Frontend[0].Should().BeNull();
            state.Player1Trash.Should().ContainSingle(c => c.InstanceID == "src");
        }

        [Fact(DisplayName = "cloud_shift でトラッシュへ送られた発動元リソースには SLA ペナルティがかからない")]
        public void Ignition_FromResource_ChargesNoSlaPenalty()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0004", slaPenalty: 400, name: "ShiftableCompute"));
            var meta = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(
                """{"faction":"SHE"}""");
            var effects = new EffectRegistry();
            effects.RegisterComposed("TST-0004", TriggerType.Ignition,
                new CustomFnOp(new CustomEffectRegistry().Build(CustomEffects.CloudShift, meta)!));

            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main, p1Budget: 5000);
            state.Player1Hand = [new UndeployedCard { InstanceID = "h_1", CardID = "TST-0001" }];
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(
                cardId: "TST-0004", instanceId: "src", faceUp: true);

            UseIgnitionProcessor.Process(state, TestFactory.MakeGame(), 1,
                Use("src", choiceData: new Dictionary<string, object> { ["cardId"] = "TST-0001" }), cc, effects);

            state.GetBudget(1).Should().Be(5000);
        }

        [Fact(DisplayName = "移設先の空きスロットがないとき、移設は不発になり発動元リソース・手札・バジェットのいずれも変わらない")]
        public void Ignition_FromResource_NoEmptySlot_LeavesEverythingUnchanged()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0004", name: "ShiftableCompute"));
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0005", name: "Occupier"));
            var meta = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(
                """{"faction":"SHE","deploy_discount":300}""");
            var effects = new EffectRegistry();
            effects.RegisterComposed("TST-0004", TriggerType.Ignition,
                new CustomFnOp(new CustomEffectRegistry().Build(CustomEffects.CloudShift, meta)!));

            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main, p1Budget: 5000);
            state.Player1Hand = [new UndeployedCard { InstanceID = "h_1", CardID = "TST-0001" }];
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(
                cardId: "TST-0004", instanceId: "src", faceUp: true);
            for (int i = 1; i < BattleConstants.SlotsPerZone; i++)
            {
                state.Player1Field.Frontend[i] = TestFactory.MakeResource(
                    cardId: "TST-0005", instanceId: $"fe_{i}", faceUp: true);
            }
            for (int i = 0; i < BattleConstants.SlotsPerZone; i++)
            {
                state.Player1Field.Backend[i] = TestFactory.MakeResource(
                    cardId: "TST-0005", instanceId: $"be_{i}", faceUp: true);
            }

            UseIgnitionProcessor.Process(state, TestFactory.MakeGame(), 1,
                Use("src", choiceData: new Dictionary<string, object> { ["cardId"] = "TST-0001" }), cc, effects);

            state.PendingSlotSelects.Should().BeEmpty();
            state.Player1Field.Frontend[0]!.InstanceID.Should().Be("src");
            state.Player1Hand.Should().ContainSingle(c => c.CardID == "TST-0001");
            state.Player1Trash.Should().BeEmpty();
            state.GetBudget(1).Should().Be(5000);
        }
    }

    [Trait("対象", "peek_reactive の起動効果")]
    public class PeekReactive
    {
        [Fact(DisplayName = "peek_reactive を起動効果で発動すると相手の伏せたリアクティブを表向きにせず覗き見る")]
        public void Ignition_PeeksHiddenReactiveWithoutFlipping()
        {
            var (cc, effects) = Env(TestFactory.ComputeCard(cardId: "TST-0001"), new PeekReactiveOp());
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "src", faceUp: true);
            state.Player2Field.Support[0] = new DeployedSupport { InstanceID = "sup_1", CardID = "TST-0400", FaceUp = false };

            UseIgnitionProcessor.Process(state, TestFactory.MakeGame(), 1, Use("src"), cc, effects);

            var sup = state.Player2Field.Support[0]!;
            sup.FaceUp.Should().BeFalse("覗き見はカードを表向きにしない");
            sup.PeekedBy.Should().Contain(1);
        }

        [Fact(DisplayName = "伏せリアクティブが 2 枚あるとき、選んだ 2 枚目だけを覗き見る")]
        public void Ignition_MultipleFaceDown_PeeksChosenCardOnly()
        {
            var (cc, effects) = Env(TestFactory.ComputeCard(cardId: "TST-0001"), new PeekReactiveOp());
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(
                cardId: "TST-0001", instanceId: "src", faceUp: true);
            state.Player2Field.Support[0] = new DeployedSupport { InstanceID = "sup_1", CardID = "TST-0400", FaceUp = false };
            state.Player2Field.Support[1] = new DeployedSupport { InstanceID = "sup_2", CardID = "TST-0400", FaceUp = false };

            var game = TestFactory.MakeGame();
            UseIgnitionProcessor.Process(state, game, 1, Use("src"), cc, effects);
            ResolvePendingChoiceProcessor.Process(
                state, game, 1, new ResolvePendingChoiceRequest { ChosenId = "sup_2" },
                cc, effects, new FakeClock());

            state.Player2Field.Support[1]!.PeekedBy.Should().Contain(1);
            state.Player2Field.Support[0]!.PeekedBy.Should().BeEmpty();
        }
    }

    [Trait("対象", "効果量のステータス参照")]
    public class AmountRefResolution
    {
        /// <summary>指定カードに yaml (JSON) の ops 定義を起動効果として読み込んだ環境を作る。</summary>
        /// <param name="cardId">起動効果を持たせるカード ID。</param>
        /// <param name="opsJson">単一 op の JSON 定義。</param>
        /// <returns>カードキャッシュと効果レジストリ。</returns>
        private static (TestCardCache Cc, EffectRegistry Effects) LoadIgnitionFromJson(string cardId, string opsJson)
        {
            var card = TestFactory.ComputeCard(cardId: cardId, tp: 600);
            card.Effects =
            [
                new EffectDef
                {
                    Trigger = "ignition",
                    Ops = [System.Text.Json.JsonDocument.Parse(opsJson).RootElement],
                },
            ];
            var cc = new TestCardCache();
            cc.Add(card);
            var registry = new EffectRegistry();
            EffectYamlLoader.LoadEffectSources([card], registry, new CustomEffectRegistry());
            return (cc, registry);
        }

        [Fact(DisplayName = "効果量に発動源のスループット参照を指定すると、その実効値分の効果量になる")]
        public void SourceTpRef_UsesEffectiveThroughput()
        {
            var (cc, effects) = LoadIgnitionFromJson("TST-0900",
                """{"lose_budget":{"target":"opponent","amount":{"ref":"source.tp"}}}""");
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(
                cardId: "TST-0900", instanceId: "src", faceUp: true, maxTP: 600, currentTP: 600);

            UseIgnitionProcessor.Process(state, TestFactory.MakeGame(), 1, Use("src"), cc, effects);

            state.Player2Budget.Should().Be(5000 - 600);
        }

        [Fact(DisplayName = "効果量に対象の最大可用性の半分を指定すると、半分の値になる")]
        public void TargetMaxAvRef_HalfMultiplier_UsesHalfValue()
        {
            var (cc, effects) = LoadIgnitionFromJson("TST-0901",
                """{"deal_damage":{"selector":"target","amount":{"ref":"target.max_av","multiply":0.5}}}""");
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0901", instanceId: "src", faceUp: true);
            var target = TestFactory.MakeResource(cardId: "TST-0901", instanceId: "tgt", faceUp: true, maxAV: 1400, damage: 0);
            state.Player2Field.Frontend[0] = target;

            UseIgnitionProcessor.Process(state, TestFactory.MakeGame(), 1, Use("src", "tgt"), cc, effects);

            target.Damage.Should().Be(700);
        }
    }

    [Trait("対象", "両者対象と発動源除外のセレクタ")]
    public class BothOwnerAndExcludeSourceSelectors
    {
        /// <summary>指定カードに yaml (JSON) の ops 定義を起動効果として読み込んだ環境を作る。</summary>
        /// <param name="cardId">起動効果を持たせるカード ID。</param>
        /// <param name="opsJson">単一 op の JSON 定義。</param>
        /// <returns>カードキャッシュと効果レジストリ。</returns>
        private static (TestCardCache Cc, EffectRegistry Effects) LoadIgnitionFromJson(string cardId, string opsJson)
        {
            var card = TestFactory.ComputeCard(cardId: cardId);
            card.Effects =
            [
                new EffectDef
                {
                    Trigger = "ignition",
                    Ops = [System.Text.Json.JsonDocument.Parse(opsJson).RootElement],
                },
            ];
            var cc = new TestCardCache();
            cc.Add(card);
            var registry = new EffectRegistry();
            EffectYamlLoader.LoadEffectSources([card], registry, new CustomEffectRegistry());
            return (cc, registry);
        }

        [Fact(DisplayName = "対象が両者の全体効果は、自分と相手のリソース両方に適用される")]
        public void OwnerBoth_AppliesToBothSides()
        {
            var (cc, effects) = LoadIgnitionFromJson("TST-0902",
                """{"deal_damage":{"selector":{"owner":"both"},"amount":200}}""");
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0902", instanceId: "src", faceUp: true);
            var mine = TestFactory.MakeResource(cardId: "TST-0902", instanceId: "mine", faceUp: true, maxAV: 1000);
            state.Player1Field.Frontend[1] = mine;
            var theirs = TestFactory.MakeResource(cardId: "TST-0902", instanceId: "theirs", faceUp: true, maxAV: 1000);
            state.Player2Field.Frontend[0] = theirs;

            UseIgnitionProcessor.Process(state, TestFactory.MakeGame(), 1, Use("src"), cc, effects);

            mine.Damage.Should().Be(200);
            theirs.Damage.Should().Be(200);
        }

        [Fact(DisplayName = "発動源除外の全体効果は、発動源自身に適用されない")]
        public void ExcludeSource_DoesNotApplyToSourceItself()
        {
            var (cc, effects) = LoadIgnitionFromJson("TST-0903",
                """{"apply_buff":{"selector":{"owner":"myself","exclude":"source"},"buff":"tp","amount":200,"duration":"this_turn"}}""");
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
            var source = TestFactory.MakeResource(cardId: "TST-0903", instanceId: "src", faceUp: true);
            state.Player1Field.Frontend[0] = source;
            var ally = TestFactory.MakeResource(cardId: "TST-0903", instanceId: "ally", faceUp: true);
            state.Player1Field.Frontend[1] = ally;

            UseIgnitionProcessor.Process(state, TestFactory.MakeGame(), 1, Use("src"), cc, effects);

            ally.TemporaryEffects.Should().ContainSingle(e => e.EffectType == EffectTypes.BuffTP);
            source.TemporaryEffects.Should().BeEmpty("発動源除外セレクタは発動源自身を対象から除く");
        }
    }

    [Trait("対象", "発動条件を満たさない起動効果")]
    public class UnsatisfiedGuard
    {
        private const string SourceCard = "TST-0800";

        /// <summary>自分のリソース数が 2 体以上という発動条件付きで gain_budget を起動効果登録した環境を作る。</summary>
        /// <returns>カードキャッシュと効果レジストリ。</returns>
        private static (TestCardCache Cc, EffectRegistry Effects) Env()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: SourceCard));
            var effects = new EffectRegistry();
            effects.RegisterComposed(SourceCard, TriggerType.Ignition,
                [new ResourceCountGuard("myself", null, null, null, null, null, min: 2, max: null)],
                new GainBudgetOp(PlayerRef.Myself, new StaticAmount(500)));
            return (cc, effects);
        }

        [Fact(DisplayName = "発動条件を満たさない起動効果は、利用可能アクションに提示されない")]
        public void NotShownInAvailableActions()
        {
            var (cc, effects) = Env();
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
            var field = TestFactory.MakeField();
            field.Frontend[0] = TestFactory.MakeResource(cardId: SourceCard, instanceId: "src", faceUp: true);

            var actions = AvailableActions.GetAllAvailableActions(
                state, 1, field, TestFactory.MakeField(), [], 5000, 0, cc, effects);

            actions.Should().NotContain(a => a.Type == ActionTypes.UseIgnition);
        }
    }
}
