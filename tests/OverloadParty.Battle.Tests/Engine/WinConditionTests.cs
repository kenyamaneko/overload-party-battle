using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Engine;

/// <summary>
/// Tests for WinConditionChecker based on RULEBOOK.md §10:
/// 1. Budget ≤ 0 → 敗北 (budget_zero)
/// 2. 表向きリソース 0 体（稼働実績あり）→ 敗北 (system_down)
/// 3. T30 → Budget 多い方が勝ち / 同額なら引き分け (turn_limit)
/// 4. TimeBank ≤ 0 → 敗北 (timeout)
/// 5. 3ターン目終了時に稼働実績なし → 敗北 (launch_failure)
/// </summary>
public class WinConditionTests
{
    // ─── Budget Zero ──────────────────────────────────────────

    [Fact]
    public void Check_Player1BudgetZero_Player2Wins()
    {
        var state = TestFactory.MakeGameState(p1Budget: 0, p2Budget: 3000);
        var game = TestFactory.MakeGame();

        var result = WinConditionChecker.Check(state, game);

        result.Should().NotBeNull();
        result!.WinnerNum.Should().Be(2);
        result.Reason.Should().Be("budget_zero");
    }

    [Fact]
    public void Check_Player2BudgetNegative_Player1Wins()
    {
        var state = TestFactory.MakeGameState(p1Budget: 1000, p2Budget: -500);
        var game = TestFactory.MakeGame();

        var result = WinConditionChecker.Check(state, game);

        result.Should().NotBeNull();
        result!.WinnerNum.Should().Be(1);
        result.Reason.Should().Be("budget_zero");
    }

    [Fact]
    public void Check_BothBudgetZero_Draw()
    {
        var state = TestFactory.MakeGameState(p1Budget: 0, p2Budget: 0);
        var game = TestFactory.MakeGame();

        var result = WinConditionChecker.Check(state, game);

        result.Should().NotBeNull();
        result!.WinnerNum.Should().Be(0);
        result.Reason.Should().Be("draw");
    }

    [Fact]
    public void Check_BothSystemDown_Draw()
    {
        var state = TestFactory.MakeGameState();
        state.Player1HasHadActiveResource = true;
        state.Player2HasHadActiveResource = true;
        var game = TestFactory.MakeGame();

        var result = WinConditionChecker.Check(state, game);

        result.Should().NotBeNull();
        result!.WinnerNum.Should().Be(0);
        result.Reason.Should().Be("draw");
    }

    [Fact]
    public void Check_BothTimeout_Draw()
    {
        var state = TestFactory.MakeGameState();
        state.Player1TimeBank = 0;
        state.Player2TimeBank = 0;
        var game = TestFactory.MakeGame();

        var result = WinConditionChecker.Check(state, game);

        result.Should().NotBeNull();
        result!.WinnerNum.Should().Be(0);
        result.Reason.Should().Be("draw");
    }

    // ─── System Down ──────────────────────────────────────────

    [Fact]
    public void Check_SystemDown_NoActiveResources()
    {
        var state = TestFactory.MakeGameState();
        var game = TestFactory.MakeGame();

        // Player 1 had active resources before, but now all face-down
        state.Player1HasHadActiveResource = true;
        state.Player1Field.Frontend[0] = TestFactory.MakeResource(faceUp: false);

        var result = WinConditionChecker.Check(state, game);

        result.Should().NotBeNull();
        result!.WinnerNum.Should().Be(2);
        result.Reason.Should().Be("system_down");
    }

    /// <summary>
    /// 稼働実績フラグが true のプレイヤーにのみ適用
    /// Never-deployed player should not trigger system down.
    /// </summary>
    [Fact]
    public void Check_NoActiveResources_ButNeverDeployed_NoSystemDown()
    {
        var state = TestFactory.MakeGameState();
        var game = TestFactory.MakeGame();

        // Player 1 has never had an active resource
        state.Player1HasHadActiveResource = false;

        WinConditionChecker.Check(state, game).Should().BeNull();
    }

    [Fact]
    public void IsSystemDown_WithFaceUpResource_ReturnsFalse()
    {
        var state = TestFactory.MakeGameState();
        state.Player1HasHadActiveResource = true;
        state.Player1Field.Backend[0] = TestFactory.MakeResource(faceUp: true);

        WinConditionChecker.IsSystemDown(state, 1).Should().BeFalse();
    }

    // ─── Turn Limit ───────────────────────────────────────────

    /// <summary>
    /// T30 → Budget 多い方が勝利
    /// </summary>
    [Fact]
    public void Check_TurnLimit_HigherBudgetWins()
    {
        var state = TestFactory.MakeGameState(turn: 30, p1Budget: 3000, p2Budget: 2000);
        var game = TestFactory.MakeGame();

        var result = WinConditionChecker.Check(state, game);

        result.Should().NotBeNull();
        result!.WinnerNum.Should().Be(1);
        result.Reason.Should().Be("turn_limit");
    }

