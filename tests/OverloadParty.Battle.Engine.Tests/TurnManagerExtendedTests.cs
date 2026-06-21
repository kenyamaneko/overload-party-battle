using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Engine;

/// <summary>
/// Additional TurnManager tests for uncovered branches:
/// - IsActionAllowedInPhase (all action × phase combinations)
/// - AdvancePhase edge cases
/// </summary>
public class TurnManagerExtendedTests
{
    /// <summary>Tests for TurnManager.IsActionAllowedInPhase across each phase.</summary>
    public class IsActionAllowedInPhase
    {
        [Theory]
        [InlineData(ActionType.PlayCard, true)]
        [InlineData(ActionType.ScaleUp, true)]
        [InlineData(ActionType.Monetize, true)]
        [InlineData(ActionType.UseEffect, true)]
        [InlineData(ActionType.EndPhase, true)]
        [InlineData(ActionType.Attack, false)]
        [InlineData(ActionType.DiscardHand, false)]
        [InlineData(ActionType.Forfeit, false)]
        public void Main(ActionType action, bool expected)
        {
            TurnManager.IsActionAllowedInPhase(Phase.Main, action).Should().Be(expected);
        }

        [Theory]
        [InlineData(ActionType.Attack, true)]
        [InlineData(ActionType.UseEffect, true)]
        [InlineData(ActionType.EndPhase, true)]
        [InlineData(ActionType.PlayCard, false)]
        [InlineData(ActionType.ScaleUp, false)]
        [InlineData(ActionType.Monetize, false)]
        [InlineData(ActionType.DiscardHand, false)]
        public void Battle(ActionType action, bool expected)
        {
            TurnManager.IsActionAllowedInPhase(Phase.Battle, action).Should().Be(expected);
        }

        [Theory]
        [InlineData(ActionType.DiscardHand, true)]
        [InlineData(ActionType.PlayCard, false)]
        [InlineData(ActionType.Attack, false)]
        [InlineData(ActionType.EndPhase, false)]
        public void End(ActionType action, bool expected)
        {
            TurnManager.IsActionAllowedInPhase(Phase.End, action).Should().Be(expected);
        }

        [Theory]
        [InlineData(ActionType.PlayCard)]
        [InlineData(ActionType.Attack)]
        [InlineData(ActionType.DiscardHand)]
        [InlineData(ActionType.EndPhase)]
        [InlineData(ActionType.Forfeit)]
        public void Draw_AllFalse(ActionType action)
        {
            TurnManager.IsActionAllowedInPhase(Phase.Draw, action).Should().BeFalse();
        }
    }

    /// <summary>Tests for TurnManager.AdvancePhase.</summary>
    public class AdvancePhase
    {
        [Theory]
        [InlineData(Phase.Draw, Phase.Main)]
        [InlineData(Phase.Battle, Phase.End)]
        public void ReturnsPreviousPhase(Phase startPhase, Phase expectedNew)
        {
            var state = TestFactory.MakeGameState(turn: 3, phase: startPhase);

            var previous = TurnManager.AdvancePhase(state);

            previous.Should().Be(startPhase);
            state.CurrentPhase.Should().Be(expectedNew);
        }

        [Theory]
        [InlineData(1, Phase.End)]
        [InlineData(2, Phase.Battle)]
        public void MainPhase_NextPhaseDependsOnTurn(long turn, Phase expectedPhase)
        {
            var state = TestFactory.MakeGameState(turn: turn, phase: Phase.Main);

            TurnManager.AdvancePhase(state);

            state.CurrentPhase.Should().Be(expectedPhase);
        }
    }

    /// <summary>Tests for TurnManager.SwitchActivePlayer.</summary>
    public class SwitchActivePlayer
    {
        [Fact]
        public void IncrementsTurn()
        {
            var state = TestFactory.MakeGameState(turn: 5, activePlayer: 1);

            TurnManager.SwitchActivePlayer(state);

            state.CurrentTurn.Should().Be(6);
        }

        [Fact]
        public void SetsDrawPhase()
        {
            var state = TestFactory.MakeGameState(turn: 5, phase: Phase.End, activePlayer: 1);

            TurnManager.SwitchActivePlayer(state);

            state.CurrentPhase.Should().Be(Phase.Draw);
        }
    }
}
