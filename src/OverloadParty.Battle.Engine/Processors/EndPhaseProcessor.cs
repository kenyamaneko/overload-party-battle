using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Processors;

public static class EndPhaseProcessor
{
    public static ActionResult Process(
        GameState state, Game game, long playerNum, ICardCache cc)
    {
        var previousPhase = TurnManager.AdvancePhase(state);

        var events = new List<GameEvent>();
        var playerId = game.GetPlayerID(playerNum);

        if (state.CurrentPhase == Phase.End)
        {
            return ProcessEndPhaseTransition(state, game, playerNum, cc, events, playerId);
        }

        events.Add(new GameEvent
        {
            GameID = game.GameID,
            EventType = WireActionTypes.PhaseChange,
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
        List<GameEvent> events, string playerId)
    {
        bool needsDiscard = ProcessEndPhaseLogic(state, cc);
        var result = new ActionResult { Events = events, StateUpdated = true };

        if (needsDiscard)
        {
            result.NeedsDiscard = true;
            events.Add(new GameEvent
            {
                GameID = game.GameID,
                EventType = WireActionTypes.PhaseEnd,
                PlayerID = playerId,
                EventData = new PhaseEndEventData
                {
                    Phase = "end",
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
        var gameOverResult = DrawPhaseProcessor.Process(state, game, cc);

        events.Add(MakeTurnEndEvent(game.GameID, playerId, state));

        if (gameOverResult is not null)
        {
            result.GameOver = gameOverResult;
        }

        return result;
    }

    /// <summary>
    /// Returns true if the player needs to discard (hand > 6).
    /// </summary>
    static bool ProcessEndPhaseLogic(GameState state, ICardCache cc)
    {
        var playerNum = state.ActivePlayer;
        var field = state.GetField(playerNum);

        CollectMaintenanceCost(state, playerNum, field, cc);
        GenerateInsight(state, playerNum, field, cc);
        ExpireTemporaryEffects(field);
        ResetPerTurnFlags(state, playerNum, field);

        return state.GetHand(playerNum).Count > GameConstants.HandLimit;
    }

    static long CalculateMaintenanceCost(ResourceInstance resource, CardDefinition card)
    {
        if (card.Elastic)
        {
            long intrinsic = card.IsComputeType ? card.BaseThroughput : card.BaseYield;
            long scaledStat = intrinsic * GameConstants.RankMultiplier(resource.Rank) + resource.ElasticBonus;
            return Math.Max(0, scaledStat - card.FreeTier) * card.CostPerRequest / 100;
        }

        return card.MaintenanceCost * GameConstants.RankMultiplier(resource.Rank);
    }

    static void CollectMaintenanceCost(GameState state, long playerNum, Field field, ICardCache cc)
    {
        long totalMC = FieldHelpers.AllFaceUpResources(field)
            .Where(r => r.MigratingFrom is null)
            .Sum(r => CalculateMaintenanceCost(r, cc.MustGet(r.CardID)));

        state.SetBudget(playerNum, state.GetBudget(playerNum) - totalMC);
    }

    static void GenerateInsight(GameState state, long playerNum, Field field, ICardCache cc)
    {
        long totalYield = 0;

        foreach (var res in field.Backend)
        {
            if (!res.FaceUp || res.MigratingFrom is not null)
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
                e.Duration is "this_turn" or "until_next_own_turn_end");
        }
    }

    static void ResetPerTurnFlags(GameState state, long playerNum, Field field)
    {
        foreach (var resource in FieldHelpers.AllResources(field))
        {
            resource.HasAttacked = false;
            resource.EffectUsedThisTurn = false;
            resource.ScaleChangedThisTurn = false;
            resource.MonetizedAmount = 0;
        }

        foreach (var support in FieldHelpers.AllSupports(field))
        {
            support.EffectUsedThisTurn = false;
        }

        state.SetIncidentPlayedThisTurn(playerNum, false);
    }

    private static GameEvent MakeTurnEndEvent(string gameID, string playerId, GameState state)
    {
        return new GameEvent
        {
            GameID = gameID,
            EventType = WireActionTypes.TurnEnd,
            PlayerID = playerId,
            EventData = new TurnEndEventData
            {
                Phase = "end",
                NextTurn = state.CurrentTurn,
                ActivePlayer = state.ActivePlayer,
                CurrentPhase = state.CurrentPhase.ToWireString(),
            }.ToDictionary(),
        };
    }
}
