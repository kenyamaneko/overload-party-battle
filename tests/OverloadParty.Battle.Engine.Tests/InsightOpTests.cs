using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Effects.Ops;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Effects;

public class InsightOpTests
{
    /// <summary>プレイヤー 1 を効果オーナーとする op コンテキストを組み立てる。</summary>
    /// <param name="state">操作対象のゲーム状態。</param>
    /// <returns>op コンテキスト。</returns>
    private static OpContext MakeOpContext(BattleGameState state)
    {
        var ctx = new EffectContext
        {
            State = state,
            Game = TestFactory.MakeGame(),
            PlayerNum = 1,
            CardCache = new TestCardCache(),
            Effects = new EffectRegistry(),
        };
        return new OpContext(ctx);
    }

    /// <summary>自分の インサイトプール に インサイト を加算する op。</summary>
    public class GainInsight
    {
        [Theory]
        [InlineData(0, 300, 300)]
        [InlineData(100, 300, 400)]
        public void GainInsightOp_AddsToOwnPool(long initial, long amount, long expected)
        {
            var state = TestFactory.MakeGameState();
            state.SetInsightPool(1, initial);

            new GainInsightOp(new StaticAmount(amount)).Execute(MakeOpContext(state));

            state.GetInsightPool(1).Should().Be(expected);
        }
    }

    /// <summary>相手の インサイト を自分へ移す op。吸収量は相手の保有量で頭打ちになる。</summary>
    public class AbsorbInsight
    {
        [Theory]
        [InlineData(120, 300, 120, 0)]
        [InlineData(300, 300, 300, 0)]
        [InlineData(500, 300, 300, 200)]
        public void AbsorbInsightOp_TransfersClampedToOpponentPool(
            long oppPool, long amount, long expectedGained, long expectedOppLeft)
        {
            var state = TestFactory.MakeGameState();
            state.SetInsightPool(1, 0);
            state.SetInsightPool(2, oppPool);

            new AbsorbInsightOp(new StaticAmount(amount)).Execute(MakeOpContext(state));

            state.GetInsightPool(1).Should().Be(expectedGained);
            state.GetInsightPool(2).Should().Be(expectedOppLeft);
        }

        [Fact]
        public void AbsorbInsightOp_TransfersNothing_WhenOpponentPoolEmpty()
        {
            var state = TestFactory.MakeGameState();
            state.SetInsightPool(1, 50);
            state.SetInsightPool(2, 0);

            new AbsorbInsightOp(new StaticAmount(300)).Execute(MakeOpContext(state));

            state.GetInsightPool(1).Should().Be(50);
            state.GetInsightPool(2).Should().Be(0);
        }
    }
}
