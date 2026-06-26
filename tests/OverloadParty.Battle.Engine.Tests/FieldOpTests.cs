using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Effects.Ops;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Effects;

public class FieldOpTests
{
    /// <summary>共有ゲームとカードキャッシュを束ねた op コンテキストを組み立てる。</summary>
    /// <param name="state">操作対象のゲーム状態。</param>
    /// <param name="cc">カードキャッシュ。</param>
    /// <param name="playerNum">操作プレイヤー番号。</param>
    /// <param name="source">自身に作用する op のソースリソース。</param>
    /// <param name="choiceData">選択で分岐する op のプレイヤー選択データ。</param>
    /// <returns>op コンテキスト。</returns>
    private static OpContext MakeOpContext(
        BattleGameState state, TestCardCache cc, long playerNum = 1,
        DeployedResource? source = null,
        Dictionary<string, object>? choiceData = null)
    {
        var ctx = new EffectContext
        {
            State = state,
            Game = TestFactory.MakeGame(),
            PlayerNum = playerNum,
            Source = source,
            ChoiceData = choiceData,
            CardCache = cc,
            Effects = new EffectRegistry(),
        };
        return new OpContext(ctx);
    }

    /// <summary>相手の伏せ リアクティブ を覗き見る op (表向きにはしない)。</summary>
    public class PeekReactive
    {
        [Fact]
        public void PeekReactiveOp_AddsPeekedBy_WhenHiddenSupportExists()
        {
            var state = TestFactory.MakeGameState();
            state.Player2Field.Support[0] = new DeployedSupport
            {
                InstanceID = "sup_1",
                CardID = "TST-0400",
                FaceUp = false,
            };

            new PeekReactiveOp().Execute(MakeOpContext(state, new TestCardCache(), playerNum: 1));

            var support = state.Player2Field.Support[0]!;
            support.FaceUp.Should().BeFalse("覗き見はカードを表向きにしない");
            support.PeekedBy.Should().Contain(1, "プレイヤー 1 が PeekedBy に入る");
        }

        [Fact]
        public void PeekReactiveOp_DoesNothing_WhenNoHiddenSupport()
        {
            var state = TestFactory.MakeGameState();
            state.Player2Field.Support[0] = new DeployedSupport
            {
                InstanceID = "sup_1",
                CardID = "TST-0400",
                FaceUp = true,
            };

            new PeekReactiveOp().Execute(MakeOpContext(state, new TestCardCache(), playerNum: 1));

            state.Player2Field.Support[0]!.PeekedBy.Should().BeEmpty();
        }

        [Fact]
        public void PeekReactiveOp_DoesNotDuplicate_WhenAlreadyPeeked()
        {
            var state = TestFactory.MakeGameState();
            state.Player2Field.Support[0] = new DeployedSupport
            {
                InstanceID = "sup_1",
                CardID = "TST-0400",
                FaceUp = false,
                PeekedBy = [1],
            };

            new PeekReactiveOp().Execute(MakeOpContext(state, new TestCardCache(), playerNum: 1));

            state.Player2Field.Support[0]!.PeekedBy.Should().HaveCount(1, "同じプレイヤー番号は重複追加しない");
        }

        [Fact]
        public void PeekReactiveOp_PeeksFirstHidden_SkippingFaceUp()
        {
            var state = TestFactory.MakeGameState();
            state.Player2Field.Support[0] = new DeployedSupport
            {
                InstanceID = "sup_visible",
                CardID = "TST-0401",
                FaceUp = true,
            };
            state.Player2Field.Support[1] = new DeployedSupport
            {
                InstanceID = "sup_hidden",
                CardID = "TST-0402",
                FaceUp = false,
            };

            new PeekReactiveOp().Execute(MakeOpContext(state, new TestCardCache(), playerNum: 1));

            state.Player2Field.Support[0]!.PeekedBy.Should().BeEmpty("表向きのカードは覗かない");
            state.Player2Field.Support[1]!.PeekedBy.Should().Contain(1);
        }
    }

