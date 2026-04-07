using System.Linq;
using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Npc;

/// <summary>
/// Helper functions for filtering and selecting NPC actions.
/// </summary>
public static class ActionFilter
{
    public static List<AvailableAction> FilterByType(List<AvailableAction> actions, string actionType)
    {
        return actions.Where(a => a.Type == actionType).ToList();
    }

    /// <summary>
    /// Selects the best zone from validZones based on card type strategy.
    /// </summary>
    public static string? PickBestZone(List<string>? validZones, CardDefinition cardDef, HashSet<string> usedZones)
    {
        if (validZones is null)
        {
            return null;
        }
        var available = FilterZones(validZones, usedZones);

        if (cardDef.IsComputeType)
        {
            var z = FirstWithPrefix(available, "frontend_");
            if (z is not null)
            {
                return z;
            }
            z = FirstWithPrefix(available, "backend_");
            if (z is not null)
            {
                return z;
            }
        }
        else if (cardDef.CardType == CardTypes.ObjectStorage)
        {
            var z = FirstWithPrefix(available, "backend_");
            if (z is not null)
            {
                return z;
            }
            z = FirstWithPrefix(available, "frontend_");
            if (z is not null)
            {
                return z;
            }
        }
        else if (cardDef.IsDataType)
        {
            var z = FirstWithPrefix(available, "backend_");
            if (z is not null)
            {
                return z;
            }
        }
        else if (cardDef.IsSupportType || FieldHelpers.IsImmediateType(cardDef.CardType))
        {
            var z = FirstWithPrefix(available, "support_");
            if (z is not null)
            {
                return z;
            }
        }

        return available.FirstOrDefault();
    }

    public static string? PickSupportZone(List<string>? validZones, HashSet<string> usedZones)
    {
        return validZones?.FirstOrDefault(z => z.StartsWith("support_") && !usedZones.Contains(z));
    }

    public static SlotPosition? ParseZoneStr(string zone)
    {
        int idx = zone.LastIndexOf('_');
        if (idx < 0)
        {
            return null;
        }
        if (!int.TryParse(zone[(idx + 1)..], out int index))
        {
            return null;
        }
        return new SlotPosition { Zone = zone[..idx], Index = index };
    }

    public static string ResolveCardIdForInstance(string instanceId, Field field)
    {
        var resource = FieldHelpers.AllResources(field).FirstOrDefault(r => r.InstanceID == instanceId);
        if (resource is not null)
        {
            return resource.CardID;
        }

        var support = field.Support.FirstOrDefault(s => s.InstanceID == instanceId);
        if (support is not null)
        {
            return support.CardID;
        }

        return "";
    }

    private static List<string> FilterZones(List<string> validZones, HashSet<string> usedZones)
    {
        return validZones.Where(z => !usedZones.Contains(z)).ToList();
    }

    private static string? FirstWithPrefix(List<string> zones, string prefix)
    {
        return zones.FirstOrDefault(z => z.StartsWith(prefix));
    }
}

public class SlotPosition
{
    public string Zone { get; init; } = "";
    public int Index { get; init; }
}
