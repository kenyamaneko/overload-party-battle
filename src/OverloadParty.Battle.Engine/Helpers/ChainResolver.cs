using System.Linq;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Helpers;

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
        if (state.ChainStack.Count >= BattleConstants.MaxChainLevel)
        {
            throw new GameRuleException($"chain stack full (max {BattleConstants.MaxChainLevel})");
        }

        // Reactive cannot chain on top of unresolved reactive
        if (entry.ActionType == ActionTypes.Reactive)
        {
            foreach (var existing in state.ChainStack)
            {
                if (existing.ActionType == ActionTypes.Reactive && !existing.Resolved)
                {
                    throw new GameRuleException("cannot chain reactive on unresolved reactive");
                }
            }
        }

        entry.ChainLevel = state.ChainStack.Count + 1;
        state.ChainStack.Add(entry);
    }

    /// <summary>
    /// Check if a reactive can be chained on the current stack.
    /// </summary>
    public static bool CanChainReactive(GameState state)
    {
        if (!state.ChainStack.Any()) { return false; }
        if (state.ChainStack.Count >= BattleConstants.MaxChainLevel) { return false; }

        // Last entry must not be reactive
        var last = state.ChainStack.Last();
        return last.ActionType != ActionTypes.Reactive;
    }

    /// <summary>
    /// Check if the chain stack has any unresolved entries.
    /// </summary>
    public static bool IsChainActive(GameState state)
    {
        return state.ChainStack.Any(e => !e.Resolved);
    }
}
