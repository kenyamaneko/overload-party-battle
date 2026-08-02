using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Engine;

public class WinConditionTests
{
    [Trait("対象", "バジェットゼロ敗北判定")]
    public class ByBudget
    {
        [Theory(DisplayName = "残りバジェットで勝敗を判定する (0 以下で敗北、両者 0 なら引き分け)")]
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

    [Trait("対象", "システムダウン敗北判定")]
    public class BySystemDown
    {
        [Fact(DisplayName = "稼働実績のあるプレイヤーの表向きリソースが 0 になると、相手が勝者になりシステムダウン敗北する")]
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

        [Fact(DisplayName = "両者とも表向きリソースが 0 のとき、システムダウンで引き分けになる")]
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

        [Fact(DisplayName = "稼働実績のないプレイヤーは表向きリソースが 0 でもシステムダウン敗北にならない")]
        public void NoActiveResources_ButNeverDeployed_NoSystemDown()
        {
            var state = TestFactory.MakeGameState();
            var game = TestFactory.MakeGame();

            // Player 1 has never had an active resource
            state.Player1HasOperated = false;

            WinConditionChecker.Check(state, game).Should().BeNull();
        }

        [Fact(DisplayName = "表向きリソースが 1 体でも存在すれば、システムダウンと判定されない")]
        public void WithFaceUpResource_IsSystemDownReturnsFalse()
        {
            var state = TestFactory.MakeGameState();
            state.Player1HasOperated = true;
            state.Player1Field.Backend[0] = TestFactory.MakeResource(faceUp: true);

            WinConditionChecker.IsSystemDown(state, 1).Should().BeFalse();
        }
    }

    [Trait("対象", "ターンリミット決着判定")]
    public class ByTurnLimit
    {
        [Theory(DisplayName = "ターン 30 のとき、残りバジェットで勝敗を判定する (多い方が勝ち、同額なら引き分け)")]
        [InlineData(3000, 2000, 1, "turn_limit")]
        [InlineData(1000, 4000, 2, "turn_limit")]
        [InlineData(2500, 2500, 0, "draw")]
        public void DecidesByRemainingBudget(long p1Budget, long p2Budget, long winner, string reason)
        {
            var state = TestFactory.MakeGameState(turn: 30, p1Budget: p1Budget, p2Budget: p2Budget);

            var result = WinConditionChecker.CheckTurnLimit(state);

            result.Should().NotBeNull();
            result!.WinnerNum.Should().Be(winner);
            result.Reason.Should().Be(reason);
        }

        [Fact(DisplayName = "ターン 29 ではターンリミットに達しておらず、勝敗が決まらない")]
        public void BeforeTurnLimit_NoGameOver()
        {
            var state = TestFactory.MakeGameState(turn: 29, p1Budget: 3000, p2Budget: 2000);

            WinConditionChecker.CheckTurnLimit(state).Should().BeNull();
        }

        [Fact(DisplayName = "ターン 30 でも、アクション解決後の判定ではターンリミットが成立せずゲームが続行する")]
        public void NotSettledByPostActionCheck()
        {
            var state = TestFactory.MakeGameState(turn: 30, p1Budget: 3000, p2Budget: 2000);
            var game = TestFactory.MakeGame();

            WinConditionChecker.Check(state, game).Should().BeNull();
        }
    }

    [Trait("対象", "タイムアウト敗北判定")]
    public class ByTimeout
    {
        [Fact(DisplayName = "プレイヤー 1 のタイムバンクが 0 になると、相手が勝者になりタイムアウト敗北する")]
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

        [Fact(DisplayName = "プレイヤー 2 のタイムバンクが 0 未満 (-10) になると、相手が勝者になりタイムアウト敗北する")]
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

        [Fact(DisplayName = "両者のタイムバンクが 0 のとき、タイムアウトで引き分けになる")]
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

    [Trait("対象", "ローンチ失敗判定")]
    public class ByLaunchFailure
    {
        [Fact(DisplayName = "先攻の 3 ターン目 (全体ターン 5) 終了時に稼働実績がなければ、ローンチ失敗と判定される")]
        public void Turn5_FirstPlayer_NoResources_True()
        {
            var state = TestFactory.MakeGameState(turn: 5);
            state.Player1HasOperated = false;

            WinConditionChecker.CheckLaunchFailure(state, 1).Should().BeTrue();
        }

        [Fact(DisplayName = "後攻の 3 ターン目 (全体ターン 6) 終了時に稼働実績がなければ、ローンチ失敗と判定される")]
        public void Turn6_SecondPlayer_NoResources_True()
        {
            var state = TestFactory.MakeGameState(turn: 6);
            state.Player2HasOperated = false;

            WinConditionChecker.CheckLaunchFailure(state, 2).Should().BeTrue();
        }

        [Fact(DisplayName = "3 ターン目より前 (全体ターン 3) では、稼働実績がなくてもローンチ失敗にならない")]
        public void BeforeTurn3_ReturnsFalse()
        {
            var state = TestFactory.MakeGameState(turn: 3); // personalTurn = 2
            state.Player1HasOperated = false;

            WinConditionChecker.CheckLaunchFailure(state, 1).Should().BeFalse();
        }

        [Fact(DisplayName = "3 ターン目 (全体ターン 5) でも稼働実績があれば、ローンチ失敗にならない")]
        public void HasDeployed_ReturnsFalse()
        {
            var state = TestFactory.MakeGameState(turn: 5);
            state.Player1HasOperated = true;

            WinConditionChecker.CheckLaunchFailure(state, 1).Should().BeFalse();
        }
    }

    [Trait("対象", "勝敗判定の優先順位")]
    public class Priority
    {
        [Fact(DisplayName = "バジェットゼロとシステムダウンが同時に成立するとき、バジェットゼロが優先して敗因になる")]
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

    [Trait("対象", "勝敗未確定の判定")]
    public class NoWinCondition
    {
        [Fact(DisplayName = "両者が稼働中で十分なバジェットがある通常状態では、勝敗が決まらない")]
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
