using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Effects.Ops;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Effects;

public class DamageOpTests
{
    /// <summary>Shared setup for damage-op tests (card cache, game, and op-context builder).</summary>
    public abstract class Base
    {
        protected readonly TestCardCache _cc = new();
        protected readonly Game _game = TestFactory.MakeGame();

        /// <summary>Builds an op context with an optional target resource.</summary>
        /// <param name="state">The game state to operate on.</param>
        /// <param name="target">The target resource selected for damage.</param>
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

    /// <summary>Tests for the op that deals ダメージ to selected resources.</summary>
    public class DealDamage : Base
    {
        [Fact]
        public void DealDamageOp_AppliesDamageToTargetOnField()
        {
            _cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
            var state = TestFactory.MakeGameState();
            var target = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "def", maxAV: 1000, damage: 0);
            state.Player2Field.Frontend[0] = target;

            var op = new DealDamageOp(TargetSelector.Instance, new StaticAmount(300));
            op.Execute(MakeOpContext(state, target: target));

            target.Damage.Should().Be(300);
        }

        [Fact]
        public void DealDamageOp_AccumulatesDamage_WhenTargetNotOnField()
        {
            var state = TestFactory.MakeGameState();
            var target = TestFactory.MakeResource(instanceId: "orphan", maxAV: 1000, damage: 100);

            var op = new DealDamageOp(TargetSelector.Instance, new StaticAmount(250));
            op.Execute(MakeOpContext(state, target: target));

            target.Damage.Should().Be(350, "フィールド外の対象でも ダメージ は累積する");
        }
    }
}
