using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine;

/// <summary>
/// Manages turn switching and phase transitions.
/// </summary>
public static class TurnManager
{
    /// <summary>Returns <c>true</c> if <paramref name="currentTurn"/> is the first turn (turn 1 skips battle phase).</summary>
    /// <param name="currentTurn">The current turn number.</param>
    public static bool IsFirstTurn(long currentTurn) => currentTurn == 1;

    /// <summary>
    /// Advance to the next phase within a turn and update state.
    /// Main → Battle (or Main → End on first turn), Battle → End, Draw → Main.
    /// Returns the previous phase.
    /// </summary>
    public static Phase AdvancePhase(GameState state)
    {
        var previous = state.CurrentPhase;
        state.CurrentPhase = previous switch
        {
            Phase.Main => IsFirstTurn(state.CurrentTurn) ? Phase.End : Phase.Battle,
            Phase.Battle => Phase.End,
            Phase.Draw => Phase.Main,
            _ => throw new GameRuleException(
                $"cannot advance from phase {previous.ToWireString()}"),
        };
        return previous;
    }

    /// <summary>
    /// Check if a given action type is allowed in the current phase.
    /// </summary>
    public static bool IsActionAllowedInPhase(Phase phase, ActionType action) => phase switch
    {
        Phase.Main => action is ActionType.PlayCard or ActionType.ScaleUp or ActionType.Monetize
            or ActionType.ActivateEffect or ActionType.Migrate or ActionType.EndPhase,
        Phase.Battle => action is ActionType.Attack or ActionType.ActivateEffect
            or ActionType.SetReactive or ActionType.EndPhase,
        Phase.End => action is ActionType.DiscardHand,
        _ => false,
    };

    /// <summary>
    /// Switch active player and start the next turn at Draw phase.
    /// </summary>
    public static void SwitchActivePlayer(GameState state)
    {
        state.ActivePlayer = state.OpponentOf(state.ActivePlayer);
        state.CurrentTurn++;
        state.CurrentPhase = Phase.Draw;
    }
}
