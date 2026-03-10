using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine;

/// <summary>
/// Checks all win conditions and returns the result.
/// </summary>
public static class WinConditionChecker
{
    /// <summary>
    /// Checks all win conditions (budget zero, system down, turn limit, timeout).
    /// </summary>
    /// <param name="state">The current game state.</param>
    /// <param name="game">The game metadata.</param>
    /// <returns>Non-null if a win condition is met.</returns>
    public static GameOverResult? Check(GameState state, Game game)
    {
        if (state.Player1Budget <= 0)
        {
            return new GameOverResult(2, WinReason.BudgetZero.ToWireString());
        }
        if (state.Player2Budget <= 0)
        {
            return new GameOverResult(1, WinReason.BudgetZero.ToWireString());
        }

        if (IsSystemDown(state, 1))
        {
            return new GameOverResult(2, WinReason.SystemDown.ToWireString());
        }
        if (IsSystemDown(state, 2))
        {
            return new GameOverResult(1, WinReason.SystemDown.ToWireString());
        }

        if (state.CurrentTurn >= GameConstants.MaxTurns)
        {
            long winnerNum = state.Player1Budget > state.Player2Budget ? 1
                : state.Player2Budget > state.Player1Budget ? 2
                : 0;
            string reason = winnerNum == 0
                ? WinReason.Draw.ToWireString()
                : WinReason.TurnLimit.ToWireString();
            return new GameOverResult(winnerNum, reason);
        }

        if (state.Player1TimeBank <= 0)
        {
            return new GameOverResult(2, WinReason.Timeout.ToWireString());
        }
        if (state.Player2TimeBank <= 0)
        {
            return new GameOverResult(1, WinReason.Timeout.ToWireString());
        }

        return null;
    }

    /// <summary>
    /// Checks only the timeout condition (TimeBank &lt;= 0).
    /// Used by GameEngine before processing an action to detect mid-turn timeout.
    /// </summary>
    public static GameOverResult? CheckTimeout(GameState state)
    {
        if (state.Player1TimeBank <= 0)
        {
            return new GameOverResult(2, WinReason.Timeout.ToWireString());
        }
        if (state.Player2TimeBank <= 0)
        {
            return new GameOverResult(1, WinReason.Timeout.ToWireString());
        }
        return null;
    }

    /// <summary>
    /// Check if a player has lost due to system down (no active resources).
    /// Only triggers if the player has previously deployed a resource.
    /// </summary>
    public static bool IsSystemDown(GameState state, long playerNum)
    {
        if (!state.GetHasHadActiveResource(playerNum))
        {
            return false;
        }
        var field = state.GetField(playerNum);
        return !FieldHelpers.HasAnyActiveResources(field);
    }

    /// <summary>
    /// Check if a player has failed to launch (no active resource by turn 3).
    /// </summary>
    public static bool CheckLaunchFailure(GameState state, long playerNum)
    {
        long personalTurn = (state.CurrentTurn + 1) / 2;
        if (personalTurn < GameConstants.LaunchFailureTurn)
        {
            return false;
        }

        return !state.GetHasHadActiveResource(playerNum);
    }
}
