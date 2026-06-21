using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Engine;

public class ScaleUpProcessorTests
{
    /// <summary>Shared setup for ScaleUpProcessor tests (card cache with resizable/fixed compute cards, game, and request helper).</summary>
    public abstract class Base
    {
        protected readonly TestCardCache _cc = new();
        protected readonly Game _game = TestFactory.MakeGame();

        protected Base()
        {
            // Resizable compute card
            _cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", tp: 600, resizable: true, deployTurns: 1));
            // Non-resizable compute card
            _cc.Add(TestFactory.ComputeCard(cardId: "TEST-0002", tp: 600, resizable: false, name: "FixedCompute"));
        }

        /// <summary>Builds a scale-up request for the given instance, target rank, and optional family.</summary>
        /// <param name="instanceId">Target resource instance ID.</param>
        /// <param name="targetRank">Requested rank.</param>
        /// <param name="family">Optional requested instance family.</param>
        /// <returns>A scale-up request with the supplied fields.</returns>
        protected static ScaleUpRequest MakeReq(string instanceId, string targetRank, string? family = null) =>
            new() { InstanceID = instanceId, TargetRank = targetRank, InstanceFamily = family };
    }

    /// <summary>Tests for the rank transition produced by a successful scale-up.</summary>
    public class RankChange : Base
    {
        [Theory]
        [InlineData(Rank.Small, null, "medium", "M", Rank.Medium)]
        [InlineData(Rank.Medium, InstanceFamily.M, "large", "M", Rank.Large)]
        public void Process_ChangesRank(Rank initialRank, InstanceFamily? initFamily, string reqRank, string reqFamily, Rank expectedRank)
        {
            var state = TestFactory.MakeGameState(turn: 3);
            var resource = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "inst_1", rank: initialRank, family: initFamily, faceUp: true);
            resource.DeployedOnTurn = 1;
            state.Player1Field.Frontend[0] = resource;

            ScaleUpProcessor.Process(state, _game, 1, MakeReq("inst_1", reqRank, reqFamily), _cc, new EffectRegistry());

