using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Effects.Ops;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Effects;

public class InsightOpTests
{
    /// <summary>指定プレイヤー視点の op コンテキストを組み立てる。</summary>
    /// <param name="state">操作対象のゲーム状態。</param>
    /// <param name="playerNum">効果オーナーのプレイヤー番号。</param>
    /// <returns>op コンテキスト。</returns>
    private static OpContext MakeOpContext(BattleGameState state, long playerNum)
    {
        var ctx = new EffectContext
        {
            State = state,
            Game = TestFactory.MakeGame(),
            PlayerNum = playerNum,
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

            new GainInsightOp(new StaticAmount(amount)).Execute(MakeOpContext(state, 1));

            state.GetInsightPool(1).Should().Be(expected);
        }
    }

    /// <summary>相手の インサイト を自分へ移す op。吸収量は相手の保有量で頭打ちになる。</summary>
    public class AbsorbInsight
    {
        // 相手プール (oppPool) を要求量未満 / 同量 / 超過 で振り、移動量 = min(要求, oppPool) を確認する。
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

            new AbsorbInsightOp(new StaticAmount(amount)).Execute(MakeOpContext(state, 1));

            state.GetInsightPool(1).Should().Be(expectedGained);
            state.GetInsightPool(2).Should().Be(expectedOppLeft);
        }

        [Fact]
        public void AbsorbInsightOp_TransfersNothing_WhenOpponentPoolEmpty()
        {
            var state = TestFactory.MakeGameState();
            state.SetInsightPool(1, 50);
            state.SetInsightPool(2, 0);

            new AbsorbInsightOp(new StaticAmount(300)).Execute(MakeOpContext(state, 1));

            state.GetInsightPool(1).Should().Be(50);
            state.GetInsightPool(2).Should().Be(0);
        }
    }
}