    /// <summary>実効 可用性 が 0 以下のリソースを 破壊 する op。</summary>
    public class DestroyCheck
    {
        // 実効 可用性 = MaxAV - Damage。境界 (0 ちょうど) とオーバーキル (負) で破壊、正で存続。
        [Theory]
        [InlineData(1000, 1000, true)]
        [InlineData(1000, 1500, true)]
        [InlineData(1000, 999, false)]
        public void DestroyCheckOp_DestroysOnlyAtOrBelowZeroAvailability(long maxAV, long damage, bool shouldDestroy)
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
            var state = TestFactory.MakeGameState();
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(
                cardId: "TST-0001", instanceId: "res", maxAV: maxAV, damage: damage);

            new DestroyCheckOp(PlayerRef.Myself).Execute(MakeOpContext(state, cc, playerNum: 1));

            (FieldHelpers.FindResourceByID(state.Player1Field, "res") is null)
                .Should().Be(shouldDestroy);
        }

        [Fact]
        public void DestroyCheckOp_MovesDestroyedResourceToTrash()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
            var state = TestFactory.MakeGameState();
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(
                cardId: "TST-0001", instanceId: "dead", maxAV: 1000, damage: 1000);

            new DestroyCheckOp(PlayerRef.Myself).Execute(MakeOpContext(state, cc, playerNum: 1));

