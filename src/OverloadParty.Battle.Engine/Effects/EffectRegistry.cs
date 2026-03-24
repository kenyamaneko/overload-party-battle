using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Effects;

/// <summary>
/// Pairs a card with its trigger type, handler, and ops for NPC classification.
/// </summary>
public class EffectRegistration
{
    /// <summary>Card ID this registration applies to.</summary>
    public string CardId { get; init; } = "";

    /// <summary>Trigger type this registration applies to.</summary>
    public TriggerType TriggerType { get; init; }

    /// <summary>The compiled effect handler.</summary>
    public required EffectHandler Handler { get; init; }

    /// <summary>Raw ops sequence, stored for NPC classification. Null for custom handlers.</summary>
    public IEffectOp[]? Ops { get; init; }
}

/// <summary>
/// Data-driven passive effect definition for stat calculations.
/// </summary>
public class PassiveDef
{
    /// <summary>Card ID owning this passive.</summary>
    public string CardId { get; init; } = "";

    /// <summary>Type of passive effect (e.g. buff_yield).</summary>
    public string PassiveType { get; init; } = "";

    /// <summary>Scope of the passive (e.g. "self", "ally").</summary>
    public string Scope { get; init; } = "";

    /// <summary>Zone filter, or null for any zone.</summary>
    public string? TargetZone { get; init; }

    /// <summary>Faction filter, or null for any faction.</summary>
    public string? TargetFaction { get; init; }

    /// <summary>Numeric value of the passive effect.</summary>
    public long Value { get; init; }

    /// <summary>Optional runtime condition for the passive to apply.</summary>
    public Func<OpContext, bool>? Condition { get; init; }
}

/// <summary>
/// Concrete implementation of IEffectRegistry.
/// Maps (cardId, triggerType) → EffectRegistration.
/// </summary>
public class EffectRegistry : IEffectRegistry
{
    private readonly Dictionary<(string CardId, TriggerType Trigger), EffectRegistration> _handlers = new();
    private readonly List<PassiveDef> _passives = [];

    /// <summary>
    /// Registers a custom effect handler for a card and trigger.
    /// </summary>
    /// <param name="cardId">Card ID.</param>
    /// <param name="trigger">Trigger type.</param>
    /// <param name="handler">The handler to register.</param>
    public void Register(string cardId, TriggerType trigger, EffectHandler handler)
    {
        var key = (cardId, trigger);
        _handlers[key] = new EffectRegistration
        {
            CardId = cardId,
            TriggerType = trigger,
            Handler = handler,
        };
    }

    /// <summary>
    /// Builds a handler from ops via Compose and stores both
    /// the handler and the ops for NPC classification.
    /// </summary>
    public void RegisterComposed(string cardId, TriggerType trigger, params IEffectOp[] ops)
    {
        var key = (cardId, trigger);
        _handlers[key] = new EffectRegistration
        {
            CardId = cardId,
            TriggerType = trigger,
            Handler = EffectComposer.Compose(ops),
            Ops = ops,
        };
    }

    /// <inheritdoc />
    public EffectHandler? Get(string cardId, TriggerType trigger)
    {
        return _handlers.GetValueOrDefault((cardId, trigger))?.Handler;
    }

    /// <summary>
    /// Retrieves the full registration (handler + ops) for a card and trigger.
    /// </summary>
    /// <param name="cardId">Card ID.</param>
    /// <param name="trigger">Trigger type.</param>
    /// <returns>The registration, or null if not found.</returns>
    public EffectRegistration? GetRegistration(string cardId, TriggerType trigger)
    {
        return _handlers.GetValueOrDefault((cardId, trigger));
    }

    /// <inheritdoc />
    public bool Has(string cardId, TriggerType trigger)
    {
        return _handlers.ContainsKey((cardId, trigger));
    }

    public BudgetRequirement? GetBudgetRequirement(string cardId, TriggerType trigger)
    {
        var reg = GetRegistration(cardId, trigger);
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

    /// <summary>
    /// Adds a passive effect definition to the registry.
    /// </summary>
    /// <param name="passive">The passive definition to add.</param>
    public void AddPassive(PassiveDef passive)
    {
        _passives.Add(passive);
    }

    /// <summary>
    /// Returns all registered passive definitions.
    /// </summary>
    /// <returns>Read-only list of passive definitions.</returns>
    public IReadOnlyList<PassiveDef> GetPassives() => _passives;

    /// <summary>Number of registered effect handlers.</summary>
    public int RegistrationCount => _handlers.Count;

    /// <summary>Number of registered passive definitions.</summary>
    public int PassiveCount => _passives.Count;

    /// <summary>
    /// Returns NPC classification for an effect. Null if no ops stored.
    /// </summary>
    public EffectInfo? GetEffectInfo(string cardId, TriggerType trigger)
    {
        var reg = GetRegistration(cardId, trigger);
        if (reg?.Ops is null)
        {
            return null;
        }
        return EffectClassifier.ClassifyOps(reg.Ops);
    }

    /// <summary>
    /// Returns branch keys if the effect uses BranchOnChoice.
    /// </summary>
    public List<string>? GetChoiceOptions(string cardId, TriggerType trigger)
    {
        var reg = GetRegistration(cardId, trigger);
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
    /// Returns all card IDs that have a handler for the given trigger.
    /// </summary>
    public List<string> CardIdsForTrigger(TriggerType trigger)
    {
        return _handlers.Keys
            .Where(key => key.Trigger == trigger)
            .Select(key => key.CardId)
            .ToList();
    }
}
