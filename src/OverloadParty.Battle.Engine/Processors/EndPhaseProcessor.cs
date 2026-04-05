using OverloadParty.Battle.Models;
using OverloadParty.Battle.Engine.Effects;

namespace OverloadParty.Battle.Engine.Processors;

/// <summary>
/// Processes phase advancement and end-of-turn logic including maintenance, insight generation, and turn switching.
/// </summary>
public static class EndPhaseProcessor
{
    /// <summary>
    /// Advances the current phase and processes end-of-turn logic when entering the end phase.
    /// </summary>
    /// <param name="state">The current game state.</param>
    /// <param name="game">The game metadata.</param>
    /// <param name="playerNum">The player number ending their phase.</param>
    /// <param name="cc">The card definition cache.</param>
    /// <returns>The action result containing phase change events and possible game-over or discard requirements.</returns>
    public static ActionResult Process(
        GameState state, Game game, long playerNum, ICardCache cc, IEffectRegistry? effects = null)
    {
        var previousPhase = TurnManager.AdvancePhase(state);

        var events = new List<GameEvent>();
        var playerId = game.GetPlayerID(playerNum);

        if (state.CurrentPhase == Phase.End)
        {
            return ProcessEndPhaseTransition(state, game, playerNum, cc, effects, events, playerId);
        }

        events.Add(new GameEvent
        {
            GameID = game.GameID,
            EventType = EventTypes.PhaseChange,
            PlayerID = playerId,
            EventData = new PhaseChangeEventData
            {
                PreviousPhase = previousPhase.ToWireString(),
                CurrentPhase = state.CurrentPhase.ToWireString(),
            }.ToDictionary(),
        });
        return new ActionResult { Events = events, StateUpdated = true };
    }

    private static ActionResult ProcessEndPhaseTransition(
        GameState state, Game game, long playerNum, ICardCache cc,
        IEffectRegistry? effects, List<GameEvent> events, string playerId)
    {
        bool needsDiscard = ProcessEndPhaseLogic(state, game, playerNum, cc, effects);
        var result = new ActionResult { Events = events, StateUpdated = true };

        if (needsDiscard)
        {
            result.NeedsDiscard = true;
            events.Add(new GameEvent
            {
                GameID = game.GameID,
                EventType = EventTypes.PhaseEnd,
                PlayerID = playerId,
                EventData = new PhaseEndEventData
                {
                    Phase = Phases.End,
                    NeedsDiscard = true,
                }.ToDictionary(),
            });
            return result;
        }

        if (WinConditionChecker.CheckLaunchFailure(state, playerNum))
        {
            result.GameOver = new GameOverResult(
                state.OpponentOf(playerNum),
                WinReason.LaunchFailure.ToWireString());
            events.Add(MakeTurnEndEvent(game.GameID, playerId, state));
            return result;
        }

        TurnManager.SwitchActivePlayer(state);
        var gameOverResult = DrawPhaseProcessor.Process(state, game, cc, effects);

        events.Add(MakeTurnEndEvent(game.GameID, playerId, state));
        events.Add(MakeTurnStartEvent(game.GameID, state));

        if (gameOverResult is not null)
        {
            result.GameOver = gameOverResult;
        }

        return result;
    }

    /// <summary>
    /// Returns true if the player needs to discard (hand > 6).
    /// </summary>
    static bool ProcessEndPhaseLogic(GameState state, Game game, long playerNum, ICardCache cc, IEffectRegistry? effects)
    {
        var field = state.GetField(playerNum);

        FirePassiveEffects(state, game, playerNum, field, cc, effects);
        CollectMaintenanceCost(state, playerNum, field, cc);
        GenerateInsight(state, playerNum, field, cc);
        ExpireTemporaryEffects(field);
        ResetPerTurnFlags(state, playerNum, field);

        return state.GetHand(playerNum).Count > BattleConstants.HandLimit;
    }

    static long CalculateMaintenanceCost(DeployedResource resource, CardDefinition card)
    {
        long baseCost;
        if (card.Elastic)
        {
            long intrinsic = card.IsComputeType ? card.BaseThroughput : card.BaseYield;
            long scaledStat = intrinsic * BattleConstants.RankMultiplier(resource.Rank) + resource.ElasticBonus;
            baseCost = Math.Max(0, scaledStat - card.FreeTier) * card.CostPerRequest / 100;
        }
        else
        {
            baseCost = card.MaintenanceCost * BattleConstants.RankMultiplier(resource.Rank);
        }

        return FieldHelpers.ApplyReduction(
            resource.TemporaryEffects, BuffTypes.MaintenanceReduction, baseCost);
    }

    static void CollectMaintenanceCost(GameState state, long playerNum, Field field, ICardCache cc)
    {
        long totalMC = FieldHelpers.AllFaceUpResources(field)
            .Sum(r => CalculateMaintenanceCost(r, cc.MustGet(r.CardID)));

        state.SetBudget(playerNum, state.GetBudget(playerNum) - totalMC);
    }

