using System.Linq;
using Microsoft.Extensions.Logging;
using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Matchmaking;
using OverloadParty.Battle.Models;
using OverloadParty.Battle.Npc;

namespace OverloadParty.Battle.Service;

/// <summary>
/// Display info for a player in a game.
/// </summary>
public class BattlePlayerInfo
{
    public required string PlayerID { get; init; }
    public string Name { get; init; } = "";
    public long Level { get; init; }
}

/// <summary>
/// Emitted for banner-related events (battle_start, turn_start).
/// </summary>
public class BattleEvent
{
    public string Type { get; init; } = "";
    // battle_start fields
    public BattlePlayerInfo? Player1Info { get; init; }
    public BattlePlayerInfo? Player2Info { get; init; }
    public string? MatchType { get; init; }
    // turn_start fields
    public long Turn { get; init; }
    public long ActivePlayer { get; init; }
}

/// <summary>
/// Result of a player action.
/// </summary>
public class GameActionResult
{
    public bool GameOver { get; init; }
    public long WinnerNum { get; init; }
    public string? WinReason { get; init; }
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
    private readonly MatchQueue _queue;
    private readonly PlayerService? _playerService;
    private readonly ILogger<GameService> _logger;

    private INpcStrategy? _npcAI;
    private Action<string, string, Dictionary<string, object>?>? _actionObserver;
    private Action<string, BattleEvent>? _battleEventObserver;

    private const int MaxNPCIterations = 50;

    private static readonly Dictionary<string, string> NpcDisplayNames = new()
    {
        ["SD"] = "Smile Delivery",
        ["Tenki"] = "天気使い",
        ["Sugar"] = "しゅがーLab",
        ["Tuners"] = "調律部",
    };
    private const long NpcDisplayLevel = 50;

    public GameService(
        GameEngine engine,
        IGameRepository gameRepo,
        IDeckRepository deckRepo,
        ICardCache cardCache,
        MatchQueue queue,
        ILogger<GameService> logger,
        PlayerService? playerService = null)
    {
        _engine = engine;
        _gameRepo = gameRepo;
        _deckRepo = deckRepo;
        _cardCache = cardCache;
        _queue = queue;
        _logger = logger;
        _playerService = playerService;
    }

    /// <summary>
    /// Sets the NPC AI strategy. Called once at startup.
    /// </summary>
    public void SetNpcAI(INpcStrategy ai) => _npcAI = ai;

    public void SetActionObserver(Action<string, string, Dictionary<string, object>?> obs) => _actionObserver = obs;
    public void SetBattleEventObserver(Action<string, BattleEvent> obs) => _battleEventObserver = obs;

    // ─── Matchmaking ────────────────────────────────────────────

    public async Task JoinQueue(string playerID, long deckID, CancellationToken ct = default)
    {
        if (_playerService is not null)
            await _playerService.CheckAndIncrementBattleCount(playerID, ct);
        _queue.Join(playerID, deckID);
    }

    public void LeaveQueue(string playerID) => _queue.Leave(playerID);

    // ─── Game creation ──────────────────────────────────────────

    /// <summary>
    /// Creates a new PvP game from a matchmaking result.
    /// </summary>
    public async Task<Game> CreateGameFromMatch(MatchResult result, CancellationToken ct = default)
    {
        var deck1Cards = await _deckRepo.GetDeckCardNos(result.Player1ID, result.Player1Deck, ct);
        var deck2Cards = await _deckRepo.GetDeckCardNos(result.Player2ID, result.Player2Deck, ct);

        var deck1 = new DeckSnapshot { DeckID = result.Player1Deck.ToString(), Cards = deck1Cards };
        var deck2 = new DeckSnapshot { DeckID = result.Player2Deck.ToString(), Cards = deck2Cards };

        long firstPlayer = Random.Shared.Next(2) == 0 ? 1 : 2;

        var gameID = await _engine.CreateNewGame(
            result.Player1ID, result.Player2ID, deck1, deck2, firstPlayer, ct);

        var game = await _gameRepo.GetGame(gameID, ct)
            ?? throw new InvalidOperationException($"created game {gameID} not found");

        await PostCreateAdvance(game, "pvp", ct);
        return game;
    }

