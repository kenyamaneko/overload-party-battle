using OverloadParty.Battle.Models;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Processors;

namespace OverloadParty.Battle.Engine;

/// <summary>
/// Stateless game engine. All state lives in GameState (persisted via IGameRepository).
/// Depends only on IGameRepository, ICardCache, and IEffectRegistry interfaces.
/// </summary>
public class GameEngine
{
    private readonly IGameRepository _repo;
    private readonly ICardCache _cardCache;
    private IEffectRegistry? _effects;

    /// <summary>Initializes a new instance of <see cref="GameEngine"/>.</summary>
    /// <param name="repo">The game persistence layer.</param>
    /// <param name="cardCache">Read-only card definitions.</param>
    public GameEngine(IGameRepository repo, ICardCache cardCache)
    {
        _repo = repo;
        _cardCache = cardCache;
    }

    /// <summary>Injects the effect registry (card effect handlers). Must be called before processing actions that use effects.</summary>
    /// <param name="registry">The effect registry to use.</param>
    public void SetEffectRegistry(IEffectRegistry registry) => _effects = registry;

    /// <summary>Gets the currently configured effect registry, or <c>null</c> if not set.</summary>
    public IEffectRegistry? EffectRegistry => _effects;

    /// <summary>
    /// Creates a new game with shuffled decks and initial hands.
    /// </summary>
    /// <param name="player1ID">Player 1's ID.</param>
    /// <param name="player2ID">Player 2's ID.</param>
    /// <param name="deck1">Player 1's deck snapshot.</param>
    /// <param name="deck2">Player 2's deck snapshot.</param>
    /// <param name="firstPlayer">Which player goes first (1 or 2).</param>
    /// <returns>The new game's ID.</returns>
    public async Task<string> CreateNewGame(
        string player1ID, string player2ID,
        DeckSnapshot deck1, DeckSnapshot deck2,
        long firstPlayer,
        string engineVersion = "", string cardDataVersion = "",
        CancellationToken ct = default)
    {
        var gameID = Guid.NewGuid().ToString("N");
        var (game, state) = GameInitializer.CreateNewGame(
            gameID, player1ID, player2ID, deck1, deck2, firstPlayer, _cardCache);

        game.EngineVersion = engineVersion;
        game.CardDataVersion = cardDataVersion;

        await _repo.CreateGame(game, state, ct);
        return gameID;
    }

    /// <summary>
    /// Runs the draw-phase auto-advance for the active player.
    /// Called at the start of each turn before player actions.
    /// </summary>
    /// <param name="gameID">The game ID.</param>
    /// <returns>Non-null if the game ended (e.g. repository out).</returns>
    public async Task<GameOverResult?> RunAutoAdvance(
        string gameID, CancellationToken ct = default)
    {
        var game = await _repo.GetGame(gameID, ct)
            ?? throw new GameRuleException($"game {gameID} not found");

        GameOverResult? gameOverResult = null;

        await _repo.UpdateGameState(gameID, state =>
        {
            gameOverResult = DrawPhaseProcessor.Process(state, game, _cardCache, _effects);

            return Task.CompletedTask;
        }, ct: ct);

        if (gameOverResult is not null)
        {
            await _repo.FinishGame(gameID, ResolveWinnerID(game, gameOverResult.WinnerNum), ct);
        }

        return gameOverResult;
    }