    static void GenerateInsight(GameState state, long playerNum, Field field, ICardCache cc)
    {
        long totalYield = 0;

        foreach (var res in field.Backend)
        {
            if (!res.FaceUp)
            {
                continue;
            }

            var card = cc.Get(res.CardID);
            if (card is null || !card.IsDataType)
            {
                continue;
            }

            totalYield += StatCalculator.CalculateEffectiveInsight(res, field, cc);

            if (card.Elastic && card.ElasticIncrement > 0)
            {
                res.ElasticBonus += card.ElasticIncrement;
            }
        }

        state.SetInsightPool(playerNum, state.GetInsightPool(playerNum) + totalYield);
    }

    static void ExpireTemporaryEffects(Field field)
    {
        foreach (var resource in FieldHelpers.AllResources(field))
        {
            resource.TemporaryEffects.RemoveAll(e =>
                e.Duration is EffectDurations.ThisTurn or EffectDurations.UntilNextOwnTurnEnd);
        }
    }

    static void ResetPerTurnFlags(GameState state, long playerNum, Field field)
    {
        foreach (var resource in FieldHelpers.AllResources(field))
        {
            resource.HasAttacked = false;
            resource.EffectUsedThisTurn = false;
            resource.MonetizedAmount = 0;
        }

        foreach (var support in FieldHelpers.AllSupports(field))
        {
            support.EffectUsedThisTurn = false;
        }

        foreach (var att in field.Support)
        {
            att.EffectUsedThisTurn = false;
        }

        state.SetIncidentPlayedThisTurn(playerNum, false);
    }

    static void FirePassiveEffects(
        GameState state, Game game, long playerNum, Field field,
        ICardCache cc, IEffectRegistry? effects)
    {
        if (effects is null) { return; }

        // リソース＋アタッチメント＋サポートを DeployOrder 昇順で収集
        var triggers = new List<(string CardId, long DeployOrder, DeployedResource? Source, DeployedSupport? SupSource)>();

        foreach (var resource in FieldHelpers.AllFaceUpResources(field))
        {
            if (HasEndPhaseHandler(effects, resource.CardID))
            {
                triggers.Add((resource.CardID, resource.DeployOrder, resource, null));
            }

            foreach (var att in field.Support.Where(a => a.TargetInstanceID == resource.InstanceID))
            {
                if (HasEndPhaseHandler(effects, att.CardID))
                {
                    triggers.Add((att.CardID, resource.DeployOrder, resource, null));
                }
            }
        }

        foreach (var support in FieldHelpers.AllSupports(field))
        {
            if (!support.FaceUp || support.DeployingTurnsLeft > 0) { continue; }
            if (HasEndPhaseHandler(effects, support.CardID))
            {
                triggers.Add((support.CardID, support.DeployOrder, null, support));
            }
        }

        triggers.Sort((a, b) => a.DeployOrder.CompareTo(b.DeployOrder));

        foreach (var (cardId, _, source, supSource) in triggers)
        {
            var handler = GetEndPhaseHandler(effects, cardId);
            if (handler is null) { continue; }

            handler(new EffectContext
            {
                State = state,
                Game = game,
                PlayerNum = playerNum,
                Source = source,
                SupSource = supSource,
                CardCache = cc,
            });
        }
    }

    private static bool HasEndPhaseHandler(IEffectRegistry effects, string cardId)
    {
        return effects.Has(cardId, TriggerType.Passive)
            || effects.Has(cardId, TriggerType.OnEndPhase);
    }

    private static EffectHandler? GetEndPhaseHandler(IEffectRegistry effects, string cardId)
    {
        return effects.Get(cardId, TriggerType.OnEndPhase)
            ?? effects.Get(cardId, TriggerType.Passive);
    }

    private static GameEvent MakeTurnEndEvent(string gameID, string playerId, GameState state)
    {
        return new GameEvent
        {
            GameID = gameID,
            EventType = EventTypes.TurnEnd,
            PlayerID = playerId,
            EventData = new TurnEndEventData
            {
                Phase = Phases.End,
                NextTurn = state.CurrentTurn,
                ActivePlayer = state.ActivePlayer,
                CurrentPhase = state.CurrentPhase.ToWireString(),
            }.ToDictionary(),
        };
    }

    internal static GameEvent MakeTurnStartEvent(string gameID, GameState state)
    {
        return new GameEvent
        {
            GameID = gameID,
            EventType = EventTypes.TurnStart,
            PlayerID = "",
            IsSystemEvent = true,
            EventData = new Dictionary<string, object>
            {
                ["turn"] = state.CurrentTurn,
                ["active_player"] = state.ActivePlayer,
            },
        };
    }
}
