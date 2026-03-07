using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Tests.Engine;

public class PlayCardProcessorTests
{
    private readonly TestCardCache _cc = new();
    private readonly Game _game = TestFactory.MakeGame();

    public PlayCardProcessorTests()
    {
        // Compute card: deployTurns=1
        _cc.Add(TestFactory.ComputeCard(cardNo: 1, deployTurns: 1));
        // Serverless card: deployTurns=0
        _cc.Add(TestFactory.ServerlessCard(cardNo: 3));
        // Database card (data type, backend-only)
        _cc.Add(TestFactory.DataCard(cardNo: 100, cardType: CardTypes.Database));
        // Attachment card
        _cc.Add(TestFactory.AttachmentCard(cardNo: 300));
        // Incident card
        _cc.Add(new CardDefinition { CardNo = 500, CardName = "TestIncident", CardType = CardTypes.Incident, DeployTurns = 0 });
        // Strategy card
        _cc.Add(new CardDefinition { CardNo = 501, CardName = "TestStrategy", CardType = CardTypes.Strategy, DeployTurns = 0 });
        // Reactive card
        _cc.Add(new CardDefinition { CardNo = 502, CardName = "TestReactive", CardType = CardTypes.Reactive, DeployTurns = 0 });
        // Platform card
        _cc.Add(TestFactory.PlatformCard(cardNo: 200));
    }

    private static PlayCardRequest MakeReq(string instanceId, string zone, int index, string? targetInstanceId = null) =>
        new()
        {
            CardInstanceID = instanceId,
            Zone = zone,
            Index = index,
            TargetInstanceID = targetInstanceId,
        };

    // ─── 1. Compute card to frontend ─────────────────────────

    [Fact]
    public void Process_ComputeCardToFrontend_PlacesResourceAndGeneratesEvent()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
        state.Player1Hand.Add(new HandCard { InstanceID = "h_1", CardID = 1 });

        var result = PlayCardProcessor.Process(
            state, _game, 1, MakeReq("h_1", GameConstants.ZoneFrontend, 0), _cc, null);

        state.Player1Hand.Should().BeEmpty();
        state.Player1Field.Frontend[0].Should().NotBeNull();
        state.Player1Field.Frontend[0]!.CardID.Should().Be(1);

        result.Events.Should().ContainSingle(e => e.EventType == WireActionTypes.PlayCard);

