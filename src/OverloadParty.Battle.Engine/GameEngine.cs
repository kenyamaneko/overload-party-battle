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

    public GameEngine(IGameRepository repo, ICardCache cardCache)
    {
        _repo = repo;
        _cardCache = cardCache;
    }

    public void SetEffectRegistry(IEffectRegistry registry) => _effects = registry;
    public IEffectRegistry? EffectRegistry => _effects;

    /// <summary>
    /// Create a new game with shuffled decks.
    /// </summary>
    public async Task<string> CreateNewGame(
        string player1ID, string player2ID,
        DeckSnapshot deck1, DeckSnapshot deck2,
        long firstPlayer, CancellationToken ct = default)
    {
        var gameID = Guid.NewGuid().ToString("N");
        var (game, state) = GameInitializer.CreateNewGame(
            gameID, player1ID, player2ID, deck1, deck2, firstPlayer, _cardCache);

        await _repo.CreateGame(game, state, ct);
        return gameID;
    }

    /// <summary>
    /// Process automatic phases (draw) and check win conditions.
    /// Returns (gameOver, winReason).
    /// </summary>
    public async Task<(bool GameOver, string? WinReason)> RunAutoAdvance(
        string gameID, CancellationToken ct = default)
    {
        bool gameOver = false;
        string? winReason = null;

        await _repo.UpdateGameState(gameID, state =>
        {
            var game = _repo.GetGame(gameID, ct).GetAwaiter().GetResult()
                ?? throw new GameRuleException($"game {gameID} not found");

            var result = TurnManager.AutoAdvancePhases(state, game, _cardCache);
            gameOver = result.GameOver;
            winReason = result.WinReason;

            return Task.CompletedTask;
        }, ct);

        if (gameOver)
        {
            var game = await _repo.GetGame(gameID, ct)
                ?? throw new GameRuleException($"game {gameID} not found");
            var state = await _repo.GetGameState(gameID, ct)
                ?? throw new GameRuleException($"game state {gameID} not found");

            var winnerNum = state.OpponentOf(state.ActivePlayer);
            var winnerID = winnerNum == 1 ? game.Player1ID : game.Player2ID;
            await _repo.FinishGame(gameID, winnerID, ct);
        }

        return (gameOver, winReason);
    }

    /// <summary>
    /// Process a player action. Returns ActionResult.
    /// </summary>
    public async Task<ActionResult> ProcessAction(
        string gameID, string playerID, ActionType actionType, object actionData,
        CancellationToken ct = default)
    {
        var game = await _repo.GetGame(gameID, ct)
            ?? throw new GameRuleException($"game {gameID} not found");

        if (game.Status != GameStatus.Playing)
            throw new GameRuleException("game is not in playing state");

        // Determine player number
        long playerNum;
        if (playerID == game.Player1ID)
            playerNum = 1;
        else if (playerID == game.Player2ID)
            playerNum = 2;
        else
            throw new GameRuleException($"player {playerID} is not in this game");

        ActionResult actionResult = null!;

        await _repo.UpdateGameState(gameID, state =>
        {
            // Validate active player (except for SetReactive which can be done by non-active player)
            if (actionType != ActionType.SetReactive && state.ActivePlayer != playerNum)
                throw new GameRuleException("not your turn");

            // Validate action allowed in current phase
            if (!IsActionAllowedInPhase(state.CurrentPhase, actionType))
                throw new GameRuleException($"action {actionType.ToWireString()} not allowed in phase {state.CurrentPhase.ToWireString()}");

            // Dispatch to specific processor
            actionResult = actionType switch
            {
                ActionType.PlayCard => PlayCardProcessor.Process(
                    state, game, playerNum, (PlayCardRequest)actionData, _cardCache, _effects),
                ActionType.Attack => AttackProcessor.Process(
                    state, game, playerNum, (AttackRequest)actionData, _cardCache, _effects),
                ActionType.ScaleUp => ScaleUpProcessor.Process(
                    state, game, playerNum, (ScaleUpRequest)actionData, _cardCache),
                ActionType.DistributeYield => DistributeYieldProcessor.Process(
                    state, game, playerNum, (DistributeYieldRequest)actionData, _cardCache),
                ActionType.EndPhase => EndPhaseProcessor.Process(
                    state, game, playerNum, _cardCache),
                ActionType.DiscardHand => DiscardProcessor.Process(
                    state, game, playerNum, (DiscardHandRequest)actionData, _cardCache),
                ActionType.ActivateEffect => ActivateEffectProcessor.Process(
                    state, game, playerNum, (ActivateEffectRequest)actionData, _cardCache, _effects),
                ActionType.Migrate => MigrateProcessor.Process(
                    state, game, playerNum, (MigrateRequest)actionData, _cardCache),
                _ => throw new GameRuleException($"unknown action type: {actionType}")
            };

            // Check win conditions after action
            if (!actionResult.GameOver)
            {
                var (winnerNum, reason, gameOverCheck) = WinConditionChecker.Check(state, game);
                if (gameOverCheck)
                {
                    actionResult.GameOver = true;
                    actionResult.WinnerNum = winnerNum;
                    actionResult.WinReason = reason;
                }
            }

            return Task.CompletedTask;
        }, ct);

        // Persist events
        var eventCount = await _repo.GetEventCount(gameID, ct);
        foreach (var evt in actionResult.Events)
        {
            eventCount++;
            evt.SequenceNumber = eventCount;
            evt.CreatedAt = DateTime.UtcNow;
            await _repo.AppendEvent(evt, ct);
        }

        // Handle game over
        if (actionResult.GameOver)
        {
            var winnerID = actionResult.WinnerNum switch
            {
                0 => "",
                1 => game.Player1ID,
                _ => game.Player2ID,
            };
            await _repo.FinishGame(gameID, winnerID, ct);
        }

        return actionResult;
    }

    /// <summary>
    /// Compute available actions for a player.
    /// </summary>
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

        return AvailableActions.Compute(
            state, game, playerNum, myField, oppField, hand, budget, insightPool, _cardCache, _effects);
    }

    /// <summary>
    /// Compute turn controls (can end phase, discard required).
    /// </summary>
    public TurnControls ComputeTurnControls(GameState state, List<HandCard> hand)
    {
        return AvailableActions.ComputeTurnControls(state, hand);
    }

    private static bool IsActionAllowedInPhase(Phase phase, ActionType action)
    {
        return phase switch
        {
            Phase.Main => action is ActionType.PlayCard or ActionType.ScaleUp or ActionType.DistributeYield
                or ActionType.ActivateEffect or ActionType.Migrate or ActionType.EndPhase,
            Phase.Battle => action is ActionType.Attack or ActionType.ActivateEffect
                or ActionType.SetReactive or ActionType.EndPhase,
            Phase.End => action is ActionType.DiscardHand,
            _ => false,
        };
    }
}
