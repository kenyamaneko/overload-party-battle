using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Engine;

public class PlayCardProcessorTests
{
    private readonly TestCardCache _cc = new();
    private readonly Game _game = TestFactory.MakeGame();

    public PlayCardProcessorTests()
    {
        // Compute card: deployTurns=1
        _cc.Add(TestFactory.ComputeCard(cardId: "SH-0001", deployTurns: 1));
        // Serverless card: deployTurns=0
        _cc.Add(TestFactory.ServerlessCard(cardId: "SH-0002"));
        // Database card (data type, backend-only)
        _cc.Add(TestFactory.DataCard(cardId: "NT-0009", cardType: CardTypes.Database));
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

    private static PlayCardRequest MakeReq(string instanceId, string zone, int index, string? targetInstanceId = null) =>
        new()
        {
            CardInstanceID = instanceId,
            Zone = zone,
            Index = index,
            TargetInstanceID = targetInstanceId,
        };

    private static PlayCardRequest MakeReq(string instanceId) =>
        new()
        {
            CardInstanceID = instanceId,
            Zone = "",
            Index = 0,
        };

    // ─── 1. Compute card to frontend ─────────────────────────

    [Fact]
    public void Process_ComputeCardToFrontend_PlacesResourceAndGeneratesEvent()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
        state.Player1Hand.Add(new UndeployedCard { InstanceID = "h_1", CardID = "SH-0001" });

        var result = PlayCardProcessor.Process(
            state, _game, 1, MakeReq("h_1", Zones.Frontend, 0), _cc, null);

        state.Player1Hand.Should().BeEmpty();
        state.Player1Field.Frontend[0].Should().NotBeNull();
        state.Player1Field.Frontend[0]!.CardID.Should().Be("SH-0001");

        result.Events.Should().ContainSingle(e => e.EventType == ActionTypes.PlayCard);

        // deployTurns=1 → face-down
        state.Player1Field.Frontend[0]!.FaceUp.Should().BeFalse();
        state.Player1Field.Frontend[0]!.DeployingTurnsLeft.Should().Be(1);
    }

    // ─── 2. Compute card to backend ──────────────────────────

    [Fact]
    public void Process_ComputeCardToBackend_PlacesResourceInBackend()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
        state.Player1Hand.Add(new UndeployedCard { InstanceID = "h_1", CardID = "SH-0001" });

        PlayCardProcessor.Process(
            state, _game, 1, MakeReq("h_1", Zones.Backend, 0), _cc, null);

