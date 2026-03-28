using System.Collections.Concurrent;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
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
    private readonly ILogger<GameService> _logger;

    private INpcStrategy? _defaultNpcAI;
    private readonly ConcurrentDictionary<string, INpcStrategy> _npcStrategies = new();

    private const int MaxNPCIterations = 50;

    // TODO: Assembly version defaults to 1.0.0.0 locally. Ensure CI injects correct versions
    // via <Version> in .csproj, or switch to git SHA / environment variable.
    private static readonly string EngineVersion =
        typeof(GameEngine).Assembly.GetName().Version?.ToString() ?? "unknown";
    private static readonly string CardDataVersion =
        typeof(GameConstants).Assembly.GetName().Version?.ToString() ?? "unknown";

    public GameService(
        GameEngine engine,
        IGameRepository gameRepo,
        ICardCache cardCache,
        ILogger<GameService> logger)
    {
        _engine = engine;
        _gameRepo = gameRepo;
        _cardCache = cardCache;
        _logger = logger;
    }

    /// <summary>
    /// Sets the default NPC AI strategy. Called once at startup.
    /// </summary>
    public void SetNpcAI(INpcStrategy ai) => _defaultNpcAI = ai;

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
            EngineVersion, CardDataVersion, ct);

        var game = await _gameRepo.GetGame(gameID, ct)
            ?? throw new InvalidOperationException($"created game {gameID} not found");

        await PostCreateAdvance(game, ct);
        return game;
    }

    /// <summary>
    /// Creates a new NPC game with fully initialized state.
    /// </summary>
    public async Task<Game> StartNPCBattle(
        string playerID, long deckID, List<DeckSnapshotCard> playerCards, string npcFaction,
        CancellationToken ct = default)
    {
        if (!playerCards.Any())
        {
            throw new InvalidOperationException("deck is empty");
        }

        var npcDeck = NpcDecks.GetDeck(npcFaction)
            ?? throw new InvalidOperationException($"unknown NPC faction: {npcFaction}");

        var deck1 = new DeckSnapshot { DeckID = deckID.ToString(), Cards = playerCards };
        var deck2 = new DeckSnapshot { DeckID = $"npc-{npcFaction}", Cards = [.. npcDeck.Cards] };

        long firstPlayer = Random.Shared.Next(2) == 0 ? 1 : 2;

        // Create faction-specific AI for this game
        INpcStrategy? factionAI = _engine.EffectRegistry is not null
            ? FactionAi.GetFactionAi(npcFaction, _cardCache, _engine.EffectRegistry)
            : null;

        var gameID = await _engine.CreateNewGame(
            playerID, NpcConstants.PlayerId, deck1, deck2, firstPlayer,
            EngineVersion, CardDataVersion, ct);

        if (factionAI is not null)
        {
            _npcStrategies[gameID] = factionAI;
        }

        var game = await _gameRepo.GetGame(gameID, ct)
            ?? throw new InvalidOperationException($"created game {gameID} not found");

        await PostCreateAdvance(game, ct);
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
        var result = await _engine.ProcessAction(gameID, playerID, actionType, actionData, ct);
        var allEvents = result.Events
            .Select(e => new ActionEventWithState { Event = e })
            .ToList();

        if (result.GameOver is { } over)
        {
            var state = await GetStateForPlayer(gameID, playerID, ct);
            return new GameActionResult
            {
                GameOver = over,
                State = state,
                Events = allEvents,
            };
        }

        var (npcEvents, npcGameOver) = await RunNPCTurnIfNeeded(gameID, playerID, ct);
        allEvents.AddRange(npcEvents);

        var clientState = await GetStateForPlayer(gameID, playerID, ct);
        return new GameActionResult { GameOver = npcGameOver, State = clientState, Events = allEvents };
    }

    // ─── State queries ──────────────────────────────────────────

    public Task<ClientGameState?> GetGameStateForPlayer(
        string gameID, string playerID, CancellationToken ct = default)
        => GetStateForPlayer(gameID, playerID, ct);

    public async Task<TurnControls?> GetTurnControlsForPlayer(
        string gameID, string playerID, CancellationToken ct = default)
    {
        var game = await _gameRepo.GetGame(gameID, ct);
        if (game is null)
        {
            return null;
        }

        var state = await _gameRepo.GetGameState(gameID, ct);
        if (state is null)
        {
            return null;
        }

        long playerNum;
        if (playerID == game.Player1ID)
        {
            playerNum = 1;
        }
        else if (playerID == game.Player2ID)
        {
            playerNum = 2;
        }
        else
        {
            return null;
        }

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
        var (npcEvents, npcGameOver) = await RunNPCTurnIfNeeded(gameID, playerID, ct);
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

    private async Task PostCreateAdvance(Game game, CancellationToken ct)
    {
        await _engine.RunAutoAdvance(game.GameID, ct);
    }

    private async Task<ClientGameState?> GetStateForPlayer(
        string gameID, string playerID, CancellationToken ct)
    {
        var game = await _gameRepo.GetGame(gameID, ct);
        if (game is null)
        {
            return null;
        }

        var state = await _gameRepo.GetGameState(gameID, ct);
        if (state is null)
        {
            return null;
        }

        long playerNum;
        if (playerID == game.Player1ID)
        {
            playerNum = 1;
        }
        else if (playerID == game.Player2ID)
        {
            playerNum = 2;
        }
        else
        {
            return null;
        }

        return GameStateView.Build(state, game, playerNum, _cardCache, _engine.EffectRegistry);
    }

    private async Task<(List<ActionEventWithState> Events, GameOverResult? GameOver)> RunNPCTurnIfNeeded(
        string gameID, string? stateForPlayerID, CancellationToken ct)
    {
        var npcEvents = new List<ActionEventWithState>();
        if (!_npcStrategies.TryGetValue(gameID, out var npcAI))
        {
            npcAI = _defaultNpcAI;
        }
        if (npcAI is null)
        {
            return (npcEvents, null);
        }

        for (int i = 0; i < MaxNPCIterations; i++)
        {
            var game = await _gameRepo.GetGame(gameID, ct);
            if (game is null || game.Status == GameStatus.Finished)
            {
                return (npcEvents, null);
            }

            var state = await _gameRepo.GetGameState(gameID, ct);
            if (state is null)
            {
                return (npcEvents, null);
            }

            // Determine NPC player number
            long npcPlayerNum;
            if (game.Player1ID == NpcConstants.PlayerId)
            {
                npcPlayerNum = 1;
            }
            else if (game.Player2ID == NpcConstants.PlayerId)
            {
                npcPlayerNum = 2;
            }
            else
            {
                return (npcEvents, null); // No NPC in this game
            }

            if (state.ActivePlayer != npcPlayerNum)
            {
                return (npcEvents, null);
            }

            // Compute available actions
            var myField = state.GetField(npcPlayerNum);
            var oppField = state.GetField(state.OpponentOf(npcPlayerNum));
            var hand = state.GetHand(npcPlayerNum);
            var budget = state.GetBudget(npcPlayerNum);
            var insightPool = state.GetInsightPool(npcPlayerNum);
            var available = AvailableActions.GetAllAvailableActions(
                state,
                myField, oppField, hand, budget, insightPool,
                _cardCache, _engine.EffectRegistry);

            List<NpcAction> actions;
            switch (state.CurrentPhase)
            {
                case Phase.Main:
                    actions = npcAI.DecideMainPhaseActions(state, game, npcPlayerNum, available);
                    break;
                case Phase.Battle:
                    actions = npcAI.DecideBattlePhaseActions(state, game, npcPlayerNum, available);
                    break;
                case Phase.End:
                    var ids = npcAI.DecideDiscard(state, npcPlayerNum);
                    if (!ids.Any())
                    {
                        _logger.LogWarning("NPC in end phase but no discard needed (game={GameID})", gameID);
                        return (npcEvents, null);
                    }
                    actions =
                    [
                        new NpcAction
                        {
                            ActionType = "discard_hand",
                            Data = new Dictionary<string, object> { ["cardInstanceIds"] = ids },
                        }
                    ];
                    break;
                default:
                    return (npcEvents, null);
            }

            foreach (var action in actions)
            {
                try
                {
                    var actionType = EnumExtensions.ParseActionType(action.ActionType);
                    var actionData = DeserializeNpcActionData(actionType, action.Data);
                    var result = await _engine.ProcessAction(
                        gameID, NpcConstants.PlayerId, actionType, actionData, ct);

                    // 1つのアクションから複数イベントが生成される場合、全イベントにアクション完了後の
                    // 同一スナップショットを付与する（中間状態は取得しない）
                    ClientGameState? snapshot = null;
                    if (stateForPlayerID is not null)
                    {
                        snapshot = await GetStateForPlayer(gameID, stateForPlayerID, ct);
                    }

                    foreach (var evt in result.Events)
                    {
                        npcEvents.Add(new ActionEventWithState { Event = evt, State = snapshot });
                    }

                    if (result.GameOver is not null)
                    {
                        _npcStrategies.TryRemove(gameID, out _);
                        return (npcEvents, result.GameOver);
                    }
                }
                catch (GameRuleException ex)
                {
                    // NPC chose an invalid action — expected, log and skip
                    _logger.LogWarning(ex, "NPC action rejected (game={GameID}, action={Action})", gameID, action.ActionType);
                }
                catch (Exception ex)
                {
                    // Deserialization or infrastructure failure — this is a bug, stop the turn
                    _logger.LogError(ex, "NPC action failed unexpectedly (game={GameID}, action={Action})", gameID, action.ActionType);
                    break;
                }
            }
        }

        _logger.LogError("NPC turn exceeded {Max} iterations (game={GameID})", MaxNPCIterations, gameID);
        return (npcEvents, null);
    }

    private static readonly JsonSerializerOptions NpcJsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private static object DeserializeNpcActionData(ActionType actionType, Dictionary<string, object> data)
    {
        var json = JsonSerializer.SerializeToElement(data, NpcJsonOpts);
        return actionType switch
        {
            ActionType.PlayCard => json.Deserialize<PlayCardRequest>(NpcJsonOpts)!,
            ActionType.Attack => json.Deserialize<AttackRequest>(NpcJsonOpts)!,
            ActionType.ScaleUp => json.Deserialize<ScaleUpRequest>(NpcJsonOpts)!,
            ActionType.Monetize => json.Deserialize<MonetizeRequest>(NpcJsonOpts)!,
            ActionType.DiscardHand => json.Deserialize<DiscardHandRequest>(NpcJsonOpts)!,
            ActionType.ActivateEffect => json.Deserialize<ActivateEffectRequest>(NpcJsonOpts)!,
            ActionType.Migrate => json.Deserialize<MigrateRequest>(NpcJsonOpts)!,
            ActionType.EndPhase => new object(),
            ActionType.SetReactive => new object(),
            ActionType.Forfeit => new object(),
            _ => throw new ArgumentException($"unknown action type: {actionType}"),
        };
    }
}
