using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Tests.Engine;

public class AttackProcessorTests
{
    private readonly TestCardCache _cc = new();
    private readonly Game _game = TestFactory.MakeGame();

    public AttackProcessorTests()
    {
        // Compute card: TP=600, AV=1400, slaPenalty=400
        _cc.Add(TestFactory.ComputeCard(cardNo: 1, tp: 600, av: 1400, slaPenalty: 400));
        // High-TP attacker: TP=1500
        _cc.Add(TestFactory.ComputeCard(cardNo: 2, tp: 1500, av: 1400, slaPenalty: 400, name: "StrongCompute"));
        // ObjectStorage card (data type, cannot attack)
        _cc.Add(TestFactory.DataCard(cardNo: 101, cardType: CardTypes.ObjectStorage, name: "TestObjStorage"));
        // Elastic compute card
        _cc.Add(TestFactory.ElasticContainerCard(cardNo: 10));
    }

    private static AttackRequest MakeReq(string attackerId, string targetId) =>
        new() { AttackerInstanceID = attackerId, TargetInstanceID = targetId };

    // ─── 1. Basic attack deals damage ────────────────────────

    [Fact]
    public void Process_BasicAttack_DealsDamageToDefender()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle);

        var attacker = TestFactory.MakeResource(cardId: 1, instanceId: "atk_1", faceUp: true);
        state.Player1Field.Frontend[0] = attacker;

        var defender = TestFactory.MakeResource(cardId: 1, instanceId: "def_1", faceUp: true);
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

        var attacker = TestFactory.MakeResource(cardId: 2, instanceId: "atk_1", faceUp: true, maxTP: 1500, currentTP: 1500);
        state.Player1Field.Frontend[0] = attacker;

        var defender = TestFactory.MakeResource(cardId: 1, instanceId: "def_1", faceUp: true);
        state.Player2Field.Frontend[0] = defender;

        long budgetBefore = state.Player2Budget;

        var result = AttackProcessor.Process(
            state, _game, 1, MakeReq("atk_1", "def_1"), _cc, null);

        // Defender should be removed from field
        state.Player2Field.Frontend[0].Should().BeNull();

        // SLA penalty event data
        var attackEvent = result.Events.First(e => e.EventType == WireActionTypes.Attack);
        attackEvent.EventData.Should().ContainKey("slaPenalty");

        // Budget decreased by SLA penalty
        state.Player2Budget.Should().Be(budgetBefore - 400);
    }

    // ─── 3. Attacker not on frontend → throws ──────────────

    [Fact]
    public void Process_AttackerNotOnFrontend_Throws()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle);

        var attacker = TestFactory.MakeResource(cardId: 1, instanceId: "atk_1", faceUp: true);
        state.Player1Field.Backend[0] = attacker;

        var defender = TestFactory.MakeResource(cardId: 1, instanceId: "def_1", faceUp: true);
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

        var attacker = TestFactory.MakeResource(cardId: 101, instanceId: "atk_1", faceUp: true);
        state.Player1Field.Frontend[0] = attacker;

        var defender = TestFactory.MakeResource(cardId: 1, instanceId: "def_1", faceUp: true);
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

        var attacker = TestFactory.MakeResource(cardId: 1, instanceId: "atk_1", faceUp: true);
        attacker.HasAttacked = true;
        state.Player1Field.Frontend[0] = attacker;

        var defender = TestFactory.MakeResource(cardId: 1, instanceId: "def_1", faceUp: true);
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

        var attacker = TestFactory.MakeResource(cardId: 1, instanceId: "atk_1", faceUp: true);
        attacker.TemporaryEffects.Add(new TemporaryEffect
        {
            EffectType = EffectTypes.CannotOperate,
            Value = 1,
            Duration = "this_turn",
            SourceID = "test",
        });
        state.Player1Field.Frontend[0] = attacker;

        var defender = TestFactory.MakeResource(cardId: 1, instanceId: "def_1", faceUp: true);
        state.Player2Field.Frontend[0] = defender;

        var act = () => AttackProcessor.Process(
            state, _game, 1, MakeReq("atk_1", "def_1"), _cc, null);

        act.Should().Throw<GameRuleException>().WithMessage("*cannot operate*");
    }

    // ─── 7-8. Attacker or defender face-down → throws ──────

    [Theory]
    [InlineData(false, true)]   // attacker face-down
    [InlineData(true,  false)]  // defender face-down
    public void Process_FaceDown_Throws(bool atkFaceUp, bool defFaceUp)
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle);

        var attacker = TestFactory.MakeResource(cardId: 1, instanceId: "atk_1", faceUp: atkFaceUp, deployLeft: atkFaceUp ? 0 : 1);
        state.Player1Field.Frontend[0] = attacker;

        var defender = TestFactory.MakeResource(cardId: 1, instanceId: "def_1", faceUp: defFaceUp, deployLeft: defFaceUp ? 0 : 1);
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

        var attacker = TestFactory.MakeResource(cardId: 1, instanceId: "atk_1", faceUp: true);
        state.Player1Field.Frontend[0] = attacker;

        // P2 has both frontend and backend resources
        var frontRes = TestFactory.MakeResource(cardId: 1, instanceId: "front_1", faceUp: true);
        state.Player2Field.Frontend[0] = frontRes;

        var backRes = TestFactory.MakeResource(cardId: 1, instanceId: "back_1", faceUp: true);
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

        var attacker = TestFactory.MakeResource(cardId: 1, instanceId: "atk_1", faceUp: true);
        state.Player1Field.Frontend[0] = attacker;

        // P2 has only backend, no frontend
        var backRes = TestFactory.MakeResource(cardId: 1, instanceId: "back_1", faceUp: true);
        state.Player2Field.Backend[0] = backRes;

        var result = AttackProcessor.Process(
            state, _game, 1, MakeReq("atk_1", "back_1"), _cc, null);

        backRes.Damage.Should().Be(600);
        result.Events.Should().Contain(e => e.EventType == WireActionTypes.Attack);
    }

    // ─── 10. Elastic defender gains bonus ──────────────────

    [Fact]
    public void Process_ElasticDefender_GainsBonus()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle);

        // Weak attacker so defender survives
        var attacker = TestFactory.MakeResource(cardId: 1, instanceId: "atk_1", faceUp: true);
        state.Player1Field.Frontend[0] = attacker;

        // Elastic container as defender (AV=1200, will survive 600 damage)
        var defender = TestFactory.MakeResource(
            cardId: 10, instanceId: "def_1", faceUp: true,
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

        var attacker = TestFactory.MakeResource(cardId: 1, instanceId: "atk_1", faceUp: true);
        state.Player1Field.Frontend[0] = attacker;

        var defender = TestFactory.MakeResource(cardId: 1, instanceId: "def_1", faceUp: true);
        state.Player2Field.Frontend[0] = defender;

        var result = AttackProcessor.Process(
            state, _game, 1, MakeReq("atk_1", "def_1"), _cc, null);

        var attackEvent = result.Events.First(e => e.EventType == WireActionTypes.Attack);
        attackEvent.EventData["attackerId"].Should().Be("atk_1");
        attackEvent.EventData["targetId"].Should().Be("def_1");
        attackEvent.EventData["damage"].Should().Be(600L);
        attackEvent.EventData["destroyed"].Should().Be(false);
    }
}
