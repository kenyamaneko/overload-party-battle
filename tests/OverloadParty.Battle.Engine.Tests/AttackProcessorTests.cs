using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Engine;

public class AttackProcessorTests
{
    private readonly TestCardCache _cc = new();
    private readonly Game _game = TestFactory.MakeGame();

    public AttackProcessorTests()
    {
        // Compute card: TP=600, AV=1400, slaPenalty=400
        _cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", tp: 600, av: 1400, slaPenalty: 400));
        // High-TP attacker: TP=1500
        _cc.Add(TestFactory.ComputeCard(cardId: "TEST-0002", tp: 1500, av: 1400, slaPenalty: 400, name: "StrongCompute"));
        // ObjectStorage card (data type, cannot attack)
        _cc.Add(TestFactory.DataCard(cardId: "TST-0002", subtype: "ObjectStorage", name: "TestObjStorage"));
        // Elastic compute card
        _cc.Add(TestFactory.ElasticContainerCard(cardId: "TST-0003"));
        // Platform card for reactive test
        _cc.Add(TestFactory.PlatformCard(cardId: "TEST-0200", name: "ReactivePlatform"));
    }

    private static AttackRequest MakeReq(string attackerId, string targetId) =>
        new() { AttackerInstanceID = attackerId, TargetInstanceID = targetId };

    // ─── 1. Basic attack deals damage ────────────────────────

