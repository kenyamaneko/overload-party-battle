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
        ResourceInstance? best = null;
        long bestAV = long.MaxValue;

        if (zone is null or "" or "frontend")
        {
            for (int i = 0; i < GameConstants.SlotsPerZone; i++)
            {
                if (field.Frontend[i] is not { FaceUp: true } r) continue;
                if (r.EffectiveAV < bestAV) { bestAV = r.EffectiveAV; best = r; }
            }
        }
        if (zone is null or "" or "backend")
        {
            for (int i = 0; i < GameConstants.SlotsPerZone; i++)
            {
                if (field.Backend[i] is not { FaceUp: true } r) continue;
                if (r.EffectiveAV < bestAV) { bestAV = r.EffectiveAV; best = r; }
            }
        }

        return best?.InstanceID;
    }

    /// <summary>
    /// Returns the instanceID of the resource with the highest TP or Yield.
    /// </summary>
    public static string? StrongestInZone(Field field, string? zone, ICardCache cc)
    {
        ResourceInstance? best = null;
        long bestValue = 0;

        void Scan(ResourceInstance?[] resources)
        {
            foreach (var r in resources)
            {
                if (r is null || !r.FaceUp) continue;
                long val = ResourceValue(r, cc);
                if (val > bestValue) { bestValue = val; best = r; }
            }
        }

        if (zone is null or "" or "frontend") Scan(field.Frontend);
        if (zone is null or "" or "backend") Scan(field.Backend);

        return best?.InstanceID;
    }

    /// <summary>
    /// Returns the instanceID of the own resource with the most damage.
    /// </summary>
    public static string? MostDamagedOwn(Field field)
    {
        ResourceInstance? best = null;
        long bestDamage = 0;

        for (int i = 0; i < GameConstants.SlotsPerZone; i++)
        {
            if (field.Frontend[i] is { FaceUp: true } fr && fr.Damage > bestDamage)
            { bestDamage = fr.Damage; best = fr; }
            if (field.Backend[i] is { FaceUp: true } br && br.Damage > bestDamage)
            { bestDamage = br.Damage; best = br; }
        }

        return best is { Damage: > 0 } ? best.InstanceID : null;
    }

    public static int CountAllResources(Field field)
    {
        int count = 0;
        for (int i = 0; i < GameConstants.SlotsPerZone; i++)
        {
            if (field.Frontend[i] is { FaceUp: true }) count++;
            if (field.Backend[i] is { FaceUp: true }) count++;
        }
        return count;
    }

    public static int CountResourcesInZone(Field field, string? zone)
    {
        int count = 0;
        if (zone is null or "" or "frontend")
        {
            for (int i = 0; i < GameConstants.SlotsPerZone; i++)
                if (field.Frontend[i] is { FaceUp: true }) count++;
        }
        if (zone is null or "" or "backend")
        {
            for (int i = 0; i < GameConstants.SlotsPerZone; i++)
                if (field.Backend[i] is { FaceUp: true }) count++;
        }
        return count;
    }

    public static bool HasDamagedResource(Field field)
    {
        for (int i = 0; i < GameConstants.SlotsPerZone; i++)
        {
            if (field.Frontend[i] is { FaceUp: true, Damage: > 0 }) return true;
            if (field.Backend[i] is { FaceUp: true, Damage: > 0 }) return true;
        }
        return false;
    }

    public static bool HasFaceDownSupport(Field field)
    {
        for (int i = 0; i < GameConstants.SlotsPerZone; i++)
            if (field.Support[i] is { FaceDown: true }) return true;
        return false;
    }

    public static bool HasPlatform(Field field, ICardCache cc)
    {
        for (int i = 0; i < GameConstants.SlotsPerZone; i++)
        {
            if (field.Support[i] is not { } sup) continue;
            var card = cc.Get(sup.CardID);
            if (card?.CardType == "Platform") return true;
        }
        return false;
    }

    public static string? FirstPlatformId(Field field, ICardCache cc)
    {
        for (int i = 0; i < GameConstants.SlotsPerZone; i++)
        {
            if (field.Support[i] is not { } sup) continue;
            var card = cc.Get(sup.CardID);
            if (card?.CardType == "Platform") return sup.InstanceID;
        }
        return null;
    }

    public static long ResourceValue(ResourceInstance r, ICardCache cc)
    {
        if (r.CurrentTP is > 0) return r.CurrentTP.Value;
        if (r.CurrentYield is > 0) return r.CurrentYield.Value;
        var card = cc.Get(r.CardID);
        if (card is null) return 0;
        return card.IsComputeType ? card.BaseThroughput : card.IsDataType ? card.BaseYield : 0;
    }
}
