using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests;

/// <summary>
/// Simple in-memory IEffectRegistry for tests.
/// </summary>
public class TestEffectRegistry : IEffectRegistry
{
    private readonly Dictionary<(string, TriggerType), EffectHandler> _handlers = new();
    private readonly Dictionary<string, List<PassiveEffectDef>> _passives = new();

    public void Register(string cardId, TriggerType trigger, EffectHandler handler)
        => _handlers[(cardId, trigger)] = handler;

    public EffectHandler? Get(string cardId, TriggerType trigger)
        => _handlers.GetValueOrDefault((cardId, trigger));

    public bool Has(string cardId, TriggerType trigger)
        => _handlers.ContainsKey((cardId, trigger));

    public BudgetRequirement? GetBudgetRequirement(string cardId, TriggerType trigger) => null;
    public EffectInfo? GetEffectInfo(string cardId, TriggerType trigger) => null;
    public List<string>? GetChoiceOptions(string cardId, TriggerType trigger) => null;
    public IEffectOp[]? GetOps(string cardId, TriggerType trigger) => null;

    public void RegisterPassive(string cardId, PassiveEffectDef def)
    {
        if (!_passives.TryGetValue(cardId, out var defs))
        {
            defs = [];
            _passives[cardId] = defs;
        }
        defs.Add(def);
    }

    public IReadOnlyList<PassiveEffectDef> GetPassives(string cardId)
        => _passives.GetValueOrDefault(cardId, []);
}
