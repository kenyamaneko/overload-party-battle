using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Effects.Ops;

/// <summary>
/// Wraps a named group of ops so that guard failures (GameRuleException) are caught,
/// allowing subsequent independent groups in the same pipeline to run.
/// The success/failure result is recorded in <see cref="OpContext.GroupResults"/>.
/// </summary>
public class EffectGroupOp(string groupId, IEffectOp[] ops) : IEffectOp
{
    public void Execute(OpContext ctx)
    {
        try
        {
            foreach (var op in ops) op.Execute(ctx);
            ctx.GroupResults[groupId] = true;
        }
        catch (GameRuleException)
        {
            ctx.GroupResults[groupId] = false;
        }
    }
}

/// <summary>
/// Runs child ops only if the parent group succeeded.
/// Guard failures in the child ops are silently swallowed.
/// </summary>
public class DependentEffectOp(string parentGroupId, IEffectOp[] ops) : IEffectOp
{
    public void Execute(OpContext ctx)
    {
        if (!ctx.GroupResults.GetValueOrDefault(parentGroupId)) return;

        try
        {
            foreach (var op in ops) op.Execute(ctx);
        }
        catch (GameRuleException)
        {
            // Dependent group guard failure is silently swallowed
        }
    }
}

/// <summary>
/// Records that a group succeeded in <see cref="OpContext.GroupResults"/>.
/// Used when the root block runs without exception isolation (single independent + dependents).
/// </summary>
public class MarkGroupSucceededOp(string groupId) : IEffectOp
{
    public void Execute(OpContext ctx)
    {
        ctx.GroupResults[groupId] = true;
    }
}

/// <summary>
/// Inverts a guard op: succeeds when the inner op throws GameRuleException,
/// and throws when the inner op succeeds.
/// </summary>
public class NegateGuardOp(IEffectOp inner) : IEffectOp
{
    public void Execute(OpContext ctx)
    {
        try
        {
            inner.Execute(ctx);
        }
        catch (GameRuleException)
        {
            return; // Inner failed → negated guard passes
        }
        throw new GameRuleException("Negated guard: inner condition was true");
    }
}

/// <summary>
/// General-purpose resource count guard. Counts resources (including support zone)
/// matching the given criteria and fails if the count doesn't meet the minimum.
/// </summary>
public class ResourceCountGuardOp(
    string owner,
    string? zone,
    string? faction,
    List<string>? cardTypes,
    List<string>? cardIds,
    int min,
    bool negate) : IEffectOp
{
    public void Execute(OpContext ctx)
    {
        int count = CountResources(ctx);
        bool satisfied = count >= min;
        if (negate) satisfied = !satisfied;
        if (!satisfied)
        {
            throw new GameRuleException($"Resource count guard failed: count={count}, min={min}, negate={negate}");
        }
    }

    private int CountResources(OpContext ctx)
    {
        int total = 0;

        if (owner is "self" or "both")
        {
            total += CountField(ctx.MyField, ctx.CardCache, ctx.Source);
        }
        if (owner is "opponent" or "both")
        {
            total += CountField(ctx.OpponentField, ctx.CardCache, ctx.Source);
        }

        return total;
    }

    private int CountField(Field field, ICardCache cc, DeployedResource? source)
    {
        if (zone == "support")
        {
            return field.Support.Count(s =>
                s.FaceUp
                && s.DeployingTurnsLeft <= 0
                && MatchesFaction(s.CardID, cc)
                && MatchesCardTypes(s.CardID, cc)
                && MatchesCardIds(s.CardID));
        }

        IEnumerable<DeployedResource> candidates = zone switch
        {
            "frontend" => field.Frontend,
            "backend" => field.Backend,
            _ => field.Frontend.Concat(field.Backend),
        };

        return candidates.Count(r =>
            r.FaceUp
            && MatchesFaction(r.CardID, cc)
            && MatchesCardTypes(r.CardID, cc)
            && MatchesCardIds(r.CardID));
    }

    private bool MatchesFaction(string cardID, ICardCache cc)
    {
        if (faction is not { Length: > 0 }) return true;
        return cc.Get(cardID)?.Faction == faction;
    }

    private bool MatchesCardTypes(string cardID, ICardCache cc)
    {
        if (cardTypes is not { Count: > 0 }) return true;
        var card = cc.Get(cardID);
        return card is not null && cardTypes.Contains(card.CardType);
    }

    private bool MatchesCardIds(string cardID)
    {
        if (cardIds is not { Count: > 0 }) return true;
        return cardIds.Contains(cardID);
    }
}
