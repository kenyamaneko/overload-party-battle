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
        if (validZones is null || validZones.Count == 0) { return null; }
        var available = FilterZones(validZones, usedZones);
        if (available.Count == 0) { return null; }

        if (cardDef.IsComputeType)
        {
            var frontend = FirstWithPrefix(available, "frontend_");
            if (frontend is not null) { return frontend; }
            var backend = FirstWithPrefix(available, "backend_");
            if (backend is not null) { return backend; }
            throw new InvalidOperationException(
                $"PickBestZone: Compute card '{cardDef.CardId}' received validZones with no frontend/backend: [{string.Join(", ", available)}]");
        }

        if (cardDef.CardType == CardTypes.ObjectStorage)
        {
            var backend = FirstWithPrefix(available, "backend_");
            if (backend is not null) { return backend; }
            var frontend = FirstWithPrefix(available, "frontend_");
            if (frontend is not null) { return frontend; }
            throw new InvalidOperationException(
                $"PickBestZone: ObjectStorage card '{cardDef.CardId}' received validZones with no frontend/backend: [{string.Join(", ", available)}]");
        }

        // Data / Support / Incident / Strategy: AvailableActions emits exactly one zone kind; any available entry is valid.
        return available[0];
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

        throw new InvalidOperationException($"Instance '{instanceId}' not found on field");
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
