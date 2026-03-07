using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Engine;

public class EndPhaseProcessorTests
{
    private readonly TestCardCache _cc = new();
    private readonly Game _game = TestFactory.MakeGame();

    public EndPhaseProcessorTests()
    {
        _cc.Add(TestFactory.ComputeCard(cardNo: 1));
    }

    // ─── Phase transition: Main → Battle (one step) ──────────

    [Fact]
    public void Process_MainPhase_AdvancesToBattle_NotToEnd()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main, activePlayer: 1);

        var result = EndPhaseProcessor.Process(state, _game, 1, _cc);

        state.CurrentPhase.Should().Be(Phase.Battle);
        result.GameOver.Should().BeNull();
        result.StateUpdated.Should().BeTrue();
    }

    [Fact]
    public void Process_MainPhase_DoesNotSwitchPlayer()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main, activePlayer: 1);

        EndPhaseProcessor.Process(state, _game, 1, _cc);

        state.ActivePlayer.Should().Be(1);
        state.CurrentTurn.Should().Be(2);
    }

    [Fact]
    public void Process_MainPhase_EmitsPhaseChangeEvent()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main, activePlayer: 1);

        var result = EndPhaseProcessor.Process(state, _game, 1, _cc);

        result.Events.Should().ContainSingle();
        result.Events[0].EventType.Should().Be(WireActionTypes.PhaseChange);
        var data = result.Events[0].EventData!;
        data["previousPhase"].Should().Be("main");
        data["currentPhase"].Should().Be("battle");
    }

    // ─── Phase transition: Main → End (first turn skip) ──────

    [Fact]
    public void Process_MainPhase_FirstTurn_SkipsBattleGoesToEnd()
    {
        var state = TestFactory.MakeGameState(turn: 1, phase: Phase.Main, activePlayer: 1);
        AddRepoCards(state, 2);

        var result = EndPhaseProcessor.Process(state, _game, 1, _cc);

        // End phase processing → turn switch → draw → main
        state.CurrentPhase.Should().Be(Phase.Main);
        state.ActivePlayer.Should().Be(2);
        state.CurrentTurn.Should().Be(2);
    }

    // ─── Phase transition: Battle → End ──────────────────────

    [Fact]
    public void Process_BattlePhase_AdvancesToEnd_ThenSwitchesTurn()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle, activePlayer: 1);
        AddRepoCards(state, 2);

        var result = EndPhaseProcessor.Process(state, _game, 1, _cc);

        // End phase processing → turn switch → draw phase → advance to main
        state.ActivePlayer.Should().Be(2);
        state.CurrentTurn.Should().Be(3);
        state.CurrentPhase.Should().Be(Phase.Main);
    }

    // ─── Two end_phase calls: Main → Battle → End ────────────

    [Fact]
    public void TwoEndPhases_MainToBattleToEnd_FullSequence()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main, activePlayer: 1);
        AddRepoCards(state, 2);

        // First end_phase: Main → Battle
        var result1 = EndPhaseProcessor.Process(state, _game, 1, _cc);
        state.CurrentPhase.Should().Be(Phase.Battle);
        state.ActivePlayer.Should().Be(1);
        state.CurrentTurn.Should().Be(2);

        // Second end_phase: Battle → End → turn switch → draw → main
        var result2 = EndPhaseProcessor.Process(state, _game, 1, _cc);
        state.ActivePlayer.Should().Be(2);
        state.CurrentTurn.Should().Be(3);
        state.CurrentPhase.Should().Be(Phase.Main);
    }

    // ─── Discard required ────────────────────────────────────

    [Fact]
    public void Process_EndPhase_HandOverLimit_NeedsDiscard()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle, activePlayer: 1);
        // Add 7 cards to hand (limit is 6)
        for (int i = 0; i < 7; i++)
        {
            state.Player1Hand.Add(new HandCard
            {
                InstanceID = $"hand_{i}",
                CardID = 1,
            });
        }

        var result = EndPhaseProcessor.Process(state, _game, 1, _cc);

        result.NeedsDiscard.Should().BeTrue();
        // Phase stays at End, no turn switch yet
        state.CurrentPhase.Should().Be(Phase.End);
        state.ActivePlayer.Should().Be(1);
        state.CurrentTurn.Should().Be(2);
    }

    [Fact]
    public void Process_EndPhase_HandWithinLimit_NoDiscard()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle, activePlayer: 1);
        AddRepoCards(state, 2);
        // 6 cards = at limit, no discard needed
        for (int i = 0; i < 6; i++)
        {
            state.Player1Hand.Add(new HandCard
            {
                InstanceID = $"hand_{i}",
                CardID = 1,
            });
        }

        var result = EndPhaseProcessor.Process(state, _game, 1, _cc);

        result.NeedsDiscard.Should().BeFalse();
        // Turn switches normally
        state.ActivePlayer.Should().Be(2);
    }

    // ─── helpers ─────────────────────────────────────────────

    private static void AddRepoCards(GameState state, long playerNum)
    {
        var repo = state.GetRepository(playerNum);
        repo.Add(new HandCard { InstanceID = "repo_1", CardID = 1 });
        repo.Add(new HandCard { InstanceID = "repo_2", CardID = 1 });
    }
}
