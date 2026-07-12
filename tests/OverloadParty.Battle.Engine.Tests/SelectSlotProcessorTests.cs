using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Engine;

[Trait("対象", "スロット選択")]
public class SelectSlotProcessorTests
{
    private readonly TestCardCache _cc = new();
    private readonly TestEffectRegistry _effects = new();
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

    [Fact(DisplayName = "有効なスロットを選択するとリソースが配置され選択待ちが解消される")]
    public void Process_ValidSlot_PlacesResourceAndClearsPending()
    {
        var state = MakeStateWithPending("frontend", 0);
        var req = new SelectSlotRequest { Zone = "frontend", Index = 0 };

        var result = SelectSlotProcessor.Process(state, _game, 1, req, _cc, _effects);

        state.Player1Field.Frontend[0].Should().NotBeNull();
        state.Player1Field.Frontend[0]!.InstanceID.Should().Be("pending_1");
        state.PendingSlotSelects.Should().BeEmpty();
    }

    [Fact(DisplayName = "バックエンドのスロットを選択するとバックエンドに配置される")]
    public void Process_ValidBackendSlot_PlacesInBackend()
    {
        var state = MakeStateWithPending("backend", 1);
        state.PendingSlotSelects[0].ValidZones = ["backend_1"];
        var req = new SelectSlotRequest { Zone = "backend", Index = 1 };

        SelectSlotProcessor.Process(state, _game, 1, req, _cc, _effects);

        state.Player1Field.Backend[1].Should().NotBeNull();
        state.Player1Field.Backend[1]!.InstanceID.Should().Be("pending_1");
    }

    [Fact(DisplayName = "スロットを選択するとゾーンとインデックスを載せたスロット選択イベントが発行される")]
    public void Process_EmitsSelectSlotEvent()
    {
        var state = MakeStateWithPending("frontend", 2);
        state.PendingSlotSelects[0].ValidZones = ["frontend_2"];
        var req = new SelectSlotRequest { Zone = "frontend", Index = 2 };

        var result = SelectSlotProcessor.Process(state, _game, 1, req, _cc, _effects);

        result.Events.Should().ContainSingle();
        var evt = result.Events[0];
        evt.EventType.Should().Be(ActionTypes.SelectSlot);
        var data = evt.EventData.Should().BeOfType<SelectSlotEventData>().Subject;
        data.Zone.Should().Be("frontend");
        data.Index.Should().Be(2);
        data.CardId.Should().Be("TST-0001");
        data.InstanceId.Should().Be("pending_1");
    }

    [Fact(DisplayName = "選択待ちがないのにスロット選択すると拒否される")]
    public void Process_NoPending_Throws()
    {
        var state = TestFactory.MakeGameState();
        var req = new SelectSlotRequest { Zone = "frontend", Index = 0 };

        var act = () => SelectSlotProcessor.Process(state, _game, 1, req, _cc, _effects);

        act.Should().Throw<GameRuleException>().WithMessage("*pending*");
    }

    [Fact(DisplayName = "選択待ちと異なるプレイヤーがスロット選択すると拒否される")]
    public void Process_WrongPlayer_Throws()
    {
        var state = MakeStateWithPending("frontend", 0);
        var req = new SelectSlotRequest { Zone = "frontend", Index = 0 };

        var act = () => SelectSlotProcessor.Process(state, _game, 2, req, _cc, _effects);

        act.Should().Throw<GameRuleException>().WithMessage("*different player*");
    }

    [Fact(DisplayName = "有効ゾーンに含まれないスロットを選択すると拒否される")]
    public void Process_SlotNotInValidZones_Throws()
    {
        var state = MakeStateWithPending("frontend", 0);
        var req = new SelectSlotRequest { Zone = "backend", Index = 0 };

        var act = () => SelectSlotProcessor.Process(state, _game, 1, req, _cc, _effects);

        act.Should().Throw<GameRuleException>().WithMessage("*Invalid slot*");
    }

    [Fact(DisplayName = "既に埋まっているスロットを選択すると拒否される")]
    public void Process_OccupiedSlot_Throws()
    {
        var state = MakeStateWithPending("frontend", 0);
        state.PendingSlotSelects[0].ValidZones = ["frontend_0"];
        state.Player1Field.Frontend[0] = TestFactory.MakeResource(instanceId: "existing");
        var req = new SelectSlotRequest { Zone = "frontend", Index = 0 };

        var act = () => SelectSlotProcessor.Process(state, _game, 1, req, _cc, _effects);

        act.Should().Throw<GameRuleException>().WithMessage("*occupied*");
    }

    [Fact(DisplayName = "未知のゾーンを選択すると拒否される")]
    public void Process_UnknownZone_Throws()
    {
        var state = MakeStateWithPending("frontend", 0);
        state.PendingSlotSelects[0].ValidZones = ["support_0"];
        var req = new SelectSlotRequest { Zone = "support", Index = 0 };

        var act = () => SelectSlotProcessor.Process(state, _game, 1, req, _cc, _effects);

        act.Should().Throw<GameRuleException>().WithMessage("*Unknown zone*");
    }

    [Fact(DisplayName = "範囲外のインデックスを選択すると拒否される")]
    public void Process_OutOfBoundsIndex_Throws()
    {
        var state = MakeStateWithPending("frontend", 0);
        state.PendingSlotSelects[0].ValidZones = ["frontend_99"];
        var req = new SelectSlotRequest { Zone = "frontend", Index = 99 };

        var act = () => SelectSlotProcessor.Process(state, _game, 1, req, _cc, _effects);

        act.Should().Throw<GameRuleException>();
    }

    [Fact(DisplayName = "選択待ちが複数あるとき先頭を配置し残りのスロット選択を要求する")]
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

        var result = SelectSlotProcessor.Process(state, _game, 1, req, _cc, _effects);

        state.Player1Field.Frontend[0]!.InstanceID.Should().Be("pending_1");
        state.PendingSlotSelects.Should().ContainSingle();
        state.PendingSlotSelects[0].Resource.InstanceID.Should().Be("pending_2");
        result.ShouldSelectSlot.Should().BeTrue();
    }

    [Fact(DisplayName = "選択待ちが 1 件だけのとき配置後はスロット選択を要求しない")]
    public void Process_SinglePending_NeedsSlotSelectIsFalse()
    {
        var state = MakeStateWithPending("frontend", 0);
        var req = new SelectSlotRequest { Zone = "frontend", Index = 0 };

        var result = SelectSlotProcessor.Process(state, _game, 1, req, _cc, _effects);

        result.ShouldSelectSlot.Should().BeFalse();
    }
}