    /// <summary>
    /// Creates a new NPC game with fully initialized state.
    /// </summary>
    public async Task<Game> StartNPCBattle(
        string playerID, long deckID, string npcFaction, CancellationToken ct = default)
    {
        if (_playerService is not null)
            await _playerService.CheckAndIncrementBattleCount(playerID, ct);

        var playerCards = await _deckRepo.GetDeckCardNos(playerID, deckID, ct);
        if (!playerCards.Any())
            throw new InvalidOperationException("deck is empty");

        var npcDeck = NpcDecks.GetDeck(npcFaction)
            ?? throw new InvalidOperationException($"unknown NPC faction: {npcFaction}");

        var deck1 = new DeckSnapshot { DeckID = deckID.ToString(), Cards = playerCards };
        var deck2 = new DeckSnapshot { DeckID = $"npc-{npcFaction}", Cards = [.. npcDeck.Cards] };

        long firstPlayer = Random.Shared.Next(2) == 0 ? 1 : 2;

        // Create faction-specific AI
        if (_engine.EffectRegistry is EffectRegistry reg)
            _npcAI = FactionAi.GetFactionAi(npcFaction, _cardCache, reg);

        var gameID = await _engine.CreateNewGame(
            playerID, NpcConstants.PlayerId, deck1, deck2, firstPlayer, ct);

        var game = await _gameRepo.GetGame(gameID, ct)
            ?? throw new InvalidOperationException($"created game {gameID} not found");

        await PostCreateAdvance(game, "npc", ct);
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
        var prevState = await _gameRepo.GetGameState(gameID, ct);
        var prevTurn = prevState?.CurrentTurn ?? 0;

        var result = await _engine.ProcessAction(gameID, playerID, actionType, actionData, ct);

        if (result.GameOver)
        {
            await FinishGame(gameID, result.WinnerNum, ct);
            var state = await GetStateForPlayer(gameID, playerID, ct);
            return new GameActionResult
            {
                GameOver = true,
                WinnerNum = result.WinnerNum,
                WinReason = result.WinReason,
                State = state,
            };
        }

        // Emit turn_start if the turn changed
        var postState = await _gameRepo.GetGameState(gameID, ct);
        if (postState is not null && postState.CurrentTurn != prevTurn)
        {
            NotifyBattleEvent(gameID, new BattleEvent
            {
                Type = "turn_start",
                Turn = postState.CurrentTurn,
                ActivePlayer = postState.ActivePlayer,
            });
        }

        await RunNPCTurnIfNeeded(gameID, ct);

        var clientState = await GetStateForPlayer(gameID, playerID, ct);
        return new GameActionResult { State = clientState };
    }

    // ─── State queries ──────────────────────────────────────────

    public Task<ClientGameState?> GetGameStateForPlayer(
        string gameID, string playerID, CancellationToken ct = default)
        => GetStateForPlayer(gameID, playerID, ct);

    // ─── Post-game ──────────────────────────────────────────────

    public async Task FinishGame(string gameID, long winnerNum, CancellationToken ct = default)
    {
        var game = await _gameRepo.GetGame(gameID, ct);
        if (game is null) return;

        // Don't track win/loss for NPC games
        if (NpcConstants.IsNpcPlayer(game.Player1ID) || NpcConstants.IsNpcPlayer(game.Player2ID))
            return;

        var winnerID = winnerNum == 1 ? game.Player1ID : game.Player2ID;
        var loserID = winnerNum == 1 ? game.Player2ID : game.Player1ID;

        await _gameRepo.UpdateWinLoss(winnerID, 1, 0, ct);
        await _gameRepo.UpdateWinLoss(loserID, 0, 1, ct);
    }

    // ─── Private helpers ────────────────────────────────────────

    private void NotifyAction(string gameID, string actionType, Dictionary<string, object>? data)
    {
        _logger.LogDebug("NotifyAction game={GameID} action={Action}", gameID, actionType);
        _actionObserver?.Invoke(gameID, actionType, data);
    }

    private void NotifyBattleEvent(string gameID, BattleEvent evt)
    {
        _logger.LogDebug("NotifyBattleEvent game={GameID} type={Type}", gameID, evt.Type);
        _battleEventObserver?.Invoke(gameID, evt);
    }

    private BattlePlayerInfo BuildBattlePlayerInfo(Game game, string playerID)
    {
        if (NpcConstants.IsNpcPlayer(playerID))
        {
            var faction = NpcFactionFromGame(game, playerID);
            var name = NpcDisplayNames.GetValueOrDefault(faction, "NPC");
            return new BattlePlayerInfo { PlayerID = playerID, Name = name, Level = NpcDisplayLevel };
        }

        // For real players, a synchronous lookup is acceptable in this context
        if (_playerService is not null)
        {
            var player = _playerService.GetPlayer(playerID).GetAwaiter().GetResult();
            if (player is not null)
                return new BattlePlayerInfo { PlayerID = playerID, Name = player.Username, Level = player.Level };
        }

        return new BattlePlayerInfo { PlayerID = playerID, Name = "Player", Level = 1 };
    }

