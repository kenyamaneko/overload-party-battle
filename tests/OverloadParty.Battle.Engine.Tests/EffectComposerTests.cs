using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Effects.Ops;
using OverloadParty.Battle.Models;
using OverloadParty.GameLogicConstants;

namespace OverloadParty.Battle.Tests.Effects;

[Trait("対象", "効果 op の合成")]
public class EffectComposerTests
{
    /// <summary>ops の実行検証に十分な最小限の EffectContext を組み立てる。</summary>
    /// <returns>テスト用の EffectContext。</returns>
    private static EffectContext MakeContext() =>
        new()
        {
            State = TestFactory.MakeGameState(),
            Game = TestFactory.MakeGame(),
            PlayerNum = 1,
            CardCache = new TestCardCache(),
            Effects = new EffectRegistry(),
            Trigger = TriggerType.Ignition,
            EffectCardId = "TST-0001",
        };

    [Fact(DisplayName = "複数の op を渡すと、合成したハンドラが登録順に実行する")]
    public void Compose_RunsOpsInOrder()
    {
        var order = new List<int>();
        var handler = EffectComposer.Compose(
            new CustomFnOp(_ => order.Add(1)),
            new CustomFnOp(_ => order.Add(2)),
            new CustomFnOp(_ => order.Add(3)));

        handler(MakeContext());

        order.Should().Equal(1, 2, 3);
    }

    [Fact(DisplayName = "op のリストを渡すと、合成したハンドラが全ての op を実行する")]
    public void Compose_FromList_RunsAllOps()
    {
        var order = new List<int>();
        var ops = new List<IEffectOp>
        {
            new CustomFnOp(_ => order.Add(1)),
            new CustomFnOp(_ => order.Add(2)),
        };

        var handler = EffectComposer.Compose(ops);
        handler(MakeContext());

        order.Should().Equal(1, 2);
    }

    /// <summary>選択待ちを立てられるよう、効果の同定情報とトリガーを備えた EffectContext を組み立てる。</summary>
    /// <param name="choiceData">効果に渡す選択値。</param>
    /// <returns>テスト用の EffectContext。</returns>
    private static EffectContext MakeSuspendableContext(Dictionary<string, object>? choiceData = null) =>
        new()
        {
            State = TestFactory.MakeGameState(),
            Game = TestFactory.MakeGame(),
            PlayerNum = 1,
            CardCache = new TestCardCache(),
            Effects = new EffectRegistry(),
            ChoiceData = choiceData,
            EffectCardId = "TST-0001",
            EffectInstanceId = "src_1",
            Trigger = TriggerType.Ignition,
        };

    /// <summary>実行されると選択待ちを立てる op。</summary>
    /// <returns>選択待ちを立てる op。</returns>
    private static IEffectOp SuspendingOp() =>
        new CustomFnOp(octx =>
            octx.SuspendForChoice("instanceId", ChoiceKinds.FieldTarget, ["cand_1"], octx.PlayerNum));

    [Fact(DisplayName = "分岐内の op が選択待ちに入ったとき、同じ分岐の後続 op は実行されない")]
    public void BranchOnChoice_OpSuspends_SkipsRemainingOpsInSameBranch()
    {
        bool laterRan = false;
        var branch = new BranchOnChoiceOp(new Dictionary<string, List<IEffectOp>>
        {
            ["chosen"] = [SuspendingOp(), new CustomFnOp(_ => laterRan = true)],
        });

        var handler = EffectComposer.Compose(branch);
        handler(MakeSuspendableContext(new Dictionary<string, object> { ["option"] = "chosen" }));

        laterRan.Should().BeFalse();
    }

    [Fact(DisplayName = "条件が成立した分岐の op が選択待ちに入ったとき、その分岐の後続 op は実行されない")]
    public void IfCondition_OpSuspends_SkipsRemainingOpsInThenBranch()
    {
        bool laterRan = false;
        var conditional = new IfConditionOp(_ => true, [SuspendingOp(), new CustomFnOp(_ => laterRan = true)]);

        var handler = EffectComposer.Compose(conditional);
        handler(MakeSuspendableContext());

        laterRan.Should().BeFalse();
    }
}
