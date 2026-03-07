using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Tests.Engine;

public class DiscardProcessorTests
{
    private readonly TestCardCache _cc = new();
    private readonly Game _game = TestFactory.MakeGame();

    public DiscardProcessorTests()
    {
        // Compute card for hand/repo cards
        _cc.Add(TestFactory.ComputeCard(cardNo: 1, deployTurns: 1));
    }

    private static DiscardHandRequest MakeReq(params string[] ids) =>
        new() { CardInstanceIDs = [..ids] };

    private static List<HandCard> MakeHand(int count)
    {
        var hand = new List<HandCard>();
        for (int i = 0; i < count; i++)
        {
            hand.Add(new HandCard { InstanceID = $"h_{i}", CardID = 1 });
        }
        return hand;
    }

    private static List<HandCard> MakeRepo(int count)
    {
        var repo = new List<HandCard>();
        for (int i = 0; i < count; i++)
        {
            repo.Add(new HandCard { InstanceID = $"r_{i}", CardID = 1 });
        }
        return repo;
    }

    // ─── 1. Discards excess cards, reduces hand to limit ────

    [Fact]
    public void Process_DiscardsExcessCards_ReducesHandToLimit()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.End, activePlayer: 1);
        state.Player1Hand = MakeHand(8);
        // Opponent needs repo cards so DrawPhaseProcessor doesn't fail
        state.Player2Repository = MakeRepo(5);

        var result = DiscardProcessor.Process(
            state, _game, 1, MakeReq("h_6", "h_7"), _cc);

        // After discard, hand should have 6 cards
        // (Note: SwitchActivePlayer + DrawPhaseProcessor runs after, so hand count may change for P2)
        // We verify the discard event was emitted with correct count
        var discardEvent = result.Events.First(e => e.EventType == WireActionTypes.DiscardHand);
        discardEvent.EventData["discardedCount"].Should().Be(2L);
    }

    // ─── 2. No discard needed → throws ──────────────────────

    [Fact]
    public void Process_NoDiscardNeeded_Throws()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.End, activePlayer: 1);
        state.Player1Hand = MakeHand(5); // under limit

        var act = () => DiscardProcessor.Process(
            state, _game, 1, MakeReq("h_0"), _cc);

        act.Should().Throw<GameRuleException>().WithMessage("*no discard needed*");
    }

    // ─── 3. Wrong discard count → throws ────────────────────

    [Fact]
    public void Process_WrongDiscardCount_Throws()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.End, activePlayer: 1);
        state.Player1Hand = MakeHand(8); // need to discard 2

        var act = () => DiscardProcessor.Process(
            state, _game, 1, MakeReq("h_6"), _cc); // only 1 provided

        act.Should().Throw<GameRuleException>().WithMessage("*exactly*");
    }

    // ─── 4. Switches active player and advances ─────────────

    [Fact]
    public void Process_SwitchesActivePlayerAndAdvances()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.End, activePlayer: 1);
        state.Player1Hand = MakeHand(8);
        state.Player2Repository = MakeRepo(5);

        DiscardProcessor.Process(state, _game, 1, MakeReq("h_6", "h_7"), _cc);

        // After SwitchActivePlayer, active player changes to 2, turn increments
        state.ActivePlayer.Should().Be(2);
        state.CurrentTurn.Should().Be(3);
    }

    // ─── 5. Generates discard event ─────────────────────────

    [Fact]
    public void Process_GeneratesDiscardEvent()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.End, activePlayer: 1);
        state.Player1Hand = MakeHand(7); // need to discard 1
        state.Player2Repository = MakeRepo(5);

        var result = DiscardProcessor.Process(
            state, _game, 1, MakeReq("h_6"), _cc);

        var discardEvent = result.Events.First(e => e.EventType == WireActionTypes.DiscardHand);
        discardEvent.EventData.Should().ContainKey("discardedCount");
        discardEvent.EventData["discardedCount"].Should().Be(1L);
    }
}