        // deployTurns=1 → face-down
        state.Player1Field.Frontend[0]!.FaceUp.Should().BeFalse();
        state.Player1Field.Frontend[0]!.DeployingTurnsLeft.Should().Be(1);
    }

    // ─── 2. Compute card to backend ──────────────────────────

    [Fact]
    public void Process_ComputeCardToBackend_PlacesResourceInBackend()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
        state.Player1Hand.Add(new HandCard { InstanceID = "h_1", CardID = 1 });

        PlayCardProcessor.Process(
            state, _game, 1, MakeReq("h_1", GameConstants.ZoneBackend, 0), _cc, null);

        state.Player1Field.Backend[0].Should().NotBeNull();
        state.Player1Field.Backend[0]!.CardID.Should().Be(1);
    }

    // ─── 3. Zero deploy turns → face-up ─────────────────────

    [Fact]
    public void Process_ZeroDeployTurns_ResourceIsFaceUp()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
        state.Player1Hand.Add(new HandCard { InstanceID = "h_1", CardID = 3 });

        PlayCardProcessor.Process(
            state, _game, 1, MakeReq("h_1", GameConstants.ZoneFrontend, 0), _cc, null);

        state.Player1Field.Frontend[0]!.FaceUp.Should().BeTrue();
        state.Player1HasHadActiveResource.Should().BeTrue();
    }

    // ─── 4. Card not in hand → throws ───────────────────────

    [Fact]
    public void Process_CardNotInHand_Throws()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);

        var act = () => PlayCardProcessor.Process(
            state, _game, 1, MakeReq("missing", GameConstants.ZoneFrontend, 0), _cc, null);

        act.Should().Throw<GameRuleException>();
    }

    // ─── 5. Occupied slot → throws ──────────────────────────

    [Fact]
    public void Process_OccupiedSlot_Throws()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
        state.Player1Field.Frontend[0] = TestFactory.MakeResource();
        state.Player1Hand.Add(new HandCard { InstanceID = "h_1", CardID = 1 });

        var act = () => PlayCardProcessor.Process(
            state, _game, 1, MakeReq("h_1", GameConstants.ZoneFrontend, 0), _cc, null);

        act.Should().Throw<GameRuleException>().WithMessage("*occupied*");
    }

    // ─── 6. Database to frontend → throws ───────────────────

    [Fact]
    public void Process_DatabaseToFrontend_Throws()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
        state.Player1Hand.Add(new HandCard { InstanceID = "h_1", CardID = 100 });

        var act = () => PlayCardProcessor.Process(
            state, _game, 1, MakeReq("h_1", GameConstants.ZoneFrontend, 0), _cc, null);

        act.Should().Throw<GameRuleException>();
    }

    // ─── 7. Compute to support → throws ─────────────────────

    [Fact]
    public void Process_ComputeToSupportZone_Throws()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
        state.Player1Hand.Add(new HandCard { InstanceID = "h_1", CardID = 1 });

        var act = () => PlayCardProcessor.Process(
            state, _game, 1, MakeReq("h_1", GameConstants.ZoneSupport, 0), _cc, null);

        act.Should().Throw<GameRuleException>();
    }

    // ─── 8. Invalid slot index → throws ─────────────────────

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void Process_InvalidSlotIndex_Throws(int index)
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
        state.Player1Hand.Add(new HandCard { InstanceID = "h_1", CardID = 1 });

        var act = () => PlayCardProcessor.Process(
            state, _game, 1, MakeReq("h_1", GameConstants.ZoneFrontend, index), _cc, null);

        act.Should().Throw<GameRuleException>().WithMessage("*invalid slot*");
    }

    // ─── 9. Attachment card attaches to target ──────────────

    [Fact]
    public void Process_AttachmentCard_AttachesToTarget()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
        var target = TestFactory.MakeResource(instanceId: "target_1");
        state.Player1Field.Frontend[0] = target;
        state.Player1Hand.Add(new HandCard { InstanceID = "h_1", CardID = 300 });

        PlayCardProcessor.Process(
            state, _game, 1, MakeReq("h_1", GameConstants.ZoneFrontend, 0, targetInstanceId: "target_1"), _cc, null);

        target.Attachments.Should().HaveCount(1);
        target.Attachments[0].CardID.Should().Be(300);
    }

    // ─── 10. Attachment max → throws ────────────────────────

    [Fact]
    public void Process_AttachmentCard_MaxAttachments_Throws()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
        var target = TestFactory.MakeResource(instanceId: "target_1");
        target.Attachments.Add(new AttachmentRef { InstanceID = "a1", CardID = 300 });
        target.Attachments.Add(new AttachmentRef { InstanceID = "a2", CardID = 300 });
        state.Player1Field.Frontend[0] = target;
        state.Player1Hand.Add(new HandCard { InstanceID = "h_1", CardID = 300 });

        var act = () => PlayCardProcessor.Process(
            state, _game, 1, MakeReq("h_1", GameConstants.ZoneFrontend, 0, targetInstanceId: "target_1"), _cc, null);

        act.Should().Throw<GameRuleException>().WithMessage("*max attachments*");
    }

    // ─── 11. Attachment without target ID → throws ──────────

    [Fact]
    public void Process_AttachmentCard_NoTargetId_Throws()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
        state.Player1Hand.Add(new HandCard { InstanceID = "h_1", CardID = 300 });

        var act = () => PlayCardProcessor.Process(
            state, _game, 1, MakeReq("h_1", GameConstants.ZoneSupport, 0), _cc, null);

        act.Should().Throw<GameRuleException>();
    }

    // ─── 12. Incident card sets flag ────────────────────────

    [Fact]
    public void Process_IncidentCard_SetsIncidentPlayedFlag()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
        state.Player1Hand.Add(new HandCard { InstanceID = "h_1", CardID = 500 });

        PlayCardProcessor.Process(
            state, _game, 1, MakeReq("h_1", GameConstants.ZoneSupport, 0), _cc, null);

        state.GetIncidentPlayedThisTurn(1).Should().BeTrue();
    }

    // ─── 13. Second incident same turn → throws ─────────────

    [Fact]
    public void Process_SecondIncidentSameTurn_Throws()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
        state.Player1Hand.Add(new HandCard { InstanceID = "h_1", CardID = 500 });
        state.Player1Hand.Add(new HandCard { InstanceID = "h_2", CardID = 500 });

        PlayCardProcessor.Process(
            state, _game, 1, MakeReq("h_1", GameConstants.ZoneSupport, 0), _cc, null);

        var act = () => PlayCardProcessor.Process(
            state, _game, 1, MakeReq("h_2", GameConstants.ZoneSupport, 0), _cc, null);

        act.Should().Throw<GameRuleException>().WithMessage("*incident already played*");
    }

    // ─── 14. Reactive card → face-down ──────────────────────

    [Fact]
    public void Process_ReactiveCard_SetsFaceDown()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
        state.Player1Hand.Add(new HandCard { InstanceID = "h_1", CardID = 502 });

        PlayCardProcessor.Process(
            state, _game, 1, MakeReq("h_1", GameConstants.ZoneSupport, 0), _cc, null);

        state.Player1Field.Support[0].Should().NotBeNull();
        state.Player1Field.Support[0]!.FaceUp.Should().BeFalse();
    }

    // ─── 15. Platform card → face-up with deploy turns ──────

    [Fact]
    public void Process_PlatformCard_SetsFaceUpWithDeployTurns()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
        state.Player1Hand.Add(new HandCard { InstanceID = "h_1", CardID = 200 });

        PlayCardProcessor.Process(
            state, _game, 1, MakeReq("h_1", GameConstants.ZoneSupport, 0), _cc, null);

        state.Player1Field.Support[0].Should().NotBeNull();
        state.Player1Field.Support[0]!.FaceUp.Should().BeTrue();
        state.Player1Field.Support[0]!.DeployingTurnsLeft.Should().Be(2);
    }
}
