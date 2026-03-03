using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Effects;

/// <summary>
/// Result of executing an effect handler.
/// </summary>
public class EffectResult
{
    public List<GameEvent> Events { get; set; } = [];
    public bool CancelAction { get; set; }
}

/// <summary>
/// Context passed to effect handlers during execution.
/// </summary>
public class EffectContext
{
    public required GameState State { get; init; }
    public required Game Game { get; init; }
    public required long PlayerNum { get; init; }
    public ResourceInstance? Source { get; init; }
    public ResourceInstance? Target { get; init; }
    public SupportInstance? SupSource { get; init; }
    public required ICardCache CardCache { get; init; }
    public Dictionary<string, object>? ChoiceData { get; init; }
}

/// <summary>
/// Delegate type for effect handlers.
/// </summary>
public delegate EffectResult EffectHandler(EffectContext ctx);

/// <summary>
/// Registry for looking up effect handlers by card number and trigger type.
/// Engine depends only on this interface; implementation is in Effects/.
/// </summary>
public interface IEffectRegistry
{
    EffectHandler? Get(long cardNo, TriggerType trigger);
    bool Has(long cardNo, TriggerType trigger);
}
