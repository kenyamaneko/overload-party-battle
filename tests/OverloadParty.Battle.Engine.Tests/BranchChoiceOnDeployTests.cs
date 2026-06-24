using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Effects.Ops;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Engine;

/// <summary>
/// 即時配置 (deploy_turns=0) のリソースが on_deploy の分岐選択を要求するとき、プレイで選択待ちへ遷移し、
/// ResolvePendingChoice で選んだ分岐が実行されることを検証する。
/// </summary>
public class BranchChoiceOnDeployTests
{
    /// <summary>on_deploy に分岐効果を持つ即時配置リソースと共有のキャッシュ・レジストリを用意する。</summary>
    public abstract class Base
    {
        protected const string ResourceCardId = "TST-0600";

        protected readonly TestCardCache _cc = new();
        protected readonly TestEffectRegistry _registry = new();
        protected readonly Game _game = TestFactory.MakeGame();

        protected Base()
        {
            _cc.Add(TestFactory.ComputeCard(cardId: ResourceCardId, deployTurns: 0));
            var branches = new Dictionary<string, List<IEffectOp>>
            {
                ["alpha"] = [new InlineOp(o => o.State.SetInsightPool(o.PlayerNum, 111))],
                ["beta"] = [new InlineOp(o => o.State.SetInsightPool(o.PlayerNum, 222))],
            };
            _registry.Register(ResourceCardId, TriggerType.OnDeploy, EffectComposer.Compose(new BranchOnChoiceOp(branches)));
        }

        /// <summary>分岐リソースを手札に持つ状態を作る。</summary>
        /// <returns>テスト用ゲーム状態。</returns>
        protected BattleGameState MakeStateWithHand()
        {
            var state = TestFactory.MakeGameState();
            state.Player1Hand.Add(new UndeployedCard { InstanceID = "h_1", CardID = ResourceCardId });
            return state;
        }

        /// <summary>分岐リソースをフロントエンドにプレイするリクエストを作る。</summary>
        /// <returns>プレイカードリクエスト。</returns>
        protected static PlayCardRequest PlayReq() =>
            new() { CardInstanceID = "h_1", Zone = Zones.Frontend, Index = 0 };

        /// <summary>任意の Action を IEffectOp として実行するテスト専用 op。</summary>
        protected sealed class InlineOp : IEffectOp
        {
            private readonly Action<OpContext> _action;

            public InlineOp(Action<OpContext> action) => _action = action;

            public void Execute(OpContext ctx) => _action(ctx);
        }
    }

    /// <summary>配置時に分岐の選択待ちへ遷移することを検証する。</summary>
    public class Play : Base
    {
        [Fact]
        public void SuspendsForBranchChoice()
        {
            var state = MakeStateWithHand();

            PlayCardProcessor.Process(state, _game, 1, PlayReq(), _cc, _registry);

            state.PendingEffectChoice.Should().NotBeNull();
            var pending = state.PendingEffectChoice!;
            pending.ChoiceKind.Should().Be(ChoiceKinds.Branch);
            pending.Candidates.Should().BeEquivalentTo("alpha", "beta");
            pending.EffectCardId.Should().Be(ResourceCardId);
            pending.Trigger.Should().Be(TriggerType.OnDeploy);
        }
    }

    /// <summary>選択を解決すると選んだ分岐が実行されることを検証する。</summary>
    public class Resolve : Base
    {
        [Fact]
        public void RunsChosenBranch()
        {
            var state = MakeStateWithHand();
            PlayCardProcessor.Process(state, _game, 1, PlayReq(), _cc, _registry);

            var resolveReq = new ResolvePendingChoiceRequest { ChosenId = "beta" };
            ResolvePendingChoiceProcessor.Process(state, _game, 1, resolveReq, _cc, _registry);

            state.PendingEffectChoice.Should().BeNull();
            state.Player1InsightPool.Should().Be(222);
        }
    }
}
