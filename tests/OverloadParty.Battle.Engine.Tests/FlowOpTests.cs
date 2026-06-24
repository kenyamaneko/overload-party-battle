using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Effects.Ops;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Effects;

public class FlowOpTests
{
    /// <summary>Shared setup for flow-op tests (card cache, game, and op-context builder).</summary>
    public abstract class Base
    {
        protected readonly TestCardCache _cc = new();
        protected readonly Game _game = TestFactory.MakeGame();

        /// <summary>Builds an op context with an optional target resource.</summary>
        /// <param name="state">The game state to operate on.</param>
        /// <param name="target">The target resource for ops that protect a chosen card.</param>
        /// <returns>An op context for player 1.</returns>
        protected OpContext MakeOpContext(BattleGameState state, DeployedResource? target = null)
        {
            var ctx = new EffectContext
            {
                State = state,
                Game = _game,
                PlayerNum = 1,
                Target = target,
                CardCache = _cc,
                Effects = new EffectRegistry(),
            };
            return new OpContext(ctx);
        }
    }

    /// <summary>Tests for the op that cancels the triggering アクション (リアクティブの効果に使う).</summary>
    public class CancelAction : Base
    {
        [Fact]
        public void SetCancelActionOp_SetsShouldCancelAction()
        {
            var state = TestFactory.MakeGameState();
            var opCtx = MakeOpContext(state);

            SetCancelActionOp.Instance.Execute(opCtx);

            opCtx.Result.ShouldCancelAction.Should().BeTrue();
        }
    }

    /// <summary>Tests for the op that prevents 破壊 by leaving the target at a surviving 可用性.</summary>
    public class SurviveDestruction : Base
    {
        [Fact]
        public void SurviveDestructionOp_LeavesTargetAtSurviveAvailability()
        {
            var state = TestFactory.MakeGameState();
            var target = TestFactory.MakeResource(instanceId: "t", maxAV: 1000, damage: 1000);

            var op = new SurviveDestructionOp(surviveAV: 200);
            op.Execute(MakeOpContext(state, target: target));

            target.EffectiveAV.Should().Be(200, "破壊を免れ surviveAV の 可用性 で残る");
        }

        [Fact]
        public void SurviveDestructionOp_CancelsTriggeringAction()
        {
            var state = TestFactory.MakeGameState();
            var target = TestFactory.MakeResource(instanceId: "t", maxAV: 1000, damage: 1000);

            var op = new SurviveDestructionOp(surviveAV: 200);
            var opCtx = MakeOpContext(state, target: target);
            op.Execute(opCtx);

            opCtx.Result.ShouldCancelAction.Should().BeTrue();
        }

        [Fact]
        public void SurviveDestructionOp_ClampsDamageAtZero_WhenSurviveAvExceedsMaxAv()
        {
            var state = TestFactory.MakeGameState();
            var target = TestFactory.MakeResource(instanceId: "t", maxAV: 100, damage: 50);

            var op = new SurviveDestructionOp(surviveAV: 500);
            op.Execute(MakeOpContext(state, target: target));

            target.Damage.Should().Be(0);
        }

        [Fact]
        public void SurviveDestructionOp_Throws_WhenNoTarget()
        {
            var state = TestFactory.MakeGameState();

            var op = new SurviveDestructionOp(surviveAV: 200);
            var act = () => op.Execute(MakeOpContext(state));

            act.Should().Throw<GameRuleException>();
        }
    }
}
