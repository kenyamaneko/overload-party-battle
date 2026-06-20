using System.Linq;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Helpers;

/// <summary>
/// Calculates effective stats for resources (TP, Yield, AV, MC).
/// Applies rank multipliers, instance family modifiers, elastic scaling,
/// and temporary effects.
/// </summary>
public static class StatCalculator
{
    /// <summary>
    /// Logarithmic diminishing returns for elastic bonus.
    /// Formula: scale * ln(1 + rawBonus / scale)
    /// </summary>
    /// <param name="rawBonus">逓減前のエラスティックボーナス値。</param>
    /// <param name="scale">逓減関数のスケール係数。</param>
    /// <returns>対数逓減後の実効エラスティックボーナス。</returns>
    public static long EffectiveElasticBonus(long rawBonus, long scale)
    {
        if (rawBonus <= 0 || scale <= 0) { return rawBonus; }
        double result = scale * Math.Log(1.0 + (double)rawBonus / scale);
        return Truncate(result);
    }

    /// <summary>
    /// Calculate effective throughput for a resource, including all bonuses.
    /// </summary>
    /// <param name="instance">対象リソース。</param>
    /// <param name="field">リソースが置かれているフィールド。</param>
    /// <param name="cc">カード定義キャッシュ。</param>
    /// <returns>各種ボーナスを含む実効スループット。</returns>
    public static long CalculateEffectiveTP(DeployedResource instance, Field field, ICardCache cc)
    {
        if (FieldHelpers.HasTemporaryEffect(instance, EffectTypes.TPSuppressed)) { return 0; }

        var card = cc.MustGet(instance.CardID);
        if (!card.IsComputeType || card.ComputeStats is null) { return 0; }

        long baseTP = card.ComputeStats.Throughput;
        long rankMult = BattleConstants.RankMultiplier(instance.Rank);

        // インスタンスファミリー倍率
        double tpMult = 1.0;
        if (instance.InstanceFamily is { } family)
        {
            var (tp, _) = BattleConstants.FamilyMultiplier(family);
            tpMult = tp;
        }

        long baseValue = Truncate(baseTP * rankMult * tpMult);

        // 逓減効果付き Elastic ボーナス
        long elasticBonus = 0;
        if (card.Elastic && card.FreeTier > 0)
        {
            elasticBonus = EffectiveElasticBonus(instance.ElasticBonus, card.FreeTier);
        }

        long tempBonus = instance.TemporaryEffects
            .Where(e => e.EffectType == EffectTypes.BuffTP).Sum(e => e.Value);
        long tempDebuff = instance.TemporaryEffects
            .Where(e => e.EffectType == EffectTypes.DebuffTP).Sum(e => e.Value);

        long total = baseValue + elasticBonus + tempBonus - tempDebuff;
        return Math.Max(0, total);
    }

    /// <summary>
    /// Calculate effective yield for a resource, including all bonuses.
    /// </summary>
    /// <param name="instance">対象リソース。</param>
    /// <param name="field">リソースが置かれているフィールド。</param>
    /// <param name="cc">カード定義キャッシュ。</param>
    /// <returns>各種ボーナスを含む実効イールド。</returns>
    public static long CalculateEffectiveInsight(DeployedResource instance, Field field, ICardCache cc)
    {
        var card = cc.MustGet(instance.CardID);
        if (!card.IsDataResource || card.DataResourceStats is null) { return 0; }

        long baseYield = card.DataResourceStats.Yield;
        long rankMult = BattleConstants.RankMultiplier(instance.Rank);

        // インスタンスファミリー倍率 (Yield はスループットと同じ倍率系統)
        double yieldMult = 1.0;
        if (instance.InstanceFamily is { } family)
        {
            var (tp, _) = BattleConstants.FamilyMultiplier(family);
            yieldMult = tp;
        }

        long baseValue = Truncate(baseYield * rankMult * yieldMult);

        long elasticBonus = 0;
        if (card.Elastic && card.FreeTier > 0)
        {
            elasticBonus = EffectiveElasticBonus(instance.ElasticBonus, card.FreeTier);
        }

        long tempBonus = instance.TemporaryEffects
            .Where(e => e.EffectType == EffectTypes.BuffYield).Sum(e => e.Value);
        long tempDebuff = instance.TemporaryEffects
            .Where(e => e.EffectType == EffectTypes.DebuffYield).Sum(e => e.Value);

        long total = baseValue + elasticBonus + tempBonus - tempDebuff;
        return Math.Max(0, total);
    }

    /// <summary>
    /// Calculate max AV for a resource, including all bonuses.
    /// </summary>
    /// <param name="instance">対象リソース。</param>
    /// <param name="field">リソースが置かれているフィールド。</param>
    /// <param name="cc">カード定義キャッシュ。</param>
    /// <returns>各種ボーナスを含む最大可用性。</returns>
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
        return Math.Max(0, baseValue);
    }

    /// <summary>
    /// Recalculate MaxTP after scale-up (non-elastic cards only).
    /// </summary>
    /// <param name="resource">対象リソース。</param>
    /// <param name="card">対象のカード定義。</param>
    /// <returns>スケールアップ後の最大スループット。</returns>
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
    /// <param name="resource">対象リソース。</param>
    /// <param name="card">対象のカード定義。</param>
    /// <returns>スケールアップ後の最大イールド。</returns>
    public static long RecalculateMaxYield(DeployedResource resource, CardDefinition card)
    {
        if (card.DataResourceStats is null) { return 0; }
        long baseYield = card.DataResourceStats.Yield;
        long rankMult = BattleConstants.RankMultiplier(resource.Rank);

        double yieldMult = 1.0;
        if (resource.InstanceFamily is { } family)
        {
            var (tp, _) = BattleConstants.FamilyMultiplier(family);
            yieldMult = tp;
        }

        return Truncate(baseYield * rankMult * yieldMult);
    }

    /// <summary>
    /// Apply elastic bonus increment to a resource.
    /// </summary>
    /// <param name="resource">対象リソース。</param>
    /// <param name="card">対象のカード定義。</param>
    public static void ApplyElasticBonus(DeployedResource resource, CardDefinition card)
    {
        if (card.Elastic && card.ElasticIncrement > 0)
        {
            resource.ElasticBonus += card.ElasticIncrement;
        }
    }

    /// <summary>Truncates a floating-point value to a long integer (floor towards zero).</summary>
    /// <param name="val">The value to truncate.</param>
    /// <returns>切り捨て後の long 値。</returns>
    public static long Truncate(double val) => (long)val;
}
