using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Engine;

public class PlayCardProcessorTests
{
    /// <summary>Shared setup for PlayCardProcessor tests (card cache seeded per card type, game, and request factories).</summary>
    public abstract class Base
    {
        protected readonly TestCardCache _cc = new();
        protected readonly Game _game = TestFactory.MakeGame();

        protected Base()
        {
            // Compute card: deployTurns=1
            _cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", deployTurns: 1));
            // Serverless card: deployTurns=0
            _cc.Add(TestFactory.ServerlessCard(cardId: "TST-0002"));
            // Database card (data type, backend-only)
            _cc.Add(TestFactory.DataCard(cardId: "TST-0003", subtype: "Database"));
            // Attachment card
            _cc.Add(TestFactory.AttachmentCard(cardId: "TEST-0300"));
            // Incident card
            _cc.Add(new CardDefinition { CardId = "TEST-0500", CardName = "TestIncident", CardType = CardTypes.Incident, DeployTurns = 0 });
            // Strategy card
            _cc.Add(new CardDefinition { CardId = "TEST-0501", CardName = "TestStrategy", CardType = CardTypes.Strategy, DeployTurns = 0 });
            // Reactive card
            _cc.Add(new CardDefinition { CardId = "TEST-0502", CardName = "TestReactive", CardType = CardTypes.Reactive, DeployTurns = 0 });
            // Platform card
            _cc.Add(TestFactory.PlatformCard(cardId: "TEST-0200"));
        }

        /// <summary>Builds a PlayCardRequest targeting an explicit zone, index, and optional target.</summary>
        protected static PlayCardRequest MakeReq(string instanceId, string zone, int index, string? targetInstanceId = null) =>
            new()
            {
                CardInstanceID = instanceId,
                Zone = zone,
                Index = index,
                TargetInstanceID = targetInstanceId,
            };

        /// <summary>Builds a zoneless PlayCardRequest for cards that do not occupy a slot.</summary>
        protected static PlayCardRequest MakeReq(string instanceId) =>
            new()
            {
                CardInstanceID = instanceId,
                Zone = "",
                Index = 0,
            };
    }

    /// <summary>Tests for placing cards into zones and rejecting invalid placements.</summary>
    public class Placement : Base
    {
        // RULEBOOK §3 / ARCHITECTURE §3: Compute カードは Frontend へデプロイ可能
        // deployTurns > 0 の間は裏向きで待機 (§4)

        [Fact]
        public void PlayCard_ComputeToFrontend_IsDeployed()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
            state.Player1Hand.Add(new UndeployedCard { InstanceID = "h_1", CardID = "TST-0001" });

            PlayCardProcessor.Process(
                state, _game, 1, MakeReq("h_1", Zones.Frontend, 0), _cc, new EffectRegistry());

            state.Player1Hand.Should().BeEmpty();
            state.Player1Field.Frontend[0]!.CardID.Should().Be("TST-0001");
        }

        [Fact]
        public void PlayCard_ComputeToFrontend_EmitsPlayCardEvent()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
            state.Player1Hand.Add(new UndeployedCard { InstanceID = "h_1", CardID = "TST-0001" });

            var result = PlayCardProcessor.Process(
                state, _game, 1, MakeReq("h_1", Zones.Frontend, 0), _cc, new EffectRegistry());

