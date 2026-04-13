using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Effects;

/// <summary>
/// IAmountResolver はエフェクト操作の動的な数値を解決します
/// </summary>
public interface IAmountResolver
{
    /// <summary>
    /// Resolves the amount value from the current pipeline context.
    /// </summary>
    /// <param name="ctx">The operation context.</param>
    /// <returns>The resolved numeric amount.</returns>
    long Resolve(OpContext ctx);
}

/// <summary>
/// 固定値リゾルバ
/// </summary>
public class StaticAmount(long value) : IAmountResolver
{
    /// <inheritdoc />
    public long Resolve(OpContext ctx) => value;
}

/// <summary>
/// カスタム関数リゾルバ
/// </summary>
public class FnAmount(Func<OpContext, long> fn) : IAmountResolver
{
    /// <inheritdoc />
    public long Resolve(OpContext ctx) => fn(ctx);
}

/// <summary>
/// ソースリソースの現在の実効イールドを解決します
/// </summary>
public class SourceYieldAmount : IAmountResolver
{
    /// <summary>Shared singleton instance.</summary>
    public static readonly SourceYieldAmount Instance = new();

    /// <inheritdoc />
    public long Resolve(OpContext ctx)
    {
        if (ctx.Source is null)
        {
            return 0;
        }
        return StatCalculator.CalculateEffectiveInsight(ctx.Source, ctx.MyField, ctx.CardCache);
    }
}

/// <summary>
/// ターゲットリソースの現在の実効 TP を解決します
/// </summary>
public class TargetTPAmount : IAmountResolver
{
    /// <summary>Shared singleton instance.</summary>
    public static readonly TargetTPAmount Instance = new();

    /// <inheritdoc />
    public long Resolve(OpContext ctx)
    {
        if (ctx.Target is null)
        {
            return 0;
        }
        return StatCalculator.CalculateEffectiveTP(ctx.Target, ctx.OpponentField, ctx.CardCache);
    }
}

/// <summary>
/// ターゲットの MaxAV の半分（切り捨て）を解決します
/// </summary>
public class HalfMaxAVAmount : IAmountResolver
{
    /// <summary>Shared singleton instance.</summary>
    public static readonly HalfMaxAVAmount Instance = new();

    /// <inheritdoc />
    public long Resolve(OpContext ctx)
    {
        if (ctx.Target is null)
        {
            return 0;
        }
        return ctx.Target.MaxAV / 2;
    }
}


/// <summary>
/// ターゲットのカード定義から SLA ペナルティを解決します
/// </summary>
public class SLAPenaltyAmount : IAmountResolver
{
    /// <summary>Shared singleton instance.</summary>
    public static readonly SLAPenaltyAmount Instance = new();

    /// <inheritdoc />
    public long Resolve(OpContext ctx)
    {
        if (ctx.Target is null)
        {
            return 0;
        }
        var card = ctx.CardCache.Get(ctx.Target.CardID);
        return card?.SLAPenalty ?? 0;
    }
}

/// <summary>
/// Scaled amount based on opponent's backend resource count.
/// Formula: base + min(count * perBackend, maxBonus)
/// </summary>
public class BackendScaledAmount(long baseVal, long perBackend, long maxBonus) : IAmountResolver
{
    /// <inheritdoc />
    public long Resolve(OpContext ctx)
    {
        var oppField = ctx.OpponentField;
        int count = oppField.Backend.Count(r => r.FaceUp);
        long bonus = Math.Min((long)count * perBackend, maxBonus);
        return baseVal + bonus;
    }
}

/// <summary>
/// Effect DSL の ref 文字列で who 側に出現する語彙。
/// プレイヤー視点の self/opponent (PlayerRefs) とは別概念で、effect の発生元/対象を指す。
/// </summary>
public static class RefWho
{
    public const string Source = "source";
    public const string Target = "target";
}

/// <summary>
/// Effect DSL の ref 文字列で stat 側に出現する派生語彙。
/// 基本 stat (tp/yield/av) は <see cref="StatTypes"/> を使用する。
/// </summary>
public static class RefStat
{
    public const string MaxAv = "max_av";
    public const string MaxTp = "max_tp";
}

/// <summary>
/// Resolves a stat reference with optional multiplier.
/// Supports: source.yield, source.tp, target.tp, target.max_av, etc.
/// </summary>
public class RefAmount(string who, string stat, double multiply = 1.0) : IAmountResolver
{
    /// <inheritdoc />
    public long Resolve(OpContext ctx)
    {
        long raw = (who, stat) switch
        {
            (RefWho.Source, StatTypes.Yield) => ctx.Source is not null
                ? StatCalculator.CalculateEffectiveInsight(ctx.Source, ctx.MyField, ctx.CardCache) : 0,
            (RefWho.Source, StatTypes.Tp) => ctx.Source is not null
                ? StatCalculator.CalculateEffectiveTP(ctx.Source, ctx.MyField, ctx.CardCache) : 0,
            (RefWho.Source, RefStat.MaxAv) => ctx.Source?.MaxAV ?? 0,
            (RefWho.Source, StatTypes.Av) => ctx.Source?.EffectiveAV ?? 0,
            (RefWho.Source, RefStat.MaxTp) => ctx.Source?.MaxTP ?? 0,
            (RefWho.Target, StatTypes.Tp) => ctx.Target is not null
                ? StatCalculator.CalculateEffectiveTP(ctx.Target, ctx.OpponentField, ctx.CardCache) : 0,
            (RefWho.Target, StatTypes.Yield) => ctx.Target is not null
                ? StatCalculator.CalculateEffectiveInsight(ctx.Target, ctx.OpponentField, ctx.CardCache) : 0,
            (RefWho.Target, RefStat.MaxAv) => ctx.Target?.MaxAV ?? 0,
            (RefWho.Target, StatTypes.Av) => ctx.Target?.EffectiveAV ?? 0,
            (RefWho.Target, RefStat.MaxTp) => ctx.Target?.MaxTP ?? 0,
            _ => throw new InvalidOperationException($"Unknown ref: {who}.{stat}"),
        };

        if (multiply == 1.0) return raw;

        return (long)(raw * multiply);
    }
}

/// <summary>
/// Resolves: base + min(count * perValue, max).
/// Count is determined by a selector.
/// </summary>
public class PerCountAmount(long baseVal, ISelector countSelector, long perValue, long? max = null) : IAmountResolver
{
    /// <inheritdoc />
    public long Resolve(OpContext ctx)
    {
        var resources = countSelector.Select(ctx);
        long count = resources.Sum(r => GetCountMultiplier(r));
        long bonus = count * perValue;
        if (max is { } m)
        {
            bonus = Math.Min(bonus, m);
        }
        return baseVal + bonus;
    }

    private static long GetCountMultiplier(DeployedResource r)
    {
        var multiplier = r.TemporaryEffects
            .Where(e => e.EffectType == "count_multiplier")
            .Select(e => e.Value)
            .DefaultIfEmpty(1)
            .Max();
        return multiplier;
    }
}
