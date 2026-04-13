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
    public static void PushToChain(BattleGameState state, ChainEntry entry)
    {
        if (state.ChainStack.Count >= BattleConstants.MaxChainLevel)
        {
            throw new GameRuleException($"chain stack full (max {BattleConstants.MaxChainLevel})");
        }

        // リアクティブ cannot chain on top of unresolved reactive
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
    public static bool CanChainReactive(BattleGameState state)
    {
        if (!state.ChainStack.Any()) { return false; }
        if (state.ChainStack.Count >= BattleConstants.MaxChainLevel) { return false; }

        // 最後のエントリがリアクティブであってはならない
        var last = state.ChainStack.Last();
        return last.ActionType != ActionTypes.Reactive;
    }

    /// <summary>
    /// Check if the chain stack has any unresolved entries.
    /// </summary>
    public static bool IsChainActive(BattleGameState state)
    {
        return state.ChainStack.Any(e => !e.Resolved);
    }

    /// <summary>
    /// Mark the chain entry at the given 1-indexed chain level as resolved.
    /// Chain levels are assigned by <see cref="PushToChain"/> starting from 1 (LIFO, RULEBOOK §12).
    /// </summary>
    /// <exception cref="GameRuleException">
    /// thrown when no entry with the given chain level exists on the stack.
    /// </exception>
    public static void MarkResolved(BattleGameState state, long chainLevel)
    {
        var entry = state.ChainStack.FirstOrDefault(e => e.ChainLevel == chainLevel)
            ?? throw new GameRuleException($"chain level {chainLevel} not on stack");
        entry.Resolved = true;
    }
}
