using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Engine;

public class TurnTimerTests
{
    [Trait("対象", "タイムバンクからの経過時間の減算")]
    public class DeductElapsedTime
    {
        /// <summary>ターン開始から指定秒だけ進んだ状態を作る。</summary>
        /// <param name="state">対象のゲーム状態。</param>
        /// <param name="seconds">ターン開始からの経過秒数。</param>
        /// <returns>進めた時計。</returns>
        private static FakeClock ElapsedSince(BattleGameState state, double seconds)
        {
            var clock = new FakeClock();
            state.TurnStartedAt = clock.UtcNow;
            clock.Advance(seconds);
            return clock;
        }

        [Fact(DisplayName = "経過 10 秒でターンプレイヤーのタイムバンクから 10 秒差し引く")]
        public void SubtractsElapsedSeconds()
        {
            var state = TestFactory.MakeGameState(activePlayer: 1);
            state.Player1TimeBank = 480;
            var clock = ElapsedSince(state, 10);

            GameEngine.DeductElapsedTime(state, clock);

            state.Player1TimeBank.Should().Be(470);
        }

        [Fact(DisplayName = "非ターンプレイヤーのタイムバンクは変えずターンプレイヤーからのみ差し引く")]
        public void OnlyDeductsActivePlayer()
        {
            var state = TestFactory.MakeGameState(activePlayer: 2);
            state.Player1TimeBank = 480;
            state.Player2TimeBank = 300;
            var clock = ElapsedSince(state, 5);

            GameEngine.DeductElapsedTime(state, clock);

            state.Player1TimeBank.Should().Be(480);
            state.Player2TimeBank.Should().Be(295);
        }

        [Fact(DisplayName = "経過時間が 0 のときタイムバンクは変わらない")]
        public void ZeroElapsed_NoChange()
        {
            var state = TestFactory.MakeGameState(activePlayer: 1);
            state.Player1TimeBank = 480;
            var clock = ElapsedSince(state, 0);

            GameEngine.DeductElapsedTime(state, clock);

            state.Player1TimeBank.Should().Be(480);
        }

        [Fact(DisplayName = "経過が 0.6 秒のときタイムバンクは変わらず、次の減算のためにターン開始時刻も動かない")]
        public void SubSecondElapsed_KeepsRemainder()
        {
            var state = TestFactory.MakeGameState(activePlayer: 1);
            state.Player1TimeBank = 480;
            var clock = ElapsedSince(state, 0.6);
            var turnStartedAt = state.TurnStartedAt;

            GameEngine.DeductElapsedTime(state, clock);

            state.Player1TimeBank.Should().Be(480);
            state.TurnStartedAt.Should().Be(turnStartedAt);
        }

        [Fact(DisplayName = "経過が 1.6 秒のときタイムバンクは 1 秒減り、ターン開始時刻は 1 秒だけ進んで 0.6 秒の端数が残る")]
        public void FractionalElapsed_CarriesRemainder()
        {
            var state = TestFactory.MakeGameState(activePlayer: 1);
            state.Player1TimeBank = 480;
            var clock = ElapsedSince(state, 1.6);
            var turnStartedAt = state.TurnStartedAt;

            GameEngine.DeductElapsedTime(state, clock);

            state.Player1TimeBank.Should().Be(479);
            state.TurnStartedAt.Should().Be(turnStartedAt.AddSeconds(1));
        }

        [Fact(DisplayName = "経過時間がタイムバンクを超えるとタイムバンクは負になる")]
        public void CanGoNegative()
        {
            var state = TestFactory.MakeGameState(activePlayer: 1);
            state.Player1TimeBank = 5;
            var clock = ElapsedSince(state, 10);

            GameEngine.DeductElapsedTime(state, clock);

            state.Player1TimeBank.Should().Be(-5);
        }
    }

    [Trait("対象", "タイムアウト判定")]
    public class CheckTimeout
    {
        [Fact(DisplayName = "プレイヤー1のタイムバンクが 0 のときプレイヤー2が turn_timeout で勝つ")]
        public void Player1TimeBankZero_Player2Wins()
        {
            var state = TestFactory.MakeGameState();
            state.Player1TimeBank = 0;
            state.Player2TimeBank = 100;

            var result = WinConditionChecker.CheckTimeout(state);

            result.Should().NotBeNull();
            result!.WinnerNum.Should().Be(2);
            result.Reason.Should().Be("turn_timeout");
        }

        [Fact(DisplayName = "プレイヤー2のタイムバンクが -5 のときプレイヤー1が turn_timeout で勝つ")]
        public void Player2TimeBankNegative_Player1Wins()
        {
            var state = TestFactory.MakeGameState();
            state.Player1TimeBank = 100;
            state.Player2TimeBank = -5;

            var result = WinConditionChecker.CheckTimeout(state);

            result.Should().NotBeNull();
            result!.WinnerNum.Should().Be(1);
            result.Reason.Should().Be("turn_timeout");
        }

        [Fact(DisplayName = "両者のタイムバンクが正のときタイムアウトにならない")]
        public void BothPositive_NoTimeout()
        {
            var state = TestFactory.MakeGameState();
            state.Player1TimeBank = 100;
            state.Player2TimeBank = 200;

            var result = WinConditionChecker.CheckTimeout(state);

            result.Should().BeNull();
        }
    }

