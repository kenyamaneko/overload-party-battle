using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Engine;

public class MonetizeProcessorTests
{
    /// <summary>Shared setup for MonetizeProcessor tests (card cache with compute and data cards, a game, and request builders).</summary>
    public abstract class Base
    {
        protected readonly TestCardCache _cc = new();
        protected readonly Game _game = TestFactory.MakeGame();

        protected Base()
        {
            // Backend compute card: TP=600
            _cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", tp: 600));
            // Second backend compute card: TP=400
            _cc.Add(TestFactory.ComputeCard(cardId: "TEST-0002", tp: 400, name: "SmallCompute"));
            // Database card (data type, not compute)
            _cc.Add(TestFactory.DataCard(cardId: "TST-0002", subtype: "Database"));
        }

        /// <summary>Builds a monetize request from the given distributions.</summary>
        protected static MonetizeRequest MakeReq(params MonetizeDistribution[] dists) =>
            new() { Distributions = [.. dists] };

        /// <summary>Builds a single monetize distribution for an instance and amount.</summary>
        protected static MonetizeDistribution Dist(string instanceId, long amount) =>
            new() { InstanceID = instanceId, Amount = amount };
    }

    [Trait("対象", "休止リソースの収益化")]
    public class DormantResource : Base
    {
        [Fact(DisplayName = "休止中のリソースを収益化しようとすると拒否される")]
        public void Process_DormantResource_Throws()
        {
            var state = TestFactory.MakeGameState(turn: 2);
            var resource = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "be_1", faceUp: true);
            resource.TemporaryEffects.Add(new TemporaryEffect { EffectType = BuffTypes.Dormant });
            state.Player1Field.Backend[0] = resource;
            state.SetInsightPool(1, 500);

            var act = () => MonetizeProcessor.Process(
                state, _game, 1, MakeReq(Dist("be_1", 300)), _cc);

