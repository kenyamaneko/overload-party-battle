using OverloadParty.Battle.Data.Mock;
using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Tests.Engine;

public class GameEngineTests
{
    private readonly MockGameRepository _repo = new();
    private readonly TestCardCache _cc = new();
    private readonly GameEngine _engine;

    public GameEngineTests()
    {
        _cc.Add(TestFactory.ComputeCard(cardNo: 1, tp: 600, av: 1400, slaPenalty: 400, deployTurns: 0));
        _cc.Add(TestFactory.ComputeCard(cardNo: 2, tp: 800, av: 1600, slaPenalty: 500, deployTurns: 1, name: "SlowCompute"));
        _cc.Add(TestFactory.DataCard(cardNo: 100));
        _engine = new GameEngine(_repo, _cc);
    }

    private DeckSnapshot MakeSingleCardDeck(long cardNo)
    {
        return TestFactory.MakeDeck(cardNo);
    }

    // ─── CreateNewGame ───────────────────────────────────────

    [Fact]
    public async Task CreateNewGame_ReturnsGameID_And_InitializesState()
    {
        var deck = MakeSingleCardDeck(1);

        var gameID = await _engine.CreateNewGame("p1", "p2", deck, deck, 1);

        gameID.Should().NotBeNullOrEmpty();

        var game = await _repo.GetGame(gameID);
        game.Should().NotBeNull();
        game!.Player1ID.Should().Be("p1");
        game.Player2ID.Should().Be("p2");
        game.Status.Should().Be(GameStatus.Playing);

        var state = await _repo.GetGameState(gameID);
        state.Should().NotBeNull();
        state!.CurrentTurn.Should().Be(1);
        state.CurrentPhase.Should().Be(Phase.Draw);
        state.ActivePlayer.Should().Be(1);
        state.Player1Budget.Should().Be(GameConstants.InitialBudget);
        state.Player2Budget.Should().Be(GameConstants.InitialBudget);

        // Each player should have initial hand cards
        state.Player1Hand.Should().HaveCount(GameConstants.InitialHandSize);
        state.Player2Hand.Should().HaveCount(GameConstants.InitialHandSize);

        // Repository should have remaining cards
        state.Player1Repository.Should().HaveCount(GameConstants.DeckSize - GameConstants.InitialHandSize);

        // All hand cards should reference the correct card
        state.Player1Hand.Should().AllSatisfy(h => h.CardID.Should().Be(1));
    }

    [Fact]
    public async Task CreateNewGame_FirstPlayer2_SetsActivePlayer2()
    {
        var deck = MakeSingleCardDeck(1);

        var gameID = await _engine.CreateNewGame("p1", "p2", deck, deck, 2);

        var state = await _repo.GetGameState(gameID);
        state!.ActivePlayer.Should().Be(2);
    }

    // ─── RunAutoAdvance ──────────────────────────────────────

    [Fact]
    public async Task RunAutoAdvance_DrawPhase_DrawsCardAndAdvancesToMain()
    {
        var deck = MakeSingleCardDeck(1);
        var gameID = await _engine.CreateNewGame("p1", "p2", deck, deck, 1);

        var state = await _repo.GetGameState(gameID);
        int handBefore = state!.Player1Hand.Count;
        int repoBefore = state.Player1Repository.Count;

        var result = await _engine.RunAutoAdvance(gameID);

        result.Should().BeNull("no game-over expected");

        // Re-read state (UpdateGameState mutates in place for MockGameRepository)
        state.Player1Hand.Should().HaveCount(handBefore + 1);
        state.Player1Repository.Should().HaveCount(repoBefore - 1);
        state.CurrentPhase.Should().Be(Phase.Main);
    }

    [Fact]
    public async Task RunAutoAdvance_NonExistentGame_Throws()
    {
        var act = () => _engine.RunAutoAdvance("nonexistent");

        await act.Should().ThrowAsync<GameRuleException>().WithMessage("*not found*");
    }

    // ─── ProcessAction: PlayCard ─────────────────────────────

    [Fact]
    public async Task ProcessAction_PlayCard_PlaysCardAndReturnsEvents()
    {
        var deck = MakeSingleCardDeck(1);
        var gameID = await _engine.CreateNewGame("p1", "p2", deck, deck, 1);

        // Advance past draw phase
        await _engine.RunAutoAdvance(gameID);

        var state = await _repo.GetGameState(gameID);
        state!.CurrentPhase.Should().Be(Phase.Main);

        var cardToPlay = state.Player1Hand.First();
        var req = new PlayCardRequest
        {
            CardInstanceID = cardToPlay.InstanceID,
            Zone = GameConstants.ZoneFrontend,
            Index = 0,
        };

        var result = await _engine.ProcessAction(
            gameID, "p1", ActionType.PlayCard, req);

        result.Should().NotBeNull();
        result.Events.Should().Contain(e => e.EventType == WireActionTypes.PlayCard);

        // Card removed from hand
        state.Player1Hand.Should().NotContain(h => h.InstanceID == cardToPlay.InstanceID);

        // Card placed on field (deployTurns=0 → face-up)
        state.Player1Field.Frontend[0].Should().NotBeNull();
        state.Player1Field.Frontend[0]!.FaceUp.Should().BeTrue();
    }

