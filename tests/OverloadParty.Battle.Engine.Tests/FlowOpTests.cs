using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Effects.Ops;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Effects;

public class FlowOpTests
{
    /// <summary>対象を任意指定して op コンテキストを組み立てる。</summary>
    /// <param name="state">操作対象のゲーム状態。</param>
    /// <param name="target">破壊を免れさせる対象リソース。</param>
    /// <returns>プレイヤー 1 視点の op コンテキスト。</returns>
    private static OpContext MakeOpContext(BattleGameState state, DeployedResource? target = null)
    {
        var ctx = new EffectContext
        {
            State = state,
            Game = TestFactory.MakeGame(),
            PlayerNum = 1,
            Target = target,
            CardCache = new TestCardCache(),
            Effects = new EffectRegistry(),
        };
        return new OpContext(ctx);
    }

    /// <summary>トリガーとなったアクションをキャンセルする op (リアクティブの効果に使う)。</summary>
    public class CancelAction
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

    /// <summary>破壊を免れさせ、対象を surviveAV の 可用性 で残す op。</summary>
    public class SurviveDestruction
    {
        // surviveAV を MaxAV 未満 / 等しい / 超過 で振り、実効 可用性 = min(surviveAV, MaxAV) になることを確認する。
        [Theory]
        [InlineData(1000, 200, 200)]
        [InlineData(1000, 1000, 1000)]
        [InlineData(100, 500, 100)]
        public void SurviveDestructionOp_LeavesTargetAtClampedAvailability(long maxAV, long surviveAV, long expectedEffectiveAV)
        {
            var state = TestFactory.MakeGameState();
            var target = TestFactory.MakeResource(instanceId: "t", maxAV: maxAV, damage: maxAV);

            var op = new SurviveDestructionOp(surviveAV);
            op.Execute(MakeOpContext(state, target));

            target.EffectiveAV.Should().Be(expectedEffectiveAV);
        }

        [Fact]
        public void SurviveDestructionOp_CancelsTriggeringAction()
        {
            var state = TestFactory.MakeGameState();
            var target = TestFactory.MakeResource(instanceId: "t", maxAV: 1000, damage: 1000);

            var opCtx = MakeOpContext(state, target);
            new SurviveDestructionOp(200).Execute(opCtx);

            opCtx.Result.ShouldCancelAction.Should().BeTrue();
        }

        [Fact]
        public void SurviveDestructionOp_Throws_WhenNoTarget()
        {
            var state = TestFactory.MakeGameState();

            var act = () => new SurviveDestructionOp(200).Execute(MakeOpContext(state));

            act.Should().Throw<GameRuleException>();
        }
    }
}
