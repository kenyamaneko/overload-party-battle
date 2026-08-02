using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Effects.Ops;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Engine;

public class BranchChoiceOnDeployTests
{
    private const string ResourceCardId = "TST-0600";

    /// <summary>on_deploy に alpha/beta 分岐を持つ即時配置リソースを登録したキャッシュとレジストリを作る。</summary>
    /// <returns>カードキャッシュと効果レジストリ。</returns>
    private static (TestCardCache Cc, TestEffectRegistry Registry) MakeEnv()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: ResourceCardId, deployTurns: 0));
        var registry = new TestEffectRegistry();
        var branches = new Dictionary<string, List<IEffectOp>>
        {
            ["alpha"] = [new InlineOp(o => o.State.SetInsightPool(o.PlayerNum, 111))],
            ["beta"] = [new InlineOp(o => o.State.SetInsightPool(o.PlayerNum, 222))],
        };
        registry.Register(ResourceCardId, TriggerType.OnDeploy, EffectComposer.Compose(new BranchOnChoiceOp(branches)));
        return (cc, registry);
    }

    /// <summary>分岐リソースを手札に持つ状態を作る。</summary>
    /// <returns>テスト用ゲーム状態。</returns>
    private static BattleGameState MakeStateWithHand()
    {
        var state = TestFactory.MakeGameState();
        state.Player1Hand.Add(new UndeployedCard { InstanceID = "h_1", CardID = ResourceCardId });
        return state;
    }

    /// <summary>分岐リソースをフロントエンドにプレイするリクエストを作る。</summary>
    /// <returns>プレイカードリクエスト。</returns>
    private static PlayCardRequest PlayReq() =>
        new() { CardInstanceID = "h_1", Zone = Zones.Frontend, Index = 0 };

    /// <summary>任意の Action を IEffectOp として実行するテスト専用 op。</summary>
    private sealed class InlineOp : IEffectOp
    {
        private readonly Action<OpContext> _action;

        public InlineOp(Action<OpContext> action) => _action = action;

        public void Execute(OpContext ctx) => _action(ctx);
    }

    [Trait("対象", "分岐選択によるプレイの中断")]
    public class Play
    {
        [Fact(DisplayName = "分岐を持つ即時配置リソースをプレイすると alpha/beta の分岐選択待ちへ中断する")]
        public void SuspendsForBranchChoice()
        {
            var (cc, registry) = MakeEnv();
            var state = MakeStateWithHand();

            PlayCardProcessor.Process(state, TestFactory.MakeGame(), 1, PlayReq(), cc, registry);

            state.PendingEffectChoice.Should().NotBeNull();
            var pending = state.PendingEffectChoice!;
            pending.ChoiceKind.Should().Be(ChoiceKinds.Branch);
            pending.Candidates.Should().BeEquivalentTo("alpha", "beta");
            pending.EffectCardId.Should().Be(ResourceCardId);
            pending.Trigger.Should().Be(TriggerType.OnDeploy);
        }
    }

    [Trait("対象", "分岐選択の解決")]
    public class Resolve
    {
        [Fact(DisplayName = "分岐選択を beta で解決すると選んだ分岐が実行されインサイトプールが 222 になる")]
        public void RunsChosenBranch()
        {
            var (cc, registry) = MakeEnv();
            var state = MakeStateWithHand();
            PlayCardProcessor.Process(state, TestFactory.MakeGame(), 1, PlayReq(), cc, registry);

            var resolveReq = new ResolvePendingChoiceRequest { ChosenId = "beta" };
            ResolvePendingChoiceProcessor.Process(state, TestFactory.MakeGame(), 1, resolveReq, cc, registry, new FakeClock());

            state.PendingEffectChoice.Should().BeNull();
            state.Player1InsightPool.Should().Be(222);
        }
    }
}
