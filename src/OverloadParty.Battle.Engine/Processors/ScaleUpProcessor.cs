using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Processors;

public static class ScaleUpProcessor
{
    public static ActionResult Process(
        GameState state, Game game, long playerNum,
        ScaleUpRequest req, ICardCache cc)
    {
        var field = state.GetField(playerNum);

        var result = FieldHelpers.FindResourceByID(field, req.InstanceID);
        if (result is null)
            throw new GameRuleException($"resource {req.InstanceID} not found");

        var (resource, zone, idx) = result.Value;
        var card = cc.MustGet(resource.CardID);

        if (!card.Resizable)
            throw new GameRuleException("card is not resizable");

        var targetRank = EnumExtensions.ParseRank(req.TargetRank);
        ValidateRankProgression(resource.Rank, targetRank);

        // Instance family required for Small → Medium
        if (resource.Rank == Rank.Small && targetRank == Rank.Medium)
        {
            if (req.InstanceFamily is null)
                throw new GameRuleException("instance family required for small to medium scale-up");

            var family = EnumExtensions.ParseInstanceFamily(req.InstanceFamily);
            resource.InstanceFamily = family;
        }

        // Update rank
        resource.Rank = targetRank;

        // Recalculate stats
        resource.MaxAV = StatCalculator.CalculateMaxAV(resource, cc);
        // CurrentAV adjusts based on damage
        // (MaxAV changed but damage stays the same)

        if (!card.Elastic)
        {
            if (card.IsComputeType)
            {
                long newTP = StatCalculator.RecalculateMaxTP(resource, card);
                resource.MaxTP = newTP;
                resource.CurrentTP = newTP;
            }
            if (card.IsDataType)
            {
                long newYield = StatCalculator.RecalculateMaxYield(resource, card);
                resource.MaxYield = newYield;
                resource.CurrentYield = newYield;
            }
        }

        var playerId = playerNum == 1 ? game.Player1ID : game.Player2ID;
        var evt = new GameEvent
        {
            GameID = game.GameID,
            EventType = "scale_up",
            PlayerID = playerId,
            EventData = new Dictionary<string, object>
            {
                ["instanceId"] = req.InstanceID,
                ["targetRank"] = req.TargetRank,
            }
        };
        if (req.InstanceFamily is not null)
            evt.EventData["instanceFamily"] = req.InstanceFamily;

        return new ActionResult { Events = [evt], StateUpdated = true };
    }

    private static void ValidateRankProgression(Rank current, Rank target)
    {
        if (current == target)
            throw new GameRuleException($"already at rank {current.ToWireString()}");
        if (current == Rank.Large)
            throw new GameRuleException("already at maximum rank");

        bool valid = (current, target) switch
        {
            (Rank.Small, Rank.Medium) => true,
            (Rank.Medium, Rank.Large) => true,
            _ => false
        };

        if (!valid)
            throw new GameRuleException($"invalid rank progression: {current.ToWireString()} → {target.ToWireString()}");
    }
}
