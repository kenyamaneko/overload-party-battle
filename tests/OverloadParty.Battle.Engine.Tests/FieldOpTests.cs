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
        /// <param name="source">The source resource for ops that act on their own card.</param>
        /// <param name="target">The target resource for ops that act on a chosen card.</param>
        /// <param name="choiceData">Player choice data for ops that branch on selection.</param>
        /// <returns>An op context for the supplied state and player.</returns>
        protected OpContext MakeOpContext(
            BattleGameState state, long playerNum,
            DeployedResource? source = null,
            DeployedResource? target = null,
            Dictionary<string, object>? choiceData = null)
        {
            var ctx = new EffectContext
            {
                State = state,
                Game = _game,
                PlayerNum = playerNum,
                Source = source,
                Target = target,
                ChoiceData = choiceData,
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

    /// <summary>Tests for the destroy-check op that 破壊 resources whose 実効 可用性 reaches zero.</summary>
    public class DestroyCheck : Base
    {
        [Fact]
        public void DestroyCheckOp_DestroysResourceAtZeroAvailability()
        {
            _cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
            var state = TestFactory.MakeGameState();
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(
                cardId: "TST-0001", instanceId: "dead", maxAV: 1000, damage: 1000);
            state.Player1Field.Frontend[1] = TestFactory.MakeResource(
                cardId: "TST-0001", instanceId: "alive", maxAV: 1000, damage: 0);

            var op = new DestroyCheckOp(PlayerRef.Myself);
            op.Execute(MakeOpContext(state, playerNum: 1));

            FieldHelpers.FindResourceByID(state.Player1Field, "dead")
                .Should().BeNull("実効 可用性 0 のリソースは破壊される");
            FieldHelpers.FindResourceByID(state.Player1Field, "alive")
                .Should().NotBeNull("可用性が残るリソースは破壊されない");
        }

        [Fact]
        public void DestroyCheckOp_MovesDestroyedResourceToTrash()
        {
            _cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
            var state = TestFactory.MakeGameState();
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(
                cardId: "TST-0001", instanceId: "dead", maxAV: 1000, damage: 1000);

            var op = new DestroyCheckOp(PlayerRef.Myself);
            op.Execute(MakeOpContext(state, playerNum: 1));

            state.GetTrash(1).Should().Contain(c => c.InstanceID == "dead");
        }

        [Fact]
        public void DestroyCheckOp_Both_ScansOwnAndOpponentFields()
        {
            _cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
            var state = TestFactory.MakeGameState();
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(
                cardId: "TST-0001", instanceId: "mine_dead", maxAV: 500, damage: 500);
            state.Player2Field.Frontend[0] = TestFactory.MakeResource(
                cardId: "TST-0001", instanceId: "opp_dead", maxAV: 500, damage: 500);

            var op = new DestroyCheckOp(PlayerRef.Both);
            op.Execute(MakeOpContext(state, playerNum: 1));

            FieldHelpers.FindResourceByID(state.Player1Field, "mine_dead").Should().BeNull();
            FieldHelpers.FindResourceByID(state.Player2Field, "opp_dead").Should().BeNull();
        }
    }

    /// <summary>Tests for the op that flips the 相手 の hidden リアクティブ face-up.</summary>
    public class RevealReactive : Base
    {
        [Fact]
        public void RevealReactiveOp_FlipsFirstHiddenSupportFaceUp()
        {
            var state = TestFactory.MakeGameState();
            state.Player2Field.Support[0] = new DeployedSupport
            {
                InstanceID = "sup_1",
                CardID = "TST-REACT",
                FaceUp = false,
            };

            var op = new RevealReactiveOp();
            op.Execute(MakeOpContext(state, playerNum: 1));

            state.Player2Field.Support[0]!.FaceUp.Should().BeTrue("伏せたリアクティブが表向きに開示される");
        }

        [Fact]
        public void RevealReactiveOp_DoesNothing_WhenNoHiddenSupport()
        {
            var state = TestFactory.MakeGameState();
            state.Player2Field.Support[0] = new DeployedSupport
            {
                InstanceID = "sup_1",
                CardID = "TST-REACT",
                FaceUp = true,
            };

            var op = new RevealReactiveOp();
            var act = () => op.Execute(MakeOpContext(state, playerNum: 1));

            act.Should().NotThrow();
            state.Player2Field.Support[0]!.FaceUp.Should().BeTrue();
        }

        [Fact]
        public void RevealReactiveOp_RevealsFirstHidden_SkippingFaceUp()
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

            var op = new RevealReactiveOp();
            op.Execute(MakeOpContext(state, playerNum: 1));

            state.Player2Field.Support[1]!.FaceUp.Should().BeTrue("最初の伏せリアクティブが開示される");
        }
    }

    /// <summary>Tests for the op that 破壊 a プラットフォーム in the 相手 の サポートゾーン.</summary>
    public class DestroyPlatform : Base
    {
        [Fact]
        public void DestroyPlatformOp_DestroysOpponentPlatform()
        {
            _cc.Add(TestFactory.PlatformCard(cardId: "TST-0200"));
            var state = TestFactory.MakeGameState();
            state.Player2Field.Support[0] = new DeployedSupport
            {
                InstanceID = "plat_1",
                CardID = "TST-0200",
                FaceUp = true,
            };

            var op = new DestroyPlatformOp();
            op.Execute(MakeOpContext(state, playerNum: 1));

            state.Player2Field.Support.Select(s => s.InstanceID).Should().NotContain("plat_1");
            state.GetTrash(2).Should().Contain(c => c.InstanceID == "plat_1");
        }

        [Fact]
        public void DestroyPlatformOp_SelectsByChoiceInstanceId()
        {
            _cc.Add(TestFactory.PlatformCard(cardId: "TST-0200"));
            var state = TestFactory.MakeGameState();
            state.Player2Field.Support[0] = new DeployedSupport { InstanceID = "plat_a", CardID = "TST-0200", FaceUp = true };
            state.Player2Field.Support[1] = new DeployedSupport { InstanceID = "plat_b", CardID = "TST-0200", FaceUp = true };
            var choice = new Dictionary<string, object> { ["instanceId"] = "plat_b" };

            var op = new DestroyPlatformOp();
            op.Execute(MakeOpContext(state, playerNum: 1, choiceData: choice));

            var remaining = state.Player2Field.Support.Select(s => s.InstanceID).ToList();
            remaining.Should().NotContain("plat_b");
            remaining.Should().Contain("plat_a");
        }

        [Fact]
        public void DestroyPlatformOp_Throws_WhenSelectedSupportIsNotPlatform()
        {
            _cc.Add(TestFactory.ReactiveCard(cardId: "TST-0400"));
            var state = TestFactory.MakeGameState();
            state.Player2Field.Support[0] = new DeployedSupport { InstanceID = "react_1", CardID = "TST-0400", FaceUp = false };
            var choice = new Dictionary<string, object> { ["instanceId"] = "react_1" };

            var op = new DestroyPlatformOp();
            var act = () => op.Execute(MakeOpContext(state, playerNum: 1, choiceData: choice));

            act.Should().Throw<GameRuleException>();
        }

        [Fact]
        public void DestroyPlatformOp_DoesNothing_WhenNoPlatformPresent()
        {
            _cc.Add(TestFactory.ReactiveCard(cardId: "TST-0400"));
            var state = TestFactory.MakeGameState();
            state.Player2Field.Support[0] = new DeployedSupport { InstanceID = "react_1", CardID = "TST-0400", FaceUp = false };

            var op = new DestroyPlatformOp();
            op.Execute(MakeOpContext(state, playerNum: 1));

            state.Player2Field.Support.Select(s => s.InstanceID).Should().Contain("react_1");
        }
    }

    /// <summary>Tests for the op that reduces the source resource's remaining デプロイターン.</summary>
    public class ReduceDeployTurns : Base
    {
        [Theory]
        [InlineData(2, 1, 1)]
        [InlineData(3, 2, 1)]
        [InlineData(1, 3, 0)]
        public void ReduceDeployTurnsOp_DecrementsAndClampsAtZero(long start, long reduce, long expected)
        {
            var state = TestFactory.MakeGameState();
            var source = TestFactory.MakeResource(instanceId: "src", deployLeft: start);

            var op = new ReduceDeployTurnsOp(new StaticAmount(reduce));
            op.Execute(MakeOpContext(state, playerNum: 1, source: source));

            source.DeployingTurnsLeft.Should().Be(expected);
        }

        [Fact]
        public void ReduceDeployTurnsOp_DoesNothing_WhenNoSource()
        {
            var state = TestFactory.MakeGameState();

            var op = new ReduceDeployTurnsOp(new StaticAmount(1));
            var act = () => op.Execute(MakeOpContext(state, playerNum: 1));

            act.Should().NotThrow();
        }
    }
}
