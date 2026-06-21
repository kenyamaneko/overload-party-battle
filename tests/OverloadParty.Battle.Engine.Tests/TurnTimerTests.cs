using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;
using OverloadParty.Battle.Tests.Fakes;

namespace OverloadParty.Battle.Tests.Engine;

public class TurnTimerTests
{
    /// <summary>Tests for GameEngine.DeductElapsedTime.</summary>
    public class DeductElapsedTime
    {
        [Fact]
        public void SubtractsElapsedSeconds()
        {
            var state = TestFactory.MakeGameState(activePlayer: 1);
            state.Player1TimeBank = 480;
            state.TurnStartedAt = DateTime.UtcNow.AddSeconds(-10);

            GameEngine.DeductElapsedTime(state);

            state.Player1TimeBank.Should().BeInRange(469, 471);
            state.TurnStartedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
        }

        [Fact]
        public void OnlyDeductsActivePlayer()
        {
            var state = TestFactory.MakeGameState(activePlayer: 2);
            state.Player1TimeBank = 480;
            state.Player2TimeBank = 300;
            state.TurnStartedAt = DateTime.UtcNow.AddSeconds(-5);

            GameEngine.DeductElapsedTime(state);

            state.Player1TimeBank.Should().Be(480, "non-active player's TimeBank unchanged");
            state.Player2TimeBank.Should().BeInRange(294, 296);
        }

        [Fact]
        public void ZeroElapsed_NoChange()
        {
            var state = TestFactory.MakeGameState(activePlayer: 1);
            state.Player1TimeBank = 480;
            state.TurnStartedAt = DateTime.UtcNow;

            GameEngine.DeductElapsedTime(state);

            state.Player1TimeBank.Should().Be(480);
        }

        [Fact]
        public void CanGoNegative()
        {
            var state = TestFactory.MakeGameState(activePlayer: 1);
            state.Player1TimeBank = 5;
            state.TurnStartedAt = DateTime.UtcNow.AddSeconds(-10);

            GameEngine.DeductElapsedTime(state);

            state.Player1TimeBank.Should().BeNegative();
        }
    }

    /// <summary>Tests for WinConditionChecker.CheckTimeout.</summary>
    public class CheckTimeout
    {
        [Fact]
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

        [Fact]
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

        [Fact]
        public void BothPositive_NoTimeout()
        {
            var state = TestFactory.MakeGameState();
            state.Player1TimeBank = 100;
            state.Player2TimeBank = 200;

            var result = WinConditionChecker.CheckTimeout(state);

            result.Should().BeNull();
        }
    }

    /// <summary>Tests for TurnManager.SwitchActivePlayer.</summary>
    public class SwitchActivePlayer
    {
        [Fact]
        public void ResetsTurnStartedAt()
        {
            var state = TestFactory.MakeGameState(activePlayer: 1);
            state.TurnStartedAt = DateTime.UtcNow.AddMinutes(-5);

            TurnManager.SwitchActivePlayer(state);

            state.TurnStartedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
            state.ActivePlayer.Should().Be(2);
        }
    }

    /// <summary>Tests for GameInitializer.CreateNewGame.</summary>
    public class CreateNewGame
    {
        [Fact]
        public void SetsTurnStartedAt()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
            var deck = TestFactory.MakeDeck("TST-0001");

            var (_, state) = GameInitializer.CreateNewGame("g1", deck, deck, 1, cc);

            state.TurnStartedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
            state.Player1TimeBank.Should().Be(BattleConstants.InitialTimeBank);
            state.Player2TimeBank.Should().Be(BattleConstants.InitialTimeBank);
        }
    }

    /// <summary>Tests for GameEngine.ProcessAction turn-timeout handling.</summary>
    public class ProcessAction
    {
        [Fact]
        public async Task TimeBankExpired_ReturnsTimeout()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", deployTurns: 0));
            var repo = new FakeGameRepository();
            var engine = new GameEngine(repo, cc, new EffectRegistry(), new InitiativeCatalog([]));
            var deck = TestFactory.MakeDeck("TST-0001");

            var gameID = await engine.CreateNewGame(deck, deck, 1);
            var game = await repo.GetGame(gameID);
            await engine.RunAutoAdvance(game!);

            // Simulate time running out: set TurnStartedAt far in the past
            var state = await repo.GetGameState(gameID);
            state!.TurnStartedAt = DateTime.UtcNow.AddSeconds(-500);
            state.Player1TimeBank = 480;

            // Player tries to play a card, but time has expired
            var cardToPlay = state.Player1Hand.First();
            var req = new PlayCardRequest
            {
                CardInstanceID = cardToPlay.InstanceID,
                Zone = Zones.Frontend,
                Index = 0,
            };

            game = await repo.GetGame(gameID);
            var result = await engine.ProcessAction(game!, 1, ActionType.PlayCard, req);

            result.GameOver.Should().NotBeNull();
            result.GameOver!.Reason.Should().Be("turn_timeout");
            result.GameOver.WinnerNum.Should().Be(2, "opponent wins on timeout");

            game = await repo.GetGame(gameID);
            game!.Status.Should().Be(GameStatus.Finished);
        }

        [Fact]
        public async Task SufficientTimeBank_Succeeds()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", deployTurns: 0));
            var repo = new FakeGameRepository();
            var engine = new GameEngine(repo, cc, new EffectRegistry(), new InitiativeCatalog([]));
            var deck = TestFactory.MakeDeck("TST-0001");

            var gameID = await engine.CreateNewGame(deck, deck, 1);
            var game = await repo.GetGame(gameID);
            await engine.RunAutoAdvance(game!);

            // Set TurnStartedAt 10 seconds ago (well within TimeBank)
            var state = await repo.GetGameState(gameID);
            state!.TurnStartedAt = DateTime.UtcNow.AddSeconds(-10);

            var cardToPlay = state.Player1Hand.First();
            var req = new PlayCardRequest
            {
                CardInstanceID = cardToPlay.InstanceID,
                Zone = Zones.Frontend,
                Index = 0,
            };

            game = await repo.GetGame(gameID);
            var result = await engine.ProcessAction(game!, 1, ActionType.PlayCard, req);

            result.GameOver.Should().BeNull("game should not be over");
            state.Player1TimeBank.Should().BeInRange(469, 471, "~10 seconds deducted");
        }
    }

    /// <summary>Tests for WinConditionChecker.Check timeout inclusion.</summary>
    public class Check
    {
        [Fact]
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
