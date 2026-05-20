using Microsoft.Extensions.Logging;
using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;
using OverloadParty.Battle.Npc;
using OverloadParty.Battle.Service;
using OverloadParty.Battle.Tests.Fakes;

namespace OverloadParty.Battle.Tests.Service;

public class GameServiceTests
{
    private readonly FakeGameRepository _repo = new();
    private readonly TestCardCache _cc = new();
    private readonly GameEngine _engine;
    private readonly GameService _svc;

    public GameServiceTests()
    {
        _cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", tp: 600, av: 1400, slaPenalty: 400, deployTurns: 0));
        _cc.Add(TestFactory.ComputeCard(cardId: "TEST-0002", tp: 800, av: 1600, slaPenalty: 500, deployTurns: 1, name: "SlowCompute"));
        _cc.Add(TestFactory.DataCard(cardId: "TST-0002"));
        _engine = new GameEngine(_repo, _cc);
        var npcDeck = Enumerable.Range(0, InitialValues.DeckSize)
            .Select(_ => new DeckEntry { CardId = "TST-0001", Copies = 1 })
            .ToList();
        var aiConfigs = new Dictionary<string, AiConfig>
        {
            [Factions.SHE] = new AiConfig { Model = Factions.SHE, Faction = Factions.SHE, Deck = npcDeck },
        };
        var npcRunner = new NpcRunner(_engine, _repo, _cc, aiConfigs, NullNpcLogger.Instance);
        _svc = new GameService(_engine, _repo, _cc, npcRunner, aiConfigs);
    }

    private List<DeckSnapshotCard> MakePlayerCards(string cardId = "TST-0001")
    {
        return Enumerable.Range(0, InitialValues.DeckSize)
            .Select(_ => new DeckSnapshotCard { CardId = cardId })
            .ToList();
    }

    private static readonly List<PlayerSummarySnapshot> DefaultPlayerSummaries =
    [
        new() { PlayerNum = 1, Name = "p1", Level = 1 },
        new() { PlayerNum = 2, Name = "p2", Level = 1 },
    ];

    private static readonly List<PlayerSummarySnapshot> NpcPlayerSummaries =
    [
        new() { PlayerNum = 1, Name = "p1", Level = 1 },
        new() { PlayerNum = 2, Name = "SHE 配達員", Level = null },
    ];

    // ─── CreateGameFromMatch ─────────────────────────────────