    [Trait("対象", "ターン切り替え時のタイマーリセット")]
    public class SwitchActivePlayer
    {
        [Fact(DisplayName = "ターンプレイヤーを切り替えるとターン開始時刻が切り替え時点にリセットされる")]
        public void ResetsTurnStartedAt()
        {
            var state = TestFactory.MakeGameState(activePlayer: 1);
            var clock = new FakeClock();
            state.TurnStartedAt = clock.UtcNow;
            clock.Advance(300);

            TurnManager.SwitchActivePlayer(state, clock);

            state.TurnStartedAt.Should().Be(clock.UtcNow);
            state.ActivePlayer.Should().Be(2);
        }
    }

    [Trait("対象", "新規ゲームのタイマー初期化")]
    public class CreateNewGame
    {
        [Fact(DisplayName = "新規ゲーム作成時にターン開始時刻が作成時点になり両者のタイムバンクが初期値になる")]
        public void SetsTurnStartedAt()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
            var deck = TestFactory.MakeDeck(cc, "TST-0001");
            var clock = new FakeClock();

            var (_, state) = GameInitializer.CreateNewGame("g1", deck, deck, 1, cc, clock);

            state.TurnStartedAt.Should().Be(clock.UtcNow);
            state.Player1TimeBank.Should().Be(BattleConstants.InitialTimeBank);
            state.Player2TimeBank.Should().Be(BattleConstants.InitialTimeBank);
        }
    }

    [Trait("対象", "アクション処理時のタイムバンク消費")]
    public class ProcessAction
    {
        private readonly TestCardCache _cc = new();
        private readonly FakeGameRepository _repo = new();
        private readonly FakeClock _clock = new();
        private readonly GameEngine _engine;

        public ProcessAction()
        {
            _cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", deployTurns: 0));
            _engine = new GameEngine(_repo, _cc, new EffectRegistry(), new InitiativeCatalog([]), _clock);
        }

        /// <summary>ドローフェーズまで進めたゲームを用意する。</summary>
        /// <returns>ゲーム ID とゲーム状態。</returns>
        private async Task<(string GameID, BattleGameState State)> StartGame()
        {
            var deck = TestFactory.MakeDeck(_cc, "TST-0001");
            var gameID = await _engine.CreateNewGame(deck, deck, 1);
            var game = await _repo.GetGame(gameID);
            await _engine.RunAutoAdvance(game!);
            var state = await _repo.GetGameState(gameID);
            return (gameID, state!);
        }

        /// <summary>手札の先頭のカードを指定スロットにデプロイする。</summary>
        /// <param name="gameID">対象ゲームの ID。</param>
        /// <param name="state">対象のゲーム状態。</param>
        /// <param name="zone">配置先のゾーン。</param>
        /// <param name="index">配置先のスロット番号。</param>
        /// <returns>アクション結果。</returns>
        private async Task<ActionResult> DeployFirstHandCard(
            string gameID, BattleGameState state, string zone, int index)
        {
            var instanceId = TestFactory.ReplaceFirstHandCard(state, 1, "TST-0001");
            var game = await _repo.GetGame(gameID);
            return await _engine.ProcessAction(game!, 1, ActionType.PlayCard,
                new PlayCardRequest { CardInstanceID = instanceId, Zone = zone, Index = index });
        }

        [Fact(DisplayName = "タイムバンクを使い切った状態でアクションするとゲームが turn_timeout で終了し相手が勝つ")]
        public async Task TimeBankExpired_ReturnsTimeout()
        {
            var (gameID, state) = await StartGame();
            state.Player1TimeBank = 480;
            _clock.Advance(500);

            var result = await DeployFirstHandCard(gameID, state, Zones.Frontend, 0);

            result.GameOver.Should().NotBeNull();
            result.GameOver!.Reason.Should().Be("turn_timeout");
            result.GameOver.WinnerNum.Should().Be(2);

            var game = await _repo.GetGame(gameID);
            game!.Status.Should().Be(GameStatus.Finished);
        }

        [Fact(DisplayName = "タイムバンクに余裕がある状態で 10 秒後にアクションするとゲームは終了せず 10 秒差し引かれる")]
        public async Task SufficientTimeBank_Succeeds()
        {
            var (gameID, state) = await StartGame();
            state.Player1TimeBank = 480;
            _clock.Advance(10);

            var result = await DeployFirstHandCard(gameID, state, Zones.Frontend, 0);

            result.GameOver.Should().BeNull();
            state.Player1TimeBank.Should().Be(470);
        }

        [Fact(DisplayName = "0.6 秒間隔でアクションを 5 回続けると、タイムバンクから合計 3 秒差し引かれる")]
        public async Task SubSecondIntervals_AccumulateDeduction()
        {
            var (gameID, state) = await StartGame();
            state.Player1TimeBank = 480;
            (string Zone, int Index)[] slots =
            [
                (Zones.Frontend, 0), (Zones.Frontend, 1), (Zones.Frontend, 2),
                (Zones.Backend, 0), (Zones.Backend, 1),
            ];

            foreach (var (zone, index) in slots)
            {
                _clock.Advance(0.6);
                await DeployFirstHandCard(gameID, state, zone, index);
            }

            state.Player1TimeBank.Should().Be(477);
        }
    }

    [Trait("対象", "勝敗判定へのタイムアウト包含")]
    public class Check
    {
        [Fact(DisplayName = "プレイヤー1のタイムバンクが -1 のとき総合勝敗判定が turn_timeout を返す")]
        public void TimeBankZero_ReturnsTimeout()
        {
            var state = TestFactory.MakeGameState();
            var game = TestFactory.MakeGame();
            state.Player1TimeBank = -1;
            state.Player2TimeBank = 100;

            var result = WinConditionChecker.Check(state, game);

            result.Should().NotBeNull();
            result!.WinnerNum.Should().Be(2);
            result.Reason.Should().Be("turn_timeout");
        }
    }
}