    // ─── ProcessAction: Attack ───────────────────────────────

    [Fact]
    public async Task ProcessAction_Attack_DealsDamageAndReturnsEvents()
    {
        var deck = MakeSingleCardDeck(1);
        var gameID = await _engine.CreateNewGame("p1", "p2", deck, deck, 1);

        // Set up the game state directly for attack testing
        var state = await _repo.GetGameState(gameID);

        // Place resources manually
        var attacker = TestFactory.MakeResource(cardId: 1, instanceId: "atk_1", faceUp: true);
        state!.Player1Field.Frontend[0] = attacker;

        var defender = TestFactory.MakeResource(cardId: 1, instanceId: "def_1", faceUp: true);
        state.Player2Field.Frontend[0] = defender;

        // Set to battle phase, turn 2+
        state.CurrentTurn = 2;
        state.CurrentPhase = Phase.Battle;
        state.ActivePlayer = 1;

        var req = new AttackRequest
        {
            AttackerInstanceID = "atk_1",
            TargetInstanceID = "def_1",
        };

        var result = await _engine.ProcessAction(
            gameID, "p1", ActionType.Attack, req);

        result.Should().NotBeNull();
        result.Events.Should().Contain(e => e.EventType == WireActionTypes.Attack);

        defender.Damage.Should().Be(600);
        attacker.HasAttacked.Should().BeTrue();

        // Events should be persisted
        var events = await _repo.GetEvents(gameID);
        events.Should().Contain(e => e.EventType == WireActionTypes.Attack);
    }

    // ─── ProcessAction: validation ───────────────────────────

    [Fact]
    public async Task ProcessAction_WrongPlayer_Throws()
    {
        var deck = MakeSingleCardDeck(1);
        var gameID = await _engine.CreateNewGame("p1", "p2", deck, deck, 1);
        await _engine.RunAutoAdvance(gameID);

        var state = await _repo.GetGameState(gameID);
        var cardToPlay = state!.Player2Hand.First();
        var req = new PlayCardRequest
        {
            CardInstanceID = cardToPlay.InstanceID,
            Zone = GameConstants.ZoneFrontend,
            Index = 0,
        };

        // Player 2 tries to act on player 1's turn
        var act = () => _engine.ProcessAction(
            gameID, "p2", ActionType.PlayCard, req);

        await act.Should().ThrowAsync<GameRuleException>().WithMessage("*not your turn*");
    }

    [Fact]
    public async Task ProcessAction_InvalidPlayer_Throws()
    {
        var deck = MakeSingleCardDeck(1);
        var gameID = await _engine.CreateNewGame("p1", "p2", deck, deck, 1);
        await _engine.RunAutoAdvance(gameID);

        var req = new PlayCardRequest
        {
            CardInstanceID = "any",
            Zone = GameConstants.ZoneFrontend,
            Index = 0,
        };

        var act = () => _engine.ProcessAction(
            gameID, "unknown_player", ActionType.PlayCard, req);

        await act.Should().ThrowAsync<GameRuleException>().WithMessage("*not in this game*");
    }

    [Fact]
    public async Task ProcessAction_Forfeit_EndsGameImmediately()
    {
        var deck = MakeSingleCardDeck(1);
        var gameID = await _engine.CreateNewGame("p1", "p2", deck, deck, 1);

        var result = await _engine.ProcessAction(
            gameID, "p1", ActionType.Forfeit, new object());

        result.GameOver.Should().NotBeNull();
        result.GameOver!.WinnerNum.Should().Be(2, "opponent wins on forfeit");

        var game = await _repo.GetGame(gameID);
        game!.Status.Should().Be(GameStatus.Finished);
        game.WinnerID.Should().Be("p2");
    }

    [Fact]
    public async Task ProcessAction_FinishedGame_Throws()
    {
        var deck = MakeSingleCardDeck(1);
        var gameID = await _engine.CreateNewGame("p1", "p2", deck, deck, 1);

        // Forfeit to finish the game
        await _engine.ProcessAction(gameID, "p1", ActionType.Forfeit, new object());

        // Trying to act on a finished game should throw
        var act = () => _engine.ProcessAction(
            gameID, "p2", ActionType.EndPhase, new object());

        await act.Should().ThrowAsync<GameRuleException>().WithMessage("*not in playing state*");
    }
}
