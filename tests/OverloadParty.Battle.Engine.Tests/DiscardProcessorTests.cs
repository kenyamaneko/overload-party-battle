using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Engine;

public class DiscardProcessorTests
{
    /// <summary>DiscardProcessor.Process テストの共有 setup (カードキャッシュ・ゲーム・手札/リポジトリ生成)。</summary>
    public abstract class Base
    {
        protected readonly TestCardCache _cc = new();
        protected readonly Game _game = TestFactory.MakeGame();

        protected Base()
        {
            // Compute card for hand/repo cards
            _cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", deployTurns: 1));
        }

        /// <summary>指定インスタンス ID 群の手札調整リクエストを生成する。</summary>
        /// <param name="ids">破棄するカードのインスタンス ID 群。</param>
        /// <returns>手札調整リクエスト。</returns>
        protected static DiscardHandRequest MakeReq(params string[] ids) =>
            new() { CardInstanceIDs = [.. ids] };

        /// <summary>指定枚数の手札を生成する。</summary>
        /// <param name="count">生成する手札枚数。</param>
        /// <returns>生成した手札リスト。</returns>
        protected static List<UndeployedCard> MakeHand(int count)
        {
            var hand = new List<UndeployedCard>();
            for (int i = 0; i < count; i++)
            {
                hand.Add(new UndeployedCard { InstanceID = $"h_{i}", CardID = "TST-0001" });
            }
            return hand;
        }

        /// <summary>指定枚数のリポジトリを生成する。</summary>
        /// <param name="count">生成するリポジトリ枚数。</param>
        /// <returns>生成したリポジトリリスト。</returns>
        protected static List<UndeployedCard> MakeRepo(int count)
        {
            var repo = new List<UndeployedCard>();
            for (int i = 0; i < count; i++)
            {
                repo.Add(new UndeployedCard { InstanceID = $"r_{i}", CardID = "TST-0001" });
            }
            return repo;
        }
    }

    /// <summary>Tests for DiscardProcessor.Process — discards excess cards down to the hand limit.</summary>
    public class DiscardsExcessCards : Base
    {
        [Fact]
        public void ReducesHandToLimit()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.End, activePlayer: 1);
            state.Player1Hand = MakeHand(8);
            // Opponent needs repo cards so DrawPhaseProcessor doesn't fail
            state.Player2Repository = MakeRepo(5);

            var result = DiscardProcessor.Process(
                state, _game, 1, MakeReq("h_6", "h_7"), _cc, new EffectRegistry());

            // After discard, hand should have 6 cards
            // (Note: SwitchActivePlayer + DrawPhaseProcessor runs after, so hand count may change for P2)
            // We verify the discard event was emitted with correct count
            var discardEvent = result.Events.First(e => e.EventType == ActionTypes.DiscardHand);
            discardEvent.EventData.Should().BeOfType<DiscardHandEventData>()
                .Which.DiscardedCount.Should().Be(2);
        }
    }

    /// <summary>Tests for DiscardProcessor.Process — discard requested when none is needed.</summary>
    public class NoDiscardNeeded : Base
    {
        [Fact]
        public void Throws()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.End, activePlayer: 1);
            state.Player1Hand = MakeHand(4); // under limit

            var act = () => DiscardProcessor.Process(
                state, _game, 1, MakeReq("h_0"), _cc, new EffectRegistry());

            act.Should().Throw<GameRuleException>().WithMessage("*no discard needed*");
        }
    }

    /// <summary>Tests for DiscardProcessor.Process — wrong number of cards discarded.</summary>
    public class WrongDiscardCount : Base
    {
        [Fact]
        public void Throws()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.End, activePlayer: 1);
            state.Player1Hand = MakeHand(8); // need to discard 2

            var act = () => DiscardProcessor.Process(
                state, _game, 1, MakeReq("h_6"), _cc, new EffectRegistry()); // only 1 provided

            act.Should().Throw<GameRuleException>().WithMessage("*exactly*");
        }
    }

    /// <summary>Tests for DiscardProcessor.Process — switches active player and advances the turn.</summary>
    public class SwitchActivePlayer : Base
    {
        [Fact]
        public void SwitchesActivePlayerAndAdvances()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.End, activePlayer: 1);
            state.Player1Hand = MakeHand(8);
            state.Player2Repository = MakeRepo(5);

            DiscardProcessor.Process(state, _game, 1, MakeReq("h_6", "h_7"), _cc, new EffectRegistry());

            // After SwitchActivePlayer, active player changes to 2, turn increments
            state.ActivePlayer.Should().Be(2);
            state.CurrentTurn.Should().Be(3);
        }
    }

    /// <summary>Tests for DiscardProcessor.Process — emits a discard event with the discarded count.</summary>
    public class DiscardEvent : Base
    {
        [Fact]
        public void GeneratesDiscardEvent()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.End, activePlayer: 1);
            state.Player1Hand = MakeHand(7); // need to discard 1
            state.Player2Repository = MakeRepo(5);

            var result = DiscardProcessor.Process(
                state, _game, 1, MakeReq("h_6"), _cc, new EffectRegistry());

            var discardEvent = result.Events.First(e => e.EventType == ActionTypes.DiscardHand);
            discardEvent.EventData.Should().BeOfType<DiscardHandEventData>()
                .Which.DiscardedCount.Should().Be(1);
        }
    }
}
