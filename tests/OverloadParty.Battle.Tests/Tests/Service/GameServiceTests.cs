using Microsoft.Extensions.Logging;
using OverloadParty.Battle.Data.Mock;
using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;
using OverloadParty.Battle.Npc;
using OverloadParty.Battle.Service;

namespace OverloadParty.Battle.Tests.Tests.Service;

public class GameServiceTests
{
    private readonly MockGameRepository _repo = new();
    private readonly TestCardCache _cc = new();
    private readonly GameEngine _engine;
    private readonly GameService _svc;

    public GameServiceTests()
    {
        _cc.Add(TestFactory.ComputeCard(cardId: "SH-0001", tp: 600, av: 1400, slaPenalty: 400, deployTurns: 0));
        _cc.Add(TestFactory.ComputeCard(cardId: "TEST-0002", tp: 800, av: 1600, slaPenalty: 500, deployTurns: 1, name: "SlowCompute"));
        _cc.Add(TestFactory.DataCard(cardId: "NT-0009"));
        _engine = new GameEngine(_repo, _cc);
        _svc = new GameService(_engine, _repo, _cc, NullLogger.Instance);
    }

    private List<DeckSnapshotCard> MakePlayerCards(string cardId = "SH-0001")
    {
        return Enumerable.Range(0, GameConstants.DeckSize)
            .Select(_ => new DeckSnapshotCard { CardId = cardId })
            .ToList();
    }

    // ─── CreateGameFromMatch ─────────────────────────────────