    /// <summary>
    /// Processes a player action (play card, attack, etc.) and persists the resulting events.
    /// </summary>
    /// <param name="gameID">The game ID.</param>
    /// <param name="playerID">The acting player's ID.</param>
    /// <param name="actionType">The type of action to process.</param>
    /// <param name="actionData">The action-specific request data.</param>
    /// <returns>The result including events and possible game-over.</returns>
    public async Task<ActionResult> ProcessAction(
        string gameID, string playerID, ActionType actionType, object actionData,
        CancellationToken ct = default)
    {
        var game = await _repo.GetGame(gameID, ct)
            ?? throw new GameRuleException($"game {gameID} not found");

        if (game.Status != GameStatus.Playing)
        {
            throw new GameRuleException("game is not in playing state");
        }

        long playerNum
            = playerID == game.Player1ID ? 1
            : playerID == game.Player2ID ? 2
            : throw new GameRuleException($"player {playerID} is not in this game");

        // Forfeit: immediate game over, no phase/turn restrictions
        if (actionType == ActionType.Forfeit)
        {
            var opponentNum = playerNum == 1 ? 2 : 1;
            var winnerID = game.GetPlayerID(opponentNum);
            await _repo.FinishGame(gameID, winnerID, ct);
            return new ActionResult
            {
                GameOver = new GameOverResult(opponentNum, WinReason.Timeout.ToWireString()),
            };
        }

        ActionResult actionResult = null!;

        var pending = new PendingAction(playerID, actionType.ToWireString(), actionData);

        await _repo.UpdateGameState(gameID, state =>
        {
            if (actionType != ActionType.SetReactive && state.ActivePlayer != playerNum)
            {
                throw new GameRuleException("not your turn");
            }

            DeductElapsedTime(state);

            var timeoutResult = WinConditionChecker.CheckTimeout(state);
            if (timeoutResult is not null)
            {
                actionResult = new ActionResult { GameOver = timeoutResult };
                return Task.CompletedTask;
            }

            if (state.AwaitingSlotSelect is { PlayerNum: var pendingPlayer }
                && pendingPlayer == playerNum
                && actionType != ActionType.SelectSlot)
            {
                throw new GameRuleException("slot selection required");
            }

            if (actionType == ActionType.SelectSlot)
            {
                actionResult = SelectSlotProcessor.Process(
                    state, game, playerNum, (SelectSlotRequest)actionData, _cardCache);
            }
            else
            {
                if (!TurnManager.IsActionAllowedInPhase(state.CurrentPhase, actionType))
                {
                    throw new GameRuleException($"action {actionType.ToWireString()} not allowed in phase {state.CurrentPhase.ToWireString()}");
                }

                actionResult = actionType switch
                {
                    ActionType.PlayCard => PlayCardProcessor.Process(
                        state, game, playerNum, (PlayCardRequest)actionData, _cardCache, _effects),
                    ActionType.Attack => AttackProcessor.Process(
                        state, game, playerNum, (AttackRequest)actionData, _cardCache, _effects),
                    ActionType.ScaleUp => ScaleUpProcessor.Process(
                        state, game, playerNum, (ScaleUpRequest)actionData, _cardCache, _effects),
                    ActionType.Monetize => MonetizeProcessor.Process(
                        state, game, playerNum, (MonetizeRequest)actionData, _cardCache),
                    ActionType.EndPhase => EndPhaseProcessor.Process(
                        state, game, playerNum, _cardCache, _effects),
                    ActionType.DiscardHand => DiscardProcessor.Process(
                        state, game, playerNum, (DiscardHandRequest)actionData, _cardCache, _effects),
                    ActionType.UseEffect => UseEffectProcessor.Process(
                        state, game, playerNum, (UseEffectRequest)actionData, _cardCache, _effects),
                    ActionType.Migrate => MigrateProcessor.Process(
                        state, game, playerNum, (MigrateRequest)actionData, _cardCache),
                    _ => throw new GameRuleException($"unknown action type: {actionType}")
                };
            }

            // エフェクト実行後に AwaitingSlotSelect がセットされていたら通知
            if (state.AwaitingSlotSelect is not null)
            {
                actionResult.NeedsSlotSelect = true;
            }

            actionResult.GameOver ??= WinConditionChecker.Check(state, game);

            return Task.CompletedTask;
        }, pending, ct);

        // Persist events
        var eventCount = await _repo.GetEventCount(gameID, ct);
        foreach (var evt in actionResult.Events)
        {
            eventCount++;
            evt.SequenceNumber = eventCount;
            evt.CreatedAt = DateTime.UtcNow;
            await _repo.AppendEvent(evt, ct);
        }

        if (actionResult.GameOver is { } over)
        {
            await _repo.FinishGame(gameID, ResolveWinnerID(game, over.WinnerNum), ct);
        }

        return actionResult;
    }

    /// <summary>
    /// Deducts elapsed time since TurnStartedAt from the active player's TimeBank
    /// and resets TurnStartedAt to now.
    /// </summary>
    internal static void DeductElapsedTime(GameState state)
    {
        var now = DateTime.UtcNow;
        var elapsed = (long)(now - state.TurnStartedAt).TotalSeconds;
        if (elapsed > 0)
        {
            var remaining = state.GetTimeBank(state.ActivePlayer) - elapsed;
            state.SetTimeBank(state.ActivePlayer, remaining);
        }
        state.TurnStartedAt = now;
    }

    private static string ResolveWinnerID(Game game, long winnerNum) => winnerNum switch
    {
        0 => "",
        1 => game.Player1ID,
        _ => game.Player2ID,
    };

    /// <summary>
    /// Computes all valid actions available to a player in the current game state.
    /// </summary>
    /// <param name="gameID">The game ID.</param>
    /// <param name="playerNum">The player number (1 or 2).</param>
    /// <returns>A list of available actions the player can take.</returns>
    public async Task<List<AvailableAction>> ComputeAvailableActions(
        string gameID, long playerNum, CancellationToken ct = default)
    {
        var game = await _repo.GetGame(gameID, ct)
            ?? throw new GameRuleException($"game {gameID} not found");
        var state = await _repo.GetGameState(gameID, ct)
            ?? throw new GameRuleException($"game state {gameID} not found");

        var myField = state.GetField(playerNum);
        var oppField = state.GetField(state.OpponentOf(playerNum));
        var hand = state.GetHand(playerNum);
        long budget = state.GetBudget(playerNum);
        long insightPool = state.GetInsightPool(playerNum);

        return AvailableActions.GetAllAvailableActions(
            state, myField, oppField, hand, budget, insightPool, _cardCache, _effects);
    }

    /// <summary>
    /// Computes turn control information (whether the player can end the phase, discard count).
    /// </summary>
    /// <param name="state">The current game state.</param>
    /// <param name="hand">The active player's hand.</param>
    /// <returns>Turn controls for the UI.</returns>
    public TurnControls ComputeTurnControls(GameState state, List<UndeployedCard> hand)
    {
        return AvailableActions.ComputeTurnControls(state, hand);
    }
}
