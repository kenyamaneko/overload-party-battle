using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Effects.Ops;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Engine;

/// <summary>
/// 効果が選択待ちで中断・再開するフローを、リソースの破壊 (攻撃) を起点に検証する。
/// 選択待ちを手動で state に注入せず、それを起こすアクション越しに作る。
/// </summary>
public class PendingEffectChoiceTests
{
    private const string HandDummyCardId = "TST-0001";
    private const string ReactiveDummyCardId = "TST-0400";
    private const string AttackerCardId = "TST-0005";

    /// <summary>攻撃リクエストを作る。</summary>
    /// <param name="attacker">攻撃側インスタンス ID。</param>
    /// <param name="target">対象インスタンス ID。</param>
    /// <returns>攻撃リクエスト。</returns>
    private static AttackRequest Atk(string attacker, string target) =>
        new() { AttackerInstanceID = attacker, TargetInstanceID = target };

    /// <summary>
    /// player2 の攻撃が player1 のリソースを破壊する盤面を作る。player1 のサポートゾーンには
    /// 伏せた reactive (<see cref="ReactiveDummyCardId"/>) を置き、on_destroy トリガーとして
    /// 指定した op を登録する。
    /// </summary>
    /// <param name="cc">リアクティブ・攻撃側・被弾側カードを登録済みのカードキャッシュ。</param>
    /// <param name="onDestroyOp">reactive の on_destroy トリガーとして登録する op。</param>
    /// <returns>攻撃実行前のゲーム状態と効果レジストリ。</returns>
    private static (BattleGameState State, EffectRegistry Effects) StateWithDestroyTrigger(
        TestCardCache cc, IEffectOp onDestroyOp)
    {
        var effects = new EffectRegistry();
        effects.RegisterComposed(ReactiveDummyCardId, TriggerType.OnDestroy, onDestroyOp);

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Battle, activePlayer: 2);
        state.Player2Field.Frontend[0] = TestFactory.MakeResource(
            cardId: AttackerCardId, instanceId: "atk", faceUp: true, maxTP: 1500, currentTP: 1500);
        state.Player1Field.Frontend[0] = TestFactory.MakeResource(
            cardId: HandDummyCardId, instanceId: "victim", faceUp: true, maxAV: 1400);
        state.Player1Field.Support[0] = new DeployedSupport
        {
            InstanceID = "sup_1",
            CardID = ReactiveDummyCardId,
            FaceUp = false,
            DeployOrder = 1,
        };

