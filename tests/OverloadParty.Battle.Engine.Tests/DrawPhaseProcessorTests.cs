using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Engine;

public class DrawPhaseProcessorTests
{
    private readonly TestCardCache _cc = new();
    private readonly Game _game = TestFactory.MakeGame();

    public DrawPhaseProcessorTests()
    {
        _cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
    }

    // ─── Normal draw ─────────────────────────────────────────

    [Fact]
    public void Process_DrawPhase_DrawsCardAndAdvancesToMain()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Draw, activePlayer: 1);
        state.Player1Repository.Add(new UndeployedCard { InstanceID = "repo_1", CardID = "TST-0001", ArtNo = 0 });

        var result = DrawPhaseProcessor.Process(state, _game, _cc, new EffectRegistry());

        result.Should().BeNull();
        state.CurrentPhase.Should().Be(Phase.Main);
        state.Player1Hand.Should().HaveCount(1);
        state.Player1Repository.Should().BeEmpty();
    }

    // ─── Not draw phase → no-op ──────────────────────────────

    [Fact]
    public void Process_NotDrawPhase_ReturnsNull_NoStateChange()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main, activePlayer: 1);
        state.Player1Repository.Add(new UndeployedCard { InstanceID = "repo_1", CardID = "TST-0001" });

        var result = DrawPhaseProcessor.Process(state, _game, _cc, new EffectRegistry());

        result.Should().BeNull();
        state.CurrentPhase.Should().Be(Phase.Main);
        state.Player1Hand.Should().BeEmpty();
    }

    // ─── Repository out ──────────────────────────────────────

    [Fact]
    public void Process_EmptyRepository_ReturnsGameOver()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Draw, activePlayer: 1);
        // No cards in repository

        var result = DrawPhaseProcessor.Process(state, _game, _cc, new EffectRegistry());

        result.Should().NotBeNull();
        result!.WinnerNum.Should().Be(2);
        result.Reason.Should().Be("repository_out");
    }

    // ─── Deploy countdown ────────────────────────────────────

    [Fact]
    public void Process_DecrementsDeployCountdown()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Draw, activePlayer: 1);
        state.Player1Repository.Add(new UndeployedCard { InstanceID = "repo_1", CardID = "TST-0001" });
        var resource = TestFactory.MakeResource(faceUp: false, deployLeft: 2);
        state.Player1Field.Frontend[0] = resource;

        DrawPhaseProcessor.Process(state, _game, _cc, new EffectRegistry());

        resource.DeployingTurnsLeft.Should().Be(1);
        resource.FaceUp.Should().BeFalse();
    }

    [Fact]
    public void Process_DeployCountdownReachesZero_FlipsFaceUp()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Draw, activePlayer: 1);
        state.Player1Repository.Add(new UndeployedCard { InstanceID = "repo_1", CardID = "TST-0001" });
        var resource = TestFactory.MakeResource(faceUp: false, deployLeft: 1);
        state.Player1Field.Frontend[0] = resource;

        DrawPhaseProcessor.Process(state, _game, _cc, new EffectRegistry());

        resource.DeployingTurnsLeft.Should().Be(0);
        resource.FaceUp.Should().BeTrue();
        state.Player1HasOperated.Should().BeTrue();
    }
}
