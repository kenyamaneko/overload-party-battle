using System.Text.Json;
using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Engine;

/// <summary>
/// ストラテジーの起動効果が「デッキ上端を提示して 1 枚選ばせる」選択待ちへ遷移し、
/// ResolvePendingChoice で選択 1 枚を手札・残りをトラッシュへ振り分けることを検証する。
/// </summary>
public class KeepOneFromDeckTopTests
{
    /// <summary>keep_one_from_deck_top を起動効果に持つストラテジーと、共有のカードキャッシュ・レジストリを用意する。</summary>
    public abstract class Base
    {
        protected const string StrategyCardId = "TST-0500";

        protected readonly TestCardCache _cc = new();
        protected readonly TestEffectRegistry _registry = new();
        protected readonly Game _game = TestFactory.MakeGame();

        protected Base()
        {
            _cc.Add(new CardDefinition
            {
                CardId = StrategyCardId,
                CardName = "TestReveal",
                CardType = CardTypes.Strategy,
                Faction = "SHE",
                DeployTurns = 0,
            });
            _registry.Register(StrategyCardId, TriggerType.Ignition, MakeHandler(peek: 2));
        }

        /// <summary>keep_one_from_deck_top カスタム効果を peek 枚で組んだ起動効果ハンドラを返す。</summary>
        /// <param name="peek">見るデッキ上端の枚数。</param>
        /// <returns>合成済みの効果ハンドラ。</returns>
        private static EffectHandler MakeHandler(int peek)
        {
            var meta = new Dictionary<string, JsonElement> { ["peek"] = JsonSerializer.SerializeToElement(peek) };
            var action = new CustomEffectRegistry().Build(CustomEffects.KeepOneFromDeckTop, meta)
                ?? throw new InvalidOperationException("failed to build keep_one_from_deck_top");
            return EffectComposer.Compose(new InlineOp(action));
        }

        /// <summary>ストラテジーを手札に 1 枚、デッキ上端に d_1/d_2/d_3 を積んだ状態を作る。</summary>
        /// <returns>テスト用ゲーム状態。</returns>
        protected static BattleGameState MakeStateWithDeck()
        {
            var state = TestFactory.MakeGameState();
            state.Player1Hand.Add(new UndeployedCard { InstanceID = "h_1", CardID = StrategyCardId });
            state.Player1Repository.Add(new UndeployedCard { InstanceID = "d_1", CardID = "TST-0001" });
            state.Player1Repository.Add(new UndeployedCard { InstanceID = "d_2", CardID = "TST-0002" });
            state.Player1Repository.Add(new UndeployedCard { InstanceID = "d_3", CardID = "TST-0003" });
            return state;
        }

        /// <summary>手札のストラテジーをプレイするリクエストを作る。</summary>
        /// <returns>プレイカードリクエスト。</returns>
        protected static PlayCardRequest PlayReq() =>
            new() { CardInstanceID = "h_1", Zone = "", Index = 0 };

        /// <summary>任意の Action を IEffectOp として実行するテスト専用 op。</summary>
        protected sealed class InlineOp : IEffectOp
        {
            private readonly Action<OpContext> _action;

            public InlineOp(Action<OpContext> action) => _action = action;

            public void Execute(OpContext ctx) => _action(ctx);
        }
    }

    /// <summary>プレイ時にデッキ上端を提示して選択待ちへ遷移することを検証する。</summary>
    public class Play : Base
    {
        [Fact]
        public void SuspendsForChoice_PresentingDeckTopCards()
        {
            var state = MakeStateWithDeck();

            PlayCardProcessor.Process(state, _game, 1, PlayReq(), _cc, _registry);

            state.PendingEffectChoice.Should().NotBeNull();
            var pending = state.PendingEffectChoice!;
            pending.ChoiceKind.Should().Be(ChoiceKinds.DeckTop);
            pending.Candidates.Should().Equal("1", "2");
            pending.EffectCardId.Should().Be(StrategyCardId);
            pending.Trigger.Should().Be(TriggerType.Ignition);
            state.Player1Repository.Should().HaveCount(3);
        }
    }

    /// <summary>選択を解決すると選択 1 枚を手札・残りをトラッシュへ振り分けることを検証する。</summary>
    public class Resolve : Base
    {
        [Fact]
        public void KeepsChosenCardToHand_PreservingId()
        {
            var state = MakeStateWithDeck();
            PlayCardProcessor.Process(state, _game, 1, PlayReq(), _cc, _registry);

            var resolveReq = new ResolvePendingChoiceRequest { ChosenId = "2" };
            ResolvePendingChoiceProcessor.Process(state, _game, 1, resolveReq, _cc, _registry);

            state.PendingEffectChoice.Should().BeNull();
            state.Player1Hand.Select(c => c.InstanceID).Should().Equal("d_2");
            state.Player1Repository.Select(c => c.InstanceID).Should().Equal("d_3");
        }

        [Fact]
        public void TrashesUnchosenRevealedCards()
        {
            var state = MakeStateWithDeck();
            PlayCardProcessor.Process(state, _game, 1, PlayReq(), _cc, _registry);

            var resolveReq = new ResolvePendingChoiceRequest { ChosenId = "2" };
            ResolvePendingChoiceProcessor.Process(state, _game, 1, resolveReq, _cc, _registry);

            state.Player1Trash.Select(c => c.InstanceID).Should().Contain("d_1");
        }
    }

    /// <summary>プレイ後、選択肢 (具象化カード付き) を持つ resolve アクションが availableActions に現れることを検証する。</summary>
    public class Surfacing : Base
    {
        [Fact]
        public void ResolveActionCarriesMaterializedOptions()
        {
            var state = MakeStateWithDeck();
            PlayCardProcessor.Process(state, _game, 1, PlayReq(), _cc, _registry);

            var actions = AvailableActions.GetAllAvailableActions(
                state, TestFactory.MakeField(), TestFactory.MakeField(),
                new List<UndeployedCard>(), 0, 0, _cc, _registry);

            var resolve = actions.Should().ContainSingle(a => a.Type == ActionTypes.ResolvePendingChoice).Subject;
            resolve.ChoiceKind.Should().Be(ChoiceKinds.DeckTop);
            resolve.ChoiceOptions!.Select(o => o.Key).Should().Equal("1", "2");
            resolve.ChoiceOptions!.Select(o => o.Card!.InstanceID).Should().Equal("d_1", "d_2");
        }
    }
}
