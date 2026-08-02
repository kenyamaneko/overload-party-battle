using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Effects.Ops;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Effects;

public class UseLimitTests
{
    /// <summary>Shared setup for use-limit op tests (game and an OpContext factory).</summary>
    public abstract class Base
    {
        protected readonly Game _game = TestFactory.MakeGame();

        /// <summary>Builds an OpContext over the given state with an optional source/support source.</summary>
        protected OpContext MakeOpContext(
            BattleGameState state,
            long playerNum,
            DeployedResource? source = null,
            DeployedSupport? supSource = null,
            TriggerType trigger = TriggerType.Ignition)
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
                Trigger = trigger,
                EffectCardId = "TST-0001",
            };
            return new OpContext(ctx);
        }
    }

    [Trait("対象", "1 ターン 1 回の使用制限チェック")]
    public class CheckUseLimitPerTurn : Base
    {
        [Fact(DisplayName = "このターン既に使用済みのリソースの起動効果を使おうとすると例外になる")]
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

        [Fact(DisplayName = "このターンまだ使用していないリソースは使用できる")]
        public void CheckUseLimitOp_Passes_WhenEffectNotYetUsed()
        {
            var state = TestFactory.MakeGameState();
            var source = TestFactory.MakeResource(instanceId: "r1");
            source.EffectUsedThisTurn = false;

            var opCtx = MakeOpContext(state, playerNum: 1, source: source);
            var op = new CheckUseLimitOp(perGame: false);

            var act = () => op.Execute(opCtx);

            act.Should().NotThrow();
            opCtx.Result.HasGuardFailed.Should().BeFalse();
        }

        [Fact(DisplayName = "このターン既に使用済みのサポートカードの起動効果を使おうとすると例外になる")]
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

        [Fact(DisplayName = "このターン既に使用済みのリソースの誘発効果が再び発動しかけたときは、例外にならず発動条件の不成立として打ち切られる")]
        public void CheckUseLimitOp_Triggered_AbortsWithoutThrowing()
        {
            var state = TestFactory.MakeGameState();
            var source = TestFactory.MakeResource(instanceId: "r1");
            source.EffectUsedThisTurn = true;

            var opCtx = MakeOpContext(state, playerNum: 1, source: source, trigger: TriggerType.OnDeploy);
            var op = new CheckUseLimitOp(perGame: false);

            var act = () => op.Execute(opCtx);

            act.Should().NotThrow();
            opCtx.Result.HasGuardFailed.Should().BeTrue();
        }
    }

    [Trait("対象", "1 ターン 1 回の使用制限マーク")]
    public class MarkUseLimitPerTurn : Base
    {
        [Fact(DisplayName = "リソースを使用済みにマークするとこのターンの使用済みフラグが立つ")]
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

        [Fact(DisplayName = "サポートカードを使用済みにマークするとこのターンの使用済みフラグが立つ")]
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
    }

    [Trait("対象", "1 ゲーム 1 回の使用制限チェック")]
    public class CheckUseLimitPerGame : Base
    {
        [Fact(DisplayName = "このゲームで既に使用済みのリソースの起動効果を使おうとすると例外になる")]
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

        [Fact(DisplayName = "このゲームでまだ使用していないリソースは使用できる")]
        public void CheckUseLimitOp_PerGame_Passes_WhenNotYetUsed()
        {
            var state = TestFactory.MakeGameState();
            var source = TestFactory.MakeResource(instanceId: "r1");
            source.EffectUsedThisGame = false;

            var opCtx = MakeOpContext(state, playerNum: 1, source: source);
            var op = new CheckUseLimitOp(perGame: true);

            var act = () => op.Execute(opCtx);

            act.Should().NotThrow();
            opCtx.Result.HasGuardFailed.Should().BeFalse();
        }

        [Fact(DisplayName = "このゲームで既に使用済みのリソースの誘発効果が再び発動しかけたときは、例外にならず発動条件の不成立として打ち切られる")]
        public void CheckUseLimitOp_PerGame_Triggered_AbortsWithoutThrowing()
        {
            var state = TestFactory.MakeGameState();
            var source = TestFactory.MakeResource(instanceId: "r1");
            source.EffectUsedThisGame = true;

            var opCtx = MakeOpContext(state, playerNum: 1, source: source, trigger: TriggerType.OnDeploy);
            var op = new CheckUseLimitOp(perGame: true);

            var act = () => op.Execute(opCtx);

            act.Should().NotThrow();
            opCtx.Result.HasGuardFailed.Should().BeTrue();
        }
    }

    [Trait("対象", "1 ゲーム 1 回の使用制限マーク")]
    public class MarkUseLimitPerGame : Base
    {
        [Fact(DisplayName = "リソースを使用済みにマークするとこのゲームの使用済みフラグが立つ")]
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

        [Fact(DisplayName = "サポートカードを使用済みにマークするとこのゲームの使用済みフラグが立つ")]
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
}
