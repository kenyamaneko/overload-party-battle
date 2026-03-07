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

        // Cannot change type on the deploy turn
        if (resource.DeployedOnTurn == state.CurrentTurn)
        {
            throw new GameRuleException("cannot change instance type on deploy turn");
        }

        // 1 type change per resource per turn
        if (resource.ScaleChangedThisTurn)
        {
            throw new GameRuleException("instance type already changed this turn");
        }

        var targetRank = EnumExtensions.ParseRank(req.TargetRank);
        var targetFamily = req.InstanceFamily is not null
            ? EnumExtensions.ParseInstanceFamily(req.InstanceFamily)
            : resource.InstanceFamily;

        // Must have a family for Medium/Large
        if (targetRank != Rank.Small && targetFamily is null)
        {
            throw new GameRuleException("instance family required for medium or large rank");
        }

        // Validate that something actually changes
        if (resource.Rank == targetRank && resource.InstanceFamily == targetFamily)
        {
            throw new GameRuleException("no change in instance type");
        }

        // Apply changes
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
