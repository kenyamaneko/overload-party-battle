using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Engine;

public class TurnManagerExtendedTests
{
    [Trait("対象", "フェーズごとに許可されるアクション")]
    public class IsActionAllowedInPhase
    {
        [Theory(DisplayName = "メインフェーズで各アクションが許可されるか判定する")]
        [InlineData(ActionType.PlayCard, true)]
        [InlineData(ActionType.ScaleUp, true)]
        [InlineData(ActionType.Monetize, true)]
        [InlineData(ActionType.UseIgnition, true)]
        [InlineData(ActionType.EndPhase, true)]
        [InlineData(ActionType.Attack, false)]
        [InlineData(ActionType.DiscardHand, false)]
        [InlineData(ActionType.Forfeit, false)]
        public void Main(ActionType action, bool expected)
        {
            TurnManager.IsActionAllowedInPhase(Phase.Main, action).Should().Be(expected);
        }

        [Theory(DisplayName = "バトルフェーズで各アクションが許可されるか判定する")]
        [InlineData(ActionType.Attack, true)]
        [InlineData(ActionType.UseIgnition, true)]
        [InlineData(ActionType.EndPhase, true)]
        [InlineData(ActionType.PlayCard, false)]
        [InlineData(ActionType.ScaleUp, false)]
        [InlineData(ActionType.Monetize, false)]
        [InlineData(ActionType.DiscardHand, false)]
        public void Battle(ActionType action, bool expected)
        {
            TurnManager.IsActionAllowedInPhase(Phase.Battle, action).Should().Be(expected);
        }

        [Theory(DisplayName = "エンドフェーズで各アクションが許可されるか判定する")]
        [InlineData(ActionType.DiscardHand, true)]
        [InlineData(ActionType.PlayCard, false)]
        [InlineData(ActionType.Attack, false)]
        [InlineData(ActionType.EndPhase, false)]
        public void End(ActionType action, bool expected)
        {
            TurnManager.IsActionAllowedInPhase(Phase.End, action).Should().Be(expected);
        }

        [Theory(DisplayName = "ドローフェーズではどのアクションも許可されない")]
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

    [Trait("対象", "フェーズの進行")]
    public class AdvancePhase
    {
        [Theory(DisplayName = "フェーズを進めると直前のフェーズを返し次のフェーズへ遷移する")]
        [InlineData(Phase.Draw, Phase.Main)]
        [InlineData(Phase.Battle, Phase.End)]
        public void ReturnsPreviousPhase(Phase startPhase, Phase expectedNew)
        {
            var state = TestFactory.MakeGameState(turn: 3, phase: startPhase);

            var previous = TurnManager.AdvancePhase(state);

            previous.Should().Be(startPhase);
            state.CurrentPhase.Should().Be(expectedNew);
        }

        [Theory(DisplayName = "メインフェーズの次フェーズはターンによって決まり初回はバトルフェーズを飛ばす")]
        [InlineData(1, Phase.End)]
        [InlineData(2, Phase.Battle)]
        public void MainPhase_NextPhaseDependsOnTurn(long turn, Phase expectedPhase)
        {
            var state = TestFactory.MakeGameState(turn: turn, phase: Phase.Main);

            TurnManager.AdvancePhase(state);

            state.CurrentPhase.Should().Be(expectedPhase);
        }
    }

    [Trait("対象", "ターンプレイヤーの切り替え")]
    public class SwitchActivePlayer
    {
        [Fact(DisplayName = "アクティブプレイヤーを切り替えるとターンが 5 から 6 に進む")]
        public void IncrementsTurn()
        {
            var state = TestFactory.MakeGameState(turn: 5, activePlayer: 1);

            TurnManager.SwitchActivePlayer(state);

            state.CurrentTurn.Should().Be(6);
        }

        [Fact(DisplayName = "アクティブプレイヤーを切り替えるとドローフェーズになる")]
        public void SetsDrawPhase()
        {
            var state = TestFactory.MakeGameState(turn: 5, phase: Phase.End, activePlayer: 1);

            TurnManager.SwitchActivePlayer(state);

            state.CurrentPhase.Should().Be(Phase.Draw);
        }
    }
}
