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

    /// <summary>Tests for DrawPhaseProcessor.Process — normal draw advances to Main.</summary>
    public class NormalDraw : Base
    {
        [Fact]
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

    /// <summary>Tests for DrawPhaseProcessor.Process — no-op when not in the draw phase.</summary>
    public class NotDrawPhase : Base
    {
        [Fact]
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

    /// <summary>Tests for DrawPhaseProcessor.Process — empty repository ends the game.</summary>
    public class EmptyRepository : Base
    {
        [Fact]
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

    /// <summary>Tests for DrawPhaseProcessor.Process — deploy countdown decrements and flips on completion.</summary>
    public class DeployCountdown : Base
    {
        [Fact]
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

        [Fact]
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

    /// <summary>デプロイのカウントダウン完了でデプロイ時効果が発動することを検証する。</summary>
    public class DeployCompletionTriggers
    {
        [Fact]
        public void Resource_OneTurnDeploy_FlipsAndFiresAfterOneDraw()
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
            fired.Should().BeTrue("1 ターンデプロイは 1 回のドローで稼働しデプロイ時効果が発動する");
        }

        [Fact]
        public void Resource_TwoTurnDeploy_StaysDeployingAfterFirstDraw()
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
            fired.Should().BeFalse("2 ターンデプロイは 1 回目のドローでは稼働せず発動しない");
        }

        [Fact]
        public void Resource_TwoTurnDeploy_FlipsAndFiresAfterSecondDraw()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Draw, activePlayer: 1);
            state.Player1Repository.Add(new UndeployedCard { InstanceID = "repo_1", CardID = "TST-0001" });
            state.Player1Repository.Add(new UndeployedCard { InstanceID = "repo_2", CardID = "TST-0001" });
            var res = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "r_1", faceUp: false, deployLeft: 2);
            state.Player1Field.Frontend[0] = res;

            bool fired = false;
            var effects = new TestEffectRegistry();
            effects.Register("TST-0001", TriggerType.OnDeploy, _ => { fired = true; return new EffectResult(); });

            DrawPhaseProcessor.Process(state, TestFactory.MakeGame(), DrawCc(), effects);
            // 次のターンのドローフェーズを模して再度ドローさせる
            state.CurrentPhase = Phase.Draw;
            DrawPhaseProcessor.Process(state, TestFactory.MakeGame(), DrawCc(), effects);

            res.DeployingTurnsLeft.Should().Be(0);
            res.FaceUp.Should().BeTrue();
            fired.Should().BeTrue("2 ターンデプロイは 2 回目のドローで稼働しデプロイ時効果が発動する");
        }

        [Fact]
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

    /// <summary>ドロー後に勝敗判定が評価されることを検証する。</summary>
    public class WinCheckAfterDraw
    {
        [Fact]
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
