using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Effects;

/// <summary>
/// Resolves a dynamic amount value for effect operations.
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
/// Constant value.
/// </summary>
public class StaticAmount(long value) : IAmountResolver
{
    /// <inheritdoc />
    public long Resolve(OpContext ctx) => value;
}

/// <summary>
/// Custom function resolver.
/// </summary>
public class FnAmount(Func<OpContext, long> fn) : IAmountResolver
{
    /// <inheritdoc />
    public long Resolve(OpContext ctx) => fn(ctx);
}

/// <summary>
/// Source resource's current effective yield.
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
/// Target resource's current effective TP.
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
/// Half of target's MaxAV, rounded to nearest 200.
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
        long half = (ctx.Target.MaxAV + 1) / 2;
        // Round to nearest 200
        return ((half + 99) / 200) * 200;
    }
}


/// <summary>
/// SLA penalty from target's card definition.
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
            ("source", "yield") => ctx.Source is not null
                ? StatCalculator.CalculateEffectiveInsight(ctx.Source, ctx.MyField, ctx.CardCache) : 0,
            ("source", "tp") => ctx.Source is not null
                ? StatCalculator.CalculateEffectiveTP(ctx.Source, ctx.MyField, ctx.CardCache) : 0,
            ("source", "max_av") => ctx.Source?.MaxAV ?? 0,
            ("source", "av") => ctx.Source?.EffectiveAV ?? 0,
            ("target", "tp") => ctx.Target is not null
                ? StatCalculator.CalculateEffectiveTP(ctx.Target, ctx.OpponentField, ctx.CardCache) : 0,
            ("target", "max_av") => ctx.Target?.MaxAV ?? 0,
            ("target", "av") => ctx.Target?.EffectiveAV ?? 0,
            ("target", "yield") => ctx.Target is not null
                ? StatCalculator.CalculateEffectiveInsight(ctx.Target, ctx.OpponentField, ctx.CardCache) : 0,
            ("target", "max_tp") => ctx.Target?.MaxTP ?? 0,
            ("source", "max_tp") => ctx.Source?.MaxTP ?? 0,
            _ => throw new InvalidOperationException($"Unknown ref: {who}.{stat}"),
        };

        if (multiply == 1.0) return raw;

        // Round to nearest 200 for half-value calculations
        long scaled = (long)(raw * multiply);
        return ((scaled + 99) / 200) * 200;
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
        int count = countSelector.Select(ctx).Count;
        long bonus = (long)count * perValue;
        if (max is { } m)
        {
            bonus = Math.Min(bonus, m);
        }
        return baseVal + bonus;
    }
}
