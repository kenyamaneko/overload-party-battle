using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Tests.Engine;

public class ScaleUpProcessorTests
{
    private readonly TestCardCache _cc = new();
    private readonly Game _game = TestFactory.MakeGame();

    public ScaleUpProcessorTests()
    {
        // Resizable compute card
        _cc.Add(TestFactory.ComputeCard(cardId: "SH-0001", tp: 600, resizable: true, deployTurns: 1));
        // Non-resizable compute card
        _cc.Add(TestFactory.ComputeCard(cardId: "TEST-0002", tp: 600, resizable: false, name: "FixedCompute"));
    }

    private static ScaleUpRequest MakeReq(string instanceId, string targetRank, string? family = null) =>
        new() { InstanceID = instanceId, TargetRank = targetRank, InstanceFamily = family };

    // ─── 1-2. Rank change (Small→Medium, Medium→Large) ───────

    [Theory]
    [InlineData(Rank.Small, null, "medium", "M", Rank.Medium)]
    [InlineData(Rank.Medium, InstanceFamily.M, "large", "M", Rank.Large)]
    public void Process_ChangesRank(Rank initialRank, InstanceFamily? initFamily, string reqRank, string reqFamily, Rank expectedRank)
    {
        var state = TestFactory.MakeGameState(turn: 3);
        var resource = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "inst_1", rank: initialRank, family: initFamily, faceUp: true);
        resource.DeployedOnTurn = 1;
        state.Player1Field.Frontend[0] = resource;

        ScaleUpProcessor.Process(state, _game, 1, MakeReq("inst_1", reqRank, reqFamily), _cc);

        resource.Rank.Should().Be(expectedRank);
    }

    // ─── 3. Not resizable → throws ──────────────────────────

    [Fact]
    public void Process_NotResizable_Throws()
    {
        var state = TestFactory.MakeGameState(turn: 3);
        var resource = TestFactory.MakeResource(cardId: "TEST-0002", instanceId: "inst_1", rank: Rank.Small, faceUp: true);
        resource.DeployedOnTurn = 1;
        state.Player1Field.Frontend[0] = resource;

        var act = () => ScaleUpProcessor.Process(
            state, _game, 1, MakeReq("inst_1", "medium", "M"), _cc);

        act.Should().Throw<GameRuleException>().WithMessage("*not resizable*");
    }

    // ─── 4. Deploy turn → throws ────────────────────────────

    [Fact]
    public void Process_DeployTurn_Throws()
    {
        var state = TestFactory.MakeGameState(turn: 3);
        var resource = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "inst_1", rank: Rank.Small, faceUp: true);
        resource.DeployedOnTurn = 3; // same as current turn
        state.Player1Field.Frontend[0] = resource;

        var act = () => ScaleUpProcessor.Process(
            state, _game, 1, MakeReq("inst_1", "medium", "M"), _cc);

        act.Should().Throw<GameRuleException>().WithMessage("*deploy turn*");
    }

    // ─── 5. Already scaled this turn → throws ───────────────

    [Fact]
    public void Process_AlreadyScaledThisTurn_Throws()
    {
        var state = TestFactory.MakeGameState(turn: 3);
        var resource = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "inst_1", rank: Rank.Small, faceUp: true);
        resource.DeployedOnTurn = 1;
        resource.ScaleChangedThisTurn = true;
        state.Player1Field.Frontend[0] = resource;

        var act = () => ScaleUpProcessor.Process(
            state, _game, 1, MakeReq("inst_1", "medium", "M"), _cc);

        act.Should().Throw<GameRuleException>().WithMessage("*already changed*");
    }

    // ─── 6. Medium without family → throws ──────────────────

    [Fact]
    public void Process_MediumWithoutFamily_Throws()
    {
        var state = TestFactory.MakeGameState(turn: 3);
        // Resource has no existing family, and no family provided in request
        var resource = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "inst_1", rank: Rank.Small, family: null, faceUp: true);
        resource.DeployedOnTurn = 1;
        state.Player1Field.Frontend[0] = resource;

        var act = () => ScaleUpProcessor.Process(
            state, _game, 1, MakeReq("inst_1", "medium"), _cc);

        act.Should().Throw<GameRuleException>().WithMessage("*family required*");
    }

    // ─── 7. Medium→Large with different family → throws ─────────────

    [Fact]
    public void Process_MediumToDifferentFamily_Throws()
    {
        var state = TestFactory.MakeGameState(turn: 3);
        var resource = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "inst_1", rank: Rank.Medium, family: InstanceFamily.M, faceUp: true);
        resource.DeployedOnTurn = 1;
        state.Player1Field.Frontend[0] = resource;

        var act = () => ScaleUpProcessor.Process(
            state, _game, 1, MakeReq("inst_1", "large", "C"), _cc);

        act.Should().Throw<GameRuleException>().WithMessage("*cannot change instance family*");
    }

    // ─── 8. Same or lower rank → throws ──────────────────────────────

    [Fact]
    public void Process_SameRank_Throws()
    {
        var state = TestFactory.MakeGameState(turn: 3);
        var resource = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "inst_1", rank: Rank.Medium, family: InstanceFamily.M, faceUp: true);
        resource.DeployedOnTurn = 1;
        state.Player1Field.Frontend[0] = resource;

        var act = () => ScaleUpProcessor.Process(
            state, _game, 1, MakeReq("inst_1", "medium", "M"), _cc);

        act.Should().Throw<GameRuleException>().WithMessage("*scale up*higher rank*");
    }

    // ─── 8. Generates scale-up event ────────────────────────

    [Fact]
    public void Process_GeneratesScaleUpEvent()
    {
        var state = TestFactory.MakeGameState(turn: 3);
        var resource = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "inst_1", rank: Rank.Small, faceUp: true);
        resource.DeployedOnTurn = 1;
        state.Player1Field.Frontend[0] = resource;

        var result = ScaleUpProcessor.Process(
            state, _game, 1, MakeReq("inst_1", "medium", "M"), _cc);

        var evt = result.Events.First(e => e.EventType == WireActionTypes.ScaleUp);
        evt.EventData!["instanceId"].Should().Be("inst_1");
        evt.EventData!["targetRank"].Should().Be("medium");
    }
}
