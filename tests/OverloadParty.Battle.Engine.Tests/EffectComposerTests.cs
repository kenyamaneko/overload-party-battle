using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Effects.Ops;

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
}
