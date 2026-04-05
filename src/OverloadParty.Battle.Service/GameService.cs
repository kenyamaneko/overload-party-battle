using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;
using OverloadParty.Battle.Npc;

namespace OverloadParty.Battle.Service;

/// <summary>
/// A game event paired with the post-action state snapshot for the requesting player.
/// </summary>
public class ActionEventWithState
{
    public required GameEvent Event { get; init; }
    /// <summary>
    /// Info-hidden state for the requesting player after this event was applied.
    /// Null for the player's own action events (gateway fetches opponent state separately).
    /// </summary>
    public ClientGameState? State { get; init; }
}

/// <summary>
/// Result of a player action.
/// </summary>
public class GameActionResult
{
    public GameOverResult? GameOver { get; init; }
    public ClientGameState? State { get; init; }
    public List<ActionEventWithState> Events { get; init; } = [];
}

/// <summary>
/// Unified facade for both PvP and NPC game operations.
/// </summary>
public class GameService
{
    private readonly GameEngine _engine;
    private readonly IGameRepository _gameRepo;
    private readonly ICardCache _cardCache;
    private readonly NpcRunner _npcRunner;
    private readonly Dictionary<string, AiConfig> _aiConfigs;

    private const int MaxNpcIterations = 50;

    private static readonly string EngineVersion =
        typeof(GameEngine).Assembly.GetName().Version?.ToString() ?? "unknown";
    private static readonly string CardDataVersion =
        typeof(GameConstants).Assembly.GetName().Version?.ToString() ?? "unknown";

    public GameService(
        GameEngine engine,
        IGameRepository gameRepo,
        ICardCache cardCache,
        NpcRunner npcRunner,
        Dictionary<string, AiConfig> aiConfigs)
    {
        _engine = engine;
        _gameRepo = gameRepo;
        _cardCache = cardCache;
        _npcRunner = npcRunner;
        _aiConfigs = aiConfigs;
    }

    // ─── Game creation ──────────────────────────────────────────

    /// <summary>
    /// Creates a new PvP game from matchmaking parameters (called by Gateway).
    /// </summary>
    public async Task<Game> CreateGameFromMatch(
        string player1ID, long player1Deck, List<DeckSnapshotCard> player1Cards,
        string player2ID, long player2Deck, List<DeckSnapshotCard> player2Cards,
        CancellationToken ct = default)
    {
        var deck1 = new DeckSnapshot { DeckID = player1Deck.ToString(), Cards = player1Cards };
        var deck2 = new DeckSnapshot { DeckID = player2Deck.ToString(), Cards = player2Cards };

        long firstPlayer = Random.Shared.Next(2) == 0 ? 1 : 2;

        var gameID = await _engine.CreateNewGame(
            player1ID, player2ID, deck1, deck2, firstPlayer,
            engineVersion: EngineVersion, cardDataVersion: CardDataVersion, ct: ct);

        var game = await _gameRepo.GetGame(gameID, ct)
            ?? throw new InvalidOperationException($"created game {gameID} not found");

        await _engine.RunAutoAdvance(game, ct);
        return game;
    }

    /// <summary>
    /// Creates a new NPC game with fully initialized state.
    /// </summary>
    public async Task<Game> StartNPCBattle(
        string playerID, long deckID, List<DeckSnapshotCard> playerCards, string npcModel,
        CancellationToken ct = default)
    {
        if (!playerCards.Any())
        {
            throw new InvalidOperationException("deck is empty");
        }

        if (!_aiConfigs.TryGetValue(npcModel, out var npcConfig))
        {
            throw new GameRuleException(
                $"No AI config found for '{npcModel}'. Available: [{string.Join(", ", _aiConfigs.Keys)}]");
        }

        var npcCards = npcConfig.Deck
            .SelectMany(e => Enumerable.Repeat(new DeckSnapshotCard { CardId = e.CardId }, e.Copies))
            .ToList();

        var deck1 = new DeckSnapshot { DeckID = deckID.ToString(), Cards = playerCards };
        var deck2 = new DeckSnapshot { DeckID = $"npc-{npcModel}", Cards = npcCards };

        long firstPlayer = Random.Shared.Next(2) == 0 ? 1 : 2;

        var gameID = await _engine.CreateNewGame(
            playerID, "", deck1, deck2, firstPlayer,
            npc2Model: npcModel,
            engineVersion: EngineVersion, cardDataVersion: CardDataVersion, ct: ct);

        var game = await _gameRepo.GetGame(gameID, ct)
            ?? throw new InvalidOperationException($"created game {gameID} not found");

        await _engine.RunAutoAdvance(game, ct);
        return game;
    }

