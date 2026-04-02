using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Effects.Ops;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Effects;

public class BuffTypeTests
{
    private readonly TestCardCache _cc = new();
    private readonly Game _game = TestFactory.MakeGame();

    public BuffTypeTests()
    {
        _cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", tp: 600, av: 1400, slaPenalty: 400));
        _cc.Add(TestFactory.ComputeCard(cardId: "TST-ATK", tp: 600, av: 1400, slaPenalty: 400, name: "Attacker"));
    }

    // ─── Helper: build OpContext with a fixed target list ─────────────

    private OpContext MakeOpContext(GameState state, long playerNum, DeployedResource? source = null, DeployedResource? target = null)
    {
        var ctx = new EffectContext
        {
            State = state,
            Game = _game,
            PlayerNum = playerNum,
            Source = source,
            Target = target,
            CardCache = _cc,
        };
        return new OpContext(ctx);
    }

    // ─── incident_immune ─────────────────────────────────────────────

    [Fact]
    public void IncidentDamageOp_SkipsResource_WithIncidentImmune()
    {
        var state = TestFactory.MakeGameState();
        var resource = TestFactory.MakeResource(instanceId: "r1", damage: 0);
        resource.TemporaryEffects.Add(new TemporaryEffect
        {
            EffectType = "incident_immune",
            Duration = "until_end_of_turn",
        });
        state.Player1Field.Frontend[0] = resource;

        var selector = new FixedSelector([resource]);
        var op = new IncidentDamageOp(selector, new StaticAmount(500));
        var opCtx = MakeOpContext(state, playerNum: 1);

        op.Execute(opCtx);

        resource.Damage.Should().Be(0, "incident_immune should skip the resource entirely");
    }

    // ─── incident_reduction ──────────────────────────────────────────

    [Fact]
    public void IncidentDamageOp_ReducesDamage_ByIncidentReduction()
    {
        var state = TestFactory.MakeGameState();
        var resource = TestFactory.MakeResource(instanceId: "r1", damage: 0);
        resource.TemporaryEffects.Add(new TemporaryEffect
        {
            EffectType = "incident_reduction",
            Value = 200,
            Duration = "until_end_of_turn",
        });
        state.Player1Field.Frontend[0] = resource;

        var selector = new FixedSelector([resource]);
        var op = new IncidentDamageOp(selector, new StaticAmount(500));
        var opCtx = MakeOpContext(state, playerNum: 1);

        op.Execute(opCtx);

        resource.Damage.Should().Be(300, "500 - 200 reduction = 300");
    }

    [Fact]
    public void IncidentDamageOp_ReducesToZero_WhenReductionExceedsDamage()
    {
        var state = TestFactory.MakeGameState();
        var resource = TestFactory.MakeResource(instanceId: "r1", damage: 0);
        resource.TemporaryEffects.Add(new TemporaryEffect
        {
            EffectType = "incident_reduction",
            Value = 800,
            Duration = "until_end_of_turn",
        });
        state.Player1Field.Frontend[0] = resource;

        var selector = new FixedSelector([resource]);
        var op = new IncidentDamageOp(selector, new StaticAmount(500));
        var opCtx = MakeOpContext(state, playerNum: 1);

        op.Execute(opCtx);

        resource.Damage.Should().Be(0, "reduction exceeds damage so effective damage is clamped to 0");
    }

    // ─── incident_halve ──────────────────────────────────────────────

    [Fact]
    public void IncidentDamageOp_HalvesDamage_WithIncidentHalve()
    {
        var state = TestFactory.MakeGameState();
        var resource = TestFactory.MakeResource(instanceId: "r1", damage: 0);
        resource.TemporaryEffects.Add(new TemporaryEffect
        {
            EffectType = "incident_halve",
            Duration = "until_end_of_turn",
        });
        state.Player1Field.Frontend[0] = resource;

        var selector = new FixedSelector([resource]);
        var op = new IncidentDamageOp(selector, new StaticAmount(500));
        var opCtx = MakeOpContext(state, playerNum: 1);

        op.Execute(opCtx);

        resource.Damage.Should().Be(250, "500 / 2 = 250");
    }

