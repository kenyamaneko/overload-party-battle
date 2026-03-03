using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine;

/// <summary>
/// Calculates effective stats for resources (TP, Yield, AV, MC).
/// Applies rank multipliers, instance family modifiers, elastic scaling,
/// passive bonuses, platform bonuses, attachment bonuses, and temporary effects.
/// </summary>
public static class StatCalculator
{
    /// <summary>
    /// Logarithmic diminishing returns for elastic bonus.
    /// Formula: scale * ln(1 + rawBonus / scale)
    /// </summary>
    public static long EffectiveElasticBonus(long rawBonus, long scale)
    {
        if (rawBonus <= 0 || scale <= 0) return rawBonus;
        double result = scale * Math.Log(1.0 + (double)rawBonus / scale);
        return Truncate(result);
    }

    /// <summary>
    /// Calculate effective throughput for a resource, including all bonuses.
    /// </summary>
    public static long CalculateEffectiveTP(ResourceInstance instance, Field field, ICardCache cc)
    {
        var card = cc.MustGet(instance.CardID);
        if (!card.IsComputeType || card.ComputeStats is null)
            return 0;

        long baseTP = card.ComputeStats.Throughput;
        long rankMult = GameConstants.RankMultiplier(instance.Rank);

        // Instance family multiplier
        double tpMult = 1.0;
        if (instance.InstanceFamily is { } family)
        {
            var (tp, _) = GameConstants.FamilyMultiplier(family);
            tpMult = tp;
        }

        long baseValue = Truncate(baseTP * rankMult * tpMult);

        // Elastic bonus with diminishing returns
        long elasticBonus = 0;
        if (card.Elastic && card.FreeTier > 0)
        {
            elasticBonus = EffectiveElasticBonus(instance.ElasticBonus, card.FreeTier);
        }

        // Platform bonus from support zone
        long platformBonus = PassiveCalculator.CalculatePlatformBonus(instance, field, "tp", cc);

        // Resource's own passive TP bonus
        long passiveBonus = PassiveCalculator.CalculatePassiveTPBonus(instance, field, cc);

        // Attachment bonus
        long attachmentBonus = PassiveCalculator.CalculateAttachmentBonus(instance, "tp", cc);

        // Temporary effects
        long tempBonus = 0;
        long tempDebuff = 0;
        foreach (var eff in instance.TemporaryEffects)
        {
            if (eff.EffectType == "buff_tp")
                tempBonus += eff.Value;
            else if (eff.EffectType == "debuff_tp")
                tempDebuff += eff.Value;
        }

        long total = baseValue + elasticBonus + platformBonus + passiveBonus + attachmentBonus + tempBonus - tempDebuff;
        return Math.Max(0, total);
    }

    /// <summary>
    /// Calculate effective yield for a resource, including all bonuses.
    /// </summary>
    public static long CalculateEffectiveYield(ResourceInstance instance, Field field, ICardCache cc)
    {
        var card = cc.MustGet(instance.CardID);
        if (!card.IsDataType || card.DataStats is null)
            return 0;

        long baseYield = card.DataStats.Yield;
        long rankMult = GameConstants.RankMultiplier(instance.Rank);

        // Instance family multiplier (uses AV multiplier for yield)
        double yieldMult = 1.0;
        if (instance.InstanceFamily is { } family)
        {
            var (_, av) = GameConstants.FamilyMultiplier(family);
            yieldMult = av;
        }

        long baseValue = Truncate(baseYield * rankMult * yieldMult);

        // Elastic bonus
        long elasticBonus = 0;
        if (card.Elastic && card.FreeTier > 0)
        {
            elasticBonus = EffectiveElasticBonus(instance.ElasticBonus, card.FreeTier);
        }

        // Platform bonus from support zone
        long platformBonus = PassiveCalculator.CalculatePlatformBonus(instance, field, "yield", cc);

        // Resource's own passive yield bonus
        long passiveBonus = PassiveCalculator.CalculatePassiveYieldBonus(instance, field, cc);

        // Attachment bonus
        long attachmentBonus = PassiveCalculator.CalculateAttachmentBonus(instance, "yield", cc);

        // Temporary effects
        long tempBonus = 0;
        long tempDebuff = 0;
        foreach (var eff in instance.TemporaryEffects)
        {
            if (eff.EffectType == "buff_yield")
                tempBonus += eff.Value;
            else if (eff.EffectType == "debuff_yield")
                tempDebuff += eff.Value;
        }

        long total = baseValue + elasticBonus + platformBonus + passiveBonus + attachmentBonus + tempBonus - tempDebuff;
        return Math.Max(0, total);
    }

    /// <summary>
    /// Calculate max AV for a resource.
    /// </summary>
    public static long CalculateMaxAV(ResourceInstance instance, ICardCache cc)
    {
        var card = cc.MustGet(instance.CardID);
        long baseAV = card.BaseAvailability;
        long rankMult = GameConstants.RankMultiplier(instance.Rank);

        double avMult = 1.0;
        if (instance.InstanceFamily is { } family)
        {
            var (_, av) = GameConstants.FamilyMultiplier(family);
            avMult = av;
        }

        return Truncate(baseAV * rankMult * avMult);
    }

    /// <summary>
    /// Recalculate MaxTP after scale-up (non-elastic cards only).
    /// </summary>
    public static long RecalculateMaxTP(ResourceInstance resource, CardDefinition card)
    {
        if (card.ComputeStats is null) return 0;
        long baseTP = card.ComputeStats.Throughput;
        long rankMult = GameConstants.RankMultiplier(resource.Rank);

        double tpMult = 1.0;
        if (resource.InstanceFamily is { } family)
        {
            var (tp, _) = GameConstants.FamilyMultiplier(family);
            tpMult = tp;
        }

        return Truncate(baseTP * rankMult * tpMult);
    }

    /// <summary>
    /// Recalculate MaxYield after scale-up (non-elastic cards only).
    /// </summary>
    public static long RecalculateMaxYield(ResourceInstance resource, CardDefinition card)
    {
        if (card.DataStats is null) return 0;
        long baseYield = card.DataStats.Yield;
        long rankMult = GameConstants.RankMultiplier(resource.Rank);

        double yieldMult = 1.0;
        if (resource.InstanceFamily is { } family)
        {
            var (_, av) = GameConstants.FamilyMultiplier(family);
            yieldMult = av;
        }

        return Truncate(baseYield * rankMult * yieldMult);
    }

    /// <summary>
    /// Apply elastic bonus increment to a resource.
    /// </summary>
    public static void ApplyElasticBonus(ResourceInstance resource, CardDefinition card)
    {
        if (card.Elastic && card.ElasticIncrement > 0)
        {
            resource.ElasticBonus += card.ElasticIncrement;
        }
    }

    public static long Truncate(double val) => (long)val;
}
