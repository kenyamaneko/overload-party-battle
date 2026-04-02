using System.Text.Json;
using Microsoft.Extensions.Logging;
using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Npc;

/// <summary>
/// A game event paired with the post-action game state snapshot (raw, not info-hidden).
/// The caller is responsible for converting this to a player-specific view.
/// </summary>
public record NpcEventWithState(GameEvent Event, GameState State);

/// <summary>
/// Result of NPC turn execution.
/// </summary>
public record NpcRunResult(List<NpcEventWithState> Events, GameOverResult? GameOver)
{
    public bool IsGameOver => GameOver is not null;
}

/// <summary>
/// Orchestrates NPC turns: resolves AI strategies, decides actions, and executes them via GameEngine.
/// </summary>
public class NpcRunner
{
    private readonly GameEngine _engine;
    private readonly IGameRepository _repo;
    private readonly ICardCache _cardCache;
    private readonly ILogger<NpcRunner> _logger;
    private readonly Dictionary<string, AiConfig> _aiConfigs;

    private const int MaxIterations = 50;

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
    }

    /// <summary>
    /// Advances NPC turns until a human player becomes active or the game ends.
    /// </summary>
    public async Task<NpcRunResult> RunTurns(Game game, CancellationToken ct = default)
    {
        var npc1AI = game.Npc1Model is not null ? ResolveAI(game.Npc1Model) : null;
        var npc2AI = game.Npc2Model is not null ? ResolveAI(game.Npc2Model) : null;

        var allEvents = new List<NpcEventWithState>();
        var gameID = game.GameID;

        for (int i = 0; i < MaxIterations; i++)
        {
            game = await _repo.GetGame(gameID, ct)
                ?? throw new InvalidOperationException($"game {gameID} lost during NPC turn");
            if (game.Status == GameStatus.Finished)
            {
                return new NpcRunResult(allEvents, null);
            }

            var state = await _repo.GetGameState(gameID, ct)
                ?? throw new InvalidOperationException($"game state {gameID} lost during NPC turn");

            var npcAI = state.ActivePlayer == 1 ? npc1AI : npc2AI;
            if (npcAI is null)
            {
                return new NpcRunResult(allEvents, null);
            }

            var outcome = await ExecuteTurn(npcAI, state, game, ct);
            allEvents.AddRange(outcome.Events);
            if (outcome.IsGameOver)
            {
                return new NpcRunResult(allEvents, outcome.GameOver);
            }
        }

        throw new InvalidOperationException($"NPC turn exceeded {MaxIterations} iterations (game={gameID})");
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

    private async Task<NpcRunResult> ExecuteTurn(
        INpcStrategy npcAI, GameState state, Game game, CancellationToken ct)
    {
        var npcPlayerNum = state.ActivePlayer;
        var actions = DecideActions(npcAI, state, game, npcPlayerNum);
        var turnEvents = new List<NpcEventWithState>();

        foreach (var action in actions)
        {
            try
            {
                var outcome = await ExecuteAction(action, game.GameID, npcPlayerNum, npcAI, ct);
                turnEvents.AddRange(outcome.Events);
                if (outcome.IsGameOver)
                {
                    return new NpcRunResult(turnEvents, outcome.GameOver);
                }
            }
            catch (GameRuleException ex)
            {
                _logger.LogWarning(ex, "NPC action rejected (game={GameID}, action={Action})", game.GameID, action.ActionType);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "NPC action failed unexpectedly (game={GameID}, action={Action})", game.GameID, action.ActionType);
                break;
            }
        }

        return new NpcRunResult(turnEvents, null);
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
                ActionType = WireActionTypes.DiscardHand,
                Data = new Dictionary<string, object> { ["cardInstanceIds"] = ids },
            }
        ];
    }

    private async Task<NpcRunResult> ExecuteAction(
        NpcAction action, string gameID, long npcPlayerNum, INpcStrategy npcAI,
        CancellationToken ct)
    {
        var actionType = EnumExtensions.ParseActionType(action.ActionType);
        var actionData = DeserializeActionData(actionType, action.Data);

        var game = await _repo.GetGame(gameID, ct)
            ?? throw new InvalidOperationException($"game {gameID} lost during NPC action");

        var result = await _engine.ProcessAction(game, npcPlayerNum, actionType, actionData, ct);
        var events = await SnapshotEvents(result, gameID, ct);

        if (result.GameOver is not null)
        {
            return new NpcRunResult(events, result.GameOver);
        }

        if (result.NeedsSlotSelect)
        {
            return await ProcessPendingSlotSelects(gameID, npcPlayerNum, npcAI, events, ct);
        }

        return new NpcRunResult(events, null);
    }

    private async Task<NpcRunResult> ProcessPendingSlotSelects(
        string gameID, long npcPlayerNum, INpcStrategy npcAI,
        List<NpcEventWithState> events, CancellationToken ct)
    {
        while (true)
        {
            var state = await _repo.GetGameState(gameID, ct)
                ?? throw new InvalidOperationException($"Game state lost during NPC slot select (game={gameID})");

            var slotAction = npcAI.DecideSlotSelect(state, npcPlayerNum)
                ?? throw new InvalidOperationException($"NPC failed to decide slot selection (game={gameID})");

            var slotType = EnumExtensions.ParseActionType(slotAction.ActionType);
            var slotData = DeserializeActionData(slotType, slotAction.Data);

            var game = await _repo.GetGame(gameID, ct)
                ?? throw new InvalidOperationException($"game {gameID} lost during NPC slot select");

            var slotResult = await _engine.ProcessAction(game, npcPlayerNum, slotType, slotData, ct);
            events.AddRange(await SnapshotEvents(slotResult, gameID, ct));

            if (slotResult.GameOver is not null)
            {
                return new NpcRunResult(events, slotResult.GameOver);
            }

            if (!slotResult.NeedsSlotSelect)
            {
                return new NpcRunResult(events, null);
            }
        }
    }

    /// <summary>
    /// Pairs each event with the post-action game state snapshot.
    /// All events from a single action share the same snapshot (the final state after the action).
    /// </summary>
    private async Task<List<NpcEventWithState>> SnapshotEvents(
        ActionResult result, string gameID, CancellationToken ct)
    {
        var state = await _repo.GetGameState(gameID, ct)
            ?? throw new InvalidOperationException($"game state {gameID} lost during snapshot");

        return result.Events
            .Select(evt => new NpcEventWithState(evt, state))
            .ToList();
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private static object DeserializeActionData(ActionType actionType, Dictionary<string, object> data)
    {
        var json = JsonSerializer.SerializeToElement(data, JsonOpts);
        return actionType switch
        {
            ActionType.PlayCard => json.Deserialize<PlayCardRequest>(JsonOpts)!,
            ActionType.Attack => json.Deserialize<AttackRequest>(JsonOpts)!,
            ActionType.ScaleUp => json.Deserialize<ScaleUpRequest>(JsonOpts)!,
            ActionType.Monetize => json.Deserialize<MonetizeRequest>(JsonOpts)!,
            ActionType.DiscardHand => json.Deserialize<DiscardHandRequest>(JsonOpts)!,
            ActionType.UseEffect => json.Deserialize<UseEffectRequest>(JsonOpts)!,
            ActionType.Migrate => json.Deserialize<MigrateRequest>(JsonOpts)!,
            ActionType.SelectSlot => json.Deserialize<SelectSlotRequest>(JsonOpts)!,
            ActionType.EndPhase => new object(),
            ActionType.Forfeit => json.Deserialize<ForfeitRequest>(JsonOpts)!,
            _ => throw new ArgumentException($"unknown action type: {actionType}"),
        };
    }
}
