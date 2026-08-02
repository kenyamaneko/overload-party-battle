using System.Text.Json;
using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Engine;

/// <summary>
/// ストラテジーの起動効果が「デッキ上端を見て 1 枚選ばせる」選択待ちへ遷移し、
/// ResolvePendingChoice で選択 1 枚を手札・残りをトラッシュへ振り分けることを検証する。
/// </summary>
public class KeepOneFromDeckTopTests
{
    private const string StrategyCardId = "TST-0500";

    /// <summary>keep_one_from_deck_top を起動効果に持つストラテジーを登録したカードキャッシュとレジストリを作る。</summary>
    /// <returns>カードキャッシュと効果レジストリ。</returns>
    private static (TestCardCache Cc, TestEffectRegistry Registry) MakeEnv()
    {
        var cc = new TestCardCache();
        cc.Add(new CardDefinition
        {
            CardId = StrategyCardId,
            CardName = "TestReveal",
            CardType = CardTypes.Strategy,
            Faction = "SHE",
            DeployTurns = 0,
        });
        var registry = new TestEffectRegistry();
        registry.Register(StrategyCardId, TriggerType.Ignition, MakeHandler(peek: 2));
        return (cc, registry);
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
    private static BattleGameState MakeStateWithDeck()
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
    private static PlayCardRequest PlayReq() =>
        new() { CardInstanceID = "h_1", Zone = "", Index = 0 };

    /// <summary>任意の Action を IEffectOp として実行するテスト専用 op。</summary>
    private sealed class InlineOp : IEffectOp
    {
        private readonly Action<OpContext> _action;

        public InlineOp(Action<OpContext> action) => _action = action;

        public void Execute(OpContext ctx) => _action(ctx);
    }

    [Trait("対象", "デッキトップ提示による選択待ち")]
    public class Play
    {
        [Fact(DisplayName = "ストラテジーをプレイするとデッキ上端の2枚を候補に選択待ちへ遷移する")]
        public void SuspendsForChoice_PresentingDeckTopCards()
        {
            var (cc, registry) = MakeEnv();
            var state = MakeStateWithDeck();

            PlayCardProcessor.Process(state, TestFactory.MakeGame(), 1, PlayReq(), cc, registry);

            state.PendingEffectChoice.Should().NotBeNull();
            var pending = state.PendingEffectChoice!;
            pending.ChoiceKind.Should().Be(ChoiceKinds.DeckTop);
            pending.Candidates.Should().Equal("d_1", "d_2");
            pending.EffectCardId.Should().Be(StrategyCardId);
            pending.Trigger.Should().Be(TriggerType.Ignition);
            state.Player1Repository.Should().HaveCount(3);
        }
    }

    [Trait("対象", "デッキトップ選択の解決")]
    public class Resolve
    {
        [Fact(DisplayName = "選択を解決すると選んだカードがインスタンス ID を保って手札へ移る")]
        public void KeepsChosenCardToHand_PreservingId()
        {
            var (cc, registry) = MakeEnv();
            var state = MakeStateWithDeck();
            PlayCardProcessor.Process(state, TestFactory.MakeGame(), 1, PlayReq(), cc, registry);

            var resolveReq = new ResolvePendingChoiceRequest { ChosenId = "d_2" };
            ResolvePendingChoiceProcessor.Process(state, TestFactory.MakeGame(), 1, resolveReq, cc, registry, new FakeClock());

            state.PendingEffectChoice.Should().BeNull();
            state.Player1Hand.Select(c => c.InstanceID).Should().Equal("d_2");
            state.Player1Repository.Select(c => c.InstanceID).Should().Equal("d_3");
        }

        [Fact(DisplayName = "選択を解決すると選ばれなかった開示カードがトラッシュへ移る")]
        public void TrashesUnchosenRevealedCards()
        {
            var (cc, registry) = MakeEnv();
            var state = MakeStateWithDeck();
            PlayCardProcessor.Process(state, TestFactory.MakeGame(), 1, PlayReq(), cc, registry);

            var resolveReq = new ResolvePendingChoiceRequest { ChosenId = "d_2" };
            ResolvePendingChoiceProcessor.Process(state, TestFactory.MakeGame(), 1, resolveReq, cc, registry, new FakeClock());

            state.Player1Trash.Select(c => c.InstanceID).Should().Contain("d_1");
        }
    }

    [Trait("対象", "選択解決アクションの提示")]
    public class Surfacing
    {
        [Fact(DisplayName = "プレイ後に利用可能アクションへ開示済みデッキ上端付きの選択解決アクションが現れる")]
        public void ResolveActionCarriesRevealedDeckTop()
        {
            var (cc, registry) = MakeEnv();
            var state = MakeStateWithDeck();
            PlayCardProcessor.Process(state, TestFactory.MakeGame(), 1, PlayReq(), cc, registry);

            var actions = AvailableActions.GetAllAvailableActions(
                state, 1, TestFactory.MakeField(), TestFactory.MakeField(),
                new List<UndeployedCard>(), 0, 0, cc, registry);

            var resolve = actions.Should().ContainSingle(a => a.Type == ActionTypes.ResolvePendingChoice).Subject;
            resolve.ChoiceKind.Should().Be(ChoiceKinds.DeckTop);
            resolve.ChoiceOptions!.Select(o => o.Key).Should().Equal("d_1", "d_2");
            resolve.RevealedDeckTop!.Select(c => c.InstanceID).Should().Equal("d_1", "d_2");
        }
    }
}
