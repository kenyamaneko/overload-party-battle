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
}
