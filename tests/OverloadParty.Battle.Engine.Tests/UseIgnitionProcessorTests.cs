using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Engine;

public class UseIgnitionProcessorTests
{
    /// <summary>Shared setup for UseIgnitionProcessor tests (card cache with a compute and platform card, and a game).</summary>
    public abstract class Base
    {
        protected readonly TestCardCache _cc = new();
        protected readonly Game _game = TestFactory.MakeGame();

        protected Base()
        {
            _cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", deployTurns: 0));
            _cc.Add(TestFactory.PlatformCard(cardId: "TEST-0200"));
        }
    }

    /// <summary>Tests for igniting a resource's effect.</summary>
    public class ResourceEffect : Base
    {
        [Fact]
        public void Process_ResourceEffect_ExecutesHandlerAndSetsFlag()
        {
            bool handlerCalled = false;
            var reg = new EffectRegistry();
            reg.Register("TST-0001", TriggerType.Ignition, ctx =>
            {
                handlerCalled = true;
                return new EffectResult();
            });

            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
            var resource = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "r_1", faceUp: true);
            state.Player1Field.Frontend[0] = resource;

            var req = new UseIgnitionRequest { InstanceID = "r_1" };
            UseIgnitionProcessor.Process(state, _game, 1, req, _cc, reg);

            handlerCalled.Should().BeTrue();
            resource.EffectUsedThisTurn.Should().BeTrue();
        }

        [Fact]
        public void Process_ResourceEffect_GeneratesUseIgnitionEvent()
        {
            var reg = new EffectRegistry();
            reg.Register("TST-0001", TriggerType.Ignition, _ => new EffectResult());

            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "r_1");

            var req = new UseIgnitionRequest { InstanceID = "r_1" };
            var result = UseIgnitionProcessor.Process(state, _game, 1, req, _cc, reg);