    [Fact]
    public void Process_BasicAttack_DealsDamageToDefender()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle);

        var attacker = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "atk_1", faceUp: true);
        state.Player1Field.Frontend[0] = attacker;

        var defender = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "def_1", faceUp: true);
        state.Player2Field.Frontend[0] = defender;

        var result = AttackProcessor.Process(
            state, _game, 1, MakeReq("atk_1", "def_1"), _cc, null);

        defender.Damage.Should().Be(600);
        attacker.HasAttacked.Should().BeTrue();
    }

    // ─── 2. Attack destroys defender + SLA penalty ──────────

    [Fact]
    public void Process_AttackDestroysDefender_SlaPenalty()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle);

        var attacker = TestFactory.MakeResource(cardId: "TEST-0002", instanceId: "atk_1", faceUp: true, maxTP: 1500, currentTP: 1500);
        state.Player1Field.Frontend[0] = attacker;

        var defender = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "def_1", faceUp: true);
        state.Player2Field.Frontend[0] = defender;

        long budgetBefore = state.Player2Budget;

        var result = AttackProcessor.Process(
            state, _game, 1, MakeReq("atk_1", "def_1"), _cc, null);

        // Defender should be removed from field
        state.Player2Field.Frontend[0].Should().BeNull();

        // SLA penalty event data
        var attackEvent = result.Events.First(e => e.EventType == ActionTypes.Attack);
        attackEvent.EventData.Should().BeOfType<AttackEventData>()
            .Which.SlaPenalty.Should().NotBeNull();

        // Budget decreased by SLA penalty
        state.Player2Budget.Should().Be(budgetBefore - 400);
    }

    // ─── 3. Attacker not on frontend → throws ──────────────

    [Fact]
    public void Process_AttackerNotOnFrontend_Throws()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle);

        var attacker = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "atk_1", faceUp: true);
        state.Player1Field.Backend[0] = attacker;

        var defender = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "def_1", faceUp: true);
        state.Player2Field.Frontend[0] = defender;

        var act = () => AttackProcessor.Process(
            state, _game, 1, MakeReq("atk_1", "def_1"), _cc, null);

        act.Should().Throw<GameRuleException>().WithMessage("*frontend*");
    }

    // ─── 4. Attacker not compute type → throws ─────────────

    [Fact]
    public void Process_AttackerNotComputeType_Throws()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle);

        var attacker = TestFactory.MakeResource(cardId: "TST-0002", instanceId: "atk_1", faceUp: true);
        state.Player1Field.Frontend[0] = attacker;

        var defender = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "def_1", faceUp: true);
        state.Player2Field.Frontend[0] = defender;

        var act = () => AttackProcessor.Process(
            state, _game, 1, MakeReq("atk_1", "def_1"), _cc, null);

        act.Should().Throw<GameRuleException>().WithMessage("*compute*");
    }

    // ─── 5. Attacker already attacked → throws ─────────────

    [Fact]
    public void Process_AttackerAlreadyAttacked_Throws()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle);

        var attacker = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "atk_1", faceUp: true);
        attacker.HasAttacked = true;
        state.Player1Field.Frontend[0] = attacker;

        var defender = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "def_1", faceUp: true);
        state.Player2Field.Frontend[0] = defender;

        var act = () => AttackProcessor.Process(
            state, _game, 1, MakeReq("atk_1", "def_1"), _cc, null);

        act.Should().Throw<GameRuleException>().WithMessage("*already attacked*");
    }

    // ─── 6. Attacker has CannotOperate effect → throws ─────

    [Fact]
    public void Process_AttackerCannotOperate_Throws()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle);

        var attacker = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "atk_1", faceUp: true);
        attacker.TemporaryEffects.Add(new TemporaryEffect
        {
            EffectType = EffectTypes.CannotOperate,
            Value = 1,
            Duration = "this_turn",
            SourceID = "test",
        });
        state.Player1Field.Frontend[0] = attacker;

        var defender = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "def_1", faceUp: true);
        state.Player2Field.Frontend[0] = defender;

        var act = () => AttackProcessor.Process(
            state, _game, 1, MakeReq("atk_1", "def_1"), _cc, null);

        act.Should().Throw<GameRuleException>().WithMessage("*cannot operate*");
    }

    // ─── 7-8. Attacker or defender face-down → throws ──────

    [Theory]
    [InlineData(false, true)]   // attacker face-down
    [InlineData(true, false)]  // defender face-down
    public void Process_FaceDown_Throws(bool atkFaceUp, bool defFaceUp)
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle);

        var attacker = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "atk_1", faceUp: atkFaceUp, deployLeft: atkFaceUp ? 0 : 1);
        state.Player1Field.Frontend[0] = attacker;

        var defender = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "def_1", faceUp: defFaceUp, deployLeft: defFaceUp ? 0 : 1);
        state.Player2Field.Frontend[0] = defender;

        var act = () => AttackProcessor.Process(
            state, _game, 1, MakeReq("atk_1", "def_1"), _cc, null);

        act.Should().Throw<GameRuleException>();
    }

    // ─── 8. Backend target with frontend present → throws ──

    [Fact]
    public void Process_BackendTargetWithFrontend_Throws()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle);

        var attacker = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "atk_1", faceUp: true);
        state.Player1Field.Frontend[0] = attacker;

        // P2 has both frontend and backend resources
        var frontRes = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "front_1", faceUp: true);
        state.Player2Field.Frontend[0] = frontRes;

        var backRes = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "back_1", faceUp: true);
        state.Player2Field.Backend[0] = backRes;

        var act = () => AttackProcessor.Process(
            state, _game, 1, MakeReq("atk_1", "back_1"), _cc, null);

        act.Should().Throw<GameRuleException>().WithMessage("*backend*frontend*");
    }

    // ─── 9. Backend target with no frontend → succeeds ─────

    [Fact]
    public void Process_BackendTargetNoFrontend_Succeeds()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle);

        var attacker = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "atk_1", faceUp: true);
        state.Player1Field.Frontend[0] = attacker;

        // P2 has only backend, no frontend
        var backRes = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "back_1", faceUp: true);
        state.Player2Field.Backend[0] = backRes;

        var result = AttackProcessor.Process(
            state, _game, 1, MakeReq("atk_1", "back_1"), _cc, null);

        backRes.Damage.Should().Be(600);
        result.Events.Should().Contain(e => e.EventType == ActionTypes.Attack);
    }

    // ─── 10. Elastic defender gains bonus ──────────────────

    [Fact]
    public void Process_ElasticDefender_GainsBonus()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle);

        // Weak attacker so defender survives
        var attacker = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "atk_1", faceUp: true);
        state.Player1Field.Frontend[0] = attacker;

        // Elastic container as defender (AV=1200, will survive 600 damage)
        var defender = TestFactory.MakeResource(
            cardId: "TST-0003", instanceId: "def_1", faceUp: true,
            maxAV: 1200, currentAV: 1200, maxTP: 500, currentTP: 500);
        state.Player2Field.Frontend[0] = defender;

        long bonusBefore = defender.ElasticBonus;

        AttackProcessor.Process(
            state, _game, 1, MakeReq("atk_1", "def_1"), _cc, null);

        defender.ElasticBonus.Should().BeGreaterThan(bonusBefore);
    }

    // ─── 11. Attack event contains correct data ────────────

    [Fact]
    public void Process_AttackEvent_ContainsCorrectData()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle);

        var attacker = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "atk_1", faceUp: true);
        state.Player1Field.Frontend[0] = attacker;

        var defender = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "def_1", faceUp: true);
        state.Player2Field.Frontend[0] = defender;

        var result = AttackProcessor.Process(
            state, _game, 1, MakeReq("atk_1", "def_1"), _cc, null);

        var attackEvent = result.Events.First(e => e.EventType == ActionTypes.Attack);
        var data = attackEvent.EventData.Should().BeOfType<AttackEventData>().Subject;
        data.AttackerId.Should().Be("atk_1");
        data.TargetId.Should().Be("def_1");
        data.Damage.Should().Be(600L);
        data.Destroyed.Should().Be(false);
    }

    // ─── 12. Attacker not found on field → throws ────────────

    [Fact]
    public void Process_AttackerNotFound_Throws()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle);

        var defender = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "def_1", faceUp: true);
        state.Player2Field.Frontend[0] = defender;

        var act = () => AttackProcessor.Process(
            state, _game, 1, MakeReq("nonexistent", "def_1"), _cc, null);

        act.Should().Throw<GameRuleException>().WithMessage("*not found*");
    }

    // ─── 13. Defender not found on field → throws ────────────

    [Fact]
    public void Process_DefenderNotFound_Throws()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle);

        var attacker = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "atk_1", faceUp: true);
        state.Player1Field.Frontend[0] = attacker;

        var act = () => AttackProcessor.Process(
            state, _game, 1, MakeReq("atk_1", "nonexistent"), _cc, null);

        act.Should().Throw<GameRuleException>().WithMessage("*not found*");
    }

    // ─── 14. Non-elastic defender does not gain elastic bonus ─

    [Fact]
    public void Process_NonElasticDefender_NoElasticBonus()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle);

        // Weak attacker so defender survives
        var attacker = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "atk_1", faceUp: true);
        state.Player1Field.Frontend[0] = attacker;

        // Non-elastic defender (AV=1400, survives 600 damage)
        var defender = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "def_1", faceUp: true);
        state.Player2Field.Frontend[0] = defender;

        AttackProcessor.Process(
            state, _game, 1, MakeReq("atk_1", "def_1"), _cc, null);

        defender.ElasticBonus.Should().Be(0);
    }

    // ─── 15. OnAttack effect fires when attacker has trigger ─

    [Fact]
    public void Process_OnAttackEffect_FiresAndAddsEvents()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle);

        var attacker = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "atk_1", faceUp: true);
        state.Player1Field.Frontend[0] = attacker;

        var defender = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "def_1", faceUp: true);
        state.Player2Field.Frontend[0] = defender;

        var effects = new TestEffectRegistry();
        bool handlerCalled = false;
        effects.Register("TST-0001", TriggerType.OnAttack, ctx =>
        {
            handlerCalled = true;
            return new EffectResult
            {
                Events = [new GameEvent { EventType = "on_attack_triggered", GameID = ctx.Game.GameID }]
            };
        });

        var result = AttackProcessor.Process(
            state, _game, 1, MakeReq("atk_1", "def_1"), _cc, effects);

        handlerCalled.Should().BeTrue();
        result.Events.Should().Contain(e => e.EventType == "on_attack_triggered");
    }

    // ─── 16. Reactive cancels attack ────────────────────────

    [Fact]
    public void Process_ReactiveCancelsAttack_DamageIsZero()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle);
        _cc.Add(TestFactory.ReactiveCard(cardId: "TEST-0400"));

        var attacker = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "atk_1", faceUp: true);
        state.Player1Field.Frontend[0] = attacker;

        var defender = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "def_1", faceUp: true);
        state.Player2Field.Frontend[0] = defender;

        // Opponent has a face-down Reactive watching for an attack declaration
        state.Player2Field.Support[0] = new DeployedSupport
        {
            InstanceID = "sup_1",
            CardID = "TEST-0400",
            FaceUp = false,
            DeployOrder = 1
        };

        var effects = new TestEffectRegistry();
        effects.Register("TEST-0400", TriggerType.OnAttackDeclared, ctx => new EffectResult
        {
            CancelAction = true,
            Events = [new GameEvent { EventType = "reactive_fired", GameID = ctx.Game.GameID }]
        });

        var result = AttackProcessor.Process(
            state, _game, 1, MakeReq("atk_1", "def_1"), _cc, effects);

        // Damage should not be applied
        defender.Damage.Should().Be(0);
        // Attacker still marked as attacked
        attacker.HasAttacked.Should().BeTrue();
        // Attack event should show cancelled=true
        var attackEvent = result.Events.First(e => e.EventType == ActionTypes.Attack);
        var data = attackEvent.EventData.Should().BeOfType<AttackEventData>().Subject;
        data.Cancelled.Should().Be(true);
        data.Damage.Should().Be(0L);
        // Reactive should be removed from support zone and sent to trash
        state.Player2Field.Support[0].Should().BeNull();
        state.Player2Trash.Should().Contain(c => c.CardID == "TEST-0400");
    }

    // ─── 17. OnDestroy trigger fires when defender is destroyed ─

    [Fact]
    public void Process_OnDestroyEffect_FiresWhenDefenderDestroyed()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle);

        var attacker = TestFactory.MakeResource(cardId: "TEST-0002", instanceId: "atk_1", faceUp: true, maxTP: 1500, currentTP: 1500);
        state.Player1Field.Frontend[0] = attacker;

        var defender = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "def_1", faceUp: true);
        state.Player2Field.Frontend[0] = defender;

        var effects = new TestEffectRegistry();
        bool destroyHandlerCalled = false;
        effects.Register("TST-0001", TriggerType.OnDestroy, ctx =>
        {
            destroyHandlerCalled = true;
            return new EffectResult
            {
                Events = [new GameEvent { EventType = "on_destroy_triggered", GameID = ctx.Game.GameID }]
            };
        });

        var result = AttackProcessor.Process(
            state, _game, 1, MakeReq("atk_1", "def_1"), _cc, effects);

        destroyHandlerCalled.Should().BeTrue();
        result.Events.Should().Contain(e => e.EventType == "on_destroy_triggered");
    }

    // ─── 18. Allied OnDestroy trigger fires for other resources ─

    [Fact]
    public void Process_AlliedOnDestroy_FiresForOtherResources()
    {
        // Card 3 = an allied resource with OnDestroy
        _cc.Add(TestFactory.ComputeCard(cardId: "TST-0004", tp: 400, av: 1000, name: "AllyCompute"));

        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle);

        var attacker = TestFactory.MakeResource(cardId: "TEST-0002", instanceId: "atk_1", faceUp: true, maxTP: 1500, currentTP: 1500);
        state.Player1Field.Frontend[0] = attacker;

        // Defender will be destroyed
        var defender = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "def_1", faceUp: true);
        state.Player2Field.Frontend[0] = defender;

        // Allied resource on opponent's field
        var ally = TestFactory.MakeResource(cardId: "TST-0004", instanceId: "ally_1", faceUp: true, maxAV: 1000, currentAV: 1000, maxTP: 400, currentTP: 400);
        ally.DeployOrder = 1;
        state.Player2Field.Frontend[1] = ally;

        var effects = new TestEffectRegistry();
        bool alliedHandlerCalled = false;
        effects.Register("TST-0004", TriggerType.OnDestroy, ctx =>
        {
            alliedHandlerCalled = true;
            return new EffectResult
            {
                Events = [new GameEvent { EventType = "allied_on_destroy", GameID = ctx.Game.GameID }]
            };
        });

        var result = AttackProcessor.Process(
            state, _game, 1, MakeReq("atk_1", "def_1"), _cc, effects);

        alliedHandlerCalled.Should().BeTrue();
        result.Events.Should().Contain(e => e.EventType == "allied_on_destroy");
    }

    // ─── 19. Elastic defender destroyed → no elastic bonus ───

    [Fact]
    public void Process_ElasticDefenderDestroyed_NoElasticBonus()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle);

        // Strong attacker that will destroy the elastic defender
        var attacker = TestFactory.MakeResource(cardId: "TEST-0002", instanceId: "atk_1", faceUp: true, maxTP: 1500, currentTP: 1500);
        state.Player1Field.Frontend[0] = attacker;

        // Elastic container with AV=1200, will be destroyed by 1500 damage
        var defender = TestFactory.MakeResource(
            cardId: "TST-0003", instanceId: "def_1", faceUp: true,
            maxAV: 1200, currentAV: 1200, maxTP: 500, currentTP: 500);
        state.Player2Field.Frontend[0] = defender;

        AttackProcessor.Process(
            state, _game, 1, MakeReq("atk_1", "def_1"), _cc, null);

        // Defender should be removed from field (destroyed)
        state.Player2Field.Frontend[0].Should().BeNull();
    }

    // ─── 20. Attack event contains slaPenalty=0 when not destroyed ─

    [Fact]
    public void Process_NotDestroyed_SlaPenaltyZero()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle);

        var attacker = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "atk_1", faceUp: true);
        state.Player1Field.Frontend[0] = attacker;

        var defender = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "def_1", faceUp: true);
        state.Player2Field.Frontend[0] = defender;

        var result = AttackProcessor.Process(
            state, _game, 1, MakeReq("atk_1", "def_1"), _cc, null);

        var attackEvent = result.Events.First(e => e.EventType == ActionTypes.Attack);
        attackEvent.EventData.Should().BeOfType<AttackEventData>()
            .Which.SlaPenalty.Should().Be(0L);
    }

    // ─── 21. Reactive with no effects registry → no cancel ───

    [Fact]
    public void Process_NullEffects_NoReactiveTriggered()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle);

        var attacker = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "atk_1", faceUp: true);
        state.Player1Field.Frontend[0] = attacker;

        var defender = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "def_1", faceUp: true);
        state.Player2Field.Frontend[0] = defender;

        // Opponent has support but effects are null
        state.Player2Field.Support[0] = new DeployedSupport
        {
            InstanceID = "sup_1",
            CardID = "TEST-0200",
            FaceUp = false,
            DeployOrder = 1
        };

        var result = AttackProcessor.Process(
            state, _game, 1, MakeReq("atk_1", "def_1"), _cc, null);

        // Attack should proceed normally
        defender.Damage.Should().Be(600);
    }

    // ─── 22. Player 2 attacks Player 1 ──────────────────────

    [Fact]
    public void Process_Player2Attacks_DealsDamageToPlayer1()
    {
        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Battle, activePlayer: 2);

        var attacker = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "atk_2", faceUp: true);
        state.Player2Field.Frontend[0] = attacker;

        var defender = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "def_1", faceUp: true);
        state.Player1Field.Frontend[0] = defender;

        var result = AttackProcessor.Process(
            state, _game, 2, MakeReq("atk_2", "def_1"), _cc, null);

        defender.Damage.Should().Be(600);
        attacker.HasAttacked.Should().BeTrue();
        result.Events.First(e => e.EventType == ActionTypes.Attack)
            .PlayerNum.Should().Be(2);
    }
}
