using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Processors;

/// <summary>
/// Processes scale-up actions that change a resource's rank or instance family.
/// </summary>
public static class ScaleUpProcessor
{
    /// <summary>
    /// Changes a resource's rank and optionally its instance family.
    /// </summary>
    /// <param name="state">The current game state.</param>
    /// <param name="game">The game metadata.</param>
    /// <param name="playerNum">The player number performing the action.</param>
    /// <param name="req">The scale-up request containing target rank and optional instance family.</param>
    /// <param name="cc">The card definition cache.</param>
    /// <returns>The action result containing the scale-up event and state update flag.</returns>
    public static ActionResult Process(
        GameState state, Game game, long playerNum,
        ScaleUpRequest req, ICardCache cc)
    {
        var field = state.GetField(playerNum);

        var resource = FieldHelpers.FindResourceByID(field, req.InstanceID)
            ?? throw new GameRuleException($"resource {req.InstanceID} not found");
        var card = cc.MustGet(resource.CardID);

        if (!card.Resizable)
        {
            throw new GameRuleException("card is not resizable");
        }

        if (resource.DeployedOnTurn == state.CurrentTurn)
        {
            throw new GameRuleException("cannot change instance type on deploy turn");
        }

        if (resource.ScaleChangedThisTurn)
        {
            throw new GameRuleException("instance type already changed this turn");
        }

        var targetRank = EnumExtensions.ParseRank(req.TargetRank);

        if (resource.Rank is null || targetRank <= resource.Rank)
        {
            throw new GameRuleException("can only scale up to a higher rank");
        }

        var targetFamily = req.InstanceFamily is not null
            ? EnumExtensions.ParseInstanceFamily(req.InstanceFamily)
            : resource.InstanceFamily;

        if (targetFamily is null)
        {
            throw new GameRuleException("instance family required for medium or large rank");
        }

        if (resource.Rank == Rank.Medium && targetFamily != resource.InstanceFamily)
        {
            throw new GameRuleException("cannot change instance family when scaling up from medium");
        }
        resource.InstanceFamily = targetFamily;
        resource.ScaleChangedThisTurn = true;

        ResourceHelpers.ChangeRank(resource, targetRank, cc);

        var playerId = game.GetPlayerID(playerNum);
        var evt = new GameEvent
        {
            GameID = game.GameID,
            EventType = WireActionTypes.ScaleUp,
            PlayerID = playerId,
            EventData = new ScaleUpEventData
            {
                InstanceId = req.InstanceID,
                TargetRank = req.TargetRank,
                InstanceFamily = req.InstanceFamily,
            }.ToDictionary(),
        };

        return new ActionResult { Events = [evt], StateUpdated = true };
    }
}
