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
        var result = new List<AvailableAction>();
        foreach (var a in actions)
            if (a.Type == actionType) result.Add(a);
        return result;
    }

    /// <summary>
    /// Selects the best zone from validZones based on card type strategy.
    /// </summary>
    public static string? PickBestZone(List<string>? validZones, CardDefinition cardDef, HashSet<string> usedZones)
    {
        if (validZones is null) return null;
        var available = FilterZones(validZones, usedZones);

        if (cardDef.IsComputeType)
        {
            var z = FirstWithPrefix(available, "frontend_");
            if (z is not null) return z;
            z = FirstWithPrefix(available, "backend_");
            if (z is not null) return z;
        }
        else if (cardDef.CardType == "ObjectStorage")
        {
            var z = FirstWithPrefix(available, "backend_");
            if (z is not null) return z;
            z = FirstWithPrefix(available, "frontend_");
            if (z is not null) return z;
        }
        else if (cardDef.IsDataType)
        {
            var z = FirstWithPrefix(available, "backend_");
            if (z is not null) return z;
        }
        else if (cardDef.IsSupportType || FieldHelpers.IsImmediateType(cardDef.CardType))
        {
            var z = FirstWithPrefix(available, "support_");
            if (z is not null) return z;
        }

        return available.Count > 0 ? available[0] : null;
    }

    public static string? PickSupportZone(List<string>? validZones, HashSet<string> usedZones)
    {
        if (validZones is null) return null;
        foreach (var z in validZones)
            if (z.StartsWith("support_") && !usedZones.Contains(z)) return z;
        return null;
    }

    public static SlotPosition? ParseZoneStr(string zone)
    {
        int idx = zone.LastIndexOf('_');
        if (idx < 0) return null;
        if (!int.TryParse(zone[(idx + 1)..], out int index)) return null;
        return new SlotPosition { Zone = zone[..idx], Index = index };
    }

    public static long ResolveCardNoForInstance(string instanceId, Field field)
    {
        for (int i = 0; i < GameConstants.SlotsPerZone; i++)
        {
            if (field.Frontend[i] is { } r && r.InstanceID == instanceId) return r.CardID;
            if (field.Backend[i] is { } b && b.InstanceID == instanceId) return b.CardID;
            if (field.Support[i] is { } s && s.InstanceID == instanceId) return s.CardID;
        }
        return 0;
    }

    /// <summary>
    /// Picks the target with lowest effective AV from validTargets.
    /// </summary>
    public static string? FindBestTargetFromValid(List<string>? validTargets, Field oppField)
    {
        if (validTargets is null || validTargets.Count == 0) return null;

        var resMap = new Dictionary<string, ResourceInstance>();
        for (int i = 0; i < GameConstants.SlotsPerZone; i++)
        {
            if (oppField.Frontend[i] is { } fr) resMap[fr.InstanceID] = fr;
            if (oppField.Backend[i] is { } br) resMap[br.InstanceID] = br;
        }

        string? bestId = null;
        long bestAV = long.MaxValue;
        foreach (var id in validTargets)
        {
            if (!resMap.TryGetValue(id, out var r)) continue;
            if (r.EffectiveAV < bestAV) { bestAV = r.EffectiveAV; bestId = id; }
        }
        return bestId;
    }

    private static List<string> FilterZones(List<string> validZones, HashSet<string> usedZones)
    {
        var result = new List<string>();
        foreach (var z in validZones)
            if (!usedZones.Contains(z)) result.Add(z);
        return result;
    }

    private static string? FirstWithPrefix(List<string> zones, string prefix)
    {
        foreach (var z in zones)
            if (z.StartsWith(prefix)) return z;
        return null;
    }
}

public class SlotPosition
{
    public string Zone { get; init; } = "";
    public int Index { get; init; }
}
