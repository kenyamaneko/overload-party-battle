using System.Text.Json;
using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Effects.Ops;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Engine;

/// <summary>カスタム効果を直接叩かず、プレイヤーのアクション (デプロイ・攻撃・フェーズ終了) を起点に検証する。</summary>
public class TriggeredEffectTests
{
    /// <summary>指定カスタム効果を 1 つの op として包む。</summary>
    /// <param name="name">カスタム効果名。</param>
    /// <returns>カスタム効果を実行する op。</returns>
    private static IEffectOp Custom(string name) =>
        new CustomFnOp(new CustomEffectRegistry().Build(name, null)!);

    [Trait("対象", "高スループットデプロイの休止付与")]
    public class DisableHighTpDeploy
    {
        [Fact(DisplayName = "スループット 1500 のコンピュート系リソースがデプロイされると、休止が付与される")]
        public void OnDeploy_AppliesDormantToHighTpDeploy()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0005", tp: 1500, deployTurns: 0));
            cc.Add(TestFactory.ReactiveCard(cardId: "TST-0400"));
            var effects = new EffectRegistry();
            effects.RegisterComposed("TST-0400", TriggerType.OnDeploy, Custom(CustomEffects.DisableHighTpDeploy));

            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main, activePlayer: 1);
            state.Player1Hand.Add(new UndeployedCard { InstanceID = "h_big", CardID = "TST-0005" });
            // 相手 (P2) のサポートに誘発効果を仕込む。P1 のデプロイで発動する。
            state.Player2Field.Support[0] = new DeployedSupport { InstanceID = "watcher", CardID = "TST-0400", FaceUp = false };

            PlayCardProcessor.Process(state, TestFactory.MakeGame(), 1,
                new PlayCardRequest { CardInstanceID = "h_big", Zone = Zones.Frontend, Index = 0 }, cc, effects);