    [Fact]
    public async Task CreateGameFromMatch_CreatesGame_WithCorrectStatus()
    {
        var cards = MakePlayerCards();
        var game = await _svc.CreateGameFromMatch(cards, cards, DefaultPlayerSummaries);

        game.Should().NotBeNull();
        game.Status.Should().Be(GameStatus.Playing);
        game.GameID.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task CreateGameFromMatch_PersistsPlayerSummaries()
    {
        var cards = MakePlayerCards();
        var summaries = new List<PlayerSummarySnapshot>
        {
            new() { PlayerNum = 1, Name = "alice", Level = 7 },
            new() { PlayerNum = 2, Name = "bob", Level = 12 },
        };

        var game = await _svc.CreateGameFromMatch(cards, cards, summaries);

        var persisted = await _repo.GetPlayerSummaries(game.GameID);
        persisted.Should().HaveCount(2);
        persisted.Should().Contain(s => s.PlayerNum == 1 && s.Name == "alice" && s.Level == 7);
        persisted.Should().Contain(s => s.PlayerNum == 2 && s.Name == "bob" && s.Level == 12);
    }

    [Fact]
    public async Task CreateGameFromMatch_InitializesState_InMainPhase()
    {
        var cards = MakePlayerCards();
        var game = await _svc.CreateGameFromMatch(cards, cards, DefaultPlayerSummaries);

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
        var game = await _svc.CreateGameFromMatch(cards, cards, DefaultPlayerSummaries);

        var state = await _repo.GetGameState(game.GameID);
        state!.Player1Budget.Should().Be(BattleConstants.InitialBudget);
        state.Player2Budget.Should().Be(BattleConstants.InitialBudget);
    }

    // ─── StartNPCBattle ─────────────────────────────────────

    [Fact]
    public async Task StartNPCBattle_CreatesGame_WithNpcPlayer()
    {
        var cards = MakePlayerCards();
        var game = await _svc.StartNPCBattle(cards, Factions.SHE, NpcPlayerSummaries);

        game.Should().NotBeNull();
        game.Npc2Model.Should().NotBeNull();
        game.Status.Should().Be(GameStatus.Playing);
    }

    [Fact]
    public async Task StartNPCBattle_EmptyDeck_Throws()
    {
        var act = () => _svc.StartNPCBattle([], "SHE-easy", NpcPlayerSummaries);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*empty*");
    }

    [Fact]
    public async Task StartNPCBattle_UnknownFaction_Throws()
    {
        var cards = MakePlayerCards();
        var act = () => _svc.StartNPCBattle(cards, "unknown_faction", NpcPlayerSummaries);

        await act.Should().ThrowAsync<GameRuleException>()
            .WithMessage("*No AI config found*");
    }

    // ─── ProcessAction ──────────────────────────────────────

    [Fact]
    public async Task ProcessAction_PlayCard_ReturnsStateWithResult()
    {
        var cards = MakePlayerCards();
        var game = await _svc.CreateGameFromMatch(cards, cards, DefaultPlayerSummaries);

        var state = await _repo.GetGameState(game.GameID);
        var cardToPlay = state!.GetHand(state.ActivePlayer).First();

        var req = new PlayCardRequest
        {
            CardInstanceID = cardToPlay.InstanceID,
            Zone = Zones.Frontend,
            Index = 0,
        };

        var result = await _svc.ProcessAction(game.GameID, state.ActivePlayer, ActionType.PlayCard, req);

        result.Should().NotBeNull();
        result.State.Should().NotBeNull();
        result.GameOver.Should().BeNull();
    }

    [Fact]
    public async Task ProcessAction_Forfeit_ReturnsGameOver()
    {
        var cards = MakePlayerCards();
        var game = await _svc.CreateGameFromMatch(cards, cards, DefaultPlayerSummaries);

        var state = await _repo.GetGameState(game.GameID);

        var result = await _svc.ProcessAction(game.GameID, state!.ActivePlayer, ActionType.Forfeit, new ForfeitRequest { Reason = WinReasons.Surrender });

        result.Should().NotBeNull();
        result.GameOver.Should().NotBeNull();
        result.State.Should().NotBeNull();
    }

    [Fact]
    public async Task ProcessAction_EndPhase_ReturnsValidState()
    {
        var cards = MakePlayerCards();
        var game = await _svc.CreateGameFromMatch(cards, cards, DefaultPlayerSummaries);

        var state = await _repo.GetGameState(game.GameID);
        state!.CurrentPhase.Should().Be(Phase.Main);

        var result = await _svc.ProcessAction(game.GameID, state.ActivePlayer, ActionType.EndPhase, new object());

        result.Should().NotBeNull();
        result.State.Should().NotBeNull();
        result.GameOver.Should().BeNull();
    }

    [Fact]
    public async Task ProcessAction_PvpGame_NpcPendingAlwaysFalse()
    {
        var cards = MakePlayerCards();
        var game = await _svc.CreateGameFromMatch(cards, cards, DefaultPlayerSummaries);

        var state = await _repo.GetGameState(game.GameID);

        var result = await _svc.ProcessAction(
            game.GameID, state!.ActivePlayer, ActionType.EndPhase, new object());

        result.NpcPending.Should().BeFalse("PvP games never have NPC pending");
    }

    [Fact]
    public async Task ProcessAction_TurnStartEvent_ContainsIsMyTurn()
    {
        var cards = MakePlayerCards();
        var game = await _svc.CreateGameFromMatch(cards, cards, DefaultPlayerSummaries);

        var state = await _repo.GetGameState(game.GameID);

        // Turn 1 Main → end_phase skips Battle (first turn), switches turn directly
        var result = await _svc.ProcessAction(game.GameID, state!.ActivePlayer, ActionType.EndPhase, new object());

        var turnStartEvent = result.Events
            .FirstOrDefault(e => e.Event.EventType == EventTypes.TurnStart);
        turnStartEvent.Should().NotBeNull("turn switch should emit a turn_start event");
        turnStartEvent!.Event.EventData.Should().BeOfType<TurnStartEventData>()
            .Which.IsMyTurn.Should().BeFalse(
                "active player switched, so the requesting player's turn is over");
    }

    // ─── GetGameStateForPlayer ──────────────────────────────

    [Fact]
    public async Task GetGameStateForPlayer_ReturnsClientState()
    {
        var cards = MakePlayerCards();
        var game = await _svc.CreateGameFromMatch(cards, cards, DefaultPlayerSummaries);

        var clientState = await _svc.GetGameStateForPlayer(game.GameID, 1);

        clientState.Should().NotBeNull();
        clientState!.GameID.Should().Be(game.GameID);
        clientState.MyView.Should().NotBeNull();
        clientState.OppView.Should().NotBeNull();
    }

    [Fact]
    public async Task GetGameStateForPlayer_UnknownGame_Throws()
    {
        var act = () => _svc.GetGameStateForPlayer("nonexistent", 1);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ─── GetTurnControlsForPlayer ───────────────────────────

    [Fact]
    public async Task GetTurnControlsForPlayer_ActivePlayer_ReturnsControls()
    {
        var cards = MakePlayerCards();
        var game = await _svc.CreateGameFromMatch(cards, cards, DefaultPlayerSummaries);

        var state = await _repo.GetGameState(game.GameID);

        var controls = await _svc.GetTurnControlsForPlayer(game.GameID, state!.ActivePlayer);

        controls.Should().NotBeNull();
        controls!.CanEndPhase.Should().BeTrue("main phase allows ending");
    }

    [Fact]
    public async Task GetTurnControlsForPlayer_InactivePlayer_ReturnsNull()
    {
        var cards = MakePlayerCards();
        var game = await _svc.CreateGameFromMatch(cards, cards, DefaultPlayerSummaries);

        var state = await _repo.GetGameState(game.GameID);
        long inactivePlayerNum = state!.ActivePlayer == 1 ? 2 : 1;

        var controls = await _svc.GetTurnControlsForPlayer(game.GameID, inactivePlayerNum);

        controls.Should().BeNull();
    }

    [Fact]
    public async Task GetTurnControlsForPlayer_UnknownGame_Throws()
    {
        var act = () => _svc.GetTurnControlsForPlayer("nonexistent", 1);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ─── NullLogger stub ────────────────────────────────────

    private class NullNpcLogger : ILogger<NpcRunner>
    {
        public static readonly NullNpcLogger Instance = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }
}