            var evt = result.Events.Should().ContainSingle(e => e.EventType == ActionTypes.UseIgnition).Subject;
            var data = evt.EventData.Should().BeOfType<UseIgnitionEventData>().Subject;
            data.CardId.Should().Be("TST-0001");
            data.SourceId.Should().Be("r_1");
        }

        [Fact]
        public void Process_ResourceEffect_EventCarriesTargetId()
        {
            var reg = new EffectRegistry();
            reg.Register("TST-0001", TriggerType.Ignition, _ => new EffectResult());

            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "r_1");
            state.Player2Field.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "opp_1");

            var req = new UseIgnitionRequest { InstanceID = "r_1", TargetInstanceID = "opp_1" };
            var result = UseIgnitionProcessor.Process(state, _game, 1, req, _cc, reg);

            var evt = result.Events.First(e => e.EventType == ActionTypes.UseIgnition);
            evt.EventData.Should().BeOfType<UseIgnitionEventData>()
                .Which.TargetId.Should().Be("opp_1");
        }

        [Fact]
        public void Process_NoUseIgnition_Throws()
        {
            var reg = new EffectRegistry(); // nothing registered

            var state = TestFactory.MakeGameState(turn: 2);
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "r_1");

            var req = new UseIgnitionRequest { InstanceID = "r_1" };
            var act = () => UseIgnitionProcessor.Process(state, _game, 1, req, _cc, reg);

            act.Should().Throw<GameRuleException>().WithMessage("*no ignition effect*");
        }

        [Fact]
        public void Process_DormantResource_Throws()
        {
            var reg = new EffectRegistry();
            reg.Register("TST-0001", TriggerType.Ignition, _ => new EffectResult());

            var state = TestFactory.MakeGameState(turn: 2);
            var resource = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "r_1", faceUp: true);
            resource.TemporaryEffects.Add(new TemporaryEffect { EffectType = BuffTypes.Dormant });
            state.Player1Field.Frontend[0] = resource;

            var req = new UseIgnitionRequest { InstanceID = "r_1" };
            var act = () => UseIgnitionProcessor.Process(state, _game, 1, req, _cc, reg);

            act.Should().Throw<GameRuleException>().WithMessage("*dormant*");
        }

        [Fact]
        public void Process_EffectAlreadyUsedThisTurn_Throws()
        {
            var reg = new EffectRegistry();
            reg.Register("TST-0001", TriggerType.Ignition, _ => new EffectResult());

            var state = TestFactory.MakeGameState(turn: 2);
            var resource = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "r_1");
            resource.EffectUsedThisTurn = true;
            state.Player1Field.Frontend[0] = resource;

            var req = new UseIgnitionRequest { InstanceID = "r_1" };
            var act = () => UseIgnitionProcessor.Process(state, _game, 1, req, _cc, reg);

            act.Should().Throw<GameRuleException>().WithMessage("*already used*");
        }

        [Fact]
        public void Process_ResourceNotFound_Throws()
        {
            var reg = new EffectRegistry();
            reg.Register("TST-0001", TriggerType.Ignition, _ => new EffectResult());

            var state = TestFactory.MakeGameState(turn: 2);
            // Nothing on the field

            var req = new UseIgnitionRequest { InstanceID = "nonexistent" };
            var act = () => UseIgnitionProcessor.Process(state, _game, 1, req, _cc, reg);

            act.Should().Throw<GameRuleException>().WithMessage("*not found*");
        }
    }

    /// <summary>Tests for resource effects that receive a target via the request.</summary>
    public class ResourceEffectWithTarget : Base
    {
        [Fact]
        public void Process_WithTargetOnOwnField_PassesTargetToHandler()
        {
            DeployedResource? capturedTarget = null;
            var reg = new EffectRegistry();
            reg.Register("TST-0001", TriggerType.Ignition, ctx =>
            {
                capturedTarget = ctx.Target;
                return new EffectResult();
            });

            var state = TestFactory.MakeGameState(turn: 2);
            var source = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "r_1");
            var target = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "r_2");
            state.Player1Field.Frontend[0] = source;
            state.Player1Field.Frontend[1] = target;

            var req = new UseIgnitionRequest { InstanceID = "r_1", TargetInstanceID = "r_2" };
            UseIgnitionProcessor.Process(state, _game, 1, req, _cc, reg);

            capturedTarget.Should().NotBeNull();
            capturedTarget!.InstanceID.Should().Be("r_2");
        }

        [Fact]
        public void Process_WithTargetOnOpponentField_PassesTargetToHandler()
        {
            DeployedResource? capturedTarget = null;
            var reg = new EffectRegistry();
            reg.Register("TST-0001", TriggerType.Ignition, ctx =>
            {
                capturedTarget = ctx.Target;
                return new EffectResult();
            });

            var state = TestFactory.MakeGameState(turn: 2);
            var source = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "r_1");
            var oppTarget = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "opp_r");
            state.Player1Field.Frontend[0] = source;
            state.Player2Field.Frontend[0] = oppTarget;

            var req = new UseIgnitionRequest { InstanceID = "r_1", TargetInstanceID = "opp_r" };
            UseIgnitionProcessor.Process(state, _game, 1, req, _cc, reg);

            capturedTarget!.InstanceID.Should().Be("opp_r");
        }
    }

    /// <summary>Tests for igniting a support card's effect.</summary>
    public class SupportEffect : Base
    {
        [Fact]
        public void Process_SupportEffect_ExecutesHandlerAndSetsFlag()
        {
            bool handlerCalled = false;
            var reg = new EffectRegistry();
            reg.Register("TEST-0200", TriggerType.Ignition, ctx =>
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

            var req = new UseIgnitionRequest { InstanceID = "sup_1" };
            UseIgnitionProcessor.Process(state, _game, 1, req, _cc, reg);

            handlerCalled.Should().BeTrue();
            support.EffectUsedThisTurn.Should().BeTrue();
        }

        [Fact]
        public void Process_SupportNoUseIgnition_Throws()
        {
            var reg = new EffectRegistry(); // 200 not registered

            var state = TestFactory.MakeGameState(turn: 2);
            state.Player1Field.Support[0] = new DeployedSupport
            {
                InstanceID = "sup_1",
                CardID = "TEST-0200",
                FaceUp = true,
            };

            var req = new UseIgnitionRequest { InstanceID = "sup_1" };
            var act = () => UseIgnitionProcessor.Process(state, _game, 1, req, _cc, reg);

            act.Should().Throw<GameRuleException>().WithMessage("*no ignition effect*");
        }

        [Fact]
        public void Process_SupportEffect_GeneratesUseIgnitionEvent()
        {
            var reg = new EffectRegistry();
            reg.Register("TEST-0200", TriggerType.Ignition, _ => new EffectResult());

            var state = TestFactory.MakeGameState(turn: 2);
            state.Player1Field.Support[0] = new DeployedSupport
            {
                InstanceID = "sup_1",
                CardID = "TEST-0200",
                FaceUp = true,
            };

            var req = new UseIgnitionRequest { InstanceID = "sup_1" };
            var result = UseIgnitionProcessor.Process(state, _game, 1, req, _cc, reg);

            var evt = result.Events.Should().ContainSingle(e => e.EventType == ActionTypes.UseIgnition).Subject;
            var data = evt.EventData.Should().BeOfType<UseIgnitionEventData>().Subject;
            data.CardId.Should().Be("TEST-0200");
            data.SourceId.Should().Be("sup_1");
        }
    }
}
