using Microsoft.Extensions.Logging;
using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Npc;

/// <summary>
/// Result of a single NPC action advancement.
/// </summary>
public record NpcAdvanceResult(
    List<GameEvent> Events,
    GameOverResult? GameOver,
    bool NpcPending)
{
    public static NpcAdvanceResult Done() => new([], null, false);
}

/// <summary>
/// Orchestrates NPC turns: resolves AI strategies, decides actions, and executes them via GameEngine.
/// Each call to AdvanceOneAction processes exactly one NPC action, matching PvP's one-action-per-call flow.
/// The gateway loops until NpcPending is false.
/// </summary>
public class NpcRunner
{
    private readonly GameEngine _engine;
    private readonly IGameRepository _repo;
    private readonly ICardCache _cardCache;
    private readonly ILogger<NpcRunner> _logger;
    private readonly Dictionary<string, AiConfig> _aiConfigs;

    public NpcRunner(
        GameEngine engine,
        IGameRepository repo,
        ICardCache cardCache,
        Dictionary<string, AiConfig> aiConfigs,
        ILogger<NpcRunner> logger)
    {
        _engine = engine;
        _repo = repo;
        _cardCache = cardCache;
        _aiConfigs = aiConfigs;
        _logger = logger;

        foreach (var (model, config) in aiConfigs)
        {
            AiConfigValidator.Validate(config, cardCache);
        }
    }

    /// <summary>
    /// Processes exactly one NPC action and returns.
    /// Returns NpcPending=true if the active player is still an NPC after the action.
    /// The gateway calls this in a loop until NpcPending is false or GameOver is set.
    /// </summary>
    public async Task<NpcAdvanceResult> AdvanceOneAction(Game game, CancellationToken ct = default)
    {
        var gameID = game.GameID;
        var state = await _repo.GetGameState(gameID, ct)
            ?? throw new InvalidOperationException($"game state {gameID} lost");

        var npcAI = ResolveNpcAIForPlayer(game, state.ActivePlayer);
        if (npcAI is null)
        {
            return NpcAdvanceResult.Done();
        }

        var npcPlayerNum = state.ActivePlayer;

        // Pending slot select takes priority
        if (state.PendingSlotSelects.Count > 0
            && state.PendingSlotSelects[0].PlayerNum == npcPlayerNum)
        {
            return await ProcessOneSlotSelect(game, state, npcPlayerNum, npcAI, ct);
        }

        // Decide actions for current phase
        var actions = DecideActions(npcAI, state, game, npcPlayerNum);

        if (actions.Count == 0)
        {
            return NpcAdvanceResult.Done();
        }

        // Try each action until one succeeds (skip rejected ones)
        foreach (var action in actions)
        {
            var actionType = EnumExtensions.ParseActionType(action.ActionType);
            try
            {
                game = await _repo.GetGame(gameID, ct)
                    ?? throw new InvalidOperationException($"game {gameID} lost during NPC action");

                var result = await _engine.ProcessAction(game, npcPlayerNum, actionType, action.Data, ct);
                var pending = await IsNpcPending(game, result, ct);
                return new NpcAdvanceResult(result.Events, result.GameOver, pending);
            }
            catch (GameRuleException ex)
            {
                _logger.LogWarning(ex, "NPC action rejected (game={GameID}, action={Action})", gameID, action.ActionType);
            }
        }

        _logger.LogWarning("All NPC actions rejected (game={GameID})", gameID);
        return NpcAdvanceResult.Done();
    }

    // ─── Private ────────────────────────────────────────────────

    private INpcStrategy ResolveAI(string npcModel)
    {
        if (_engine.EffectRegistry is null)
        {
            throw new InvalidOperationException("EffectRegistry is not configured");
        }

        if (!_aiConfigs.TryGetValue(npcModel, out var config))
        {
            throw new InvalidOperationException(
                $"No AI config found for model '{npcModel}'. Available: [{string.Join(", ", _aiConfigs.Keys)}]");
        }

        return new NpcAi(config, _cardCache, _engine.EffectRegistry);
    }

    private INpcStrategy? ResolveNpcAIForPlayer(Game game, long playerNum)
    {
        var npcModel = game.GetNpcModel(playerNum);
        return npcModel is not null ? ResolveAI(npcModel) : null;
    }

    private async Task<NpcAdvanceResult> ProcessOneSlotSelect(
        Game game, GameState state, long npcPlayerNum, INpcStrategy npcAI,
        CancellationToken ct)
    {
        var gameID = game.GameID;
        var slotAction = npcAI.DecideSlotSelect(state, npcPlayerNum)
            ?? throw new InvalidOperationException($"NPC failed to decide slot selection (game={gameID})");

        var slotType = EnumExtensions.ParseActionType(slotAction.ActionType);

        game = await _repo.GetGame(gameID, ct)
            ?? throw new InvalidOperationException($"game {gameID} lost during NPC slot select");

        var result = await _engine.ProcessAction(game, npcPlayerNum, slotType, slotAction.Data, ct);
        var pending = await IsNpcPending(game, result, ct);
        return new NpcAdvanceResult(result.Events, result.GameOver, pending);
    }

    private List<NpcAction> DecideActions(
        INpcStrategy npcAI, GameState state, Game game, long npcPlayerNum)
    {
        var myField = state.GetField(npcPlayerNum);
        var oppField = state.GetField(state.OpponentOf(npcPlayerNum));
        var hand = state.GetHand(npcPlayerNum);
        var budget = state.GetBudget(npcPlayerNum);
        var insightPool = state.GetInsightPool(npcPlayerNum);
        var available = AvailableActions.GetAllAvailableActions(
            state, myField, oppField, hand, budget, insightPool,
            _cardCache, _engine.EffectRegistry);

        return state.CurrentPhase switch
        {
            Phase.Main => npcAI.DecideMainPhaseActions(state, game, npcPlayerNum, available),
            Phase.Battle => npcAI.DecideBattlePhaseActions(state, game, npcPlayerNum, available),
            Phase.End => BuildDiscardActions(npcAI, state, npcPlayerNum, game.GameID),
            _ => throw new InvalidOperationException(
                $"NPC encountered unexpected phase {state.CurrentPhase} (game={game.GameID})"),
        };
    }

    private static List<NpcAction> BuildDiscardActions(
        INpcStrategy npcAI, GameState state, long npcPlayerNum, string gameID)
    {
        var hand = state.GetHand(npcPlayerNum);
        var discardCount = hand.Count - BattleConstants.HandLimit;
        if (discardCount <= 0)
        {
            throw new InvalidOperationException(
                $"NPC in end phase but no discard needed (game={gameID})");
        }

        var ids = npcAI.DecideDiscard(state, npcPlayerNum, discardCount);
        if (ids.Count == 0)
        {
            throw new InvalidOperationException(
                $"NPC in end phase but AI returned no discard targets (game={gameID})");
        }
        return
        [
            new NpcAction
            {
                ActionType = ActionTypes.DiscardHand,
                Data = new DiscardHandRequest { CardInstanceIDs = ids },
            }
        ];
    }

    private async Task<bool> IsNpcPending(Game game, OverloadParty.Battle.Engine.ActionResult result, CancellationToken ct)
    {
        if (result.GameOver is not null) return false;
        if (result.NeedsSlotSelect) return true;

        var state = await _repo.GetGameState(game.GameID, ct);
        if (state is null) return false;

        return ResolveNpcAIForPlayer(game, state.ActivePlayer) is not null;
    }
}
