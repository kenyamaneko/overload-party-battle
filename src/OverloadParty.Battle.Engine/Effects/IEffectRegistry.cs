using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Effects;

/// <summary>
/// Result of executing an effect handler.
/// </summary>
public class EffectResult
{
    /// <summary>Events generated during effect execution.</summary>
    public List<GameEvent> Events { get; set; } = [];

    /// <summary>Whether the triggering action should be cancelled.</summary>
    public bool CancelAction { get; set; }
}

/// <summary>
/// Context passed to effect handlers during execution.
/// </summary>
public class EffectContext
{
    /// <summary>Current game state.</summary>
    public required GameState State { get; init; }

    /// <summary>The game metadata.</summary>
    public required Game Game { get; init; }

    /// <summary>Player number of the effect owner (1 or 2).</summary>
    public required long PlayerNum { get; init; }

    /// <summary>Source resource that triggered the effect, if any.</summary>
    public DeployedResource? Source { get; init; }

    /// <summary>Target resource of the effect, if any.</summary>
    public DeployedResource? Target { get; init; }

    /// <summary>Support-zone source (for platform/reactive cards).</summary>
    public DeployedSupport? SupSource { get; init; }

    /// <summary>Card definition cache for lookups.</summary>
    public required ICardCache CardCache { get; init; }

    /// <summary>Player choice data for branching effects.</summary>
    public Dictionary<string, object>? ChoiceData { get; init; }
}

/// <summary>
/// Delegate type for effect handlers.
/// </summary>
public delegate EffectResult EffectHandler(EffectContext ctx);

/// <summary>
/// Budget conditions extracted from an effect's ops.
/// </summary>
public class BudgetRequirement
{
    /// <summary>Minimum budget needed (from RequireBudgetOp). Null if no minimum.</summary>
    public long? MinBudget { get; init; }

    /// <summary>Maximum budget allowed (from RequireMaxBudgetOp). Null if no maximum.</summary>
    public long? MaxBudget { get; init; }

    /// <summary>
    /// Checks whether the given budget satisfies both min and max constraints.
    /// </summary>
    /// <param name="budget">The budget value to check.</param>
    /// <returns>True if the budget is within the required range.</returns>
    public bool IsSatisfied(long budget) =>
        (MinBudget is null || budget >= MinBudget) &&
        (MaxBudget is null || budget <= MaxBudget);
}

/// <summary>
/// Registry for looking up effect handlers by card ID and trigger type.
/// Engine depends only on this interface; implementation is in Effects/.
/// </summary>
public interface IEffectRegistry
{
    /// <summary>
    /// Retrieves the effect handler for the given card and trigger, or null if none registered.
    /// </summary>
    /// <param name="cardId">Card ID to look up.</param>
    /// <param name="trigger">Trigger type to look up.</param>
    /// <returns>The handler, or null if not found.</returns>
    EffectHandler? Get(string cardId, TriggerType trigger);

    /// <summary>
    /// Returns whether a handler is registered for the given card and trigger.
    /// </summary>
    /// <param name="cardId">Card ID to check.</param>
    /// <param name="trigger">Trigger type to check.</param>
    /// <returns>True if a handler exists.</returns>
    bool Has(string cardId, TriggerType trigger);

    /// <summary>
    /// Returns budget requirements for the given effect, or null if none.
    /// </summary>
    BudgetRequirement? GetBudgetRequirement(string cardId, TriggerType trigger);

    /// <summary>
    /// Returns NPC classification for an effect, or null if no ops are stored.
    /// </summary>
    EffectInfo? GetEffectInfo(string cardId, TriggerType trigger);

    /// <summary>
    /// Returns branch keys if the effect uses BranchOnChoice, or null otherwise.
    /// </summary>
    List<string>? GetChoiceOptions(string cardId, TriggerType trigger);
}