            var deployed = state.Player1Field.Frontend[0]!;
            FieldHelpers.HasTemporaryEffect(deployed, BuffTypes.Dormant).Should().BeTrue();
        }

        [Fact(DisplayName = "スループット 600 のリソースがデプロイされても、休止は付与されない")]
        public void OnDeploy_NoDormant_BelowThreshold()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", tp: 600, deployTurns: 0));
            cc.Add(TestFactory.ReactiveCard(cardId: "TST-0400"));
            var effects = new EffectRegistry();
            effects.RegisterComposed("TST-0400", TriggerType.OnDeploy, Custom(CustomEffects.DisableHighTpDeploy));

            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main, activePlayer: 1);
            state.Player1Hand.Add(new UndeployedCard { InstanceID = "h_small", CardID = "TST-0001" });
            state.Player2Field.Support[0] = new DeployedSupport { InstanceID = "watcher", CardID = "TST-0400", FaceUp = false };

            PlayCardProcessor.Process(state, TestFactory.MakeGame(), 1,
                new PlayCardRequest { CardInstanceID = "h_small", Zone = Zones.Frontend, Index = 0 }, cc, effects);

            FieldHelpers.HasTemporaryEffect(state.Player1Field.Frontend[0]!, BuffTypes.Dormant).Should().BeFalse();
        }
    }

    [Trait("対象", "相手の 3 体目のデプロイキャンセル")]
    public class CancelNthDeploy
    {
        private const long Turn = 4;

        /// <summary>相手が当ターン既に指定体数をデプロイし、こちらがリアクティブを伏せた状態を作る。</summary>
        /// <param name="alreadyDeployed">相手が当ターン既にデプロイした体数。</param>
        /// <returns>カードキャッシュ・効果レジストリ・ゲーム状態。</returns>
        private static (TestCardCache Cc, EffectRegistry Effects, BattleGameState State) Setup(int alreadyDeployed)
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", deployTurns: 0));
            cc.Add(TestFactory.ReactiveCard(cardId: "TST-0400"));
            var effects = new EffectRegistry();
            effects.RegisterComposed("TST-0400", TriggerType.OnDeploy, Custom(CustomEffects.CancelNthDeploy));

            var state = TestFactory.MakeGameState(turn: Turn, phase: Phase.Main, activePlayer: 1);
            for (int i = 0; i < alreadyDeployed; i++)
            {
                state.Player1Field.Frontend[i] = DeployedThisTurn($"d_{i}", Turn);
            }
            state.Player1Hand.Add(new UndeployedCard { InstanceID = "h_next", CardID = "TST-0001" });
            state.Player2Field.Support[0] = new DeployedSupport { InstanceID = "watcher", CardID = "TST-0400", FaceUp = false };

            return (cc, effects, state);
        }

        /// <summary>手札のカードを指定のフロントエンドスロットへデプロイする。</summary>
        /// <param name="state">対象のゲーム状態。</param>
        /// <param name="cc">カード定義キャッシュ。</param>
        /// <param name="effects">効果ハンドラのレジストリ。</param>
        /// <param name="index">配置先のスロット番号。</param>
        private static void Deploy(BattleGameState state, TestCardCache cc, EffectRegistry effects, int index) =>
            PlayCardProcessor.Process(state, TestFactory.MakeGame(), 1,
                new PlayCardRequest { CardInstanceID = "h_next", Zone = Zones.Frontend, Index = index }, cc, effects);

        [Fact(DisplayName = "同一ターンの 2 体目のデプロイは成立し、フロントエンドに残る")]
        public void OnDeploy_SecondDeploy_Succeeds()
        {
            var (cc, effects, state) = Setup(alreadyDeployed: 1);

            Deploy(state, cc, effects, index: 1);

            state.Player1Field.Frontend[1].Should().NotBeNull();
        }

        [Fact(DisplayName = "同一ターンの 2 体目のデプロイでは、リアクティブは裏向きでサポートゾーンに残る")]
        public void OnDeploy_SecondDeploy_DoesNotConsumeReactive()
        {
            var (cc, effects, state) = Setup(alreadyDeployed: 1);

            Deploy(state, cc, effects, index: 1);

            var watcher = state.Player2Field.Support.FirstOrDefault(s => s.InstanceID == "watcher");
            watcher.Should().NotBeNull();
            watcher!.FaceUp.Should().BeFalse();
            state.Player2Trash.Should().BeEmpty();
        }

        [Fact(DisplayName = "同一ターンの 3 体目のデプロイは無効化され、そのリソースはフロントエンドに残らずトラッシュへ送られる")]
        public void OnDeploy_ThirdDeploy_IsCancelledAndTrashed()
        {
            var (cc, effects, state) = Setup(alreadyDeployed: 2);

            Deploy(state, cc, effects, index: 2);

            state.Player1Field.Frontend[2].Should().BeNull();
            state.Player1Trash.Should().ContainSingle(c => c.CardID == "TST-0001");
        }

        [Fact(DisplayName = "3 体目のデプロイを無効化したリアクティブは、表向きでトラッシュへ送られる")]
        public void OnDeploy_ThirdDeploy_ConsumesReactive()
        {
            var (cc, effects, state) = Setup(alreadyDeployed: 2);

            Deploy(state, cc, effects, index: 2);

            state.Player2Field.Support.Should().NotContain(s => s.InstanceID == "watcher");
            state.Player2Trash.Should().ContainSingle(c => c.InstanceID == "watcher");
        }

        private static DeployedResource DeployedThisTurn(string id, long turn)
        {
            var r = TestFactory.MakeResource(cardId: "TST-0001", instanceId: id, faceUp: true);
            r.DeployedOnTurn = turn;
            return r;
        }
    }

    [Trait("対象", "破壊時の同タイプデプロイ選択")]
    public class DeploySameTypeFromHand
    {
        [Fact(DisplayName = "リソースが破壊されると、同タイプのカードを手札から選ぶ選択待ちへ遷移する")]
        public void OnDestroy_SuspendsForHandCardChoice()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", subtype: "VM"));
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0005", tp: 1500, av: 1400, name: "StrongCompute"));
            cc.Add(TestFactory.ReactiveCard(cardId: "TST-0400"));
            var effects = new EffectRegistry();
            effects.RegisterComposed("TST-0400", TriggerType.OnDestroy, Custom(CustomEffects.DeploySameTypeFromHand));

            // P2 が P1 のリソースを破壊する。P1 の on_destroy リアクティブが発動する。
            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Battle, activePlayer: 2);
            state.Player2Field.Frontend[0] = TestFactory.MakeResource(
                cardId: "TST-0005", instanceId: "atk", faceUp: true, maxTP: 1500, currentTP: 1500);
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "victim", faceUp: true);
            state.Player1Field.Support[0] = new DeployedSupport { InstanceID = "watcher", CardID = "TST-0400", FaceUp = false };
            state.Player1Hand.Add(new UndeployedCard { InstanceID = "h_same", CardID = "TST-0001" });

            AttackProcessor.Process(state, TestFactory.MakeGame(), 2,
                new AttackRequest { AttackerInstanceID = "atk", TargetInstanceID = "victim" }, cc, effects);

            state.PendingEffectChoice.Should().NotBeNull("同タイプのカードを選ぶ選択待ちへ遷移する");
            state.PendingEffectChoice!.ChoiceKind.Should().Be(ChoiceKinds.HandCard);
            state.PendingEffectChoice.Candidates.Should().Contain("TST-0001");
        }
    }

    [Trait("対象", "エンドフェーズでの期限切れ自壊")]
    public class SpotExpiry
    {
        [Fact(DisplayName = "デプロイから規定ターン経過後のエンドフェーズで、リソースが自壊する")]
        public void OnEndPhase_SelfDestructsAfterExpiry()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
            var meta = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>("""{"turns":2}""");
            var effects = new EffectRegistry();
            effects.RegisterComposed("TST-0001", TriggerType.OnEndPhase,
                new CustomFnOp(new CustomEffectRegistry().Build(CustomEffects.SpotExpiry, meta)!));

            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Battle, activePlayer: 1);
            state.GetRepository(2).Add(new UndeployedCard { InstanceID = "repo_1", CardID = "TST-0001" });
            var res = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "spot", faceUp: true);
            res.DeployedOnTurn = 1; // 経過 2 ターン == turns
            state.Player1Field.Frontend[0] = res;

            EndPhaseProcessor.Process(state, TestFactory.MakeGame(), 1, cc, effects, new FakeClock());

            FieldHelpers.FindResourceByID(state.Player1Field, "spot").Should().BeNull("自壊する");
        }

        [Fact(DisplayName = "デプロイから規定ターン経過後のエンドフェーズでの自壊は、SLA ペナルティ減算と on_destroy 発火を伴う")]
        public void OnEndPhase_ExpiryAppliesSlaPenaltyAndFiresOnDestroy()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", slaPenalty: 400));
            var meta = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>("""{"turns":2}""");
            var effects = new EffectRegistry();
            effects.RegisterComposed("TST-0001", TriggerType.OnEndPhase,
                new CustomFnOp(new CustomEffectRegistry().Build(CustomEffects.SpotExpiry, meta)!));
            bool onDestroyFired = false;
            effects.Register("TST-0001", TriggerType.OnDestroy, _ => { onDestroyFired = true; return new EffectResult(); });

            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Battle, activePlayer: 1);
            var res = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "spot", faceUp: true);
            res.DeployedOnTurn = 1; // 経過 2 ターン == turns
            state.Player1Field.Frontend[0] = res;
            long budgetBefore = state.Player1Budget;

            EndPhaseProcessor.Process(state, TestFactory.MakeGame(), 1, cc, effects, new FakeClock());

            state.Player1Budget.Should().Be(budgetBefore - 400, "SLA ペナルティが減算される");
            onDestroyFired.Should().BeTrue("自壊した本体の on_destroy が発火する");
        }

        [Fact(DisplayName = "デプロイから規定ターン経過後のエンドフェーズでの自壊で、盤面変化を条件とする常時効果が再計算される")]
        public void OnEndPhase_ExpiryFiresOnFieldChange()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0005", name: "Watcher"));
            var meta = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>("""{"turns":2}""");
            var effects = new EffectRegistry();
            effects.RegisterComposed("TST-0001", TriggerType.OnEndPhase,
                new CustomFnOp(new CustomEffectRegistry().Build(CustomEffects.SpotExpiry, meta)!));
            effects.RegisterPassive("TST-0005", new PassiveEffectDef
            {
                Guards = [new ResourceCountGuard("myself", null, null, null, null, ["TST-0001"], 1, null)],
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
            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Battle, activePlayer: 1);
            var res = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "spot", faceUp: true);
            res.DeployedOnTurn = 1; // 経過 2 ターン == turns
            state.Player1Field.Frontend[0] = res;
            var watcher = TestFactory.MakeResource(cardId: "TST-0005", instanceId: "watcher", faceUp: true);
            state.Player1Field.Frontend[1] = watcher;
            PassiveRecalculator.Recalculate(state, game, cc, effects);
            StatCalculator.CalculateEffectiveTP(watcher, state.Player1Field, cc).Should().Be(
                800, "スポットが場にいる間はパッシブ効果で+200される");

            EndPhaseProcessor.Process(state, game, 1, cc, effects, new FakeClock());

            StatCalculator.CalculateEffectiveTP(watcher, state.Player1Field, cc).Should().Be(
                600, "スポット失効の自壊で発動条件を失い基礎値に戻る");
        }
    }
}