        state.Player1Field.Backend[0].Should().NotBeNull();
        state.Player1Field.Backend[0]!.CardID.Should().Be("SH-0001");
    }

    // ─── 3. Zero deploy turns → face-up ─────────────────────

    [Fact]
    public void Process_ZeroDeployTurns_ResourceIsFaceUp()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
        state.Player1Hand.Add(new UndeployedCard { InstanceID = "h_1", CardID = "SH-0002" });

        PlayCardProcessor.Process(
            state, _game, 1, MakeReq("h_1", Zones.Frontend, 0), _cc, null);

        state.Player1Field.Frontend[0]!.FaceUp.Should().BeTrue();
        state.Player1HasHadActiveResource.Should().BeTrue();
    }

    // ─── 4. Card not in hand → throws ───────────────────────

    [Fact]
    public void Process_CardNotInHand_Throws()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);

        var act = () => PlayCardProcessor.Process(
            state, _game, 1, MakeReq("missing", Zones.Frontend, 0), _cc, null);

        act.Should().Throw<GameRuleException>();
    }

    // ─── 5. Occupied slot → throws ──────────────────────────

    [Fact]
    public void Process_OccupiedSlot_Throws()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
        state.Player1Field.Frontend[0] = TestFactory.MakeResource();
        state.Player1Hand.Add(new UndeployedCard { InstanceID = "h_1", CardID = "SH-0001" });

        var act = () => PlayCardProcessor.Process(
            state, _game, 1, MakeReq("h_1", Zones.Frontend, 0), _cc, null);

        act.Should().Throw<GameRuleException>().WithMessage("*occupied*");
    }

    // ─── 6. Database to frontend → throws ───────────────────

    [Fact]
    public void Process_DatabaseToFrontend_Throws()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
        state.Player1Hand.Add(new UndeployedCard { InstanceID = "h_1", CardID = "NT-0009" });

        var act = () => PlayCardProcessor.Process(
            state, _game, 1, MakeReq("h_1", Zones.Frontend, 0), _cc, null);

        act.Should().Throw<GameRuleException>();
    }

    // ─── 7. Compute to support → throws ─────────────────────

    [Fact]
    public void Process_ComputeToSupportZone_Throws()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
        state.Player1Hand.Add(new UndeployedCard { InstanceID = "h_1", CardID = "SH-0001" });

        var act = () => PlayCardProcessor.Process(
            state, _game, 1, MakeReq("h_1", Zones.Support, 0), _cc, null);

        act.Should().Throw<GameRuleException>();
    }

    // ─── 8. Invalid slot index → throws ─────────────────────

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void Process_InvalidSlotIndex_Throws(int index)
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
        state.Player1Hand.Add(new UndeployedCard { InstanceID = "h_1", CardID = "SH-0001" });

        var act = () => PlayCardProcessor.Process(
            state, _game, 1, MakeReq("h_1", Zones.Frontend, index), _cc, null);

        act.Should().Throw<GameRuleException>().WithMessage("*invalid slot*");
    }

    // ─── 9. Attachment card attaches to target ──────────────

    [Fact]
    public void Process_AttachmentCard_AttachesToTarget()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
        var target = TestFactory.MakeResource(instanceId: "target_1");
        state.Player1Field.Frontend[0] = target;
        state.Player1Hand.Add(new UndeployedCard { InstanceID = "h_1", CardID = "TEST-0300" });

        PlayCardProcessor.Process(
            state, _game, 1, MakeReq("h_1", Zones.Support, 0, targetInstanceId: "target_1"), _cc, null);

        state.Player1Field.Support[0].Should().NotBeNull();
        state.Player1Field.Support[0]!.CardID.Should().Be("TEST-0300");
        state.Player1Field.Support[0]!.TargetInstanceID.Should().Be("target_1");
    }

    // ─── 10. Attachment without target ID → throws ──────────

    [Fact]
    public void Process_AttachmentCard_NoTargetId_Throws()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
        state.Player1Hand.Add(new UndeployedCard { InstanceID = "h_1", CardID = "TEST-0300" });

        var act = () => PlayCardProcessor.Process(
            state, _game, 1, MakeReq("h_1", Zones.Support, 0), _cc, null);

        act.Should().Throw<GameRuleException>();
    }

    // ─── 12. Incident card sets flag ────────────────────────

    [Fact]
    public void Process_IncidentCard_SetsIncidentPlayedFlag()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
        state.Player1Hand.Add(new UndeployedCard { InstanceID = "h_1", CardID = "TEST-0500" });

        PlayCardProcessor.Process(
            state, _game, 1, MakeReq("h_1"), _cc, null);

        state.GetIncidentPlayedThisTurn(1).Should().BeTrue();
    }

    [Fact]
    public void Process_IncidentCard_DoesNotOccupySupportSlot()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
        state.Player1Hand.Add(new UndeployedCard { InstanceID = "h_1", CardID = "TEST-0500" });

        PlayCardProcessor.Process(
            state, _game, 1, MakeReq("h_1"), _cc, null);

        state.Player1Field.Support.ToList().Should().AllSatisfy(s => s.Should().BeNull());
    }

    // ─── 13. Second incident same turn → throws ─────────────

    [Fact]
    public void Process_SecondIncidentSameTurn_Throws()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
        state.Player1Hand.Add(new UndeployedCard { InstanceID = "h_1", CardID = "TEST-0500" });
        state.Player1Hand.Add(new UndeployedCard { InstanceID = "h_2", CardID = "TEST-0500" });

        PlayCardProcessor.Process(
            state, _game, 1, MakeReq("h_1"), _cc, null);

        var act = () => PlayCardProcessor.Process(
            state, _game, 1, MakeReq("h_2"), _cc, null);

        act.Should().Throw<GameRuleException>().WithMessage("*incident already played*");
    }

    // ─── 14. Reactive card → face-down ──────────────────────

    [Fact]
    public void Process_ReactiveCard_SetsFaceDown()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
        state.Player1Hand.Add(new UndeployedCard { InstanceID = "h_1", CardID = "TEST-0502" });

        PlayCardProcessor.Process(
            state, _game, 1, MakeReq("h_1", Zones.Support, 0), _cc, null);

        state.Player1Field.Support[0].Should().NotBeNull();
        state.Player1Field.Support[0]!.FaceUp.Should().BeFalse();
    }

    // ─── 15. Platform card → face-up with deploy turns ──────

    [Fact]
    public void Process_PlatformCard_SetsFaceUpWithDeployTurns()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
        state.Player1Hand.Add(new UndeployedCard { InstanceID = "h_1", CardID = "TEST-0200" });

        PlayCardProcessor.Process(
            state, _game, 1, MakeReq("h_1", Zones.Support, 0), _cc, null);

        state.Player1Field.Support[0].Should().NotBeNull();
        state.Player1Field.Support[0]!.FaceUp.Should().BeTrue();
        state.Player1Field.Support[0]!.DeployingTurnsLeft.Should().Be(2);
    }

    // ─── Support replacement (張り替え) ─────────────────────

    [Fact]
    public void Process_SupportReplacement_OldSupportTrashed()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
        state.Player1Field.Support[0] = new DeployedSupport
        {
            InstanceID = "old_sup", CardID = "TEST-0200", ArtNo = 0, FaceUp = true,
        };
        state.Player1Hand.Add(new UndeployedCard { InstanceID = "h_1", CardID = "TEST-0200" });

        PlayCardProcessor.Process(
            state, _game, 1, MakeReq("h_1", Zones.Support, 0), _cc, null);

        state.Player1Field.Support[0].Should().NotBeNull();
        state.Player1Field.Support[0]!.InstanceID.Should().NotBe("old_sup");
        state.Player1Trash.Should().Contain(c => c.InstanceID == "old_sup");
    }

    [Fact]
    public void Process_AttachmentReplacement_OldSupportTrashed()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
        state.Player1Field.Frontend[0] = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "res_1");
        state.Player1Field.Support[0] = new DeployedSupport
        {
            InstanceID = "old_att", CardID = "TEST-0300", ArtNo = 0, FaceUp = true,
            TargetInstanceID = "res_1",
        };
        state.Player1Hand.Add(new UndeployedCard { InstanceID = "h_1", CardID = "TEST-0300" });

        PlayCardProcessor.Process(
            state, _game, 1, MakeReq("h_1", Zones.Support, 0, "res_1"), _cc, null);

        state.Player1Field.Support[0].Should().NotBeNull();
        state.Player1Field.Support[0]!.InstanceID.Should().NotBe("old_att");
        state.Player1Trash.Should().Contain(c => c.InstanceID == "old_att");
    }

    [Fact]
    public void Process_ResourceOccupiedSlot_StillThrows()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
        state.Player1Field.Frontend[0] = TestFactory.MakeResource();
        state.Player1Hand.Add(new UndeployedCard { InstanceID = "h_1", CardID = "SH-0001" });

        var act = () => PlayCardProcessor.Process(
            state, _game, 1, MakeReq("h_1", Zones.Frontend, 0), _cc, null);

        act.Should().Throw<GameRuleException>().WithMessage("*occupied*");
    }
}