    private static string NpcFactionFromGame(Game game, string playerID)
    {
        var snapshot = playerID == game.Player1ID
            ? game.Player1DeckSnapshot
            : game.Player2DeckSnapshot;
        if (snapshot is null) return "";
        return snapshot.DeckID.StartsWith("npc-") ? snapshot.DeckID[4..] : "";
    }

    private async Task PostCreateAdvance(Game game, string matchType, CancellationToken ct)
    {
        var gameID = game.GameID;

        NotifyBattleEvent(gameID, new BattleEvent
        {
            Type = "battle_start",
            Player1Info = BuildBattlePlayerInfo(game, game.Player1ID),
            Player2Info = BuildBattlePlayerInfo(game, game.Player2ID),
            MatchType = matchType,
        });

        var (gameOver, _) = await _engine.RunAutoAdvance(gameID, ct);

        if (!gameOver)
        {
            var advState = await _gameRepo.GetGameState(gameID, ct);
            if (advState is not null)
            {
                NotifyBattleEvent(gameID, new BattleEvent
                {
                    Type = "turn_start",
                    Turn = advState.CurrentTurn,
                    ActivePlayer = advState.ActivePlayer,
                });
            }

            await RunNPCTurnIfNeeded(gameID, ct);
        }
    }

    private async Task<ClientGameState?> GetStateForPlayer(
        string gameID, string playerID, CancellationToken ct)
    {
        var game = await _gameRepo.GetGame(gameID, ct);
        if (game is null) return null;

        var state = await _gameRepo.GetGameState(gameID, ct);
        if (state is null) return null;

        long playerNum;
        if (playerID == game.Player1ID) playerNum = 1;
        else if (playerID == game.Player2ID) playerNum = 2;
        else return null;

        return GameStateView.Build(state, game, playerNum, _cardCache, _engine.EffectRegistry);
    }

    private async Task RunNPCTurnIfNeeded(string gameID, CancellationToken ct)
    {
        if (_npcAI is null) return;

        long prevTurn = 0;

        for (int i = 0; i < MaxNPCIterations; i++)
        {
            var game = await _gameRepo.GetGame(gameID, ct);
            if (game is null || game.Status == GameStatus.Finished) return;

            var state = await _gameRepo.GetGameState(gameID, ct);
            if (state is null) return;

            // Determine NPC player number
            long npcPlayerNum;
            if (game.Player1ID == NpcConstants.PlayerId) npcPlayerNum = 1;
            else if (game.Player2ID == NpcConstants.PlayerId) npcPlayerNum = 2;
            else return; // No NPC in this game

            if (state.ActivePlayer != npcPlayerNum) return;

            // Emit turn_start when the NPC's turn number changes
            if (prevTurn != 0 && state.CurrentTurn != prevTurn)
            {
                NotifyBattleEvent(gameID, new BattleEvent
                {
                    Type = "turn_start",
                    Turn = state.CurrentTurn,
                    ActivePlayer = state.ActivePlayer,
                });
            }
            prevTurn = state.CurrentTurn;

            // Compute available actions
            var myField = state.GetField(npcPlayerNum);
            var oppField = state.GetField(state.OpponentOf(npcPlayerNum));
            var hand = state.GetHand(npcPlayerNum);
            var budget = state.GetBudget(npcPlayerNum);
            var insightPool = state.GetInsightPool(npcPlayerNum);
            var available = AvailableActions.Compute(
                state, game, npcPlayerNum,
                myField, oppField, hand, budget, insightPool,
                _cardCache, _engine.EffectRegistry);

            List<NpcAction> actions;
            switch (state.CurrentPhase)
            {
                case Phase.Main:
                    actions = _npcAI.DecideMainPhaseActions(state, game, npcPlayerNum, available);
                    break;
                case Phase.Battle:
                    actions = _npcAI.DecideBattlePhaseActions(state, game, npcPlayerNum, available);
                    break;
                case Phase.End:
                    var ids = _npcAI.DecideDiscard(state, npcPlayerNum);
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

                    NotifyAction(gameID, action.ActionType, action.Data);

                    if (result.GameOver) return;
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