    [Fact]
    public async Task CreateGameFromMatch_CreatesGame_WithCorrectPlayers()
    {
        var cards = MakePlayerCards();
        var game = await _svc.CreateGameFromMatch("alice", 1, cards, "bob", 1, cards);

        game.Should().NotBeNull();
        game.Player1ID.Should().Be("alice");
        game.Player2ID.Should().Be("bob");
        game.Status.Should().Be(GameStatus.Playing);
        game.GameID.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task CreateGameFromMatch_InitializesState_InMainPhase()
    {
        var cards = MakePlayerCards();
        var game = await _svc.CreateGameFromMatch("alice", 1, cards, "bob", 1, cards);

        var state = await _repo.GetGameState(game.GameID);
        state.Should().NotBeNull();
        // After PostCreateAdvance, draw phase auto-advances to main
        state!.CurrentPhase.Should().Be(Phase.Main);
        state.CurrentTurn.Should().Be(1);
    }

    [Fact]
    public async Task CreateGameFromMatch_PlayersHaveInitialBudget()
    {
        var cards = MakePlayerCards();
        var game = await _svc.CreateGameFromMatch("alice", 1, cards, "bob", 1, cards);

        var state = await _repo.GetGameState(game.GameID);
        state!.Player1Budget.Should().Be(GameConstants.InitialBudget);
        state.Player2Budget.Should().Be(GameConstants.InitialBudget);
    }

    // ─── StartNPCBattle ─────────────────────────────────────

    [Fact]
    public async Task StartNPCBattle_CreatesGame_WithNpcPlayer()
    {
        var cards = MakePlayerCards();
        var game = await _svc.StartNPCBattle("player1", 1, cards, GameConstants.FactionSHE);

        game.Should().NotBeNull();
        game.Player1ID.Should().Be("player1");
        game.Player2ID.Should().Be(NpcConstants.PlayerId);
        game.Status.Should().Be(GameStatus.Playing);
    }

    [Fact]
    public async Task StartNPCBattle_EmptyDeck_Throws()
    {
        var act = () => _svc.StartNPCBattle("player1", 1, [], GameConstants.FactionSHE);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*empty*");
    }

    [Fact]
    public async Task StartNPCBattle_UnknownFaction_Throws()
    {
        var cards = MakePlayerCards();
        var act = () => _svc.StartNPCBattle("player1", 1, cards, "unknown_faction");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*unknown NPC faction*");
    }

    // ─── ProcessAction ──────────────────────────────────────

    [Fact]
    public async Task ProcessAction_PlayCard_ReturnsStateWithResult()
    {
        var cards = MakePlayerCards();
        var game = await _svc.CreateGameFromMatch("alice", 1, cards, "bob", 1, cards);

        var state = await _repo.GetGameState(game.GameID);
        var cardToPlay = state!.GetHand(state.ActivePlayer).First();
        string activePlayerID = state.ActivePlayer == 1 ? "alice" : "bob";

        var req = new PlayCardRequest
        {
            CardInstanceID = cardToPlay.InstanceID,
            Zone = GameConstants.ZoneFrontend,
            Index = 0,
        };

        var result = await _svc.ProcessAction(game.GameID, activePlayerID, ActionType.PlayCard, req);

        result.Should().NotBeNull();
        result.State.Should().NotBeNull();
        result.GameOver.Should().BeNull();
    }

    [Fact]
    public async Task ProcessAction_Forfeit_ReturnsGameOver()
    {
        var cards = MakePlayerCards();
        var game = await _svc.CreateGameFromMatch("alice", 1, cards, "bob", 1, cards);

        var state = await _repo.GetGameState(game.GameID);
        string activePlayerID = state!.ActivePlayer == 1 ? "alice" : "bob";

        var result = await _svc.ProcessAction(game.GameID, activePlayerID, ActionType.Forfeit, new object());

        result.Should().NotBeNull();
        result.GameOver.Should().NotBeNull();
        result.State.Should().NotBeNull();
    }

    [Fact]
    public async Task ProcessAction_EndPhase_ReturnsValidState()
    {
        var cards = MakePlayerCards();
        var game = await _svc.CreateGameFromMatch("alice", 1, cards, "bob", 1, cards);

        var state = await _repo.GetGameState(game.GameID);
        state!.CurrentPhase.Should().Be(Phase.Main);
        string activePlayerID = state.ActivePlayer == 1 ? "alice" : "bob";

        var result = await _svc.ProcessAction(game.GameID, activePlayerID, ActionType.EndPhase, new object());

        result.Should().NotBeNull();
        result.State.Should().NotBeNull();
        result.GameOver.Should().BeNull();
    }

    // ─── GetGameStateForPlayer ──────────────────────────────

    [Fact]
    public async Task GetGameStateForPlayer_ReturnsClientState()
    {
        var cards = MakePlayerCards();
        var game = await _svc.CreateGameFromMatch("alice", 1, cards, "bob", 1, cards);

        var clientState = await _svc.GetGameStateForPlayer(game.GameID, "alice");

        clientState.Should().NotBeNull();
        clientState!.GameID.Should().Be(game.GameID);
        clientState.MyView.Should().NotBeNull();
        clientState.OppView.Should().NotBeNull();
    }

    [Fact]
    public async Task GetGameStateForPlayer_UnknownPlayer_ReturnsNull()
    {
        var cards = MakePlayerCards();
        var game = await _svc.CreateGameFromMatch("alice", 1, cards, "bob", 1, cards);

        var clientState = await _svc.GetGameStateForPlayer(game.GameID, "charlie");

        clientState.Should().BeNull();
    }

    [Fact]
    public async Task GetGameStateForPlayer_UnknownGame_ReturnsNull()
    {
        var clientState = await _svc.GetGameStateForPlayer("nonexistent", "alice");

        clientState.Should().BeNull();
    }

    // ─── GetTurnControlsForPlayer ───────────────────────────

    [Fact]
    public async Task GetTurnControlsForPlayer_ActivePlayer_ReturnsControls()
    {
        var cards = MakePlayerCards();
        var game = await _svc.CreateGameFromMatch("alice", 1, cards, "bob", 1, cards);

        var state = await _repo.GetGameState(game.GameID);
        string activePlayerID = state!.ActivePlayer == 1 ? "alice" : "bob";

        var controls = await _svc.GetTurnControlsForPlayer(game.GameID, activePlayerID);

        controls.Should().NotBeNull();
        controls!.CanEndPhase.Should().BeTrue("main phase allows ending");
    }

    [Fact]
    public async Task GetTurnControlsForPlayer_InactivePlayer_ReturnsNull()
    {
        var cards = MakePlayerCards();
        var game = await _svc.CreateGameFromMatch("alice", 1, cards, "bob", 1, cards);

        var state = await _repo.GetGameState(game.GameID);
        string inactivePlayerID = state!.ActivePlayer == 1 ? "bob" : "alice";

        var controls = await _svc.GetTurnControlsForPlayer(game.GameID, inactivePlayerID);

        controls.Should().BeNull();
    }

    [Fact]
    public async Task GetTurnControlsForPlayer_UnknownPlayer_ReturnsNull()
    {
        var cards = MakePlayerCards();
        var game = await _svc.CreateGameFromMatch("alice", 1, cards, "bob", 1, cards);

        var controls = await _svc.GetTurnControlsForPlayer(game.GameID, "charlie");

        controls.Should().BeNull();
    }

    [Fact]
    public async Task GetTurnControlsForPlayer_UnknownGame_ReturnsNull()
    {
        var controls = await _svc.GetTurnControlsForPlayer("nonexistent", "alice");

        controls.Should().BeNull();
    }

    // ─── NullLogger stub ────────────────────────────────────

    private class NullLogger : ILogger<GameService>
    {
        public static readonly NullLogger Instance = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }
}
