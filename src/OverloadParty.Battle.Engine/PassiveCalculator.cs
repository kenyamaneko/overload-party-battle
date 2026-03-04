using System.Linq;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine;

/// <summary>
/// Calculates passive, platform, and attachment bonuses for resources.
/// </summary>
public static class PassiveCalculator
{
    public static long CalculatePassiveTPBonus(ResourceInstance instance, Field field, ICardCache cc)
    {
        var card = cc.MustGet(instance.CardID);
        long total = 0;
        foreach (var pe in card.PassiveEffects)
        {
            total += ApplyPassiveEffect(pe, instance, field, cc, "tp");
        }
        return total;
    }

    public static long CalculatePassiveYieldBonus(ResourceInstance instance, Field field, ICardCache cc)
    {
        var card = cc.MustGet(instance.CardID);
        long total = 0;
        foreach (var pe in card.PassiveEffects)
        {
            total += ApplyPassiveEffect(pe, instance, field, cc, "yield");
        }
        return total;
    }

    private static long ApplyPassiveEffect(PassiveEffect pe, ResourceInstance instance, Field field, ICardCache cc, string statType)
    {
        return pe.Type switch
        {
            PassiveEffectTypes.TPPerBackendDB when statType == "tp"
                => CalculateTPPerBackendDB(instance, field, pe.Params, cc),
            PassiveEffectTypes.TPPerBackendData when statType == "tp"
                => CalculateTPPerBackendData(instance, field, pe.Params, cc),
            PassiveEffectTypes.TPIfCardTypeOnField when statType == "tp"
                => CalculateTPIfCardTypeOnField(field, pe.Params, cc),
            PassiveEffectTypes.YieldPerOtherDB when statType == "yield"
                => CalculateYieldPerOtherDB(instance, field, pe.Params, cc),
            PassiveEffectTypes.YieldIfCardOnField when statType == "yield"
                => CalculateYieldIfCardOnField(field, pe.Params, cc),
            _ => 0
        };
    }

    private static long CalculateTPPerBackendDB(ResourceInstance instance, Field field, PassiveEffectConfig cfg, ICardCache cc)
    {
        int count = 0;
        foreach (var res in field.Backend.Where(r => r.FaceUp))
        {
            if (cfg.ExcludeSelf && res.InstanceID == instance.InstanceID) continue;

            var resCard = cc.Get(res.CardID);
            if (resCard is null) continue;
            if (resCard.CardType is not ("Database" or "CacheDB" or "Datawarehouse")) continue;

            count += cfg.MultiModelCards is { } mm && mm.Contains(resCard.CardNo) ? 2 : 1;
        }
        return cfg.BonusPerCard * count;
    }

    private static long CalculateTPPerBackendData(ResourceInstance instance, Field field, PassiveEffectConfig cfg, ICardCache cc)
    {
        int count = 0;
        foreach (var res in field.Backend.Where(r => r.FaceUp))
        {
            if (cfg.ExcludeSelf && res.InstanceID == instance.InstanceID) continue;

            var resCard = cc.Get(res.CardID);
            if (resCard is null || !resCard.IsDataType) continue;

            count += cfg.MultiModelCards is { } mm && mm.Contains(resCard.CardNo) ? 2 : 1;
        }
        return cfg.BonusPerCard * count;
    }

    private static long CalculateTPIfCardTypeOnField(Field field, PassiveEffectConfig cfg, ICardCache cc)
    {
        if (cfg.CardTypes?.Any() != true) return 0;

        foreach (var res in FieldHelpers.AllFaceUpResources(field))
        {
            var resCard = cc.Get(res.CardID);
            if (resCard is null) continue;

            if (cfg.CardTypes.Contains(resCard.CardType))
            {
                if (cfg.Faction is null || cfg.Faction == "" || resCard.Faction == cfg.Faction)
                    return cfg.FlatBonus;
            }
        }
        return 0;
    }

    private static long CalculateYieldPerOtherDB(ResourceInstance instance, Field field, PassiveEffectConfig cfg, ICardCache cc)
    {
        int count = 0;
        foreach (var res in field.Backend.Where(r => r.FaceUp))
        {
            if (res.InstanceID == instance.InstanceID) continue; // Always exclude self

            var resCard = cc.Get(res.CardID);
            if (resCard is null) continue;
            if (resCard.CardType is not ("Database" or "CacheDB" or "Datawarehouse")) continue;

            count += cfg.MultiModelCards is { } mm && mm.Contains(resCard.CardNo) ? 2 : 1;
        }
        return cfg.BonusPerCard * count;
    }

    private static long CalculateYieldIfCardOnField(Field field, PassiveEffectConfig cfg, ICardCache cc)
    {
        if (cfg.SpecificCardNos?.Any() != true) return 0;

        foreach (var res in FieldHelpers.AllFaceUpResources(field))
        {
            if (cfg.SpecificCardNos.Contains(res.CardID))
                return cfg.FlatBonus;
        }
        return 0;
    }

    /// <summary>
    /// Calculate platform bonus from support zone cards.
    /// </summary>
    public static long CalculatePlatformBonus(ResourceInstance instance, Field field, string statType, ICardCache cc)
    {
        long total = 0;
        foreach (var support in field.Support.Where(s => !s.FaceDown && s.DeployingTurnsLeft <= 0))
        {
            var supCard = cc.Get(support.CardID);
            if (supCard is null) continue;

            foreach (var pe in supCard.PlatformEffects)
            {
                total += ApplyPlatformEffect(pe, instance, statType, cc);
            }
        }
        return total;
    }

    private static long ApplyPlatformEffect(PlatformEffect pe, ResourceInstance target, string statType, ICardCache cc)
    {
        // Check stat type match
        string expectedStatType = pe.Type switch
        {
            PlatformEffectTypes.TPBonus => "tp",
            PlatformEffectTypes.YieldBonus => "yield",
            PlatformEffectTypes.AVBonus => "av",
            _ => ""
        };
        if (expectedStatType != statType) return 0;

        var cfg = pe.Params;
        var targetCard = cc.Get(target.CardID);
        if (targetCard is null) return 0;

        // Check faction match
        if (cfg.TargetFaction is { Length: > 0 } faction && targetCard.Faction != faction)
            return 0;

        // Check card type match
        if (cfg.TargetCardTypes?.Any() == true && !cfg.TargetCardTypes.Contains(targetCard.CardType))
            return 0;

        return cfg.Bonus;
    }

    /// <summary>
    /// Calculate attachment bonus from attached cards.
    /// </summary>
    public static long CalculateAttachmentBonus(ResourceInstance instance, string statType, ICardCache cc)
    {
        long total = 0;
        foreach (var att in instance.Attachments)
        {
            var attCard = cc.Get(att.CardID);
            if (attCard is null) continue;

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