            act.Should().Throw<GameRuleException>().WithMessage("*dormant*");
        }
    }

    [Trait("対象", "収益化")]
    public class BasicMonetize : Base
    {
        [Fact(DisplayName = "インサイト 300 を収益化するとプールが 200 に減りバジェットが 5300 になる")]
        public void Process_BasicMonetize_TransfersInsightToBudget()
        {
            var state = TestFactory.MakeGameState(turn: 2);
            var resource = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "be_1", faceUp: true);
            state.Player1Field.Backend[0] = resource;
            state.SetInsightPool(1, 500);

            var result = MonetizeProcessor.Process(
                state, _game, 1, MakeReq(Dist("be_1", 300)), _cc);

            state.GetInsightPool(1).Should().Be(200);
            state.GetBudget(1).Should().Be(5300);
            resource.MonetizedAmount.Should().Be(300);
        }
    }

    [Trait("対象", "初回ターンの収益化制限")]
    public class FirstTurn : Base
    {
        [Fact(DisplayName = "初回ターンに収益化しようとすると拒否される")]
        public void Process_FirstTurn_Throws()
        {
            var state = TestFactory.MakeGameState(turn: 1);
            state.Player1Field.Backend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "be_1", faceUp: true);
            state.SetInsightPool(1, 500);

            var act = () => MonetizeProcessor.Process(
                state, _game, 1, MakeReq(Dist("be_1", 100)), _cc);

            act.Should().Throw<GameRuleException>().WithMessage("*first turn*");
        }
    }

    [Trait("対象", "配分なしの収益化")]
    public class EmptyDistributions : Base
    {
        [Fact(DisplayName = "配分が空の収益化リクエストは拒否される")]
        public void Process_EmptyDistributions_Throws()
        {
            var state = TestFactory.MakeGameState(turn: 2);
            state.SetInsightPool(1, 500);

            var act = () => MonetizeProcessor.Process(
                state, _game, 1, new MonetizeRequest { Distributions = [] }, _cc);

            act.Should().Throw<GameRuleException>();
        }
    }

    [Trait("対象", "フロントエンドコンピュートの収益化")]
    public class FrontendCompute : Base
    {
        [Fact(DisplayName = "フロントエンドのコンピュート系リソースを収益化しようとすると拒否される")]
        public void Process_FrontendCompute_Throws()
        {
            var state = TestFactory.MakeGameState(turn: 2);
            var resource = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "fe_1", faceUp: true);
            state.Player1Field.Frontend[0] = resource;
            state.SetInsightPool(1, 500);

            var act = () => MonetizeProcessor.Process(
                state, _game, 1, MakeReq(Dist("fe_1", 100)), _cc);

            act.Should().Throw<GameRuleException>().WithMessage("*backend*");
        }
    }

    [Trait("対象", "非コンピュートリソースの収益化")]
    public class NonComputeType : Base
    {
        [Fact(DisplayName = "コンピュート系でないリソースを収益化しようとすると拒否される")]
        public void Process_NonComputeType_Throws()
        {
            var state = TestFactory.MakeGameState(turn: 2);
            var resource = TestFactory.MakeResource(cardId: "TST-0002", instanceId: "be_1", faceUp: true);
            state.Player1Field.Backend[0] = resource;
            state.SetInsightPool(1, 500);

            var act = () => MonetizeProcessor.Process(
                state, _game, 1, MakeReq(Dist("be_1", 100)), _cc);

            act.Should().Throw<GameRuleException>().WithMessage("*compute*");
        }
    }

    [Trait("対象", "インサイトプールを超える収益化")]
    public class ExceedsInsightPool : Base
    {
        [Fact(DisplayName = "インサイトプールを超える量を収益化しようとすると拒否される")]
        public void Process_ExceedsInsightPool_Throws()
        {
            var state = TestFactory.MakeGameState(turn: 2);
            var resource = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "be_1", faceUp: true);
            state.Player1Field.Backend[0] = resource;
            state.SetInsightPool(1, 100);

            var act = () => MonetizeProcessor.Process(
                state, _game, 1, MakeReq(Dist("be_1", 200)), _cc);

            act.Should().Throw<GameRuleException>().WithMessage("*exceeds insight pool*");
        }
    }

    [Trait("対象", "スループット上限を超える収益化")]
    public class ExceedsThroughputCapacity : Base
    {
        [Fact(DisplayName = "スループット上限 600 を超える 700 を収益化しようとすると拒否される")]
        public void Process_ExceedsThroughputCapacity_Throws()
        {
            var state = TestFactory.MakeGameState(turn: 2);
            var resource = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "be_1", faceUp: true);
            state.Player1Field.Backend[0] = resource;
            state.SetInsightPool(1, 5000);

            // TP=600 (Small rank, no family → effective TP = 600*1 = 600), requesting 700
            var act = () => MonetizeProcessor.Process(
                state, _game, 1, MakeReq(Dist("be_1", 700)), _cc);

            act.Should().Throw<GameRuleException>().WithMessage("*exceeds remaining capacity*");
        }
    }

    [Trait("対象", "負の量の収益化")]
    public class NegativeAmount : Base
    {
        [Fact(DisplayName = "負の量を収益化しようとすると拒否される")]
        public void Process_NegativeAmount_Throws()
        {
            var state = TestFactory.MakeGameState(turn: 2);
            var resource = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "be_1", faceUp: true);
            state.Player1Field.Backend[0] = resource;
            state.SetInsightPool(1, 500);

            var act = () => MonetizeProcessor.Process(
                state, _game, 1, MakeReq(Dist("be_1", -10)), _cc);

            act.Should().Throw<GameRuleException>();
        }
    }

    [Trait("対象", "複数リソースへの収益化配分")]
    public class MultipleDistributions : Base
    {
        [Fact(DisplayName = "複数リソースへ 300 と 200 を配分すると両方に適用されバジェットが 5500 になる")]
        public void Process_MultipleDistributions_AllApplied()
        {
            var state = TestFactory.MakeGameState(turn: 2);
            var res1 = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "be_1", faceUp: true);
            var res2 = TestFactory.MakeResource(cardId: "TEST-0002", instanceId: "be_2", faceUp: true,
                maxTP: 400, currentTP: 400);
            state.Player1Field.Backend[0] = res1;
            state.Player1Field.Backend[1] = res2;
            state.SetInsightPool(1, 800);

            var result = MonetizeProcessor.Process(
                state, _game, 1, MakeReq(Dist("be_1", 300), Dist("be_2", 200)), _cc);

            res1.MonetizedAmount.Should().Be(300);
            res2.MonetizedAmount.Should().Be(200);
            state.GetInsightPool(1).Should().Be(300);
            state.GetBudget(1).Should().Be(5500);
            result.Events.Should().Contain(e => e.EventType == ActionTypes.Monetize);
        }
    }

    /// <summary>バックエンド収益化に使うコンピュート系リソース 2 種を持つキャッシュを作る。</summary>
    /// <returns>TST-0001 (TP=600) / TST-0005 (TP=400) を登録したキャッシュ。</returns>
    private static TestCardCache ComputeCc()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", tp: 600));
        cc.Add(TestFactory.ComputeCard(cardId: "TST-0005", tp: 400, name: "SmallCompute"));
        return cc;
    }

    /// <summary>配分群から収益化リクエストを作る。</summary>
    /// <param name="dists">収益化の配分。</param>
    /// <returns>収益化リクエスト。</returns>
    private static MonetizeRequest Req(params MonetizeDistribution[] dists) => new() { Distributions = [.. dists] };

    /// <summary>インスタンスと量から 1 件の収益化配分を作る。</summary>
    /// <param name="id">バックエンドリソースのインスタンス ID。</param>
    /// <param name="amount">配分する インサイト 量。</param>
    /// <returns>収益化配分。</returns>
    private static MonetizeDistribution Dist(string id, long amount) => new() { InstanceID = id, Amount = amount };

    [Trait("対象", "収益化イベント")]
    public class MonetizeEvent
    {
        [Fact(DisplayName = "収益化イベントに配分したインサイト合計 450 が載る")]
        public void Process_EventIncludesTotalAmount()
        {
            var cc = ComputeCc();
            var state = TestFactory.MakeGameState(turn: 2);
            state.Player1Field.Backend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "be_1", faceUp: true);
            state.Player1Field.Backend[1] = TestFactory.MakeResource(
                cardId: "TST-0005", instanceId: "be_2", faceUp: true, maxTP: 400, currentTP: 400);
            state.SetInsightPool(1, 800);

            var result = MonetizeProcessor.Process(state, TestFactory.MakeGame(), 1, Req(Dist("be_1", 300), Dist("be_2", 150)), cc);

            var evt = result.Events.First(e => e.EventType == ActionTypes.Monetize);
            evt.EventData.Should().BeOfType<MonetizeEventData>()
                .Which.TotalAmount.Should().Be(450);
        }
    }

    [Trait("対象", "エラスティックリソースの収益化")]
    public class ElasticMonetize
    {
        [Fact(DisplayName = "エラスティックリソースを収益化するとエラスティックボーナスが加算される")]
        public void Process_AppliesElasticBonus()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ElasticContainerCard(cardId: "TST-0003"));
            var state = TestFactory.MakeGameState(turn: 2);
            var res = TestFactory.MakeResource(cardId: "TST-0003", instanceId: "be_1", faceUp: true);
            state.Player1Field.Backend[0] = res;
            state.SetInsightPool(1, 500);

            MonetizeProcessor.Process(state, TestFactory.MakeGame(), 1, Req(Dist("be_1", 100)), cc);

            res.ElasticBonus.Should().BeGreaterThan(0, "Elastic リソースの収益化で エラスティックボーナス が加算される");
        }
    }

    [Trait("対象", "インサイト 0 での収益化")]
    public class ZeroInsight
    {
        [Fact(DisplayName = "インサイトプールが 0 のとき収益化しようとすると拒否される")]
        public void Process_ZeroInsightPool_Throws()
        {
            var cc = ComputeCc();
            var state = TestFactory.MakeGameState(turn: 2);
            state.Player1Field.Backend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "be_1", faceUp: true);
            state.SetInsightPool(1, 0);

            var act = () => MonetizeProcessor.Process(state, TestFactory.MakeGame(), 1, Req(Dist("be_1", 100)), cc);

            act.Should().Throw<GameRuleException>().WithMessage("*insight pool*");
        }
    }
}
