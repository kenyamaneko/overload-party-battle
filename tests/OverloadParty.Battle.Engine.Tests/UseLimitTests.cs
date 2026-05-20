using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Effects.Ops;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Effects;

public class UseLimitTests
{
    private readonly Game _game = TestFactory.MakeGame();

    private OpContext MakeOpContext(
        BattleGameState state,
        long playerNum,
        DeployedResource? source = null,
        DeployedSupport? supSource = null)
    {
        var cc = new TestCardCache();
        var ctx = new EffectContext
        {
            State = state,
            Game = _game,
            PlayerNum = playerNum,
            Source = source,
            SupSource = supSource,
            CardCache = cc,
            Effects = new EffectRegistry(),
        };
        return new OpContext(ctx);
    }

    // ─── CheckUseLimitOp (per-turn) ──────────────────────────────────

    [Fact]
    public void CheckUseLimitOp_Throws_WhenEffectAlreadyUsedThisTurn()
    {
        var state = TestFactory.MakeGameState();
        var source = TestFactory.MakeResource(instanceId: "r1");
        source.EffectUsedThisTurn = true;

        var opCtx = MakeOpContext(state, playerNum: 1, source: source);
        var op = new CheckUseLimitOp(perGame: false);

        var act = () => op.Execute(opCtx);

        act.Should().Throw<GameRuleException>().WithMessage("*turn*");
    }

    [Fact]
    public void CheckUseLimitOp_Passes_WhenEffectNotYetUsed()
    {
        var state = TestFactory.MakeGameState();
        var source = TestFactory.MakeResource(instanceId: "r1");
        source.EffectUsedThisTurn = false;

        var opCtx = MakeOpContext(state, playerNum: 1, source: source);
        var op = new CheckUseLimitOp(perGame: false);

        var act = () => op.Execute(opCtx);

        act.Should().NotThrow();
    }

    [Fact]
    public void CheckUseLimitOp_Throws_WhenSupSourceUsedThisTurn()
    {
        var state = TestFactory.MakeGameState();
        var supSource = new DeployedSupport
        {
            InstanceID = "sup_1",
            CardID = "TST-SUP",
            FaceUp = true,
            EffectUsedThisTurn = true,
        };

        var opCtx = MakeOpContext(state, playerNum: 1, supSource: supSource);
        var op = new CheckUseLimitOp(perGame: false);

        var act = () => op.Execute(opCtx);

        act.Should().Throw<GameRuleException>().WithMessage("*turn*");
    }

    // ─── MarkUseLimitOp (per-turn) ───────────────────────────────────

    [Fact]
    public void MarkUseLimitOp_SetsEffectUsedFlag()
    {
        var state = TestFactory.MakeGameState();
        var source = TestFactory.MakeResource(instanceId: "r1");
        source.EffectUsedThisTurn = false;

        var opCtx = MakeOpContext(state, playerNum: 1, source: source);
        var op = new MarkUseLimitOp(perGame: false);

        op.Execute(opCtx);

        source.EffectUsedThisTurn.Should().BeTrue();
    }

    [Fact]
    public void MarkUseLimitOp_SetsSupSourceFlag()
    {
        var state = TestFactory.MakeGameState();
        var supSource = new DeployedSupport
        {
            InstanceID = "sup_1",
            CardID = "TST-SUP",
            FaceUp = true,
            EffectUsedThisTurn = false,
        };

        var opCtx = MakeOpContext(state, playerNum: 1, supSource: supSource);
        var op = new MarkUseLimitOp(perGame: false);

        op.Execute(opCtx);

        supSource.EffectUsedThisTurn.Should().BeTrue();
    }

    // ─── CheckUseLimitOp (per-game) ──────────────────────────────────

    [Fact]
    public void CheckUseLimitOp_PerGame_Throws_WhenAlreadyUsed()
    {
        var state = TestFactory.MakeGameState();
        var source = TestFactory.MakeResource(instanceId: "r1");
        source.EffectUsedThisGame = true;

        var opCtx = MakeOpContext(state, playerNum: 1, source: source);
        var op = new CheckUseLimitOp(perGame: true);

        var act = () => op.Execute(opCtx);

        act.Should().Throw<GameRuleException>().WithMessage("*game*");
    }

    [Fact]
    public void CheckUseLimitOp_PerGame_Passes_WhenNotYetUsed()
    {
        var state = TestFactory.MakeGameState();
        var source = TestFactory.MakeResource(instanceId: "r1");
        source.EffectUsedThisGame = false;

        var opCtx = MakeOpContext(state, playerNum: 1, source: source);
        var op = new CheckUseLimitOp(perGame: true);

        var act = () => op.Execute(opCtx);

        act.Should().NotThrow();
    }

    // ─── MarkUseLimitOp (per-game) ───────────────────────────────────

    [Fact]
    public void MarkUseLimitOp_PerGame_SetsGameFlag()
    {
        var state = TestFactory.MakeGameState();
        var source = TestFactory.MakeResource(instanceId: "r1");
        source.EffectUsedThisGame = false;

        var opCtx = MakeOpContext(state, playerNum: 1, source: source);
        var op = new MarkUseLimitOp(perGame: true);

        op.Execute(opCtx);

        source.EffectUsedThisGame.Should().BeTrue();
    }

    [Fact]
    public void MarkUseLimitOp_PerGame_SetsSupSourceGameFlag()
    {
        var state = TestFactory.MakeGameState();
        var supSource = new DeployedSupport
        {
            InstanceID = "sup_1",
            CardID = "TST-SUP",
            FaceUp = true,
            EffectUsedThisGame = false,
        };

        var opCtx = MakeOpContext(state, playerNum: 1, supSource: supSource);
        var op = new MarkUseLimitOp(perGame: true);

        op.Execute(opCtx);

        supSource.EffectUsedThisGame.Should().BeTrue();
    }
}
