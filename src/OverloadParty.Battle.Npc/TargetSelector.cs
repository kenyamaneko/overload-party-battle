using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Npc;

/// <summary>
/// Target selection helpers for NPC category-based decisions.
/// </summary>
public static class TargetSelector
{
    /// <summary>
    /// Returns the instanceID of the resource with the lowest effective AV.
    /// </summary>
    public static string? WeakestInZone(Field field, string? zone)
    {
        return FaceUpInZone(field, zone)
            .MinBy(r => r.EffectiveAV)
            ?.InstanceID;
    }

    /// <summary>
    /// Returns the instanceID of the resource with the highest TP or Yield.
    /// </summary>
    public static string? StrongestInZone(Field field, string? zone, ICardCache cc)
    {
        return FaceUpInZone(field, zone)
            .MaxBy(r => ResourceValue(r, cc))
            ?.InstanceID;
    }

    /// <summary>
    /// Returns the instanceID of the own resource with the most damage.
    /// </summary>
    public static string? MostDamagedOwn(Field field)
    {
        return FieldHelpers.AllFaceUpResources(field)
            .Where(r => r.Damage > 0)
            .MaxBy(r => r.Damage)
            ?.InstanceID;
    }

    public static int CountAllResources(Field field)
    {
        return FieldHelpers.AllFaceUpResources(field).Count();
    }

    public static int CountResourcesInZone(Field field, string? zone)
    {
        return FaceUpInZone(field, zone).Count();
    }

    public static bool HasDamagedResource(Field field)
    {
        return FieldHelpers.AllFaceUpResources(field).Any(r => r.Damage > 0);
    }

    public static bool HasFaceDownSupport(Field field)
    {
        return field.Support.Any(s => !s.FaceUp);
    }

    public static bool HasPlatform(Field field, ICardCache cc)
    {
        return field.Support
            .Any(sup => cc.Get(sup.CardID)?.CardType == CardTypes.Platform);
    }

    public static string? FirstPlatformId(Field field, ICardCache cc)
    {
        return field.Support
            .FirstOrDefault(sup => cc.Get(sup.CardID)?.CardType == CardTypes.Platform)
            ?.InstanceID;
    }

    public static long ResourceValue(ResourceInstance r, ICardCache cc)
    {
        if (r.CurrentTP is > 0)
        {
            return r.CurrentTP.Value;
        }
        if (r.CurrentYield is > 0)
        {
            return r.CurrentYield.Value;
        }
        var card = cc.Get(r.CardID);
        if (card is null)
        {
            return 0;
        }
        return card.IsComputeType ? card.BaseThroughput : card.IsDataType ? card.BaseYield : 0;
    }

    // ─── Private helpers ────────────────────────────────────────

    private static IEnumerable<ResourceInstance> FaceUpInZone(Field field, string? zone)
    {
        var sources = Enumerable.Empty<ResourceInstance>();
        if (zone is null or "" or GameConstants.ZoneFrontend)
        {
            sources = sources.Concat(field.Frontend);
        }
        if (zone is null or "" or GameConstants.ZoneBackend)
        {
            sources = sources.Concat(field.Backend);
        }
        return sources.Where(r => r.FaceUp);
    }
}
