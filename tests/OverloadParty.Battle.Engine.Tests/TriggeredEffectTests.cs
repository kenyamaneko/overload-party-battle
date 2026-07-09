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
        [Fact(DisplayName = "同一ターンの 3 体目のデプロイはキャンセルされる")]
        public void OnDeploy_CancelsThirdDeploy()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", deployTurns: 0));
            cc.Add(TestFactory.ReactiveCard(cardId: "TST-0400"));
            var effects = new EffectRegistry();
            effects.RegisterComposed("TST-0400", TriggerType.OnDeploy, Custom(CustomEffects.CancelNthDeploy));

            var state = TestFactory.MakeGameState(turn: 4, phase: Phase.Main, activePlayer: 1);
            // P1 は当ターン既に 2 体デプロイ済み
            state.Player1Field.Frontend[0] = DeployedThisTurn("d_0", 4);
            state.Player1Field.Frontend[1] = DeployedThisTurn("d_1", 4);
            state.Player1Hand.Add(new UndeployedCard { InstanceID = "h_3", CardID = "TST-0001" });
            state.Player2Field.Support[0] = new DeployedSupport { InstanceID = "watcher", CardID = "TST-0400", FaceUp = false };

            PlayCardProcessor.Process(state, TestFactory.MakeGame(), 1,
                new PlayCardRequest { CardInstanceID = "h_3", Zone = Zones.Frontend, Index = 2 }, cc, effects);

            state.Player1Field.Frontend[2].Should().BeNull("3 体目のデプロイはキャンセルされる");
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

            EndPhaseProcessor.Process(state, TestFactory.MakeGame(), 1, cc, effects);

            FieldHelpers.FindResourceByID(state.Player1Field, "spot").Should().BeNull("自壊する");
        }
    }
}
