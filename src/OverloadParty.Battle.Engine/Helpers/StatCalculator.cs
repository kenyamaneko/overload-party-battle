using System.Linq;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Helpers;

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
        if (rawBonus <= 0 || scale <= 0) { return rawBonus; }
        double result = scale * Math.Log(1.0 + (double)rawBonus / scale);
        return Truncate(result);
    }

    /// <summary>
    /// Calculate effective throughput for a resource, including all bonuses.
    /// </summary>
    public static long CalculateEffectiveTP(DeployedResource instance, Field field, ICardCache cc)
    {
        if (FieldHelpers.HasTemporaryEffect(instance, EffectTypes.TPSuppressed)) { return 0; }

        var card = cc.MustGet(instance.CardID);
        if (!card.IsComputeType || card.ComputeStats is null) { return 0; }

        long baseTP = card.ComputeStats.Throughput;
        long rankMult = BattleConstants.RankMultiplier(instance.Rank);

        // Instance family multiplier
        double tpMult = 1.0;
        if (instance.InstanceFamily is { } family)
        {
            var (tp, _) = BattleConstants.FamilyMultiplier(family);
            tpMult = tp;
        }

        long baseValue = Truncate(baseTP * rankMult * tpMult);

        // Elastic bonus with diminishing returns
        long elasticBonus = 0;
        if (card.Elastic && card.FreeTier > 0)
        {
            elasticBonus = EffectiveElasticBonus(instance.ElasticBonus, card.FreeTier);
        }

        long platformBonus = CalculatePlatformBonus(instance, field, BattleConstants.StatTP, cc);
        long passiveBonus = CalculatePassiveTPBonus(instance, field, cc);
        long attachmentBonus = CalculateAttachmentBonus(instance, field, BattleConstants.StatTP, cc);

        long tempBonus = instance.TemporaryEffects
            .Where(e => e.EffectType == EffectTypes.BuffTP).Sum(e => e.Value);
        long tempDebuff = instance.TemporaryEffects
            .Where(e => e.EffectType == EffectTypes.DebuffTP).Sum(e => e.Value);

        long total = baseValue + elasticBonus + platformBonus + passiveBonus + attachmentBonus + tempBonus - tempDebuff;
        return Math.Max(0, total);
    }

    /// <summary>
    /// Calculate effective yield for a resource, including all bonuses.
    /// </summary>
    public static long CalculateEffectiveInsight(DeployedResource instance, Field field, ICardCache cc)
    {
        var card = cc.MustGet(instance.CardID);
        if (!card.IsDataType || card.DataStats is null) { return 0; }

        long baseYield = card.DataStats.Yield;
        long rankMult = BattleConstants.RankMultiplier(instance.Rank);

        // Instance family multiplier (uses AV multiplier for yield)
        double yieldMult = 1.0;
        if (instance.InstanceFamily is { } family)
        {
            var (_, av) = BattleConstants.FamilyMultiplier(family);
            yieldMult = av;
        }

        long baseValue = Truncate(baseYield * rankMult * yieldMult);

        long elasticBonus = 0;
        if (card.Elastic && card.FreeTier > 0)
        {
            elasticBonus = EffectiveElasticBonus(instance.ElasticBonus, card.FreeTier);
        }

        long platformBonus = CalculatePlatformBonus(instance, field, BattleConstants.StatYield, cc);
        long passiveBonus = CalculatePassiveYieldBonus(instance, field, cc);
        long attachmentBonus = CalculateAttachmentBonus(instance, field, BattleConstants.StatYield, cc);

        long tempBonus = instance.TemporaryEffects
            .Where(e => e.EffectType == EffectTypes.BuffYield).Sum(e => e.Value);
        long tempDebuff = instance.TemporaryEffects
            .Where(e => e.EffectType == EffectTypes.DebuffYield).Sum(e => e.Value);

        long total = baseValue + elasticBonus + platformBonus + passiveBonus + attachmentBonus + tempBonus - tempDebuff;
        return Math.Max(0, total);
    }

    /// <summary>
    /// Calculate max AV for a resource, including all bonuses.
    /// </summary>
    public static long CalculateMaxAV(DeployedResource instance, Field field, ICardCache cc)
    {
        var card = cc.MustGet(instance.CardID);
        long baseAV = card.BaseAvailability;
        long rankMult = BattleConstants.RankMultiplier(instance.Rank);

        double avMult = 1.0;
        if (instance.InstanceFamily is { } family)
        {
            var (_, av) = BattleConstants.FamilyMultiplier(family);
            avMult = av;
        }

        long baseValue = Truncate(baseAV * rankMult * avMult);

        long platformBonus = CalculatePlatformBonus(instance, field, BattleConstants.StatAV, cc);
        long attachmentBonus = CalculateAttachmentBonus(instance, field, BattleConstants.StatAV, cc);

        long total = baseValue + platformBonus + attachmentBonus;
        return Math.Max(0, total);
    }

    /// <summary>
    /// Recalculate MaxTP after scale-up (non-elastic cards only).
    /// </summary>
    public static long RecalculateMaxTP(DeployedResource resource, CardDefinition card)
    {
        if (card.ComputeStats is null) { return 0; }
        long baseTP = card.ComputeStats.Throughput;
        long rankMult = BattleConstants.RankMultiplier(resource.Rank);

        double tpMult = 1.0;
        if (resource.InstanceFamily is { } family)
        {
            var (tp, _) = BattleConstants.FamilyMultiplier(family);
            tpMult = tp;
        }

        return Truncate(baseTP * rankMult * tpMult);
    }

    /// <summary>
    /// Recalculate MaxYield after scale-up (non-elastic cards only).
    /// </summary>
    public static long RecalculateMaxYield(DeployedResource resource, CardDefinition card)
    {
        if (card.DataStats is null) { return 0; }
        long baseYield = card.DataStats.Yield;
        long rankMult = BattleConstants.RankMultiplier(resource.Rank);

        double yieldMult = 1.0;
        if (resource.InstanceFamily is { } family)
        {
            var (_, av) = BattleConstants.FamilyMultiplier(family);
            yieldMult = av;
        }

        return Truncate(baseYield * rankMult * yieldMult);
    }

    /// <summary>
    /// Apply elastic bonus increment to a resource.
    /// </summary>
    public static void ApplyElasticBonus(DeployedResource resource, CardDefinition card)
    {
        if (card.Elastic && card.ElasticIncrement > 0)
        {
            resource.ElasticBonus += card.ElasticIncrement;
        }
    }

    /// <summary>Truncates a floating-point value to a long integer (floor towards zero).</summary>
    /// <param name="val">The value to truncate.</param>
    public static long Truncate(double val) => (long)val;

    // ─── Passive bonuses ─────────────────────────────────────

    static long CalculatePassiveTPBonus(DeployedResource instance, Field field, ICardCache cc)
    {
        var card = cc.MustGet(instance.CardID);
        return card.PassiveEffects.Sum(pe => ApplyPassiveEffect(pe, instance, field, cc, BattleConstants.StatTP));
    }

    static long CalculatePassiveYieldBonus(DeployedResource instance, Field field, ICardCache cc)
    {
        var card = cc.MustGet(instance.CardID);
        return card.PassiveEffects.Sum(pe => ApplyPassiveEffect(pe, instance, field, cc, BattleConstants.StatYield));
    }

    private static long ApplyPassiveEffect(PassiveEffect pe, DeployedResource instance, Field field, ICardCache cc, string statType)
    {
        return pe.Type switch
        {
            PassiveEffectTypes.TPPerBackendDB when statType == BattleConstants.StatTP
                => CalculateTPPerBackendDB(instance, field, pe.Params, cc),
            PassiveEffectTypes.TPPerBackendData when statType == BattleConstants.StatTP
                => CalculateTPPerBackendData(instance, field, pe.Params, cc),
            PassiveEffectTypes.TPIfCardTypeOnField when statType == BattleConstants.StatTP
                => CalculateTPIfCardTypeOnField(field, pe.Params, cc),
            PassiveEffectTypes.YieldPerOtherDB when statType == BattleConstants.StatYield
                => CalculateYieldPerOtherDB(instance, field, pe.Params, cc),
            PassiveEffectTypes.YieldIfCardOnField when statType == BattleConstants.StatYield
                => CalculateYieldIfCardOnField(field, pe.Params, cc),
            _ => 0
        };
    }

    private static long CalculateTPPerBackendDB(DeployedResource instance, Field field, PassiveEffectConfig cfg, ICardCache cc)
    {
        int count = 0;
        foreach (var res in field.Backend.Where(r => r.FaceUp))
        {
            if (cfg.ExcludeSelf && res.InstanceID == instance.InstanceID) { continue; }

            var resCard = cc.Get(res.CardID);
            if (resCard is null) { continue; }
            if (resCard.CardType is not (CardTypes.Database or CardTypes.CacheDB)) { continue; }

            count += cfg.MultiModelCardIDs is { } mm && mm.Contains(resCard.CardId) ? 2 : 1;
        }
        return cfg.BonusPerCard * count;
    }

    private static long CalculateTPPerBackendData(DeployedResource instance, Field field, PassiveEffectConfig cfg, ICardCache cc)
    {
        int count = 0;
        foreach (var res in field.Backend.Where(r => r.FaceUp))
        {
            if (cfg.ExcludeSelf && res.InstanceID == instance.InstanceID) { continue; }

            var resCard = cc.Get(res.CardID);
            if (resCard is null || !resCard.IsDataType) { continue; }

            count += cfg.MultiModelCardIDs is { } mm && mm.Contains(resCard.CardId) ? 2 : 1;
        }
        return cfg.BonusPerCard * count;
    }

    private static long CalculateTPIfCardTypeOnField(Field field, PassiveEffectConfig cfg, ICardCache cc)
    {
        if (!(cfg.CardTypes?.Count > 0)) { return 0; }

        foreach (var res in FieldHelpers.AllFaceUpResources(field))
        {
            var resCard = cc.Get(res.CardID);
            if (resCard is null){ continue; }

            if (cfg.CardTypes.Contains(resCard.CardType))
            {
                if (cfg.Faction is null
                    || cfg.Faction == ""
                    || resCard.Faction == cfg.Faction)
                {
                    return cfg.FlatBonus;
                }
            }
        }
        return 0;
    }

    private static long CalculateYieldPerOtherDB(DeployedResource instance, Field field, PassiveEffectConfig cfg, ICardCache cc)
    {
        int count = 0;
        foreach (var res in field.Backend.Where(r => r.FaceUp))
        {
            if (res.InstanceID == instance.InstanceID) { continue; }

            var resCard = cc.Get(res.CardID);
            if (resCard is null) { continue; }
            if (resCard.CardType is not (CardTypes.Database or CardTypes.CacheDB)) { continue; }

            count += cfg.MultiModelCardIDs is { } mm && mm.Contains(resCard.CardId) ? 2 : 1;
        }
        return cfg.BonusPerCard * count;
    }

    private static long CalculateYieldIfCardOnField(Field field, PassiveEffectConfig cfg, ICardCache cc)
    {
        if (!(cfg.SpecificCardIDs?.Count > 0)) { return 0; }

        foreach (var res in FieldHelpers.AllFaceUpResources(field))
        {
            var resCard = cc.Get(res.CardID);
            if (resCard is not null && cfg.SpecificCardIDs.Contains(resCard.CardId)) { return cfg.FlatBonus; }
        }
        return 0;
    }

    // ─── Platform bonuses ────────────────────────────────────

    static long CalculatePlatformBonus(DeployedResource instance, Field field, string statType, ICardCache cc)
    {
        long total = 0;
        foreach (var support in field.Support.Where(s => s.FaceUp && s.DeployingTurnsLeft <= 0))
        {
            var supCard = cc.Get(support.CardID);
            if (supCard is null) { continue; }

            foreach (var pe in supCard.PlatformEffects)
            {
                total += ApplyPlatformEffect(pe, instance, statType, cc);
            }
        }
        return total;
    }

    private static long ApplyPlatformEffect(PlatformEffect pe, DeployedResource target, string statType, ICardCache cc)
    {
        string expectedStatType = pe.Type switch
        {
            PlatformEffectTypes.TPBonus => BattleConstants.StatTP,
            PlatformEffectTypes.YieldBonus => BattleConstants.StatYield,
            PlatformEffectTypes.AVBonus => BattleConstants.StatAV,
            _ => ""
        };
        if (expectedStatType != statType) { return 0; }

        var cfg = pe.Params;
        var targetCard = cc.Get(target.CardID);
        if (targetCard is null) { return 0; }

        if (cfg.TargetFaction is { Length: > 0 } faction
            && targetCard.Faction != faction) { return 0; }


        if (cfg.TargetCardTypes?.Count > 0
            && !cfg.TargetCardTypes.Contains(targetCard.CardType)) { return 0; }

        return cfg.Bonus;
    }

    // ─── Attachment bonuses ──────────────────────────────────

    static long CalculateAttachmentBonus(DeployedResource instance, Field field, string statType, ICardCache cc)
    {
        long total = 0;
        foreach (var att in field.Support.Where(a => a.TargetInstanceID == instance.InstanceID))
        {
            var attCard = cc.Get(att.CardID);
            if (attCard is null) { continue; }

            foreach (var ae in attCard.AttachmentEffects)
            {
                if (ae.Type == AttachmentEffectTypes.StatBonus && ae.Params.StatType == statType)
                {
                    total += ae.Params.Bonus;
                }
            }
        }
        return total;
    }
}
