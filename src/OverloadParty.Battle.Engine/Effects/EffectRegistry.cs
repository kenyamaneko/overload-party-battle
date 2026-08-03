using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Effects;

/// <summary>
/// Pairs a card with its trigger type, handler, and built block for NPC classification.
/// </summary>
public class EffectRegistration
{
    /// <summary>Card ID this registration applies to.</summary>
    public string CardId { get; init; } = "";

    /// <summary>Trigger type this registration applies to.</summary>
    public TriggerType TriggerType { get; init; }

    /// <summary>The compiled effect handler.</summary>
    public required EffectHandler Handler { get; init; }

    /// <summary>Top-level guards / ops, stored for NPC classification. Null for custom handlers.</summary>
    public BuiltBlock? Block { get; init; }

    /// <summary>後方互換: top-level ops のみ参照したい consumer 向け。</summary>
    public IEffectOp[]? Ops => Block?.Ops;

    /// <summary>top-level guards のみ参照したい consumer 向け。</summary>
    public IEffectGuard[]? Guards => Block?.Guards;

    /// <summary>効果全体を 1 ブロックで宣言したときの回数制限。複数ブロックに分かれる効果では null。</summary>
    public UseLimitKind? UseLimit => Block?.UseLimit;
}

/// <summary>
/// Concrete implementation of IEffectRegistry.
/// Maps (cardId, triggerType) → EffectRegistration.
/// </summary>
public class EffectRegistry : IEffectRegistry
{
    private readonly Dictionary<(string CardId, TriggerType Trigger), EffectRegistration> _handlers = new();
    private readonly Dictionary<string, List<PassiveEffectDef>> _passives = new();

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
    /// BuiltBlock から handler を生成し、ブロックと一緒に登録します。
    /// </summary>
    /// <param name="cardId">カード ID。</param>
    /// <param name="trigger">トリガー種別。</param>
    /// <param name="block">guards / ops の組。</param>
    public void RegisterComposed(string cardId, TriggerType trigger, BuiltBlock block)
    {
        var key = (cardId, trigger);
        _handlers[key] = new EffectRegistration
        {
            CardId = cardId,
            TriggerType = trigger,
            Handler = EffectComposer.Compose(block),
            Block = block,
        };
    }

    /// <summary>
    /// ops のみ (guard なし) を BuiltBlock に包んで登録する shorthand。テスト向け。
    /// </summary>
    /// <param name="cardId">カード ID。</param>
    /// <param name="trigger">トリガー種別。</param>
    /// <param name="ops">構成する ops 列。</param>
    public void RegisterComposed(string cardId, TriggerType trigger, params IEffectOp[] ops)
    {
        RegisterComposed(cardId, trigger, new BuiltBlock { Ops = ops });
    }

    /// <summary>
    /// guards + ops を BuiltBlock に包んで登録する shorthand。テスト向け。
    /// </summary>
    /// <param name="cardId">カード ID。</param>
    /// <param name="trigger">トリガー種別。</param>
    /// <param name="guards">guard 述語列。</param>
    /// <param name="ops">構成する ops 列。</param>
    public void RegisterComposed(string cardId, TriggerType trigger, IEffectGuard[] guards, params IEffectOp[] ops)
    {
        RegisterComposed(cardId, trigger, new BuiltBlock { Guards = guards, Ops = ops });
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
        if (reg?.Guards is null)
        {
            return null;
        }

        long? minBudget = null;
        long? maxBudget = null;

        foreach (var guard in reg.Guards)
        {
            switch (guard)
            {
                case MinBudgetGuard min:
                    minBudget = min.Min;
                    break;
                case MaxBudgetGuard max:
                    maxBudget = max.Max;
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
        if (reg?.Block is null)
        {
            return null;
        }
        return EffectClassifier.ClassifyBlock(reg.Block);
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

    /// <inheritdoc />
    public UseLimitKind? GetUseLimit(string cardId, TriggerType trigger)
    {
        return GetRegistration(cardId, trigger)?.UseLimit;
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

    /// <inheritdoc />
    public void RegisterPassive(string cardId, PassiveEffectDef def)
    {
        if (!_passives.TryGetValue(cardId, out var defs))
        {
            defs = [];
            _passives[cardId] = defs;
        }
        defs.Add(def);
    }

    /// <inheritdoc />
    public IReadOnlyList<PassiveEffectDef> GetPassives(string cardId)
    {
        return _passives.GetValueOrDefault(cardId, []);
    }
}
