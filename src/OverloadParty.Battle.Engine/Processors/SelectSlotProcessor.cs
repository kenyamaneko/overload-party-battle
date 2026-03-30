using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Processors;

/// <summary>
/// Resolves a pending effect deploy by placing the resource at the player-chosen slot.
/// </summary>
public static class SelectSlotProcessor
{
    public static ActionResult Process(
        GameState state, Game game, long playerNum,
        SelectSlotRequest req, ICardCache cc)
    {
        var pending = state.AwaitingSlotSelect
            ?? throw new GameRuleException("No pending slot selection");

        if (pending.PlayerNum != playerNum)
        {
            throw new GameRuleException("Slot selection is for a different player");
        }

        string slotKey = $"{req.Zone}_{req.Index}";
        if (!pending.ValidZones.Contains(slotKey))
        {
            throw new GameRuleException($"Invalid slot: {slotKey}");
        }

        var field = state.GetField(playerNum);
        ValidateAndPlace(field, pending.Resource, req.Zone, req.Index);

        state.AwaitingSlotSelect = null;

        string playerId = game.GetPlayerID(playerNum);
        var events = new List<GameEvent>
        {
            new()
            {
                GameID = game.GameID,
                EventType = WireActionTypes.SelectSlot,
                PlayerID = playerId,
                EventData = new Dictionary<string, object>
                {
                    ["cardId"] = pending.Resource.CardID,
                    ["instanceId"] = pending.Resource.InstanceID,
                    ["zone"] = req.Zone,
                    ["index"] = req.Index,
                },
            },
        };

        return new ActionResult { Events = events, StateUpdated = true };
    }

    private static void ValidateAndPlace(Field field, DeployedResource resource, string zone, int index)
    {
        if (index < 0 || index >= BattleConstants.SlotsPerZone)
        {
            throw new GameRuleException($"Invalid slot index {index}");
        }

        switch (zone)
        {
            case GameConstants.ZoneFrontend:
                if (field.Frontend[index] is not null)
                {
                    throw new GameRuleException($"Frontend slot {index} is occupied");
                }
                field.Frontend[index] = resource;
                break;

            case GameConstants.ZoneBackend:
                if (field.Backend[index] is not null)
                {
                    throw new GameRuleException($"Backend slot {index} is occupied");
                }
                field.Backend[index] = resource;
                break;

            default:
                throw new GameRuleException($"Unknown zone: {zone}");
        }
    }
}
