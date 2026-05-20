using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Helpers;

/// <summary>
/// WinConditionChecker は全勝利条件を判定し結果を返します
/// </summary>
public static class WinConditionChecker
{
    /// <summary>
    /// Checks all win conditions (budget zero, system down, turn limit, timeout).
    /// </summary>
    /// <param name="state">The current game state.</param>
    /// <param name="game">The game metadata.</param>
    /// <returns>Non-null if a win condition is met.</returns>
    public static GameOverResult? Check(BattleGameState state, Game game)
    {
        bool p1BudgetZero = state.Player1Budget <= 0;
        bool p2BudgetZero = state.Player2Budget <= 0;
        if (p1BudgetZero && p2BudgetZero)
        {
            return new GameOverResult(0, WinReason.Draw.ToWireString());
        }
        if (p1BudgetZero)
        {
            return new GameOverResult(2, WinReason.BudgetZero.ToWireString());
        }
        if (p2BudgetZero)
        {
            return new GameOverResult(1, WinReason.BudgetZero.ToWireString());
        }

        bool p1SystemDown = IsSystemDown(state, 1);
        bool p2SystemDown = IsSystemDown(state, 2);
        if (p1SystemDown && p2SystemDown)
        {
            return new GameOverResult(0, WinReason.Draw.ToWireString());
        }
        if (p1SystemDown)
        {
            return new GameOverResult(2, WinReason.SystemDown.ToWireString());
        }
        if (p2SystemDown)
        {
            return new GameOverResult(1, WinReason.SystemDown.ToWireString());
        }

        if (state.CurrentTurn >= BattleConstants.MaxTurns)
        {
            long winnerNum = state.Player1Budget > state.Player2Budget ? 1
                : state.Player2Budget > state.Player1Budget ? 2
                : 0;
            string reason = winnerNum == 0
                ? WinReason.Draw.ToWireString()
                : WinReason.TurnLimit.ToWireString();
            return new GameOverResult(winnerNum, reason);
        }

        bool p1Timeout = state.Player1TimeBank <= 0;
        bool p2Timeout = state.Player2TimeBank <= 0;
        if (p1Timeout && p2Timeout)
        {
            return new GameOverResult(0, WinReason.Draw.ToWireString());
        }
        if (p1Timeout)
        {
            return new GameOverResult(2, WinReason.TurnTimeout.ToWireString());
        }
        if (p2Timeout)
        {
            return new GameOverResult(1, WinReason.TurnTimeout.ToWireString());
        }

        return null;
    }

    /// <summary>
    /// Checks only the timeout condition (TimeBank &lt;= 0).
    /// Used by GameEngine before processing an action to detect mid-turn timeout.
    /// </summary>
    /// <param name="state">現在のゲーム状態。</param>
    /// <returns>タイムアウトによる勝敗結果。未確定なら null。</returns>
    public static GameOverResult? CheckTimeout(BattleGameState state)
    {
        if (state.Player1TimeBank <= 0)
        {
            return new GameOverResult(2, WinReason.TurnTimeout.ToWireString());
        }
        if (state.Player2TimeBank <= 0)
        {
            return new GameOverResult(1, WinReason.TurnTimeout.ToWireString());
        }
        return null;
    }

    /// <summary>
    /// Check if a player has lost due to system down (no active resources).
    /// Only triggers if the player has previously deployed a resource.
    /// </summary>
    /// <param name="state">現在のゲーム状態。</param>
    /// <param name="playerNum">判定対象プレイヤー番号 (1 または 2)。</param>
    /// <returns>システムダウン条件を満たしていれば true。</returns>
    public static bool IsSystemDown(BattleGameState state, long playerNum)
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
    /// <param name="state">現在のゲーム状態。</param>
    /// <param name="playerNum">判定対象プレイヤー番号 (1 または 2)。</param>
    /// <returns>ローンチ失敗条件を満たしていれば true。</returns>
    public static bool CheckLaunchFailure(BattleGameState state, long playerNum)
    {
        long personalTurn = (state.CurrentTurn + 1) / 2;
        if (personalTurn < BattleConstants.LaunchFailureTurn)
        {
            return false;
        }

        return !state.GetHasHadActiveResource(playerNum);
    }
}
