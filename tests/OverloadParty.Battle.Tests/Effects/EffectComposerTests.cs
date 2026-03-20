using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Effects.Ops;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Effects;

/// <summary>
/// Tests for EffectComposer — composes IEffectOps into an EffectHandler.
/// </summary>
public class EffectComposerTests
{
    private static EffectContext MakeContext(GameState? state = null)
    {
        state ??= TestFactory.MakeGameState();
        return new EffectContext
        {
            State = state,
            Game = TestFactory.MakeGame(),
            PlayerNum = 1,
            CardCache = new TestCardCache(),
        };
    }

    [Fact]
    public void Compose_RunsOpsInSequence()
    {
        var order = new List<int>();
        var ops = new IEffectOp[]
        {
            new CustomFnOp(_ => order.Add(1)),
            new CustomFnOp(_ => order.Add(2)),
            new CustomFnOp(_ => order.Add(3)),
        };

        var handler = EffectComposer.Compose(ops);
        handler(MakeContext());

        order.Should().Equal(1, 2, 3);
    }

    [Fact]
    public void Compose_GainBudget_ModifiesState()
    {
        var state = TestFactory.MakeGameState(p1Budget: 5000);
        var ops = new IEffectOp[]
        {
            new GainBudgetOp(PlayerRef.Self, new StaticAmount(500)),
        };

        var handler = EffectComposer.Compose(ops);
        handler(MakeContext(state));

        state.Player1Budget.Should().Be(5500);
    }

    [Fact]
    public void Compose_LoseBudget_ModifiesOpponent()
    {
        var state = TestFactory.MakeGameState(p1Budget: 5000, p2Budget: 3000);
        var ops = new IEffectOp[]
        {
            new LoseBudgetOp(PlayerRef.Opponent, new StaticAmount(800)),
        };

        var handler = EffectComposer.Compose(ops);
        handler(MakeContext(state));

        state.Player2Budget.Should().Be(2200);
    }

    [Fact]
    public void Compose_MultipleOps_CumulativeEffect()
    {
        var state = TestFactory.MakeGameState(p1Budget: 5000);
        var ops = new IEffectOp[]
        {
            new GainBudgetOp(PlayerRef.Self, new StaticAmount(300)),
            new GainBudgetOp(PlayerRef.Self, new StaticAmount(200)),
        };

        var handler = EffectComposer.Compose(ops);
        handler(MakeContext(state));

        state.Player1Budget.Should().Be(5500);
    }

    [Fact]
    public void Compose_LoseBudget_AllowsNegative()
    {
        var state = TestFactory.MakeGameState(p1Budget: 5000, p2Budget: 300);
        var ops = new IEffectOp[]
        {
            new LoseBudgetOp(PlayerRef.Opponent, new StaticAmount(800)),
        };

        var handler = EffectComposer.Compose(ops);
        handler(MakeContext(state));

        state.Player2Budget.Should().Be(-500);
    }

    [Fact]
    public void Compose_FromList_AlsoWorks()
    {
        var state = TestFactory.MakeGameState(p1Budget: 5000);
        var ops = new List<IEffectOp>
        {
            new GainBudgetOp(PlayerRef.Self, new StaticAmount(1000)),
        };

        var handler = EffectComposer.Compose(ops);
        handler(MakeContext(state));

        state.Player1Budget.Should().Be(6000);
    }
}
