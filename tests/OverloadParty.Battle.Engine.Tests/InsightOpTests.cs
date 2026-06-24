using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Effects.Ops;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Effects;

public class InsightOpTests
{
    /// <summary>Shared setup for insight-op tests (card cache, game, and op-context builder).</summary>
    public abstract class Base
    {
        protected readonly TestCardCache _cc = new();
        protected readonly Game _game = TestFactory.MakeGame();

        /// <summary>Builds an op context for the supplied state and player.</summary>
        /// <param name="state">The game state to operate on.</param>
        /// <param name="playerNum">The acting player number.</param>
        /// <returns>An op context for the supplied state and player.</returns>
        protected OpContext MakeOpContext(BattleGameState state, long playerNum)
        {
            var ctx = new EffectContext
            {
                State = state,
                Game = _game,
                PlayerNum = playerNum,
                CardCache = _cc,
                Effects = new EffectRegistry(),
            };
            return new OpContext(ctx);
        }
    }

    /// <summary>Tests for the op that adds インサイト to the 自分 の インサイトプール.</summary>
    public class GainInsight : Base
    {
        [Theory]
        [InlineData(0, 300, 300)]
        [InlineData(100, 300, 400)]
        public void GainInsightOp_AddsToOwnPool(long initial, long amount, long expected)
        {
            var state = TestFactory.MakeGameState();
            state.SetInsightPool(1, initial);

            var op = new GainInsightOp(new StaticAmount(amount));
            op.Execute(MakeOpContext(state, playerNum: 1));

            state.GetInsightPool(1).Should().Be(expected);
        }
    }

    /// <summary>Tests for the op that transfers インサイト from the 相手 の プール to the 自分 の プール.</summary>
    public class AbsorbInsight : Base
    {
        [Fact]
        public void AbsorbInsightOp_TransfersFromOpponentToOwner()
        {
            var state = TestFactory.MakeGameState();
            state.SetInsightPool(1, 100);
            state.SetInsightPool(2, 500);

            var op = new AbsorbInsightOp(new StaticAmount(300));
            op.Execute(MakeOpContext(state, playerNum: 1));

            state.GetInsightPool(1).Should().Be(400);
            state.GetInsightPool(2).Should().Be(200);
        }

        [Fact]
        public void AbsorbInsightOp_ClampsToOpponentAvailableInsight()
        {
            var state = TestFactory.MakeGameState();
            state.SetInsightPool(1, 0);
            state.SetInsightPool(2, 120);

            var op = new AbsorbInsightOp(new StaticAmount(300));
            op.Execute(MakeOpContext(state, playerNum: 1));

            state.GetInsightPool(1).Should().Be(120, "相手が持つ インサイト を超えて吸収できない");
            state.GetInsightPool(2).Should().Be(0);
        }
    }
}