    // ─── Actions ────────────────────────────────────────────────

    /// <summary>
    /// Processes a player action, then runs NPC turns if applicable.
    /// </summary>
    public async Task<GameActionResult> ProcessAction(
        string gameID, string playerID, ActionType actionType, object actionData,
        CancellationToken ct = default)
    {
        var game = await _gameRepo.GetGame(gameID, ct)
            ?? throw new GameRuleException($"game {gameID} not found");

        var playerNum = ResolveHumanPlayerNum(game, playerID);

        ActionResult result;
        if (actionType == ActionType.Forfeit)
        {
            var req = actionData as ForfeitRequest;
            var reason = ParseForfeitReason(req?.Reason);
            result = await _engine.Forfeit(game, playerNum, reason, ct);
        }
        else
        {
            result = await _engine.ProcessAction(game, playerNum, actionType, actionData, ct);
        }

        var allEvents = result.Events
            .Select(e =>
            {
                e.EventData = MapEventData(e, playerNum);
                return new ActionEventWithState { Event = e };
            })
            .ToList();

        if (result.GameOver is not null)
        {
            var state = await GetStateForPlayer(gameID, playerID, ct);
            return new GameActionResult { GameOver = result.GameOver, State = state, Events = allEvents };
        }

        if (game.Npc1Model is not null || game.Npc2Model is not null)
        {
            var (npcEvents, npcGameOver) = await RunNpcLoop(game, playerNum, gameID, playerID, ct);
            allEvents.AddRange(npcEvents);

            var state = await GetStateForPlayer(gameID, playerID, ct);
            return new GameActionResult { GameOver = npcGameOver, State = state, Events = allEvents };
        }

        var clientState = await GetStateForPlayer(gameID, playerID, ct);
        return new GameActionResult { State = clientState, Events = allEvents };
    }

    // ─── State queries ──────────────────────────────────────────

    public Task<ClientGameState> GetGameStateForPlayer(
        string gameID, string playerID, CancellationToken ct = default)
        => GetStateForPlayer(gameID, playerID, ct);

    public async Task<TurnControlsMessage?> GetTurnControlsForPlayer(
        string gameID, string playerID, CancellationToken ct = default)
    {
        var game = await _gameRepo.GetGame(gameID, ct)
            ?? throw new GameRuleException($"game {gameID} not found");

        var state = await _gameRepo.GetGameState(gameID, ct)
            ?? throw new InvalidOperationException($"game state {gameID} not found");

        var playerNum = ResolveHumanPlayerNum(game, playerID);

        if (state.ActivePlayer != playerNum)
        {
            return null;
        }

        var hand = state.GetHand(playerNum);
        return AvailableActions.ComputeTurnControls(state, hand);
    }

