using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Effects.Ops;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Effects;

public class FieldOpTests
{
    /// <summary>Shared setup for field-op tests (card cache, game, and op-context builder).</summary>
    public abstract class Base
    {
        protected readonly TestCardCache _cc = new();
        protected readonly Game _game = TestFactory.MakeGame();

        /// <summary>Builds an op context wired with the shared game and card cache.</summary>
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

    /// <summary>Tests for the reactive-support peek op.</summary>
    public class PeekReactive : Base
    {
        [Fact]
        public void PeekReactiveOp_AddsPeekedBy_WhenHiddenSupportExists()
        {
            var state = TestFactory.MakeGameState();
            state.Player2Field.Support[0] = new DeployedSupport
            {
                InstanceID = "sup_1",
                CardID = "TST-REACT",
                FaceUp = false,
            };

            var op = new PeekReactiveOp();
            var opCtx = MakeOpContext(state, playerNum: 1);

            op.Execute(opCtx);

            var support = state.Player2Field.Support[0]!;
            support.FaceUp.Should().BeFalse("peek should not flip the card face-up");
            support.PeekedBy.Should().Contain(1, "player 1 should be in PeekedBy");
        }

        [Fact]
        public void PeekReactiveOp_DoesNothing_WhenNoHiddenSupport()
        {
            var state = TestFactory.MakeGameState();
            state.Player2Field.Support[0] = new DeployedSupport
            {
                InstanceID = "sup_1",
                CardID = "TST-REACT",
                FaceUp = true,
            };

            var op = new PeekReactiveOp();
            var opCtx = MakeOpContext(state, playerNum: 1);

            op.Execute(opCtx);

            state.Player2Field.Support[0]!.PeekedBy.Should().BeEmpty();
        }

        [Fact]
        public void PeekReactiveOp_DoesNotDuplicate_WhenAlreadyPeeked()
        {
            var state = TestFactory.MakeGameState();
            state.Player2Field.Support[0] = new DeployedSupport
            {
                InstanceID = "sup_1",
                CardID = "TST-REACT",
                FaceUp = false,
                PeekedBy = [1],
            };

            var op = new PeekReactiveOp();
            var opCtx = MakeOpContext(state, playerNum: 1);

            op.Execute(opCtx);

            state.Player2Field.Support[0]!.PeekedBy.Should().HaveCount(1,
                "should not add duplicate player number");
        }

        [Fact]
        public void PeekReactiveOp_PeeksFirstHidden_SkippingFaceUp()
        {
            var state = TestFactory.MakeGameState();
            state.Player2Field.Support[0] = new DeployedSupport
            {
                InstanceID = "sup_visible",
                CardID = "TST-REACT-A",
                FaceUp = true,
            };
            state.Player2Field.Support[1] = new DeployedSupport
            {
                InstanceID = "sup_hidden",
                CardID = "TST-REACT-B",
                FaceUp = false,
            };

            var op = new PeekReactiveOp();
            var opCtx = MakeOpContext(state, playerNum: 1);

            op.Execute(opCtx);

            state.Player2Field.Support[0]!.PeekedBy.Should().BeEmpty("face-up card should not be peeked");
            state.Player2Field.Support[1]!.PeekedBy.Should().Contain(1);
        }
    }
}
