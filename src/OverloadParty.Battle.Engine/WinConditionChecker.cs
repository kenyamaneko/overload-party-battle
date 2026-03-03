using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine;

/// <summary>
/// Checks all win conditions and returns the result.
/// </summary>
public static class WinConditionChecker
{
    /// <summary>
    /// Check all win conditions. Returns (winnerNum, reason, gameOver).
    /// winnerNum=0 means draw.
    /// </summary>
    public static (long WinnerNum, string Reason, bool GameOver) Check(GameState state, Game game)
    {
        // 1. Budget Zero
        if (state.Player1Budget <= 0)
            return (2, WinReason.BudgetZero.ToWireString(), true);
        if (state.Player2Budget <= 0)
            return (1, WinReason.BudgetZero.ToWireString(), true);

        // 2. System Down (no face-up resources, but player has had active resources before)
        if (IsSystemDown(state, 1))
            return (2, WinReason.SystemDown.ToWireString(), true);
        if (IsSystemDown(state, 2))
            return (1, WinReason.SystemDown.ToWireString(), true);

        // 3. Turn Limit (30 turns = 15 full rounds)
        if (state.CurrentTurn >= GameConstants.MaxTurns)
        {
            if (state.Player1Budget > state.Player2Budget)
                return (1, WinReason.TurnLimit.ToWireString(), true);
            if (state.Player2Budget > state.Player1Budget)
                return (2, WinReason.TurnLimit.ToWireString(), true);
            return (0, WinReason.Draw.ToWireString(), true);
        }

        // 4. Timeout
        if (state.Player1TimeBank <= 0)
            return (2, WinReason.Timeout.ToWireString(), true);
        if (state.Player2TimeBank <= 0)
            return (1, WinReason.Timeout.ToWireString(), true);

        return (0, "", false);
    }

    /// <summary>
    /// Check if a player has lost due to system down (no active resources).
    /// Only triggers if the player has previously deployed a resource.
    /// </summary>
    public static bool IsSystemDown(GameState state, long playerNum)
    {
        var field = state.GetField(playerNum);
        if (!field.HasHadActiveResource) return false;
        return !FieldHelpers.HasAnyActiveResources(field);
    }

    /// <summary>
    /// Check if a player has failed to launch (no active resource by turn 3).
    /// </summary>
    public static bool CheckLaunchFailure(GameState state, long playerNum)
    {
        long personalTurn = (state.CurrentTurn + 1) / 2;
        if (personalTurn < GameConstants.LaunchFailureTurn) return false;

        var field = state.GetField(playerNum);
        return !field.HasHadActiveResource;
    }
}
