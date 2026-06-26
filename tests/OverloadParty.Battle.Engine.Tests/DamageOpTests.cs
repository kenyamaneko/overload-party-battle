using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Effects.Ops;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Effects;

public class DamageOpTests
{
    /// <summary>対象を指定して op コンテキストを組み立てる。</summary>
    /// <param name="state">操作対象のゲーム状態。</param>
    /// <param name="cc">カードキャッシュ。</param>
    /// <param name="target">ダメージを与える対象リソース。</param>
    /// <returns>プレイヤー 1 視点の op コンテキスト。</returns>
    private static OpContext MakeOpContext(BattleGameState state, TestCardCache cc, DeployedResource target)
    {
        var ctx = new EffectContext
        {
            State = state,
            Game = TestFactory.MakeGame(),
            PlayerNum = 1,
            Target = target,
            CardCache = cc,
            Effects = new EffectRegistry(),
        };
        return new OpContext(ctx);
    }

    /// <summary>選択した対象に ダメージ を与える op。</summary>
    public class DealDamage
    {
        [Fact]
        public void DealDamageOp_AppliesDamageToTargetOnField()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
            var state = TestFactory.MakeGameState();
            var target = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "def", maxAV: 1000, damage: 0);
            state.Player2Field.Frontend[0] = target;

            new DealDamageOp(TargetSelector.Instance, new StaticAmount(300)).Execute(MakeOpContext(state, cc, target));

            target.Damage.Should().Be(300);
        }

        [Fact]
        public void DealDamageOp_AccumulatesDamage_WhenTargetNotOnField()
        {
            var cc = new TestCardCache();
            var state = TestFactory.MakeGameState();
            var target = TestFactory.MakeResource(instanceId: "orphan", maxAV: 1000, damage: 100);

            new DealDamageOp(TargetSelector.Instance, new StaticAmount(250)).Execute(MakeOpContext(state, cc, target));

            target.Damage.Should().Be(350, "フィールド外の対象でも ダメージ は累積する");
        }

        [Fact]
        public void DealDamageOp_AllowsOverkill_PastZeroAvailability()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
            var state = TestFactory.MakeGameState();
            var target = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "def", maxAV: 1000, damage: 0);
            state.Player2Field.Frontend[0] = target;

            new DealDamageOp(TargetSelector.Instance, new StaticAmount(1500)).Execute(MakeOpContext(state, cc, target));

            target.Damage.Should().Be(1500);
            target.EffectiveAV.Should().Be(-500, "実効 可用性 は 0 を下回りうる (オーバーキル分は別途消失)");
        }
    }
}
