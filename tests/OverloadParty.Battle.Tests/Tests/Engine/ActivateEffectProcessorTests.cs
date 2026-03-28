using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Tests.Engine;

public class ActivateEffectProcessorTests
{
    private readonly TestCardCache _cc = new();
    private readonly Game _game = TestFactory.MakeGame();

    public ActivateEffectProcessorTests()
    {
        _cc.Add(TestFactory.ComputeCard(cardId: "SH-0001", deployTurns: 0));
        _cc.Add(TestFactory.PlatformCard(cardId: "TEST-0200"));
    }

    // ─── Resource effect ────────────────────────────────────────

    [Fact]
    public void Process_ResourceEffect_ExecutesHandlerAndSetsFlag()
    {
        bool handlerCalled = false;
        var reg = new EffectRegistry();
        reg.Register("SH-0001", TriggerType.Activate, ctx =>
        {
            handlerCalled = true;
            return new EffectResult();
        });

        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
        var resource = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "r_1", faceUp: true);
        state.Player1Field.Frontend[0] = resource;

        var req = new ActivateEffectRequest { InstanceID = "r_1" };
        ActivateEffectProcessor.Process(state, _game, 1, req, _cc, reg);

        handlerCalled.Should().BeTrue();
        resource.EffectUsedThisTurn.Should().BeTrue();
    }

    [Fact]
    public void Process_ResourceEffect_GeneratesActivateEvent()
    {
        var reg = new EffectRegistry();
        reg.Register("SH-0001", TriggerType.Activate, _ => new EffectResult());

        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
        state.Player1Field.Frontend[0] = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "r_1");

        var req = new ActivateEffectRequest { InstanceID = "r_1" };
        var result = ActivateEffectProcessor.Process(state, _game, 1, req, _cc, reg);

        result.Events.Should().ContainSingle(e => e.EventType == WireActionTypes.ActivateEffect);
    }

    [Fact]
    public void Process_NoActivateEffect_Throws()
    {
        var reg = new EffectRegistry(); // nothing registered

        var state = TestFactory.MakeGameState(turn: 2);
        state.Player1Field.Frontend[0] = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "r_1");

        var req = new ActivateEffectRequest { InstanceID = "r_1" };
        var act = () => ActivateEffectProcessor.Process(state, _game, 1, req, _cc, reg);

        act.Should().Throw<GameRuleException>().WithMessage("*no activate effect*");
    }

    [Fact]
    public void Process_EffectAlreadyUsedThisTurn_Throws()
    {
        var reg = new EffectRegistry();
        reg.Register("SH-0001", TriggerType.Activate, _ => new EffectResult());

        var state = TestFactory.MakeGameState(turn: 2);
        var resource = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "r_1");
        resource.EffectUsedThisTurn = true;
        state.Player1Field.Frontend[0] = resource;

        var req = new ActivateEffectRequest { InstanceID = "r_1" };
        var act = () => ActivateEffectProcessor.Process(state, _game, 1, req, _cc, reg);

        act.Should().Throw<GameRuleException>().WithMessage("*already used*");
    }

    [Fact]
    public void Process_CannotOperateEffect_Throws()
    {
        var reg = new EffectRegistry();
        reg.Register("SH-0001", TriggerType.Activate, _ => new EffectResult());

        var state = TestFactory.MakeGameState(turn: 2);
        var resource = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "r_1");
        resource.TemporaryEffects.Add(new TemporaryEffect
        {
            EffectType = EffectTypes.CannotOperate,
            Value = 1,
            Duration = "permanent",
            SourceID = "test",
        });
        state.Player1Field.Frontend[0] = resource;

        var req = new ActivateEffectRequest { InstanceID = "r_1" };
        var act = () => ActivateEffectProcessor.Process(state, _game, 1, req, _cc, reg);

        act.Should().Throw<GameRuleException>().WithMessage("*cannot operate*");
    }

    [Fact]
    public void Process_ResourceNotFound_Throws()
    {
        var reg = new EffectRegistry();
        reg.Register("SH-0001", TriggerType.Activate, _ => new EffectResult());

        var state = TestFactory.MakeGameState(turn: 2);
        // Nothing on the field

        var req = new ActivateEffectRequest { InstanceID = "nonexistent" };
        var act = () => ActivateEffectProcessor.Process(state, _game, 1, req, _cc, reg);

        act.Should().Throw<GameRuleException>().WithMessage("*not found*");
    }

    [Fact]
    public void Process_NullEffectRegistry_Throws()
    {
        var state = TestFactory.MakeGameState(turn: 2);
        state.Player1Field.Frontend[0] = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "r_1");

        var req = new ActivateEffectRequest { InstanceID = "r_1" };
        var act = () => ActivateEffectProcessor.Process(state, _game, 1, req, _cc, effects: null);

        act.Should().Throw<GameRuleException>().WithMessage("*not initialized*");
    }

    // ─── Resource effect with target ────────────────────────────

    [Fact]
    public void Process_WithTargetOnOwnField_PassesTargetToHandler()
    {
        DeployedResource? capturedTarget = null;
        var reg = new EffectRegistry();
        reg.Register("SH-0001", TriggerType.Activate, ctx =>
        {
            capturedTarget = ctx.Target;
            return new EffectResult();
        });

        var state = TestFactory.MakeGameState(turn: 2);
        var source = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "r_1");
        var target = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "r_2");
        state.Player1Field.Frontend[0] = source;
        state.Player1Field.Frontend[1] = target;

        var req = new ActivateEffectRequest { InstanceID = "r_1", TargetInstanceID = "r_2" };
        ActivateEffectProcessor.Process(state, _game, 1, req, _cc, reg);

        capturedTarget.Should().NotBeNull();
        capturedTarget!.InstanceID.Should().Be("r_2");
    }

    [Fact]
    public void Process_WithTargetOnOpponentField_PassesTargetToHandler()
    {
        DeployedResource? capturedTarget = null;
        var reg = new EffectRegistry();
        reg.Register("SH-0001", TriggerType.Activate, ctx =>
        {
            capturedTarget = ctx.Target;
            return new EffectResult();
        });

        var state = TestFactory.MakeGameState(turn: 2);
        var source = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "r_1");
        var oppTarget = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "opp_r");
        state.Player1Field.Frontend[0] = source;
        state.Player2Field.Frontend[0] = oppTarget;

        var req = new ActivateEffectRequest { InstanceID = "r_1", TargetInstanceID = "opp_r" };
        ActivateEffectProcessor.Process(state, _game, 1, req, _cc, reg);

        capturedTarget!.InstanceID.Should().Be("opp_r");
    }

    // ─── Support effect ─────────────────────────────────────────

    [Fact]
    public void Process_SupportEffect_ExecutesHandlerAndSetsFlag()
    {
        bool handlerCalled = false;
        var reg = new EffectRegistry();
        reg.Register("TEST-0200", TriggerType.Activate, ctx =>
        {
            handlerCalled = true;
            return new EffectResult();
        });

        var state = TestFactory.MakeGameState(turn: 2);
        var support = new DeployedSupport
        {
            InstanceID = "sup_1",
            CardID = "TEST-0200",
            FaceUp = true,
        };
        state.Player1Field.Support[0] = support;

        var req = new ActivateEffectRequest { InstanceID = "sup_1" };
        ActivateEffectProcessor.Process(state, _game, 1, req, _cc, reg);

        handlerCalled.Should().BeTrue();
        support.EffectUsedThisTurn.Should().BeTrue();
    }

    [Fact]
    public void Process_SupportNoActivateEffect_Throws()
    {
        var reg = new EffectRegistry(); // 200 not registered

        var state = TestFactory.MakeGameState(turn: 2);
        state.Player1Field.Support[0] = new DeployedSupport
        {
            InstanceID = "sup_1",
            CardID = "TEST-0200",
            FaceUp = true,
        };

        var req = new ActivateEffectRequest { InstanceID = "sup_1" };
        var act = () => ActivateEffectProcessor.Process(state, _game, 1, req, _cc, reg);

        act.Should().Throw<GameRuleException>().WithMessage("*no activate effect*");
    }

    [Fact]
    public void Process_SupportEffect_GeneratesActivateEvent()
    {
        var reg = new EffectRegistry();
        reg.Register("TEST-0200", TriggerType.Activate, _ => new EffectResult());

        var state = TestFactory.MakeGameState(turn: 2);
        state.Player1Field.Support[0] = new DeployedSupport
        {
            InstanceID = "sup_1",
            CardID = "TEST-0200",
            FaceUp = true,
        };

        var req = new ActivateEffectRequest { InstanceID = "sup_1" };
        var result = ActivateEffectProcessor.Process(state, _game, 1, req, _cc, reg);

        result.Events.Should().ContainSingle(e => e.EventType == WireActionTypes.ActivateEffect);
    }
}
