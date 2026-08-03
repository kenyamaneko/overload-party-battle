using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Effects.Ops;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Effects;

/// <summary>
/// デッキ / 手札からのデプロイ要求 op を起動効果・誘発効果として登録し、起動効果の使用や破壊といった
/// アクション越しにスロット選択待ち (PendingSlotSelects) が積まれることを検証する。op を直接叩かない。
/// </summary>
public class SlotRequestOpsTests
{
    private const string SourceCard = "TST-0009";

    /// <summary>op を起動効果として発動元カードに登録した環境を作る。</summary>
    /// <param name="op">起動効果として登録する op。</param>
    /// <returns>カードキャッシュと効果レジストリ。</returns>
    private static (TestCardCache Cc, EffectRegistry Effects) IgnitionEnv(IEffectOp op)
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: SourceCard));
        cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
        cc.Add(TestFactory.DataCard(cardId: "TST-DB01", subtype: "Database"));
        var effects = new EffectRegistry();
        effects.RegisterComposed(SourceCard, TriggerType.Ignition, op);
        return (cc, effects);
    }

    /// <summary>発動元リソースを 1 体置いた状態を作る。</summary>
    /// <returns>発動元を配置済みのゲーム状態。</returns>
    private static BattleGameState StateWithSource()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
        state.Player1Field.Frontend[0] = TestFactory.MakeResource(cardId: SourceCard, instanceId: "src", faceUp: true);
        return state;
    }

    /// <summary>発動元の起動効果を使用する。</summary>
    /// <param name="state">対象のゲーム状態。</param>
    /// <param name="cc">カードキャッシュ。</param>
    /// <param name="effects">効果レジストリ。</param>
    /// <param name="choiceData">任意の選択データ。</param>
    /// <returns>アクション結果。</returns>
    private static ActionResult Ignite(
        BattleGameState state, TestCardCache cc, EffectRegistry effects, Dictionary<string, object>? choiceData = null) =>
        UseIgnitionProcessor.Process(state, TestFactory.MakeGame(), 1,
            new UseIgnitionRequest { InstanceID = "src", ChoiceData = choiceData }, cc, effects);

    [Trait("対象", "デッキからのデプロイ要求")]
    public class FromRepo
    {
        [Fact(DisplayName = "デッキからのデプロイ要求でスロット選択待ちが 1 件積まれる")]
        public void Ignition_EnqueuesPendingSlotSelect()
        {
            var (cc, effects) = IgnitionEnv(new RequestSlotFromRepoOp());
            var state = StateWithSource();
            state.Player1Repository = [new UndeployedCard { InstanceID = "r_1", CardID = "TST-0001" }];

            Ignite(state, cc, effects);

            var pending = state.PendingSlotSelects.Should().ContainSingle().Subject;
            pending.PlayerNum.Should().Be(1);
            pending.Resource.CardID.Should().Be("TST-0001");
        }

        [Fact(DisplayName = "デッキからのデプロイ要求で対象カードがデッキから取り除かれる")]
        public void Ignition_RemovesCardFromRepo()
        {
            var (cc, effects) = IgnitionEnv(new RequestSlotFromRepoOp());
            var state = StateWithSource();
            state.Player1Repository = [new UndeployedCard { InstanceID = "r_1", CardID = "TST-0001" }];

            Ignite(state, cc, effects);

            state.Player1Repository.Should().BeEmpty();
        }

        [Fact(DisplayName = "可用性を上書きして 200 を指定するとデプロイするリソースの最大可用性が 200 になる")]
        public void Ignition_OverrideAV_AppliedToDeployedResource()
        {
            var (cc, effects) = IgnitionEnv(new RequestSlotFromRepoOp { OverrideAV = 200 });
            var state = StateWithSource();
            state.Player1Repository = [new UndeployedCard { InstanceID = "r_1", CardID = "TST-0001" }];

            Ignite(state, cc, effects);

            state.PendingSlotSelects[0].Resource.MaxAV.Should().Be(200);
        }

        [Fact(DisplayName = "条件に合うカードがデッキにないとき選択待ちが積まれずデッキは変わらない")]
        public void Ignition_NoMatchingCard_LeavesRepoUntouched()
        {
            var (cc, effects) = IgnitionEnv(new RequestSlotFromRepoOp { Filter = card => card.CardId == "NONEXISTENT" });
            var state = StateWithSource();
            state.Player1Repository = [new UndeployedCard { InstanceID = "r_1", CardID = "TST-0001" }];

            Ignite(state, cc, effects);

            state.PendingSlotSelects.Should().BeEmpty();
            state.Player1Repository.Should().HaveCount(1);
        }

        [Theory(DisplayName = "Compute系リソースの選択待ちは、フロントエンドにもバックエンドにも配置できる")]
        [InlineData(Zones.Frontend)]
        [InlineData(Zones.Backend)]
        public void Ignition_ComputeCard_PlaceableInFrontendAndBackend(string zone)
        {
            var (cc, effects) = IgnitionEnv(new RequestSlotFromRepoOp());
            var state = StateWithSource();
            state.Player1Repository = [new UndeployedCard { InstanceID = "r_1", CardID = "TST-0001" }];

            Ignite(state, cc, effects);
            SelectSlotProcessor.Process(state, TestFactory.MakeGame(), 1,
                new SelectSlotRequest { Zone = zone, Index = 1 }, cc, effects);

            state.PendingSlotSelects.Should().BeEmpty();
        }

        [Fact(DisplayName = "データベースリソースの選択待ちは、フロントエンドを選ぶと拒否される")]
        public void Ignition_DatabaseCard_RejectsFrontend()
        {
            var (cc, effects) = IgnitionEnv(new RequestSlotFromRepoOp());
            var state = StateWithSource();
            state.Player1Repository = [new UndeployedCard { InstanceID = "r_1", CardID = "TST-DB01" }];

            Ignite(state, cc, effects);
            var act = () => SelectSlotProcessor.Process(state, TestFactory.MakeGame(), 1,
                new SelectSlotRequest { Zone = Zones.Frontend, Index = 1 }, cc, effects);

            act.Should().Throw<GameRuleException>().WithMessage("*Invalid slot: frontend_1*");
        }

        [Fact(DisplayName = "空きスロットがないとき選択待ちが積まれずデッキは変わらない")]
        public void Ignition_NoEmptySlots_LeavesRepoUntouched()
        {
            var (cc, effects) = IgnitionEnv(new RequestSlotFromRepoOp());
            var state = StateWithSource();
            state.Player1Repository = [new UndeployedCard { InstanceID = "r_1", CardID = "TST-DB01" }];
            for (int i = 0; i < BattleConstants.SlotsPerZone; i++)
            {
                state.Player1Field.Backend[i] = TestFactory.MakeResource(instanceId: $"occ_{i}");
            }

            Ignite(state, cc, effects);

            state.PendingSlotSelects.Should().BeEmpty();
            state.Player1Repository.Should().HaveCount(1);
        }
    }

    [Trait("対象", "手札からのデプロイ要求")]
    public class FromHand
    {
        /// <summary>デプロイするカードを指定する選択データを作る。</summary>
        /// <param name="cardId">デプロイ対象のカード ID。</param>
        /// <returns>カード ID を含む選択データ。</returns>
        private static Dictionary<string, object> Choose(string cardId) =>
            new() { ["cardId"] = cardId };

        [Fact(DisplayName = "手札からのデプロイ要求でスロット選択待ちが 1 件積まれる")]
        public void Ignition_EnqueuesPendingSlotSelect()
        {
            var (cc, effects) = IgnitionEnv(new RequestSlotFromHandOp());
            var state = StateWithSource();
            state.Player1Hand = [new UndeployedCard { InstanceID = "h_1", CardID = "TST-0001" }];

            Ignite(state, cc, effects, Choose("TST-0001"));

            state.PendingSlotSelects.Should().ContainSingle()
                .Which.Resource.CardID.Should().Be("TST-0001");
        }

        [Fact(DisplayName = "手札からのデプロイ要求で対象カードが手札から取り除かれる")]
        public void Ignition_RemovesCardFromHand()
        {
            var (cc, effects) = IgnitionEnv(new RequestSlotFromHandOp());
            var state = StateWithSource();
            state.Player1Hand = [new UndeployedCard { InstanceID = "h_1", CardID = "TST-0001" }];

            Ignite(state, cc, effects, Choose("TST-0001"));

            state.Player1Hand.Should().BeEmpty();
        }

        [Fact(DisplayName = "手札のカードを選ばずにデプロイ要求すると拒否される")]
        public void Ignition_NoChoice_Throws()
        {
            var (cc, effects) = IgnitionEnv(new RequestSlotFromHandOp());
            var state = StateWithSource();
            state.Player1Hand = [new UndeployedCard { InstanceID = "h_1", CardID = "TST-0001" }];

            var act = () => Ignite(state, cc, effects);

            act.Should().Throw<GameRuleException>();
        }

        [Fact(DisplayName = "条件に合わないカードを選ぶと拒否され手札はそのまま残る")]
        public void Ignition_FilterRejectsCard_Throws()
        {
            var (cc, effects) = IgnitionEnv(new RequestSlotFromHandOp { Filter = _ => false });
            var state = StateWithSource();
            state.Player1Hand = [new UndeployedCard { InstanceID = "h_1", CardID = "TST-0001" }];

            var act = () => Ignite(state, cc, effects, Choose("TST-0001"));

            act.Should().Throw<GameRuleException>();
            state.Player1Hand.Should().HaveCount(1);
        }
    }

    [Trait("対象", "破壊時の同名カードのデプロイ要求")]
    public class FromRepoSameCard
    {
        /// <summary>攻撃リクエストを作る。</summary>
        /// <param name="attacker">攻撃側インスタンス ID。</param>
        /// <param name="target">対象インスタンス ID。</param>
        /// <returns>攻撃リクエスト。</returns>
        private static AttackRequest Atk(string attacker, string target) =>
            new() { AttackerInstanceID = attacker, TargetInstanceID = target };

        [Fact(DisplayName = "リソースが破壊されるとデッキから同名カードのデプロイ要求が積まれる")]
        public void OnDestroy_DeploysSameCardFromRepo()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
            cc.Add(TestFactory.DataCard(cardId: "TST-DB01", subtype: "Database"));
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0005", tp: 1500, av: 1400, name: "StrongCompute"));
            var effects = new EffectRegistry();
            effects.RegisterComposed("TST-0001", TriggerType.OnDestroy, new RequestSlotFromRepoSameCardOp());

            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Battle, activePlayer: 2);
            state.Player2Field.Frontend[0] = TestFactory.MakeResource(
                cardId: "TST-0005", instanceId: "atk", faceUp: true, maxTP: 1500, currentTP: 1500);
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "victim", faceUp: true);
            state.Player1Repository =
            [
                new UndeployedCard { InstanceID = "r_1", CardID = "TST-0001" },
                new UndeployedCard { InstanceID = "r_2", CardID = "TST-DB01" },
            ];

            AttackProcessor.Process(state, TestFactory.MakeGame(), 2, Atk("atk", "victim"), cc, effects);

            var pending = state.PendingSlotSelects.Should().ContainSingle().Subject;
            pending.PlayerNum.Should().Be(1);
            pending.Resource.CardID.Should().Be("TST-0001");
            state.Player1Repository.Should().ContainSingle(c => c.CardID == "TST-DB01");
        }

        [Fact(DisplayName = "盤面が満杯でも、破壊された 1 体の空きスロットへ同名カードをデプロイできる")]
        public void OnDestroy_BoardWasFull_DeploysIntoFreedSlot()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
            cc.Add(TestFactory.DataCard(cardId: "TST-DB01", subtype: "Database"));
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0005", tp: 1500, av: 1400, name: "StrongCompute"));
            var effects = new EffectRegistry();
            effects.RegisterComposed("TST-0001", TriggerType.OnDestroy, new RequestSlotFromRepoSameCardOp());

            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Battle, activePlayer: 2);
            state.Player2Field.Frontend[0] = TestFactory.MakeResource(
                cardId: "TST-0005", instanceId: "atk", faceUp: true, maxTP: 1500, currentTP: 1500);
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "victim", faceUp: true);
            for (int i = 1; i < BattleConstants.SlotsPerZone; i++)
            {
                state.Player1Field.Frontend[i] = TestFactory.MakeResource(cardId: "TST-0005", instanceId: $"fe_{i}", faceUp: true);
            }
            for (int i = 0; i < BattleConstants.SlotsPerZone; i++)
            {
                state.Player1Field.Backend[i] = TestFactory.MakeResource(cardId: "TST-DB01", instanceId: $"be_{i}", faceUp: true);
            }
            state.Player1Repository = [new UndeployedCard { InstanceID = "r_1", CardID = "TST-0001" }];

            AttackProcessor.Process(state, TestFactory.MakeGame(), 2, Atk("atk", "victim"), cc, effects);
            SelectSlotProcessor.Process(state, TestFactory.MakeGame(), 1,
                new SelectSlotRequest { Zone = Zones.Frontend, Index = 0 }, cc, effects);

            state.Player1Field.Frontend[0]!.CardID.Should().Be("TST-0001");
            state.PendingSlotSelects.Should().BeEmpty();
        }
    }
}