    [Fact]
    public void Check_TurnLimit_Player2HigherBudget()
    {
        var state = TestFactory.MakeGameState(turn: 30, p1Budget: 1000, p2Budget: 4000);
        var game = TestFactory.MakeGame();

        var result = WinConditionChecker.Check(state, game);

        result.Should().NotBeNull();
        result!.WinnerNum.Should().Be(2);
        result.Reason.Should().Be("turn_limit");
    }

    /// <summary>
    /// 同額なら引き分け
    /// </summary>
    [Fact]
    public void Check_TurnLimit_EqualBudget_Draw()
    {
        var state = TestFactory.MakeGameState(turn: 30, p1Budget: 2500, p2Budget: 2500);
        var game = TestFactory.MakeGame();

        var result = WinConditionChecker.Check(state, game);

        result.Should().NotBeNull();
        result!.WinnerNum.Should().Be(0);
        result.Reason.Should().Be("draw");
    }

    [Fact]
    public void Check_BeforeTurnLimit_NoGameOver()
    {
        var state = TestFactory.MakeGameState(turn: 29);
        var game = TestFactory.MakeGame();

        WinConditionChecker.Check(state, game).Should().BeNull();
    }

    // ─── Timeout ──────────────────────────────────────────────

    [Fact]
    public void Check_Player1Timeout_Player2Wins()
    {
        var state = TestFactory.MakeGameState();
        state.Player1TimeBank = 0;
        var game = TestFactory.MakeGame();

        var result = WinConditionChecker.Check(state, game);

        result.Should().NotBeNull();
        result!.WinnerNum.Should().Be(2);
        result.Reason.Should().Be("timeout");
    }

    [Fact]
    public void Check_Player2Timeout_Player1Wins()
    {
        var state = TestFactory.MakeGameState();
        state.Player2TimeBank = -10;
        var game = TestFactory.MakeGame();

        var result = WinConditionChecker.Check(state, game);

        result.Should().NotBeNull();
        result!.WinnerNum.Should().Be(1);
        result.Reason.Should().Be("timeout");
    }

    // ─── Launch Failure ───────────────────────────────────────

    /// <summary>
    /// 自分の3ターン目（先攻T5、後攻T6）のエンドフェーズ終了時に
    /// 一度も表向きリソースが存在しなかった場合に敗北。
    /// personalTurn = (currentTurn + 1) / 2
    /// Turn 5 → personalTurn = 3 (先攻)
    /// </summary>
    [Fact]
    public void CheckLaunchFailure_Turn5_FirstPlayer_NoResources_True()
    {
        var state = TestFactory.MakeGameState(turn: 5);
        state.Player1HasHadActiveResource = false;

        WinConditionChecker.CheckLaunchFailure(state, 1).Should().BeTrue();
    }

    /// <summary>
    /// Turn 6 → personalTurn = (6+1)/2 = 3 (後攻の3ターン目)
    /// </summary>
    [Fact]
    public void CheckLaunchFailure_Turn6_SecondPlayer_NoResources_True()
    {
        var state = TestFactory.MakeGameState(turn: 6);
        state.Player2HasHadActiveResource = false;

        WinConditionChecker.CheckLaunchFailure(state, 2).Should().BeTrue();
    }

    [Fact]
    public void CheckLaunchFailure_BeforeTurn3_ReturnsFalse()
    {
        var state = TestFactory.MakeGameState(turn: 3); // personalTurn = 2
        state.Player1HasHadActiveResource = false;

        WinConditionChecker.CheckLaunchFailure(state, 1).Should().BeFalse();
    }

    [Fact]
    public void CheckLaunchFailure_HasDeployed_ReturnsFalse()
    {
        var state = TestFactory.MakeGameState(turn: 5);
        state.Player1HasHadActiveResource = true;

        WinConditionChecker.CheckLaunchFailure(state, 1).Should().BeFalse();
    }

    // ─── Priority: Budget checked before SystemDown ───────────

    [Fact]
    public void Check_BudgetZero_TakesPriority_OverSystemDown()
    {
        var state = TestFactory.MakeGameState(p1Budget: 0);
        state.Player1HasHadActiveResource = true;
        // No resources at all → would also be system down
        var game = TestFactory.MakeGame();

        var result = WinConditionChecker.Check(state, game);
        result.Should().NotBeNull();
        result!.Reason.Should().Be("budget_zero");
    }

    // ─── No win condition ─────────────────────────────────────

    [Fact]
    public void Check_NormalState_NoGameOver()
    {
        var state = TestFactory.MakeGameState(turn: 5, p1Budget: 4000, p2Budget: 3500);
        state.Player1HasHadActiveResource = true;
        state.Player1Field.Frontend[0] = TestFactory.MakeResource(faceUp: true);
        state.Player2HasHadActiveResource = true;
        state.Player2Field.Frontend[0] = TestFactory.MakeResource(instanceId: "inst_2", faceUp: true);
        var game = TestFactory.MakeGame();

        WinConditionChecker.Check(state, game).Should().BeNull();
    }
}
