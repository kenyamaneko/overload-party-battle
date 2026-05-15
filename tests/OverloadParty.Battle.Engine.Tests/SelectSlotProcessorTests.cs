using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Engine;

public class SelectSlotProcessorTests
{
    private readonly TestCardCache _cc = new();
    private readonly Game _game = TestFactory.MakeGame();

    public SelectSlotProcessorTests()
    {
        _cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
    }

    private BattleGameState MakeStateWithPending(string zone = "frontend", int index = 0)
    {
        var state = TestFactory.MakeGameState(phase: Phase.Main);
        state.PendingSlotSelects.Add(new AwaitingSlotSelect
        {
            PlayerNum = 1,
            Resource = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "pending_1"),
            ValidZones = [$"{zone}_{index}"],
        });
        return state;
    }

    [Fact]
    public void Process_ValidSlot_PlacesResourceAndClearsPending()
    {
        var state = MakeStateWithPending("frontend", 0);
        var req = new SelectSlotRequest { Zone = "frontend", Index = 0 };

        var result = SelectSlotProcessor.Process(state, _game, 1, req, _cc);

        state.Player1Field.Frontend[0].Should().NotBeNull();
        state.Player1Field.Frontend[0]!.InstanceID.Should().Be("pending_1");
        state.PendingSlotSelects.Should().BeEmpty();
        result.StateUpdated.Should().BeTrue();
    }

    [Fact]
    public void Process_ValidBackendSlot_PlacesInBackend()
    {
        var state = MakeStateWithPending("backend", 1);
        state.PendingSlotSelects[0].ValidZones = ["backend_1"];
        var req = new SelectSlotRequest { Zone = "backend", Index = 1 };

        SelectSlotProcessor.Process(state, _game, 1, req, _cc);

        state.Player1Field.Backend[1].Should().NotBeNull();
        state.Player1Field.Backend[1]!.InstanceID.Should().Be("pending_1");
    }

    [Fact]
    public void Process_EmitsSelectSlotEvent()
    {
        var state = MakeStateWithPending("frontend", 2);
        state.PendingSlotSelects[0].ValidZones = ["frontend_2"];
        var req = new SelectSlotRequest { Zone = "frontend", Index = 2 };

        var result = SelectSlotProcessor.Process(state, _game, 1, req, _cc);

        result.Events.Should().ContainSingle();
        var evt = result.Events[0];
        evt.EventType.Should().Be(ActionTypes.SelectSlot);
        var data = evt.EventData.Should().BeOfType<SelectSlotEventData>().Subject;
        data.Zone.Should().Be("frontend");
        data.Index.Should().Be(2);
    }

    [Fact]
    public void Process_NoPending_Throws()
    {
        var state = TestFactory.MakeGameState();
        var req = new SelectSlotRequest { Zone = "frontend", Index = 0 };

        var act = () => SelectSlotProcessor.Process(state, _game, 1, req, _cc);

        act.Should().Throw<GameRuleException>().WithMessage("*pending*");
    }

    [Fact]
    public void Process_WrongPlayer_Throws()
    {
        var state = MakeStateWithPending("frontend", 0);
        var req = new SelectSlotRequest { Zone = "frontend", Index = 0 };

        var act = () => SelectSlotProcessor.Process(state, _game, 2, req, _cc);

        act.Should().Throw<GameRuleException>().WithMessage("*different player*");
    }

    [Fact]
    public void Process_SlotNotInValidZones_Throws()
    {
        var state = MakeStateWithPending("frontend", 0);
        var req = new SelectSlotRequest { Zone = "backend", Index = 0 };

        var act = () => SelectSlotProcessor.Process(state, _game, 1, req, _cc);

        act.Should().Throw<GameRuleException>().WithMessage("*Invalid slot*");
    }

    [Fact]
    public void Process_OccupiedSlot_Throws()
    {
        var state = MakeStateWithPending("frontend", 0);
        state.PendingSlotSelects[0].ValidZones = ["frontend_0"];
        state.Player1Field.Frontend[0] = TestFactory.MakeResource(instanceId: "existing");
        var req = new SelectSlotRequest { Zone = "frontend", Index = 0 };

        var act = () => SelectSlotProcessor.Process(state, _game, 1, req, _cc);

        act.Should().Throw<GameRuleException>().WithMessage("*occupied*");
    }

    [Fact]
    public void Process_UnknownZone_Throws()
    {
        var state = MakeStateWithPending("frontend", 0);
        state.PendingSlotSelects[0].ValidZones = ["support_0"];
        var req = new SelectSlotRequest { Zone = "support", Index = 0 };

        var act = () => SelectSlotProcessor.Process(state, _game, 1, req, _cc);

        act.Should().Throw<GameRuleException>().WithMessage("*Unknown zone*");
    }

    [Fact]
    public void Process_OutOfBoundsIndex_Throws()
    {
        var state = MakeStateWithPending("frontend", 0);
        state.PendingSlotSelects[0].ValidZones = ["frontend_99"];
        var req = new SelectSlotRequest { Zone = "frontend", Index = 99 };

        var act = () => SelectSlotProcessor.Process(state, _game, 1, req, _cc);

        act.Should().Throw<GameRuleException>();
    }

    [Fact]
    public void Process_MultiplePending_ConsumesFirstAndKeepsNeedsSlotSelect()
    {
        var state = MakeStateWithPending("frontend", 0);
        state.PendingSlotSelects.Add(new AwaitingSlotSelect
        {
            PlayerNum = 1,
            Resource = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "pending_2"),
            ValidZones = ["frontend_1"],
        });
        var req = new SelectSlotRequest { Zone = "frontend", Index = 0 };

        var result = SelectSlotProcessor.Process(state, _game, 1, req, _cc);

        state.Player1Field.Frontend[0]!.InstanceID.Should().Be("pending_1");
        state.PendingSlotSelects.Should().ContainSingle();
        state.PendingSlotSelects[0].Resource.InstanceID.Should().Be("pending_2");
        result.NeedsSlotSelect.Should().BeTrue();
    }

    [Fact]
    public void Process_SinglePending_NeedsSlotSelectIsFalse()
    {
        var state = MakeStateWithPending("frontend", 0);
        var req = new SelectSlotRequest { Zone = "frontend", Index = 0 };

        var result = SelectSlotProcessor.Process(state, _game, 1, req, _cc);

        result.NeedsSlotSelect.Should().BeFalse();
    }
}
