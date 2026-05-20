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
/// Concrete implementation of IEffectRegistry.
/// Maps (cardId, triggerType) → EffectRegistration.
/// </summary>
public class EffectRegistry : IEffectRegistry
{
    private readonly Dictionary<(string CardId, TriggerType Trigger), EffectRegistration> _handlers = new();

    /// <summary>
    /// Registers a custom effect handler for a card and trigger.
    /// </summary>
    /// <param name="cardId">カード ID。</param>
    /// <param name="trigger">トリガー種別。</param>
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
    /// <param name="cardId">カード ID。</param>
    /// <param name="trigger">トリガー種別。</param>
    /// <param name="ops">構成する効果 op の列。</param>
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
    /// <param name="cardId">カード ID。</param>
    /// <param name="trigger">トリガー種別。</param>
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

    /// <summary>
    /// 効果のバジェット条件を返します。条件がなければ null。
    /// </summary>
    /// <param name="cardId">カード ID。</param>
    /// <param name="trigger">トリガー種別。</param>
    /// <returns>抽出したバジェット条件、または条件なしの場合は null。</returns>
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

    /// <summary>Number of registered effect handlers.</summary>
    public int RegistrationCount => _handlers.Count;

    /// <summary>
    /// Returns NPC classification for an effect. Null if no ops stored.
    /// </summary>
    /// <param name="cardId">カード ID。</param>
    /// <param name="trigger">トリガー種別。</param>
    /// <returns>分類結果。op 列が未保存の場合は null。</returns>
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
    /// <param name="cardId">カード ID。</param>
    /// <param name="trigger">トリガー種別。</param>
    /// <returns>分岐キーのリスト。BranchOnChoice が使われていない場合は null。</returns>
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

    /// <inheritdoc />
    public IEffectOp[]? GetOps(string cardId, TriggerType trigger)
    {
        return GetRegistration(cardId, trigger)?.Ops;
    }

    /// <summary>
    /// Returns all card IDs that have a handler for the given trigger.
    /// </summary>
    /// <param name="trigger">トリガー種別。</param>
    /// <returns>該当トリガーのハンドラを持つ Card ID 一覧。</returns>
    public List<string> CardIdsForTrigger(TriggerType trigger)
    {
        return _handlers.Keys
            .Where(key => key.Trigger == trigger)
            .Select(key => key.CardId)
            .ToList();
    }
}