            state.GetTrash(1).Should().Contain(c => c.InstanceID == "dead");
        }

        [Fact]
        public void DestroyCheckOp_Both_ScansOwnAndOpponentFields()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
            var state = TestFactory.MakeGameState();
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(
                cardId: "TST-0001", instanceId: "mine_dead", maxAV: 500, damage: 500);
            state.Player2Field.Frontend[0] = TestFactory.MakeResource(
                cardId: "TST-0001", instanceId: "opp_dead", maxAV: 500, damage: 500);

            new DestroyCheckOp(PlayerRef.Both).Execute(MakeOpContext(state, cc, playerNum: 1));

            FieldHelpers.FindResourceByID(state.Player1Field, "mine_dead").Should().BeNull();
            FieldHelpers.FindResourceByID(state.Player2Field, "opp_dead").Should().BeNull();
        }
    }

    /// <summary>相手の伏せ リアクティブ を表向きに開示する op。</summary>
    public class RevealReactive
    {
        [Fact]
        public void RevealReactiveOp_FlipsFirstHiddenSupportFaceUp()
        {
            var state = TestFactory.MakeGameState();
            state.Player2Field.Support[0] = new DeployedSupport
            {
                InstanceID = "sup_1",
                CardID = "TST-0400",
                FaceUp = false,
            };

            new RevealReactiveOp().Execute(MakeOpContext(state, new TestCardCache(), playerNum: 1));

            state.Player2Field.Support[0]!.FaceUp.Should().BeTrue("伏せたリアクティブが表向きに開示される");
        }

        [Fact]
        public void RevealReactiveOp_DoesNothing_WhenNoHiddenSupport()
        {
            var state = TestFactory.MakeGameState();
            state.Player2Field.Support[0] = new DeployedSupport
            {
                InstanceID = "sup_1",
                CardID = "TST-0400",
                FaceUp = true,
            };

            var act = () => new RevealReactiveOp().Execute(MakeOpContext(state, new TestCardCache(), playerNum: 1));

            act.Should().NotThrow();
            state.Player2Field.Support[0]!.FaceUp.Should().BeTrue();
        }

        [Fact]
        public void RevealReactiveOp_RevealsFirstHidden_SkippingFaceUp()
        {
            var state = TestFactory.MakeGameState();
            state.Player2Field.Support[0] = new DeployedSupport
            {
                InstanceID = "sup_visible",
                CardID = "TST-0401",
                FaceUp = true,
            };
            state.Player2Field.Support[1] = new DeployedSupport
            {
                InstanceID = "sup_hidden",
                CardID = "TST-0402",
                FaceUp = false,
            };

            new RevealReactiveOp().Execute(MakeOpContext(state, new TestCardCache(), playerNum: 1));

            state.Player2Field.Support[1]!.FaceUp.Should().BeTrue("最初の伏せリアクティブが開示される");
        }
    }

    /// <summary>相手の サポートゾーン の プラットフォーム を 破壊 する op。</summary>
    public class DestroyPlatform
    {
        [Fact]
        public void DestroyPlatformOp_DestroysOpponentPlatform()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.PlatformCard(cardId: "TST-0200"));
            var state = TestFactory.MakeGameState();
            state.Player2Field.Support[0] = new DeployedSupport
            {
                InstanceID = "plat_1",
                CardID = "TST-0200",
                FaceUp = true,
            };

            new DestroyPlatformOp().Execute(MakeOpContext(state, cc, playerNum: 1));

            state.Player2Field.Support.Select(s => s.InstanceID).Should().NotContain("plat_1");
            state.GetTrash(2).Should().Contain(c => c.InstanceID == "plat_1");
        }

        [Fact]
        public void DestroyPlatformOp_SelectsByChoiceInstanceId()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.PlatformCard(cardId: "TST-0200"));
            var state = TestFactory.MakeGameState();
            state.Player2Field.Support[0] = new DeployedSupport { InstanceID = "plat_a", CardID = "TST-0200", FaceUp = true };
            state.Player2Field.Support[1] = new DeployedSupport { InstanceID = "plat_b", CardID = "TST-0200", FaceUp = true };

            new DestroyPlatformOp().Execute(MakeOpContext(state, cc, playerNum: 1,
                choiceData: new Dictionary<string, object> { ["instanceId"] = "plat_b" }));

            var remaining = state.Player2Field.Support.Select(s => s.InstanceID).ToList();
            remaining.Should().NotContain("plat_b");
            remaining.Should().Contain("plat_a");
        }

        [Fact]
        public void DestroyPlatformOp_Throws_WhenSelectedSupportIsNotPlatform()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ReactiveCard(cardId: "TST-0400"));
            var state = TestFactory.MakeGameState();
            state.Player2Field.Support[0] = new DeployedSupport { InstanceID = "react_1", CardID = "TST-0400", FaceUp = false };

            var act = () => new DestroyPlatformOp().Execute(MakeOpContext(state, cc, playerNum: 1,
                choiceData: new Dictionary<string, object> { ["instanceId"] = "react_1" }));

            act.Should().Throw<GameRuleException>();
        }

        [Fact]
        public void DestroyPlatformOp_DoesNothing_WhenNoPlatformPresent()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ReactiveCard(cardId: "TST-0400"));
            var state = TestFactory.MakeGameState();
            state.Player2Field.Support[0] = new DeployedSupport { InstanceID = "react_1", CardID = "TST-0400", FaceUp = false };

            new DestroyPlatformOp().Execute(MakeOpContext(state, cc, playerNum: 1));

            state.Player2Field.Support.Select(s => s.InstanceID).Should().Contain("react_1");
        }
    }

    /// <summary>ソースリソースの残り デプロイターン を減らす op。</summary>
    public class ReduceDeployTurns
    {
        // 通常減算と、0 で頭打ちになる境界を確認する。
        [Theory]
        [InlineData(2, 1, 1)]
        [InlineData(3, 2, 1)]
        [InlineData(1, 3, 0)]
        [InlineData(0, 1, 0)]
        public void ReduceDeployTurnsOp_DecrementsAndClampsAtZero(long start, long reduce, long expected)
        {
            var state = TestFactory.MakeGameState();
            var source = TestFactory.MakeResource(instanceId: "src", deployLeft: start);

            new ReduceDeployTurnsOp(new StaticAmount(reduce)).Execute(
                MakeOpContext(state, new TestCardCache(), playerNum: 1, source: source));

            source.DeployingTurnsLeft.Should().Be(expected);
        }

        [Fact]
        public void ReduceDeployTurnsOp_DoesNothing_WhenNoSource()
        {
            var state = TestFactory.MakeGameState();

            var act = () => new ReduceDeployTurnsOp(new StaticAmount(1)).Execute(
                MakeOpContext(state, new TestCardCache(), playerNum: 1));

            act.Should().NotThrow();
        }
    }
}
