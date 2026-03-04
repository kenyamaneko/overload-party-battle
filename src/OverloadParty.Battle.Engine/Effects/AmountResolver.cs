using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Effects;

/// <summary>
/// Resolves a dynamic amount value for effect operations.
/// </summary>
public interface IAmountResolver
{
    long Resolve(OpContext ctx);
}

/// <summary>
/// Constant value.
/// </summary>
public class StaticAmount(long value) : IAmountResolver
{
    public long Resolve(OpContext ctx) => value;
}

/// <summary>
/// Custom function resolver.
/// </summary>
public class FnAmount(Func<OpContext, long> fn) : IAmountResolver
{
    public long Resolve(OpContext ctx) => fn(ctx);
}

/// <summary>
/// Source resource's current effective yield.
/// </summary>
public class SourceYieldAmount : IAmountResolver
{
    public static readonly SourceYieldAmount Instance = new();
    public long Resolve(OpContext ctx)
    {
        if (ctx.Source is null) return 0;
        return StatCalculator.CalculateEffectiveYield(ctx.Source, ctx.MyField, ctx.CardCache);
    }
}

/// <summary>
/// Target resource's current effective TP.
/// </summary>
public class TargetTPAmount : IAmountResolver
{
    public static readonly TargetTPAmount Instance = new();
    public long Resolve(OpContext ctx)
    {
        if (ctx.Target is null) return 0;
        return StatCalculator.CalculateEffectiveTP(ctx.Target, ctx.OpponentField, ctx.CardCache);
    }
}

/// <summary>
/// Half of target's MaxAV, rounded to nearest 200.
/// </summary>
public class HalfMaxAVAmount : IAmountResolver
{
    public static readonly HalfMaxAVAmount Instance = new();
    public long Resolve(OpContext ctx)
    {
        if (ctx.Target is null) return 0;
        long half = (ctx.Target.MaxAV + 1) / 2;
        // Round to nearest 200
        return ((half + 99) / 200) * 200;
    }
}

/// <summary>
/// Target's card ID as the amount.
/// </summary>
public class TargetCardIDAmount : IAmountResolver
{
    public static readonly TargetCardIDAmount Instance = new();
    public long Resolve(OpContext ctx) => ctx.Target?.CardID ?? 0;
}

/// <summary>
/// SLA penalty from target's card definition.
/// </summary>
public class SLAPenaltyAmount : IAmountResolver
{
    public static readonly SLAPenaltyAmount Instance = new();
    public long Resolve(OpContext ctx)
    {
        if (ctx.Target is null) return 0;
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
    public long Resolve(OpContext ctx)
    {
        var oppField = ctx.OpponentField;
        int count = oppField.Backend.Count(r => r.FaceUp);
        long bonus = Math.Min((long)count * perBackend, maxBonus);
        return baseVal + bonus;
    }
}
