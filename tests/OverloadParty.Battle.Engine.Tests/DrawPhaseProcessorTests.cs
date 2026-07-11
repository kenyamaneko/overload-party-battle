using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Engine;

public class DrawPhaseProcessorTests
{
    /// <summary>DrawPhaseProcessor.Process テストの共有 setup (カードキャッシュ・ゲーム)。</summary>
    public abstract class Base
    {
        protected readonly TestCardCache _cc = new();
        protected readonly Game _game = TestFactory.MakeGame();

        protected Base()
        {
            _cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
        }
    }

    [Trait("対象", "通常ドローからメインフェーズへの進行")]
    public class NormalDraw : Base
    {
        [Fact(DisplayName = "ドローフェーズでカードを 1 枚引き手札に加えてメインフェーズへ進む")]
        public void DrawsCardAndAdvancesToMain()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Draw, activePlayer: 1);
            state.Player1Repository.Add(new UndeployedCard { InstanceID = "repo_1", CardID = "TST-0001", ArtNo = 0 });

            var result = DrawPhaseProcessor.Process(state, _game, _cc, new EffectRegistry());

            result.Should().BeNull();
            state.CurrentPhase.Should().Be(Phase.Main);
            state.Player1Hand.Should().HaveCount(1);
            state.Player1Repository.Should().BeEmpty();
        }
    }

    [Trait("対象", "ドローフェーズ以外での無処理")]
    public class NotDrawPhase : Base
    {
        [Fact(DisplayName = "メインフェーズではドロー処理を行わず手札が空のままになる")]
        public void ReturnsNull_NoStateChange()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main, activePlayer: 1);
            state.Player1Repository.Add(new UndeployedCard { InstanceID = "repo_1", CardID = "TST-0001" });

            var result = DrawPhaseProcessor.Process(state, _game, _cc, new EffectRegistry());

            result.Should().BeNull();
            state.CurrentPhase.Should().Be(Phase.Main);
            state.Player1Hand.Should().BeEmpty();
        }
    }

    [Trait("対象", "デッキアウトによる決着")]
    public class EmptyRepository : Base
    {
        [Fact(DisplayName = "デッキが空のときドローフェーズで deck_out により相手が勝つ")]
        public void ReturnsGameOver()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Draw, activePlayer: 1);
            // No cards in repository

            var result = DrawPhaseProcessor.Process(state, _game, _cc, new EffectRegistry());

            result.Should().NotBeNull();
            result!.WinnerNum.Should().Be(2);
            result.Reason.Should().Be("deck_out");
        }
    }

    [Trait("対象", "デプロイカウントダウンの進行")]
    public class DeployCountdown : Base
    {
        [Fact(DisplayName = "ドローフェーズでデプロイターンが 2 から 1 に減り裏向きのままになる")]
        public void Decrements()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Draw, activePlayer: 1);
            state.Player1Repository.Add(new UndeployedCard { InstanceID = "repo_1", CardID = "TST-0001" });
            var resource = TestFactory.MakeResource(faceUp: false, deployLeft: 2);
            state.Player1Field.Frontend[0] = resource;

            DrawPhaseProcessor.Process(state, _game, _cc, new EffectRegistry());

            resource.DeployingTurnsLeft.Should().Be(1);
            resource.FaceUp.Should().BeFalse();
        }

        [Fact(DisplayName = "デプロイターンが 1 から 0 になると表向きに反転し稼働実績フラグが立つ")]
        public void ReachesZero_FlipsFaceUp()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Draw, activePlayer: 1);
            state.Player1Repository.Add(new UndeployedCard { InstanceID = "repo_1", CardID = "TST-0001" });
            var resource = TestFactory.MakeResource(faceUp: false, deployLeft: 1);
            state.Player1Field.Frontend[0] = resource;

            DrawPhaseProcessor.Process(state, _game, _cc, new EffectRegistry());

            resource.DeployingTurnsLeft.Should().Be(0);
            resource.FaceUp.Should().BeTrue();
            state.Player1HasOperated.Should().BeTrue();
        }
    }

    /// <summary>ドローフェーズ用にコンピュート系リソースを登録したキャッシュを作る。</summary>
    /// <returns>TST-0001 を登録したキャッシュ。</returns>
    private static TestCardCache DrawCc()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
        return cc;
    }

    [Trait("対象", "デプロイ完了時のデプロイ時効果の発動")]
    public class DeployCompletionTriggers
    {
        [Fact(DisplayName = "デプロイターン 1 のリソースは 1 回のドローフェーズで稼働しデプロイ時効果が発動する")]
        public void Resource_OneTurnDeploy_FlipsAndFiresAfterOneDrawPhase()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Draw, activePlayer: 1);
            state.Player1Repository.Add(new UndeployedCard { InstanceID = "repo_1", CardID = "TST-0001" });
            var res = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "r_1", faceUp: false, deployLeft: 1);
            state.Player1Field.Frontend[0] = res;

            bool fired = false;
            var effects = new TestEffectRegistry();
            effects.Register("TST-0001", TriggerType.OnDeploy, _ => { fired = true; return new EffectResult(); });

            DrawPhaseProcessor.Process(state, TestFactory.MakeGame(), DrawCc(), effects);

            res.DeployingTurnsLeft.Should().Be(0);
            res.FaceUp.Should().BeTrue();
            fired.Should().BeTrue("デプロイターン 1 のリソースは 1 回のドローフェーズ通過で稼働しデプロイ時効果が発動する");
        }

        [Fact(DisplayName = "デプロイターン 2 のリソースは 1 回目のドローフェーズでは稼働せずデプロイ時効果は発動しない")]
        public void Resource_TwoTurnDeploy_StaysDeployingAfterFirstDrawPhase()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Draw, activePlayer: 1);
            state.Player1Repository.Add(new UndeployedCard { InstanceID = "repo_1", CardID = "TST-0001" });
            var res = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "r_1", faceUp: false, deployLeft: 2);
            state.Player1Field.Frontend[0] = res;

            bool fired = false;
            var effects = new TestEffectRegistry();
            effects.Register("TST-0001", TriggerType.OnDeploy, _ => { fired = true; return new EffectResult(); });

            DrawPhaseProcessor.Process(state, TestFactory.MakeGame(), DrawCc(), effects);

            res.DeployingTurnsLeft.Should().Be(1);
            res.FaceUp.Should().BeFalse();
            fired.Should().BeFalse("デプロイターン 2 のリソースは 1 回目のドローフェーズ通過では稼働せず発動しない");
        }

        [Fact(DisplayName = "カウントダウンが 0 になったサポートカードのデプロイ時効果が発動する")]
        public void Support_FiresOnDeploy_WhenCountdownReachesZero()
        {
            var cc = DrawCc();
            cc.Add(TestFactory.PlatformCard(cardId: "TST-0200"));
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Draw, activePlayer: 1);
            state.Player1Repository.Add(new UndeployedCard { InstanceID = "repo_1", CardID = "TST-0001" });
            state.Player1Field.Support[0] = new DeployedSupport
            {
                InstanceID = "sup_1",
                CardID = "TST-0200",
                FaceUp = true,
                DeployingTurnsLeft = 1,
            };

            bool fired = false;
            var effects = new TestEffectRegistry();
            effects.Register("TST-0200", TriggerType.OnDeploy, _ => { fired = true; return new EffectResult(); });

            DrawPhaseProcessor.Process(state, TestFactory.MakeGame(), cc, effects);

            fired.Should().BeTrue("カウントダウン完了で稼働したサポートカードのデプロイ時効果が発動する");
        }
    }

    [Trait("対象", "ドロー後の勝敗判定")]
    public class WinCheckAfterDraw
    {
        [Fact(DisplayName = "ドロー後にバジェットが 0 のときバジェットゼロで相手が勝つ")]
        public void ReturnsGameOver_WhenWinConditionMetAfterDraw()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Draw, activePlayer: 1, p1Budget: 0);
            state.Player1Repository.Add(new UndeployedCard { InstanceID = "repo_1", CardID = "TST-0001" });

            var result = DrawPhaseProcessor.Process(state, TestFactory.MakeGame(), DrawCc(), new EffectRegistry());

            result.Should().NotBeNull();
            result!.WinnerNum.Should().Be(2);
            result.Reason.Should().Be(WinReasons.BudgetZero);
        }
    }
}
