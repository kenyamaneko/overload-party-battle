using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Effects;

/// <summary>
/// Pairs a card with its trigger type, handler, and ops for NPC classification.
/// </summary>
public class EffectRegistration
{
    public long CardNo { get; init; }
    public TriggerType TriggerType { get; init; }
    public required EffectHandler Handler { get; init; }
    public IEffectOp[]? Ops { get; init; } // Stored for NPC classification
}

/// <summary>
/// Data-driven passive effect definition for stat calculations.
/// </summary>
public class PassiveDef
{
    public long CardNo { get; init; }
    public string PassiveType { get; init; } = "";
    public string Scope { get; init; } = "";
    public string? TargetZone { get; init; }
    public string? TargetFaction { get; init; }
    public long Value { get; init; }
    public Func<OpContext, bool>? Condition { get; init; }
}

/// <summary>
/// Concrete implementation of IEffectRegistry.
/// Maps (cardNo, triggerType) → EffectRegistration.
/// </summary>
public class EffectRegistry : IEffectRegistry
{
    private readonly Dictionary<(long CardNo, TriggerType Trigger), EffectRegistration> _handlers = new();
    private readonly List<PassiveDef> _passives = [];

    public void Register(long cardNo, TriggerType trigger, EffectHandler handler)
    {
        var key = (cardNo, trigger);
        _handlers[key] = new EffectRegistration
        {
            CardNo = cardNo,
            TriggerType = trigger,
            Handler = handler,
        };
    }

    /// <summary>
    /// Builds a handler from ops via Compose and stores both
    /// the handler and the ops for NPC classification.
    /// </summary>
    public void RegisterComposed(long cardNo, TriggerType trigger, params IEffectOp[] ops)
    {
        var key = (cardNo, trigger);
        _handlers[key] = new EffectRegistration
        {
            CardNo = cardNo,
            TriggerType = trigger,
            Handler = EffectComposer.Compose(ops),
            Ops = ops,
        };
    }

    public EffectHandler? Get(long cardNo, TriggerType trigger)
    {
        return _handlers.GetValueOrDefault((cardNo, trigger))?.Handler;
    }

    public EffectRegistration? GetRegistration(long cardNo, TriggerType trigger)
    {
        return _handlers.GetValueOrDefault((cardNo, trigger));
    }

    public bool Has(long cardNo, TriggerType trigger)
    {
        return _handlers.ContainsKey((cardNo, trigger));
    }

    public BudgetRequirement? GetBudgetRequirement(long cardNo, TriggerType trigger)
    {
        var reg = GetRegistration(cardNo, trigger);
        if (reg?.Ops is null)
        {
            return null;
        }

        long? minBudget = null;
        long? maxBudget = null;

        foreach (var op in reg.Ops)
        {
            switch (op)
            {
                case Ops.RequireBudgetOp rb:
                    minBudget = rb.Min;
                    break;
                case Ops.RequireMaxBudgetOp rmb:
                    maxBudget = rmb.Max;
                    break;
            }
        }

        if (minBudget is null && maxBudget is null)
        {
            return null;
        }
        return new BudgetRequirement { MinBudget = minBudget, MaxBudget = maxBudget };
    }

    public void AddPassive(PassiveDef passive)
    {
        _passives.Add(passive);
    }

    public IReadOnlyList<PassiveDef> GetPassives() => _passives;

    public int RegistrationCount => _handlers.Count;
    public int PassiveCount => _passives.Count;

    /// <summary>
    /// Returns NPC classification for an effect. Null if no ops stored.
    /// </summary>
    public EffectInfo? GetEffectInfo(long cardNo, TriggerType trigger)
    {
        var reg = GetRegistration(cardNo, trigger);
        if (reg?.Ops is null)
        {
            return null;
        }
        return EffectClassifier.ClassifyOps(reg.Ops);
    }

    /// <summary>
    /// Returns branch keys if the effect uses BranchOnChoice.
    /// </summary>
    public List<string>? GetChoiceOptions(long cardNo, TriggerType trigger)
    {
        var reg = GetRegistration(cardNo, trigger);
        if (reg?.Ops is null)
        {
            return null;
        }

        return reg.Ops
            .OfType<Ops.BranchOnChoiceOp>()
            .Select(branch => branch.Branches.Keys.ToList())
            .FirstOrDefault();
    }

    /// <summary>
    /// Returns all card numbers that have a handler for the given trigger.
    /// </summary>
    public List<long> CardNosForTrigger(TriggerType trigger)
    {
        return _handlers.Keys
            .Where(key => key.Trigger == trigger)
            .Select(key => key.CardNo)
            .ToList();
    }
}
