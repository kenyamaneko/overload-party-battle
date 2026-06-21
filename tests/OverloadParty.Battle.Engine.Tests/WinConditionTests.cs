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
    /// <summary>Tests for the budget-zero win condition.</summary>
    public class ByBudget
    {
        [Theory]
        [InlineData(0, 3000, 2, "budget_zero")]
        [InlineData(1000, -500, 1, "budget_zero")]
        [InlineData(0, 0, 0, "draw")]
        public void DecidesByRemainingBudget(long p1Budget, long p2Budget, long winner, string reason)
        {
            var state = TestFactory.MakeGameState(p1Budget: p1Budget, p2Budget: p2Budget);
            var game = TestFactory.MakeGame();

            var result = WinConditionChecker.Check(state, game);

            result.Should().NotBeNull();
            result!.WinnerNum.Should().Be(winner);
            result.Reason.Should().Be(reason);
        }
    }

    /// <summary>Tests for the system-down win condition.</summary>
    public class BySystemDown
    {
        [Fact]
        public void NoActiveResources_OpponentWins()
        {
            var state = TestFactory.MakeGameState();
            var game = TestFactory.MakeGame();

            // Player 1 had active resources before, but now all face-down
            state.Player1HasOperated = true;
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(faceUp: false);

            var result = WinConditionChecker.Check(state, game);

            result.Should().NotBeNull();
            result!.WinnerNum.Should().Be(2);
            result.Reason.Should().Be("system_down");
        }

        [Fact]
        public void BothDown_Draw()
        {
            var state = TestFactory.MakeGameState();
            state.Player1HasOperated = true;
            state.Player2HasOperated = true;
            var game = TestFactory.MakeGame();

            var result = WinConditionChecker.Check(state, game);

            result.Should().NotBeNull();
            result!.WinnerNum.Should().Be(0);
            result.Reason.Should().Be("draw");
        }

        /// <summary>
        /// 稼働実績フラグが true のプレイヤーにのみ適用
        /// Never-deployed player should not trigger system down.
        /// </summary>
        [Fact]
        public void NoActiveResources_ButNeverDeployed_NoSystemDown()
        {
            var state = TestFactory.MakeGameState();
            var game = TestFactory.MakeGame();

            // Player 1 has never had an active resource
            state.Player1HasOperated = false;

            WinConditionChecker.Check(state, game).Should().BeNull();
        }

        [Fact]
        public void WithFaceUpResource_IsSystemDownReturnsFalse()
        {
            var state = TestFactory.MakeGameState();
            state.Player1HasOperated = true;
            state.Player1Field.Backend[0] = TestFactory.MakeResource(faceUp: true);

            WinConditionChecker.IsSystemDown(state, 1).Should().BeFalse();
        }
    }

    /// <summary>Tests for the turn-limit win condition.</summary>
    public class ByTurnLimit
    {
        /// <summary>
        /// T30 → Budget 多い方が勝利
        /// </summary>
        [Fact]
        public void HigherBudgetWins()
        {
            var state = TestFactory.MakeGameState(turn: 30, p1Budget: 3000, p2Budget: 2000);
            var game = TestFactory.MakeGame();

            var result = WinConditionChecker.Check(state, game);

            result.Should().NotBeNull();
            result!.WinnerNum.Should().Be(1);
            result.Reason.Should().Be("turn_limit");
        }

        [Fact]
        public void Player2HigherBudget()
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
        public void EqualBudget_Draw()
        {
            var state = TestFactory.MakeGameState(turn: 30, p1Budget: 2500, p2Budget: 2500);
            var game = TestFactory.MakeGame();

            var result = WinConditionChecker.Check(state, game);

            result.Should().NotBeNull();
            result!.WinnerNum.Should().Be(0);
            result.Reason.Should().Be("draw");
        }

        [Fact]
        public void BeforeTurnLimit_NoGameOver()
        {
            var state = TestFactory.MakeGameState(turn: 29);
            var game = TestFactory.MakeGame();

            WinConditionChecker.Check(state, game).Should().BeNull();
        }
    }

    /// <summary>Tests for the timeout win condition.</summary>
    public class ByTimeout
    {
        [Fact]
        public void Player1Timeout_Player2Wins()
        {
            var state = TestFactory.MakeGameState();
            state.Player1TimeBank = 0;
            var game = TestFactory.MakeGame();

            var result = WinConditionChecker.Check(state, game);

            result.Should().NotBeNull();
            result!.WinnerNum.Should().Be(2);
            result.Reason.Should().Be("turn_timeout");
        }

        [Fact]
        public void Player2Timeout_Player1Wins()
        {
            var state = TestFactory.MakeGameState();
            state.Player2TimeBank = -10;
            var game = TestFactory.MakeGame();

            var result = WinConditionChecker.Check(state, game);

            result.Should().NotBeNull();
            result!.WinnerNum.Should().Be(1);
            result.Reason.Should().Be("turn_timeout");
        }

        [Fact]
        public void BothTimeout_Draw()
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
    }

    /// <summary>Tests for WinConditionChecker.CheckLaunchFailure.</summary>
    public class ByLaunchFailure
    {
        /// <summary>
        /// 自分の3ターン目（先攻T5、後攻T6）のエンドフェーズ終了時に
        /// 一度も表向きリソースが存在しなかった場合に敗北。
        /// personalTurn = (currentTurn + 1) / 2
        /// Turn 5 → personalTurn = 3 (先攻)
        /// </summary>
        [Fact]
        public void Turn5_FirstPlayer_NoResources_True()
        {
            var state = TestFactory.MakeGameState(turn: 5);
            state.Player1HasOperated = false;

            WinConditionChecker.CheckLaunchFailure(state, 1).Should().BeTrue();
        }

        /// <summary>
        /// Turn 6 → personalTurn = (6+1)/2 = 3 (後攻の3ターン目)
        /// </summary>
        [Fact]
        public void Turn6_SecondPlayer_NoResources_True()
        {
            var state = TestFactory.MakeGameState(turn: 6);
            state.Player2HasOperated = false;

            WinConditionChecker.CheckLaunchFailure(state, 2).Should().BeTrue();
        }

        [Fact]
        public void BeforeTurn3_ReturnsFalse()
        {
            var state = TestFactory.MakeGameState(turn: 3); // personalTurn = 2
            state.Player1HasOperated = false;

            WinConditionChecker.CheckLaunchFailure(state, 1).Should().BeFalse();
        }

        [Fact]
        public void HasDeployed_ReturnsFalse()
        {
            var state = TestFactory.MakeGameState(turn: 5);
            state.Player1HasOperated = true;

            WinConditionChecker.CheckLaunchFailure(state, 1).Should().BeFalse();
        }
    }

    /// <summary>Tests for win condition priority ordering.</summary>
    public class Priority
    {
        [Fact]
        public void BudgetZero_TakesPriority_OverSystemDown()
        {
            var state = TestFactory.MakeGameState(p1Budget: 0);
            state.Player1HasOperated = true;
            // No resources at all → would also be system down
            var game = TestFactory.MakeGame();

            var result = WinConditionChecker.Check(state, game);
            result.Should().NotBeNull();
            result!.Reason.Should().Be("budget_zero");
        }
    }

    /// <summary>Tests that a normal game state yields no win condition.</summary>
    public class NoWinCondition
    {
        [Fact]
        public void NormalState_NoGameOver()
        {
            var state = TestFactory.MakeGameState(turn: 5, p1Budget: 4000, p2Budget: 3500);
            state.Player1HasOperated = true;
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(faceUp: true);
            state.Player2HasOperated = true;
            state.Player2Field.Frontend[0] = TestFactory.MakeResource(instanceId: "inst_2", faceUp: true);
            var game = TestFactory.MakeGame();

            WinConditionChecker.Check(state, game).Should().BeNull();
        }
    }
}
