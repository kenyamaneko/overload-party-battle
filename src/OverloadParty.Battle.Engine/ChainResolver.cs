using System.Linq;
using OverloadParty.Battle.Models;
using OverloadParty.Battle.Engine.Effects;

namespace OverloadParty.Battle.Engine;

/// <summary>
/// Manages the chain stack (LIFO, max 3 levels) for reactive effects.
/// </summary>
public static class ChainResolver
{
    /// <summary>
    /// Push an entry onto the chain stack.
    /// </summary>
    public static void PushToChain(GameState state, ChainEntry entry)
    {
        if (state.ChainStack.Count >= GameConstants.MaxChainLevel)
        {
            throw new GameRuleException($"chain stack full (max {GameConstants.MaxChainLevel})");
        }

        // Reactive cannot chain on top of unresolved reactive
        if (entry.ActionType == WireActionTypes.Reactive)
        {
            foreach (var existing in state.ChainStack)
            {
                if (existing.ActionType == WireActionTypes.Reactive && !existing.Resolved)
                {
                    throw new GameRuleException("cannot chain reactive on unresolved reactive");
                }
            }
        }

        entry.ChainLevel = state.ChainStack.Count + 1;
        state.ChainStack.Add(entry);
    }

    /// <summary>
    /// Resolve all chain entries in LIFO order (last added = first resolved).
    /// </summary>
    public static List<GameEvent> ResolveChain(
        GameState state, Game game, ICardCache cc, IEffectRegistry effects)
    {
        var allEvents = new List<GameEvent>();
        var stack = state.ChainStack;

        // LIFO: iterate backwards
        for (int i = stack.Count - 1; i >= 0; i--)
        {
            var entry = stack[i];
            if (entry.Resolved) { continue; }

            var events = ResolveChainEntry(state, game, entry, cc, effects);
            allEvents.AddRange(events);
            entry.Resolved = true;
        }

        // Clear the stack after resolution
        state.ChainStack.Clear();
        return allEvents;
    }

    private static List<GameEvent> ResolveChainEntry(
        GameState state, Game game, ChainEntry entry, ICardCache cc, IEffectRegistry effects)
    {
        var trigger = ChainActionToTrigger(entry.ActionType);

        // Find the source card (resource or support)
        var playerNum = entry.SourcePlayerID == game.Player1ID ? 1L : 2L;
        var field = state.GetField(playerNum);

        var handler = effects.Get(0, trigger); // Lookup by source card
        if (handler is null) { return []; }

        // Build context and execute
        // TODO: Full implementation when effect system is complete
        return [];
    }

    public static TriggerType ChainActionToTrigger(string actionType) => actionType switch
    {
        WireActionTypes.Reactive => TriggerType.Reactive,
        WireActionTypes.Attack => TriggerType.OnAttack,
        _ => TriggerType.Activate
    };

    /// <summary>
    /// Check if a reactive can be chained on the current stack.
    /// </summary>
    public static bool CanChainReactive(GameState state)
    {
        if (!state.ChainStack.Any()) { return false; }
        if (state.ChainStack.Count >= GameConstants.MaxChainLevel) { return false; }

        // Last entry must not be reactive
        var last = state.ChainStack.Last();
        return last.ActionType != WireActionTypes.Reactive;
    }

    /// <summary>
    /// Check if the chain stack has any unresolved entries.
    /// </summary>
    public static bool IsChainActive(GameState state)
    {
        return state.ChainStack.Any(e => !e.Resolved);
    }
}
