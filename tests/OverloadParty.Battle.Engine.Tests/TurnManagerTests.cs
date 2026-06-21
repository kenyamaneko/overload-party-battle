using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Engine;

public class TurnManagerTests
{
    /// <summary>Tests for TurnManager.IsFirstTurn.</summary>
    public class IsFirstTurn
    {
        [Fact]
        public void Turn1_ReturnsTrue()
        {
            TurnManager.IsFirstTurn(1).Should().BeTrue();
        }

        [Theory]
        [InlineData(2)]
        [InlineData(10)]
        [InlineData(30)]
        public void LaterTurns_ReturnsFalse(long turn)
        {
            TurnManager.IsFirstTurn(turn).Should().BeFalse();
        }
    }

    /// <summary>Tests for TurnManager.AdvancePhase.</summary>
    public class AdvancePhase
    {
        [Fact]
        public void DrawPhase_GoesToMain()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Draw);

            var previous = TurnManager.AdvancePhase(state);

            previous.Should().Be(Phase.Draw);
            state.CurrentPhase.Should().Be(Phase.Main);
        }

        [Fact]
        public void MainPhase_FirstTurn_SkipsBattleGoesToEnd()
        {
            var state = TestFactory.MakeGameState(turn: 1, phase: Phase.Main);

            var previous = TurnManager.AdvancePhase(state);

            previous.Should().Be(Phase.Main);
            state.CurrentPhase.Should().Be(Phase.End);
        }

        [Theory]
        [InlineData(2)]
        [InlineData(5)]
        [InlineData(30)]
        public void MainPhase_LaterTurns_GoesToBattle(long turn)
        {
            var state = TestFactory.MakeGameState(turn: turn, phase: Phase.Main);

            var previous = TurnManager.AdvancePhase(state);

            previous.Should().Be(Phase.Main);
            state.CurrentPhase.Should().Be(Phase.Battle);
        }

        [Fact]
        public void BattlePhase_GoesToEnd()
        {
            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Battle);

            var previous = TurnManager.AdvancePhase(state);

            previous.Should().Be(Phase.Battle);
            state.CurrentPhase.Should().Be(Phase.End);
        }

        [Fact]
        public void EndPhase_Throws()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.End);

            var act = () => TurnManager.AdvancePhase(state);

            act.Should().Throw<GameRuleException>();
        }
    }

    /// <summary>Tests for TurnManager.SwitchActivePlayer.</summary>
    public class SwitchActivePlayer
    {
        [Fact]
        public void Player1ToPlayer2()
        {
            var state = TestFactory.MakeGameState(turn: 1, activePlayer: 1);

            TurnManager.SwitchActivePlayer(state);

            state.ActivePlayer.Should().Be(2);
            state.CurrentTurn.Should().Be(2);
            state.CurrentPhase.Should().Be(Phase.Draw);
        }

        [Fact]
        public void Player2ToPlayer1()
        {
            var state = TestFactory.MakeGameState(turn: 2, activePlayer: 2);

            TurnManager.SwitchActivePlayer(state);

            state.ActivePlayer.Should().Be(1);
            state.CurrentTurn.Should().Be(3);
            state.CurrentPhase.Should().Be(Phase.Draw);
        }
    }
}
