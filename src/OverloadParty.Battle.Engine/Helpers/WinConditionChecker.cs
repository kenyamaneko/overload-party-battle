using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Helpers;

/// <summary>
/// WinConditionChecker は全勝利条件を判定し結果を返します
/// </summary>
public static class WinConditionChecker
{
    /// <summary>
    /// アクション解決後の汎用判定を、バジェットゼロ → システムダウン → タイムアウトの順で行います。
    /// ターンリミットは T30 終了時にのみ成立するため含みません。
    /// </summary>
    /// <param name="state">The current game state.</param>
    /// <param name="game">The game metadata.</param>
    /// <returns>Non-null if a win condition is met.</returns>
    public static GameOverResult? Check(BattleGameState state, Game game) =>
        CheckBudgetZero(state) ?? CheckSystemDown(state) ?? CheckTimeout(state);

    /// <summary>
    /// バジェットが 0 以下になったプレイヤーの敗北を判定します。両者同時なら引き分けになります。
    /// </summary>
    /// <param name="state">現在のゲーム状態。</param>
    /// <returns>バジェットゼロによる勝敗結果。未確定なら null。</returns>
    public static GameOverResult? CheckBudgetZero(BattleGameState state)
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
        return null;
    }

    /// <summary>
    /// 表向きリソースを失ったプレイヤーの敗北を判定します。両者同時なら引き分けになります。
    /// </summary>
    /// <param name="state">現在のゲーム状態。</param>
    /// <returns>システムダウンによる勝敗結果。未確定なら null。</returns>
    public static GameOverResult? CheckSystemDown(BattleGameState state)
    {
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
        return null;
    }

    /// <summary>
    /// 最終ターンに到達したときの決着を判定します。バジェットが多い側が勝ち、同額なら引き分けになります。
    /// </summary>
    /// <param name="state">現在のゲーム状態。</param>
    /// <returns>ターンリミットによる勝敗結果。最終ターンに達していなければ null。</returns>
    public static GameOverResult? CheckTurnLimit(BattleGameState state)
    {
        if (state.CurrentTurn < BattleConstants.MaxTurns)
        {
            return null;
        }

        long winnerNum = state.Player1Budget > state.Player2Budget ? 1
            : state.Player2Budget > state.Player1Budget ? 2
            : 0;
        string reason = winnerNum == 0
            ? WinReason.Draw.ToWireString()
            : WinReason.TurnLimit.ToWireString();
        return new GameOverResult(winnerNum, reason);
    }

    /// <summary>
    /// Checks only the timeout condition (TimeBank &lt;= 0).
    /// Used by GameEngine before processing an action to detect mid-turn timeout.
    /// </summary>
    /// <param name="state">現在のゲーム状態。</param>
    /// <returns>タイムアウトによる勝敗結果。未確定なら null。</returns>
    public static GameOverResult? CheckTimeout(BattleGameState state)
    {
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
    /// Check if a player has lost due to system down (no active resources).
    /// Only triggers if the player has previously deployed a resource.
    /// </summary>
    /// <param name="state">現在のゲーム状態。</param>
    /// <param name="playerNum">判定対象プレイヤー番号 (1 または 2)。</param>
    /// <returns>システムダウン条件を満たしていれば true。</returns>
    public static bool IsSystemDown(BattleGameState state, long playerNum)
    {
        if (!state.GetHasOperated(playerNum))
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

        return !state.GetHasOperated(playerNum);
    }
}
