using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Engine;

public class TurnManagerTests
{
    [Trait("対象", "初回ターンの判定")]
    public class IsFirstTurn
    {
        [Fact(DisplayName = "ターンが 1 のとき初回ターンと判定する")]
        public void Turn1_ReturnsTrue()
        {
            TurnManager.IsFirstTurn(1).Should().BeTrue();
        }

        [Theory(DisplayName = "初回でないターンでは初回ターンと判定しない")]
        [InlineData(2)]
        [InlineData(10)]
        [InlineData(30)]
        public void LaterTurns_ReturnsFalse(long turn)
        {
            TurnManager.IsFirstTurn(turn).Should().BeFalse();
        }
    }

    [Trait("対象", "フェーズの進行")]
    public class AdvancePhase
    {
        [Fact(DisplayName = "ドローフェーズを進めるとメインフェーズになる")]
        public void DrawPhase_GoesToMain()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Draw);

            var previous = TurnManager.AdvancePhase(state);

            previous.Should().Be(Phase.Draw);
            state.CurrentPhase.Should().Be(Phase.Main);
        }

        [Fact(DisplayName = "初回ターンのメインフェーズを進めるとバトルフェーズを飛ばしてエンドフェーズになる")]
        public void MainPhase_FirstTurn_SkipsBattleGoesToEnd()
        {
            var state = TestFactory.MakeGameState(turn: 1, phase: Phase.Main);

            var previous = TurnManager.AdvancePhase(state);

            previous.Should().Be(Phase.Main);
            state.CurrentPhase.Should().Be(Phase.End);
        }

        [Theory(DisplayName = "初回でないターンのメインフェーズを進めるとバトルフェーズになる")]
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

        [Fact(DisplayName = "バトルフェーズを進めるとエンドフェーズになる")]
        public void BattlePhase_GoesToEnd()
        {
            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Battle);

            var previous = TurnManager.AdvancePhase(state);

            previous.Should().Be(Phase.Battle);
            state.CurrentPhase.Should().Be(Phase.End);
        }

        [Fact(DisplayName = "エンドフェーズをさらに進めると例外になる")]
        public void EndPhase_Throws()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.End);

            var act = () => TurnManager.AdvancePhase(state);

            act.Should().Throw<GameRuleException>();
        }
    }

    [Trait("対象", "ターンプレイヤーの切り替え")]
    public class SwitchActivePlayer
    {
        [Fact(DisplayName = "プレイヤー1のターンから切り替えるとプレイヤー2のターンになりターンが 2 に進みドローフェーズになる")]
        public void Player1ToPlayer2()
        {
            var state = TestFactory.MakeGameState(turn: 1, activePlayer: 1);

            TurnManager.SwitchActivePlayer(state, new FakeClock());

            state.ActivePlayer.Should().Be(2);
            state.CurrentTurn.Should().Be(2);
            state.CurrentPhase.Should().Be(Phase.Draw);
        }

        [Fact(DisplayName = "プレイヤー2のターンから切り替えるとプレイヤー1のターンになりターンが 3 に進みドローフェーズになる")]
        public void Player2ToPlayer1()
        {
            var state = TestFactory.MakeGameState(turn: 2, activePlayer: 2);

            TurnManager.SwitchActivePlayer(state, new FakeClock());

            state.ActivePlayer.Should().Be(1);
            state.CurrentTurn.Should().Be(3);
            state.CurrentPhase.Should().Be(Phase.Draw);
        }
    }
}
