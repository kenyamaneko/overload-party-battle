using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Engine;

public class DiscardProcessorTests
{
    /// <summary>手札破棄に使うコンピュート系リソースを登録したキャッシュを作る。</summary>
    /// <returns>TST-0001 を登録したキャッシュ。</returns>
    private static TestCardCache DiscardCc()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", deployTurns: 1));
        return cc;
    }

    /// <summary>指定インスタンス ID 群の手札破棄リクエストを作る。</summary>
    /// <param name="ids">破棄するインスタンス ID 群。</param>
    /// <returns>手札破棄リクエスト。</returns>
    private static DiscardHandRequest Req(params string[] ids) => new() { CardInstanceIDs = [.. ids] };

    /// <summary>指定枚数の手札を作る。</summary>
    /// <param name="count">手札枚数。</param>
    /// <returns>手札リスト。</returns>
    private static List<UndeployedCard> Hand(int count) =>
        [.. Enumerable.Range(0, count).Select(i => new UndeployedCard { InstanceID = $"h_{i}", CardID = "TST-0001" })];

    /// <summary>指定枚数のリポジトリを作る。</summary>
    /// <param name="count">リポジトリ枚数。</param>
    /// <returns>リポジトリリスト。</returns>
    private static List<UndeployedCard> Repo(int count) =>
        [.. Enumerable.Range(0, count).Select(i => new UndeployedCard { InstanceID = $"r_{i}", CardID = "TST-0001" })];

    /// <summary>手札破棄を実行する。</summary>
    /// <param name="state">対象のゲーム状態。</param>
    /// <param name="req">手札破棄リクエスト。</param>
    /// <returns>アクション結果。</returns>
    private static ActionResult Discard(BattleGameState state, DiscardHandRequest req) =>
        DiscardProcessor.Process(
            state, TestFactory.MakeGame(), 1, req, DiscardCc(), new EffectRegistry(), new FakeClock());

    [Trait("対象", "破棄不要時の破棄要求")]
    public class NoDiscardNeeded
    {
        [Fact(DisplayName = "手札が上限以下のときに破棄を要求すると例外になる")]
        public void Throws()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.End, activePlayer: 1);
            state.Player1Hand = Hand(4); // 上限未満

            var act = () => Discard(state, Req("h_0"));

            act.Should().Throw<GameRuleException>().WithMessage("*no discard needed*");
        }
    }

    [Trait("対象", "破棄枚数の不一致")]
    public class WrongDiscardCount
    {
        [Fact(DisplayName = "手札 8 枚で 1 枚だけ指定すると、ちょうど 2 枚を求める例外になり手札は 8 枚のまま変わらない")]
        public void TooFew_Throws()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.End, activePlayer: 1);
            state.Player1Hand = Hand(8);

            var act = () => Discard(state, Req("h_6"));

            act.Should().Throw<GameRuleException>().WithMessage("*must discard exactly 2 cards*");
            state.Player1Hand.Should().HaveCount(8);
        }

        [Fact(DisplayName = "手札 8 枚で 0 枚指定すると、ちょうど 2 枚を求める例外になり手札は 8 枚のまま変わらない")]
        public void None_Throws()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.End, activePlayer: 1);
            state.Player1Hand = Hand(8);

            var act = () => Discard(state, Req());

            act.Should().Throw<GameRuleException>().WithMessage("*must discard exactly 2 cards*");
            state.Player1Hand.Should().HaveCount(8);
        }
    }

    [Trait("対象", "破棄するカードの指定不正")]
    public class InvalidCardSelection
    {
        [Fact(DisplayName = "手札 8 枚で同じカードを 2 回指定すると、重複を理由に拒否され手札もトラッシュもターンも変わらない")]
        public void Duplicate_Throws()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.End, activePlayer: 1);
            state.Player1Hand = Hand(8);

            var act = () => Discard(state, Req("h_6", "h_6"));

            act.Should().Throw<GameRuleException>().WithMessage("*duplicate cards specified: h_6*");
            state.Player1Hand.Should().HaveCount(8);
            state.Player1Trash.Should().BeEmpty();
            state.ActivePlayer.Should().Be(1);
            state.CurrentTurn.Should().Be(2);
        }

        [Fact(DisplayName = "手札 8 枚で手札にないカードを含む 2 枚を指定すると、手札にないことを理由に拒否され手札もトラッシュもターンも変わらない")]
        public void NotInHand_Throws()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.End, activePlayer: 1);
            state.Player1Hand = Hand(8);

            var act = () => Discard(state, Req("h_6", "h_99"));

            act.Should().Throw<GameRuleException>().WithMessage("*not found in hand: h_99*");
            state.Player1Hand.Should().HaveCount(8);
            state.Player1Trash.Should().BeEmpty();
            state.ActivePlayer.Should().Be(1);
            state.CurrentTurn.Should().Be(2);
        }
    }

    [Trait("対象", "破棄後のターン進行")]
    public class SwitchActivePlayer
    {
        [Fact(DisplayName = "手札破棄後にターンプレイヤーが 2 に切り替わりターンが 3 に進む")]
        public void SwitchesActivePlayerAndAdvances()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.End, activePlayer: 1);
            state.Player1Hand = Hand(8);
            state.Player2Repository = Repo(5);

            Discard(state, Req("h_6", "h_7"));

            state.ActivePlayer.Should().Be(2);
            state.CurrentTurn.Should().Be(3);
        }
    }

    [Trait("対象", "破棄イベントの生成")]
    public class DiscardEvent
    {
        [Fact(DisplayName = "手札 7 枚で 1 枚を破棄すると破棄イベントの枚数が 1 になる")]
        public void GeneratesDiscardEvent()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.End, activePlayer: 1);
            state.Player1Hand = Hand(7); // 1 枚破棄が必要
            state.Player2Repository = Repo(5);

            var result = Discard(state, Req("h_6"));

            var discardEvent = result.Events.First(e => e.EventType == ActionTypes.DiscardHand);
            discardEvent.EventData.Should().BeOfType<DiscardHandEventData>()
                .Which.DiscardedCount.Should().Be(1);
        }
    }

    [Trait("対象", "破棄カードの手札からの除去")]
    public class HandReduction
    {
        [Fact(DisplayName = "手札 8 枚で相異なる 2 枚を破棄すると、その 2 枚が手札から消えて手札が 6 枚になる")]
        public void RemovesDiscardedCardsFromHand()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.End, activePlayer: 1);
            state.Player1Hand = Hand(8);
            state.Player2Repository = Repo(5);

            Discard(state, Req("h_6", "h_7"));

            state.Player1Hand.Should().HaveCount(6);
            state.Player1Hand.Should().NotContain(c => c.InstanceID == "h_6" || c.InstanceID == "h_7");
        }

        [Fact(DisplayName = "手札 7 枚で 1 枚を破棄すると手札が 6 枚になる")]
        public void SingleDiscard_ReducesHandToLimit()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.End, activePlayer: 1);
            state.Player1Hand = Hand(7);
            state.Player2Repository = Repo(5);

            Discard(state, Req("h_6"));

            state.Player1Hand.Should().HaveCount(6);
            state.Player1Hand.Should().NotContain(c => c.InstanceID == "h_6");
        }

        [Fact(DisplayName = "破棄イベントに破棄したカードの ID h_6 と h_7 が載る")]
        public void EventIncludesDiscardedIds()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.End, activePlayer: 1);
            state.Player1Hand = Hand(8);
            state.Player2Repository = Repo(5);

            var result = Discard(state, Req("h_6", "h_7"));

            var discardEvent = result.Events.First(e => e.EventType == ActionTypes.DiscardHand);
            discardEvent.EventData.Should().BeOfType<DiscardHandEventData>()
                .Which.DiscardedIds.Should().BeEquivalentTo(["h_6", "h_7"]);
        }
    }
}