        return (state, effects);
    }

    [Trait("対象", "候補ありの手札選択")]
    public class HandChoiceWithCandidates
    {
        [Fact(DisplayName = "リソースが破壊されると、reactive 経由で手札の候補があるとき手札選択の選択待ちへ遷移する")]
        public void OnDestroy_ViaReactive_WithCandidates_SuspendsForHandChoice()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: HandDummyCardId));
            cc.Add(TestFactory.ComputeCard(cardId: AttackerCardId, tp: 1500, av: 1400, name: "StrongCompute"));
            cc.Add(TestFactory.ReactiveCard(cardId: ReactiveDummyCardId));
            var (state, effects) = StateWithDestroyTrigger(cc, new RequestSlotFromHandOp());
            var victim = state.Player1Field.Frontend[0];
            state.Player1Hand.Add(new UndeployedCard { InstanceID = "h_1", CardID = HandDummyCardId });

            AttackProcessor.Process(state, TestFactory.MakeGame(), 2, Atk("atk", "victim"), cc, effects);

            state.PendingEffectChoice.Should().NotBeNull();
            var pending = state.PendingEffectChoice!;
            pending.ChooserPlayerNum.Should().Be(1);
            pending.OwnerPlayerNum.Should().Be(1);
            pending.EffectCardId.Should().Be(ReactiveDummyCardId);
            pending.EffectInstanceId.Should().Be("sup_1");
            pending.ChoiceKind.Should().Be(ChoiceKinds.HandCard);
            pending.Candidates.Should().Equal(HandDummyCardId);
            pending.Trigger.Should().Be(TriggerType.OnDestroy);
            pending.Target.Should().BeSameAs(victim);
        }
    }

    [Trait("対象", "候補なしの手札選択")]
    public class HandChoiceWithoutCandidates
    {
        [Fact(DisplayName = "リソースが破壊されても、reactive 経由で手札に候補がないとき発動条件を満たさず選択待ちにならない")]
        public void OnDestroy_ViaReactive_WithoutCandidates_FailsGuard()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: HandDummyCardId));
            cc.Add(TestFactory.ComputeCard(cardId: AttackerCardId, tp: 1500, av: 1400, name: "StrongCompute"));
            cc.Add(TestFactory.ReactiveCard(cardId: ReactiveDummyCardId));
            var (state, effects) = StateWithDestroyTrigger(cc, new RequestSlotFromHandOp());
            // player1 の手札は空 = 候補なし

            AttackProcessor.Process(state, TestFactory.MakeGame(), 2, Atk("atk", "victim"), cc, effects);

            state.PendingEffectChoice.Should().BeNull();
            state.Player1Field.Support[0].Should().NotBeNull("発動条件不成立のリアクティブは消費されず場に残る");
            state.Player1Field.Support[0]!.CardID.Should().Be(ReactiveDummyCardId);
        }
    }

    [Trait("対象", "フィールド対象選択の選択者")]
    public class FieldTargetForeignChooser
    {
        /// <summary>テスト専用のインライン op (任意の Action を実行する)。SuspendForChoice の chooser 引数が
        /// 所有者と異なるプレイヤーを取れることを、実カードに依存せず汎用的に確認するために使う。</summary>
        private class InlineOp(Action<OpContext> action) : IEffectOp
        {
            public void Execute(OpContext ctx) => action(ctx);
        }

        [Fact(DisplayName = "選択者に所有者と異なるプレイヤーを指定できる")]
        public void OnDestroy_SuspendsWithChooserDifferentFromOwner()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: HandDummyCardId));
            cc.Add(TestFactory.ComputeCard(cardId: AttackerCardId, tp: 1500, av: 1400, name: "StrongCompute"));
            cc.Add(TestFactory.ReactiveCard(cardId: ReactiveDummyCardId));
            var (state, effects) = StateWithDestroyTrigger(cc, new InlineOp(octx =>
            {
                var candidates = octx.OpponentField.Frontend
                    .ToArray()
                    .Where(r => r is not null)
                    .Select(r => r!.InstanceID)
                    .ToList();
                long attackerNum = octx.State.OpponentOf(octx.PlayerNum);
                octx.SuspendForChoice("instanceId", ChoiceKinds.FieldTarget, candidates, attackerNum);
            }));
            state.Player2Field.Frontend[1] = TestFactory.MakeResource(
                cardId: HandDummyCardId, instanceId: "p2_fe_b", faceUp: true);

            AttackProcessor.Process(state, TestFactory.MakeGame(), 2, Atk("atk", "victim"), cc, effects);

            state.PendingEffectChoice.Should().NotBeNull();
            var pending = state.PendingEffectChoice!;
            pending.ChooserPlayerNum.Should().Be(2, "攻撃側 (player2) が選択者になる");
            pending.OwnerPlayerNum.Should().Be(1, "リアクティブの所有者は player1 のまま");
            pending.ChoiceKind.Should().Be(ChoiceKinds.FieldTarget);
            pending.Candidates.Should().BeEquivalentTo("atk", "p2_fe_b");
        }
    }

    [Trait("対象", "選択の解決")]
    public class Resolve
    {
        /// <summary>
        /// player1 の同タイプ再デプロイ効果 (実カードが使う DeploySameTypeFromHand) を on_destroy に登録し、
        /// 攻撃で破壊 → 手札選択の選択待ちへ遷移させた状態を作る。
        /// </summary>
        /// <returns>選択待ちのゲーム状態・カードキャッシュ・効果レジストリ。</returns>
        private static (BattleGameState State, TestCardCache Cc, EffectRegistry Effects) Suspended()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: HandDummyCardId, subtype: "VM"));
            cc.Add(TestFactory.ComputeCard(cardId: AttackerCardId, tp: 1500, av: 1400, name: "StrongCompute"));
            cc.Add(TestFactory.ReactiveCard(cardId: ReactiveDummyCardId));
            var op = new CustomFnOp(new CustomEffectRegistry().Build(CustomEffects.DeploySameTypeFromHand, null)!);
            var (state, effects) = StateWithDestroyTrigger(cc, op);
            state.Player1Hand.Add(new UndeployedCard { InstanceID = "h_1", CardID = HandDummyCardId });

            AttackProcessor.Process(state, TestFactory.MakeGame(), 2, Atk("atk", "victim"), cc, effects);

            return (state, cc, effects);
        }

        [Fact(DisplayName = "選択を解決すると選択したカードが手札からデプロイされ選択待ちが解消される")]
        public void Resolve_AppliesChoiceData_DeploysChosenCard()
        {
            var (state, cc, effects) = Suspended();

            var req = new ResolvePendingChoiceRequest { ChosenId = HandDummyCardId };
            ResolvePendingChoiceProcessor.Process(state, TestFactory.MakeGame(), 1, req, cc, effects);

            state.PendingEffectChoice.Should().BeNull();
            state.Player1Hand.Should().NotContain(c => c.InstanceID == "h_1");
            var pendingSlot = state.PendingSlotSelects.Should().ContainSingle().Subject;
            pendingSlot.Resource.CardID.Should().Be(HandDummyCardId);
        }

        [Fact(DisplayName = "選択者と異なるプレイヤーが解決しようとすると拒否される")]
        public void Resolve_WithWrongChooser_Throws()
        {
            var (state, cc, effects) = Suspended();

            var req = new ResolvePendingChoiceRequest { ChosenId = HandDummyCardId };
            var act = () => ResolvePendingChoiceProcessor.Process(state, TestFactory.MakeGame(), 2, req, cc, effects);

            act.Should().Throw<GameRuleException>().WithMessage("*different player*");
        }

        [Fact(DisplayName = "候補にない ID を選んで解決しようとすると拒否される")]
        public void Resolve_WithUnknownChoiceId_Throws()
        {
            var (state, cc, effects) = Suspended();

            var req = new ResolvePendingChoiceRequest { ChosenId = "TST-9999" };
            var act = () => ResolvePendingChoiceProcessor.Process(state, TestFactory.MakeGame(), 1, req, cc, effects);

            act.Should().Throw<GameRuleException>().WithMessage("*not in candidates*");
        }
    }
}