    [Fact]
    public void IncidentDamageOp_ReductionThenHalve_AppliedInOrder()
    {
        var state = TestFactory.MakeGameState();
        var resource = TestFactory.MakeResource(instanceId: "r1", damage: 0);
        resource.TemporaryEffects.Add(new TemporaryEffect
        {
            EffectType = "incident_reduction",
            Value = 100,
            Duration = "until_end_of_turn",
        });
        resource.TemporaryEffects.Add(new TemporaryEffect
        {
            EffectType = "incident_halve",
            Duration = "until_end_of_turn",
        });
        state.Player1Field.Frontend[0] = resource;

        var selector = new FixedSelector([resource]);
        var op = new IncidentDamageOp(selector, new StaticAmount(500));
        var opCtx = MakeOpContext(state, playerNum: 1);

        op.Execute(opCtx);

        resource.Damage.Should().Be(200, "(500 - 100) / 2 = 200");
    }

    // ─── attack_damage_reduction ─────────────────────────────────────

    [Fact]
    public void AttackDamage_ReducedByAttackDamageReduction()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle);

        var attacker = TestFactory.MakeResource(
            cardId: "TST-ATK", instanceId: "atk_1", faceUp: true, maxTP: 600, currentTP: 600);
        state.Player1Field.Frontend[0] = attacker;

        var defender = TestFactory.MakeResource(
            cardId: "TST-0001", instanceId: "def_1", faceUp: true);
        defender.TemporaryEffects.Add(new TemporaryEffect
        {
            EffectType = "attack_damage_reduction",
            Value = 200,
            Duration = "until_end_of_turn",
        });
        state.Player2Field.Frontend[0] = defender;

        var req = new AttackRequest { AttackerInstanceID = "atk_1", TargetInstanceID = "def_1" };
        AttackProcessor.Process(state, _game, 1, req, _cc, null);

        defender.Damage.Should().Be(400, "600 TP - 200 reduction = 400 damage");
    }

    [Fact]
    public void AttackDamage_ClampedToZero_WhenReductionExceedsTP()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle);

        var attacker = TestFactory.MakeResource(
            cardId: "TST-ATK", instanceId: "atk_1", faceUp: true, maxTP: 600, currentTP: 600);
        state.Player1Field.Frontend[0] = attacker;

        var defender = TestFactory.MakeResource(
            cardId: "TST-0001", instanceId: "def_1", faceUp: true);
        defender.TemporaryEffects.Add(new TemporaryEffect
        {
            EffectType = "attack_damage_reduction",
            Value = 1000,
            Duration = "until_end_of_turn",
        });
        state.Player2Field.Frontend[0] = defender;

        var req = new AttackRequest { AttackerInstanceID = "atk_1", TargetInstanceID = "def_1" };
        AttackProcessor.Process(state, _game, 1, req, _cc, null);

        defender.Damage.Should().Be(0, "reduction exceeds TP so damage is clamped to 0");
    }

    // ─── sla_penalty_reduction ───────────────────────────────────────

    [Fact]
    public void SLAPenalty_ReducedBySLAPenaltyReduction()
    {
        var state = TestFactory.MakeGameState(p1Budget: 5000);

        var resource = TestFactory.MakeResource(
            cardId: "TST-0001", instanceId: "r1", faceUp: true);
        resource.TemporaryEffects.Add(new TemporaryEffect
        {
            EffectType = "sla_penalty_reduction",
            Value = 100,
            Duration = "until_end_of_turn",
        });
        state.Player1Field.Frontend[0] = resource;

        ResourceHelpers.DestroyResource(state, 1, state.Player1Field, resource, _cc);

        // Card SLAPenalty=400, reduction=100 → effective penalty=300
        state.Player1Budget.Should().Be(4700, "5000 - (400 - 100) = 4700");
    }

    [Fact]
    public void SLAPenalty_ClampedToZero_WhenReductionExceedsPenalty()
    {
        var state = TestFactory.MakeGameState(p1Budget: 5000);

        var resource = TestFactory.MakeResource(
            cardId: "TST-0001", instanceId: "r1", faceUp: true);
        resource.TemporaryEffects.Add(new TemporaryEffect
        {
            EffectType = "sla_penalty_reduction",
            Value = 500,
            Duration = "until_end_of_turn",
        });
        state.Player1Field.Frontend[0] = resource;

        ResourceHelpers.DestroyResource(state, 1, state.Player1Field, resource, _cc);

        state.Player1Budget.Should().Be(5000, "reduction exceeds penalty so no budget loss");
    }

    /// <summary>
    /// Simple ISelector that returns a fixed list of resources.
    /// </summary>
    private class FixedSelector(List<DeployedResource> targets) : ISelector
    {
        public List<DeployedResource> Select(OpContext ctx) => targets;
    }
}