            result.Events.Should().ContainSingle(e => e.EventType == ActionTypes.PlayCard);
        }

        [Fact]
        public void PlayCard_WithDeployTurns_IsFaceDownDuringDeploy()
        {
            // TST-0001 has deployTurns=1 → must wait 1 turn face-down
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
            state.Player1Hand.Add(new UndeployedCard { InstanceID = "h_1", CardID = "TST-0001" });

            PlayCardProcessor.Process(
                state, _game, 1, MakeReq("h_1", Zones.Frontend, 0), _cc, new EffectRegistry());

            state.Player1Field.Frontend[0]!.FaceUp.Should().BeFalse();
            state.Player1Field.Frontend[0]!.DeployingTurnsLeft.Should().Be(1);
        }

        [Fact]
        public void PlayCard_ComputeToBackend_IsDeployed()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
            state.Player1Hand.Add(new UndeployedCard { InstanceID = "h_1", CardID = "TST-0001" });

            PlayCardProcessor.Process(
                state, _game, 1, MakeReq("h_1", Zones.Backend, 0), _cc, new EffectRegistry());

            state.Player1Field.Backend[0]!.CardID.Should().Be("TST-0001");
        }

        [Fact]
        public void PlayCard_ZeroDeployTurns_IsImmediatelyFaceUp()
        {
            // Serverless (TST-0002) has deployTurns=0 → face-up immediately (RULEBOOK §4)
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
            state.Player1Hand.Add(new UndeployedCard { InstanceID = "h_1", CardID = "TST-0002" });

            PlayCardProcessor.Process(
                state, _game, 1, MakeReq("h_1", Zones.Frontend, 0), _cc, new EffectRegistry());

            state.Player1Field.Frontend[0]!.FaceUp.Should().BeTrue();
            state.Player1HasOperated.Should().BeTrue();
        }

        // RULEBOOK §3 / ARCHITECTURE §3: 手札に無いカードはプレイ不可
        [Fact]
        public void PlayCard_CardNotInHand_IsRejected()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);

            var act = () => PlayCardProcessor.Process(
                state, _game, 1, MakeReq("missing", Zones.Frontend, 0), _cc, new EffectRegistry());

            act.Should().Throw<GameRuleException>();
        }

        // RULEBOOK §3: 既に埋まっているリソーススロットへは配置不可
        [Fact]
        public void PlayCard_IntoOccupiedResourceSlot_IsRejected()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
            state.Player1Field.Frontend[0] = TestFactory.MakeResource();
            state.Player1Hand.Add(new UndeployedCard { InstanceID = "h_1", CardID = "TST-0001" });

            var act = () => PlayCardProcessor.Process(
                state, _game, 1, MakeReq("h_1", Zones.Frontend, 0), _cc, new EffectRegistry());

            act.Should().Throw<GameRuleException>().WithMessage("*occupied*");
        }

        // RULEBOOK §3: Data カード (Database) は Frontend へ配置不可
        [Fact]
        public void PlayCard_DatabaseToFrontend_IsRejected()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
            state.Player1Hand.Add(new UndeployedCard { InstanceID = "h_1", CardID = "TST-0003" });

            var act = () => PlayCardProcessor.Process(
                state, _game, 1, MakeReq("h_1", Zones.Frontend, 0), _cc, new EffectRegistry());

            act.Should().Throw<GameRuleException>();
        }

        // RULEBOOK §3: Compute カードは Support ゾーンへ配置不可
        [Fact]
        public void PlayCard_ComputeToSupport_IsRejected()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
            state.Player1Hand.Add(new UndeployedCard { InstanceID = "h_1", CardID = "TST-0001" });

            var act = () => PlayCardProcessor.Process(
                state, _game, 1, MakeReq("h_1", Zones.Support, 0), _cc, new EffectRegistry());

            act.Should().Throw<GameRuleException>();
        }

        // Frontend/Backend は SlotsPerZone スロットのみ (0..SlotsPerZone-1)
        [Theory]
        [InlineData(-1)]
        [InlineData(3)]
        public void PlayCard_OutOfRangeSlotIndex_IsRejected(int index)
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
            state.Player1Hand.Add(new UndeployedCard { InstanceID = "h_1", CardID = "TST-0001" });

            var act = () => PlayCardProcessor.Process(
                state, _game, 1, MakeReq("h_1", Zones.Frontend, index), _cc, new EffectRegistry());

            act.Should().Throw<GameRuleException>().WithMessage("*invalid slot*");
        }
    }

    /// <summary>Tests for attachment cards attaching to a target or rejecting a missing target.</summary>
    public class Attachment : Base
    {
        [Fact]
        public void Process_AttachmentCard_AttachesToTarget()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
            var target = TestFactory.MakeResource(instanceId: "target_1");
            state.Player1Field.Frontend[0] = target;
            state.Player1Hand.Add(new UndeployedCard { InstanceID = "h_1", CardID = "TEST-0300" });

            PlayCardProcessor.Process(
                state, _game, 1, MakeReq("h_1", Zones.Support, 0, targetInstanceId: "target_1"), _cc, new EffectRegistry());

            state.Player1Field.Support[0].Should().NotBeNull();
            state.Player1Field.Support[0]!.CardID.Should().Be("TEST-0300");
            state.Player1Field.Support[0]!.TargetInstanceID.Should().Be("target_1");
        }

        [Fact]
        public void Process_AttachmentCard_NoTargetId_Throws()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
            state.Player1Hand.Add(new UndeployedCard { InstanceID = "h_1", CardID = "TEST-0300" });

            var act = () => PlayCardProcessor.Process(
                state, _game, 1, MakeReq("h_1", Zones.Support, 0), _cc, new EffectRegistry());

            act.Should().Throw<GameRuleException>();
        }
    }

    /// <summary>Tests for incident cards being once-per-turn and not occupying a slot.</summary>
    public class Incident : Base
    {
        // RULEBOOK §3: Incident はターンに1回のみプレイ可 (フラグ管理)
        [Fact]
        public void PlayCard_Incident_MarksIncidentPlayedThisTurn()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
            state.Player1Hand.Add(new UndeployedCard { InstanceID = "h_1", CardID = "TEST-0500" });

            PlayCardProcessor.Process(
                state, _game, 1, MakeReq("h_1"), _cc, new EffectRegistry());

            state.GetIncidentPlayedThisTurn(1).Should().BeTrue();
        }

        [Fact]
        public void Process_IncidentCard_DoesNotOccupySupportSlot()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
            state.Player1Hand.Add(new UndeployedCard { InstanceID = "h_1", CardID = "TEST-0500" });

            PlayCardProcessor.Process(
                state, _game, 1, MakeReq("h_1"), _cc, new EffectRegistry());

            state.Player1Field.Support.ToList().Should().AllSatisfy(s => s.Should().BeNull());
        }

        // RULEBOOK §3: 同一ターン内の2枚目 Incident は拒否
        [Fact]
        public void PlayCard_SecondIncidentSameTurn_IsRejected()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
            state.Player1Hand.Add(new UndeployedCard { InstanceID = "h_1", CardID = "TEST-0500" });
            state.Player1Hand.Add(new UndeployedCard { InstanceID = "h_2", CardID = "TEST-0500" });

            PlayCardProcessor.Process(
                state, _game, 1, MakeReq("h_1"), _cc, new EffectRegistry());

            var act = () => PlayCardProcessor.Process(
                state, _game, 1, MakeReq("h_2"), _cc, new EffectRegistry());

            act.Should().Throw<GameRuleException>().WithMessage("*incident already played*");
        }

        // RULEBOOK §9: 先攻 T1 では Incident を使用できない (先攻1キル防止)
        [Fact]
        public void PlayCard_IncidentOnFirstTurn_IsRejected()
        {
            var state = TestFactory.MakeGameState(turn: 1, phase: Phase.Main, activePlayer: 1);
            state.Player1Hand.Add(new UndeployedCard { InstanceID = "h_1", CardID = "TEST-0500" });

            var act = () => PlayCardProcessor.Process(
                state, _game, 1, MakeReq("h_1"), _cc, new EffectRegistry());

            act.Should().Throw<GameRuleException>().WithMessage("*cannot play incident on first turn*");
        }
    }

    /// <summary>Tests for reactive cards being placed face-down.</summary>
    public class Reactive : Base
    {
        [Fact]
        public void Process_ReactiveCard_SetsFaceDown()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
            state.Player1Hand.Add(new UndeployedCard { InstanceID = "h_1", CardID = "TEST-0502" });

            PlayCardProcessor.Process(
                state, _game, 1, MakeReq("h_1", Zones.Support, 0), _cc, new EffectRegistry());

            state.Player1Field.Support[0].Should().NotBeNull();
            state.Player1Field.Support[0]!.FaceUp.Should().BeFalse();
        }
    }

    /// <summary>Tests for platform cards being placed face-up with deploy turns.</summary>
    public class Platform : Base
    {
        [Fact]
        public void Process_PlatformCard_SetsFaceUpWithDeployTurns()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
            state.Player1Hand.Add(new UndeployedCard { InstanceID = "h_1", CardID = "TEST-0200" });

            PlayCardProcessor.Process(
                state, _game, 1, MakeReq("h_1", Zones.Support, 0), _cc, new EffectRegistry());

            state.Player1Field.Support[0].Should().NotBeNull();
            state.Player1Field.Support[0]!.FaceUp.Should().BeTrue();
            state.Player1Field.Support[0]!.DeployingTurnsLeft.Should().Be(2);
        }
    }

    /// <summary>Tests for replacing an existing support/attachment and trashing the old one.</summary>
    public class SupportReplacement : Base
    {
        [Fact]
        public void Process_SupportReplacement_OldSupportTrashed()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
            state.Player1Field.Support[0] = new DeployedSupport
            {
                InstanceID = "old_sup",
                CardID = "TEST-0200",
                ArtNo = 0,
                FaceUp = true,
            };
            state.Player1Hand.Add(new UndeployedCard { InstanceID = "h_1", CardID = "TEST-0200" });

            PlayCardProcessor.Process(
                state, _game, 1, MakeReq("h_1", Zones.Support, 0), _cc, new EffectRegistry());

            state.Player1Field.Support[0].Should().NotBeNull();
            state.Player1Field.Support[0]!.InstanceID.Should().NotBe("old_sup");
            state.Player1Trash.Should().Contain(c => c.InstanceID == "old_sup");
        }

        [Fact]
        public void Process_AttachmentReplacement_OldSupportTrashed()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "res_1");
            state.Player1Field.Support[0] = new DeployedSupport
            {
                InstanceID = "old_att",
                CardID = "TEST-0300",
                ArtNo = 0,
                FaceUp = true,
                TargetInstanceID = "res_1",
            };
            state.Player1Hand.Add(new UndeployedCard { InstanceID = "h_1", CardID = "TEST-0300" });

            PlayCardProcessor.Process(
                state, _game, 1, MakeReq("h_1", Zones.Support, 0, "res_1"), _cc, new EffectRegistry());

            state.Player1Field.Support[0].Should().NotBeNull();
            state.Player1Field.Support[0]!.InstanceID.Should().NotBe("old_att");
            state.Player1Trash.Should().Contain(c => c.InstanceID == "old_att");
        }
    }
}
