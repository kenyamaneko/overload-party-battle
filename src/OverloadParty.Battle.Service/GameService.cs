using System.Collections.Concurrent;
using System.Linq;
using Microsoft.Extensions.Logging;
using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Models;
using OverloadParty.Battle.Npc;

namespace OverloadParty.Battle.Service;

/// <summary>
/// Result of a player action.
/// </summary>
public class GameActionResult
{
    public GameOverResult? GameOver { get; init; }
    public ClientGameState? State { get; init; }
}

/// <summary>
/// Unified facade for both PvP and NPC game operations.
/// </summary>
public class GameService
{
    private readonly GameEngine _engine;
    private readonly IGameRepository _gameRepo;
    private readonly IDeckRepository _deckRepo;
    private readonly ICardCache _cardCache;
    private readonly ILogger<GameService> _logger;

    private INpcStrategy? _defaultNpcAI;
    private readonly ConcurrentDictionary<string, INpcStrategy> _npcStrategies = new();

    private const int MaxNPCIterations = 50;

    public GameService(
        GameEngine engine,
        IGameRepository gameRepo,
        IDeckRepository deckRepo,
        ICardCache cardCache,
        ILogger<GameService> logger)
    {
        _engine = engine;
        _gameRepo = gameRepo;
        _deckRepo = deckRepo;
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
    public async Task<Game> CreateGameFromMatch(string player1ID, long player1Deck, string player2ID, long player2Deck, CancellationToken ct = default)
    {
        var deck1Cards = await _deckRepo.GetDeckCardNos(player1ID, player1Deck, ct);
        var deck2Cards = await _deckRepo.GetDeckCardNos(player2ID, player2Deck, ct);

        var deck1 = new DeckSnapshot { DeckID = player1Deck.ToString(), Cards = deck1Cards };
        var deck2 = new DeckSnapshot { DeckID = player2Deck.ToString(), Cards = deck2Cards };

        long firstPlayer = Random.Shared.Next(2) == 0 ? 1 : 2;

        var gameID = await _engine.CreateNewGame(
            player1ID, player2ID, deck1, deck2, firstPlayer, ct);

        var game = await _gameRepo.GetGame(gameID, ct)
            ?? throw new InvalidOperationException($"created game {gameID} not found");

        await PostCreateAdvance(game, ct);
        return game;
    }

    /// <summary>
    /// Creates a new NPC game with fully initialized state.
    /// </summary>
    public async Task<Game> StartNPCBattle(
        string playerID, long deckID, string npcFaction, CancellationToken ct = default)
    {
        var playerCards = await _deckRepo.GetDeckCardNos(playerID, deckID, ct);
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
        INpcStrategy? factionAI = null;
        if (_engine.EffectRegistry is EffectRegistry reg)
        {
            factionAI = FactionAi.GetFactionAi(npcFaction, _cardCache, reg);
        }

        var gameID = await _engine.CreateNewGame(
            playerID, NpcConstants.PlayerId, deck1, deck2, firstPlayer, ct);

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

        if (result.GameOver is { } over)
        {
            var state = await GetStateForPlayer(gameID, playerID, ct);
            return new GameActionResult
            {
                GameOver = over,
                State = state,
            };
        }

        await RunNPCTurnIfNeeded(gameID, ct);

        var clientState = await GetStateForPlayer(gameID, playerID, ct);
        return new GameActionResult { State = clientState };
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

    // ─── Private helpers ────────────────────────────────────────

    private async Task PostCreateAdvance(Game game, CancellationToken ct)
    {
        var result = await _engine.RunAutoAdvance(game.GameID, ct);

        if (result is null)
        {
            await RunNPCTurnIfNeeded(game.GameID, ct);
        }
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

    private async Task RunNPCTurnIfNeeded(string gameID, CancellationToken ct)
    {
        if (!_npcStrategies.TryGetValue(gameID, out var npcAI))
        {
            npcAI = _defaultNpcAI;
        }
        if (npcAI is null)
        {
            return;
        }

        for (int i = 0; i < MaxNPCIterations; i++)
        {
            var game = await _gameRepo.GetGame(gameID, ct);
            if (game is null || game.Status == GameStatus.Finished)
            {
                return;
            }

            var state = await _gameRepo.GetGameState(gameID, ct);
            if (state is null)
            {
                return;
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
                return; // No NPC in this game
            }

            if (state.ActivePlayer != npcPlayerNum)
            {
                return;
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
                        return;
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
                    return;
            }

            foreach (var action in actions)
            {
                try
                {
                    var actionType = EnumExtensions.ParseActionType(action.ActionType);
                    var result = await _engine.ProcessAction(
                        gameID, NpcConstants.PlayerId, actionType, action.Data, ct);

                    if (result.GameOver is not null)
                    {
                        _npcStrategies.TryRemove(gameID, out _);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "NPC action failed (game={GameID}, action={Action})", gameID, action.ActionType);
                }
            }
        }

        _logger.LogWarning("NPC turn exceeded {Max} iterations (game={GameID})", MaxNPCIterations, gameID);
    }
}