    /// <summary>
    /// Runs the NPC turn if the active player is an NPC.
    /// Called by the gateway after the human player enters the game,
    /// so that NPC action events can be delivered via WebSocket.
    /// </summary>
    public async Task<GameActionResult> AdvanceNpcTurn(
        string gameID, string playerID, CancellationToken ct = default)
    {
        var game = await _gameRepo.GetGame(gameID, ct)
            ?? throw new GameRuleException($"game {gameID} not found");

        if (game.Npc1Model is null && game.Npc2Model is null)
        {
            return new GameActionResult();
        }

        var playerNum = ResolveHumanPlayerNum(game, playerID);
        var (npcEvents, npcGameOver) = await RunNpcLoop(game, playerNum, gameID, playerID, ct);

        if (npcEvents.Count == 0)
        {
            return new GameActionResult();
        }

        var clientState = await GetStateForPlayer(gameID, playerID, ct);

        return new GameActionResult
        {
            GameOver = npcGameOver,
            State = clientState,
            Events = npcEvents,
        };
    }

    // ─── Private helpers ────────────────────────────────────────

    /// <summary>
    /// Runs NPC actions one at a time, building a ClientGameState snapshot after each action.
    /// ClientGameState is an independent object created by GameStateView.Build, so it is
    /// not affected by subsequent mutations to the underlying GameState.
    /// </summary>
    private async Task<(List<ActionEventWithState> Events, GameOverResult? GameOver)> RunNpcLoop(
        Game game, long viewerPlayerNum, string gameID, string playerID,
        CancellationToken ct)
    {
        var events = new List<ActionEventWithState>();

        for (int i = 0; i < MaxNpcIterations; i++)
        {
            game = await _gameRepo.GetGame(gameID, ct)
                ?? throw new InvalidOperationException($"game {gameID} lost during NPC loop");

            var npcResult = await _npcRunner.AdvanceOneAction(game, ct);
            if (npcResult.Events.Count == 0) break;

            var state = await GetStateForPlayer(gameID, playerID, ct);

            foreach (var evt in npcResult.Events)
            {
                evt.EventData = MapEventData(evt, viewerPlayerNum);
                events.Add(new ActionEventWithState { Event = evt, State = state });
            }

            if (npcResult.GameOver is not null)
            {
                return (events, npcResult.GameOver);
            }

            if (!npcResult.NpcPending) break;
        }

        return (events, null);
    }

    private static long ResolveHumanPlayerNum(Game game, string playerID)
    {
        try
        {
            return game.ResolvePlayerNum(playerID);
        }
        catch (ArgumentException ex)
        {
            throw new GameRuleException(ex.Message);
        }
    }

    private static WinReason ParseForfeitReason(string? reason) => reason switch
    {
        WinReasons.TurnTimeout => WinReason.TurnTimeout,
        WinReasons.Disconnect => WinReason.Disconnect,
        WinReasons.Surrender => WinReason.Surrender,
        null => throw new GameRuleException("forfeit reason is required"),
        _ => throw new GameRuleException($"unknown forfeit reason: {reason}"),
    };

    private async Task<ClientGameState> GetStateForPlayer(
        string gameID, string playerID, CancellationToken ct)
    {
        var game = await _gameRepo.GetGame(gameID, ct)
            ?? throw new InvalidOperationException($"game {gameID} not found");

        var state = await _gameRepo.GetGameState(gameID, ct)
            ?? throw new InvalidOperationException($"game state {gameID} not found");

        var playerNum = ResolveHumanPlayerNum(game, playerID);

        return GameStateView.Build(state, game, playerNum, _cardCache, _engine.EffectRegistry);
    }

    /// <summary>
    /// Maps view-dependent event data fields for a specific player.
    /// TurnStart: replaces internal active_player with player-relative is_my_turn.
    /// </summary>
    private static Dictionary<string, object>? MapEventData(GameEvent evt, long viewerPlayerNum)
    {
        if (evt.EventData is null) { return null; }
        if (evt.EventType != EventTypes.TurnStart) { return evt.EventData; }

        return new Dictionary<string, object>
        {
            ["turn"] = evt.EventData["turn"],
            ["is_my_turn"] = Convert.ToInt64(evt.EventData["active_player"]) == viewerPlayerNum,
        };
    }
}