            resource.Rank.Should().Be(expectedRank);
        }
    }

    /// <summary>Tests that a non-resizable resource cannot be scaled up.</summary>
    public class NotResizable : Base
    {
        [Fact]
        public void Process_NotResizable_Throws()
        {
            var state = TestFactory.MakeGameState(turn: 3);
            var resource = TestFactory.MakeResource(cardId: "TEST-0002", instanceId: "inst_1", rank: Rank.Small, faceUp: true);
            resource.DeployedOnTurn = 1;
            state.Player1Field.Frontend[0] = resource;

            var act = () => ScaleUpProcessor.Process(
                state, _game, 1, MakeReq("inst_1", "medium", "M"), _cc, new EffectRegistry());

            act.Should().Throw<GameRuleException>().WithMessage("*not resizable*");
        }
    }

    /// <summary>Tests that a dormant resource cannot be scaled up.</summary>
    public class DormantResource : Base
    {
        [Fact]
        public void Process_DormantResource_Throws()
        {
            var state = TestFactory.MakeGameState(turn: 3);
            var resource = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "inst_1", rank: Rank.Small, faceUp: true);
            resource.DeployedOnTurn = 1;
            resource.TemporaryEffects.Add(new TemporaryEffect { EffectType = BuffTypes.Dormant });
            state.Player1Field.Frontend[0] = resource;

            var act = () => ScaleUpProcessor.Process(
                state, _game, 1, MakeReq("inst_1", "medium", "M"), _cc, new EffectRegistry());

            act.Should().Throw<GameRuleException>().WithMessage("*dormant*");
        }
    }

    /// <summary>Tests that scaling up is allowed on the deploy turn.</summary>
    public class DeployTurn : Base
    {
        [Fact]
        public void Process_DeployTurn_Succeeds()
        {
            var state = TestFactory.MakeGameState(turn: 3);
            var resource = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "inst_1", rank: Rank.Small, faceUp: true);
            resource.DeployedOnTurn = 3;
            state.Player1Field.Frontend[0] = resource;

            ScaleUpProcessor.Process(
                state, _game, 1, MakeReq("inst_1", "medium", "M"), _cc, new EffectRegistry());

            resource.Rank.Should().Be(Rank.Medium);
        }
    }

    /// <summary>Tests that scaling up twice in the same turn is allowed.</summary>
    public class TwiceInSameTurn : Base
    {
        [Fact]
        public void Process_TwiceInSameTurn_Succeeds()
        {
            var state = TestFactory.MakeGameState(turn: 3);
            var resource = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "inst_1", rank: Rank.Small, faceUp: true);
            resource.DeployedOnTurn = 1;
            state.Player1Field.Frontend[0] = resource;

            ScaleUpProcessor.Process(
                state, _game, 1, MakeReq("inst_1", "medium", "M"), _cc, new EffectRegistry());
            ScaleUpProcessor.Process(
                state, _game, 1, MakeReq("inst_1", "large", "M"), _cc, new EffectRegistry());

            resource.Rank.Should().Be(Rank.Large);
        }
    }

    /// <summary>Tests instance-family validation when scaling to Medium or changing family.</summary>
    public class FamilyValidation : Base
    {
        [Fact]
        public void Process_MediumWithoutFamily_Throws()
        {
            var state = TestFactory.MakeGameState(turn: 3);
            // Resource has no existing family, and no family provided in request
            var resource = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "inst_1", rank: Rank.Small, family: null, faceUp: true);
            resource.DeployedOnTurn = 1;
            state.Player1Field.Frontend[0] = resource;

            var act = () => ScaleUpProcessor.Process(
                state, _game, 1, MakeReq("inst_1", "medium"), _cc, new EffectRegistry());

            act.Should().Throw<GameRuleException>().WithMessage("*family required*");
        }

        [Fact]
        public void Process_MediumToDifferentFamily_Throws()
        {
            var state = TestFactory.MakeGameState(turn: 3);
            var resource = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "inst_1", rank: Rank.Medium, family: InstanceFamily.M, faceUp: true);
            resource.DeployedOnTurn = 1;
            state.Player1Field.Frontend[0] = resource;

            var act = () => ScaleUpProcessor.Process(
                state, _game, 1, MakeReq("inst_1", "large", "C"), _cc, new EffectRegistry());

            act.Should().Throw<GameRuleException>().WithMessage("*cannot change instance family*");
        }
    }

    /// <summary>Tests that scaling to the same or a lower rank is rejected.</summary>
    public class SameRank : Base
    {
        [Fact]
        public void Process_SameRank_Throws()
        {
            var state = TestFactory.MakeGameState(turn: 3);
            var resource = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "inst_1", rank: Rank.Medium, family: InstanceFamily.M, faceUp: true);
            resource.DeployedOnTurn = 1;
            state.Player1Field.Frontend[0] = resource;

            var act = () => ScaleUpProcessor.Process(
                state, _game, 1, MakeReq("inst_1", "medium", "M"), _cc, new EffectRegistry());

            act.Should().Throw<GameRuleException>().WithMessage("*scale up*higher rank*");
        }
    }

    /// <summary>Tests that a successful scale-up emits a scale-up event.</summary>
    public class ScaleUpEvent : Base
    {
        [Fact]
        public void Process_GeneratesScaleUpEvent()
        {
            var state = TestFactory.MakeGameState(turn: 3);
            var resource = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "inst_1", rank: Rank.Small, faceUp: true);
            resource.DeployedOnTurn = 1;
            state.Player1Field.Frontend[0] = resource;

            var result = ScaleUpProcessor.Process(
                state, _game, 1, MakeReq("inst_1", "medium", "M"), _cc, new EffectRegistry());

            var evt = result.Events.First(e => e.EventType == ActionTypes.ScaleUp);
            var data = evt.EventData.Should().BeOfType<ScaleUpEventData>().Subject;
            data.InstanceId.Should().Be("inst_1");
            data.TargetRank.Should().Be("medium");
        }
    }
}
