using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Tests.Engine;

public class MigrateProcessorTests
{
    private readonly TestCardCache _cc = new();
    private readonly Game _game = TestFactory.MakeGame();

    public MigrateProcessorTests()
    {
        // Source card: deployTurns=0
        _cc.Add(TestFactory.ComputeCard(cardNo: 1, tp: 600, deployTurns: 0, name: "SourceCompute"));
        // Target card: deployTurns=1 (>= source)
        _cc.Add(TestFactory.ComputeCard(cardNo: 2, tp: 800, deployTurns: 1, name: "TargetCompute"));
        // Card with fewer deploy turns than source (deployTurns=0, but we'll use cardNo 3 with deployTurns < cardNo 4)
        _cc.Add(TestFactory.ComputeCard(cardNo: 3, tp: 500, deployTurns: 0, name: "LowDeployCompute"));
        _cc.Add(TestFactory.ComputeCard(cardNo: 4, tp: 700, deployTurns: 2, name: "HighDeployCompute"));
    }

    private static MigrateRequest MakeReq(string sourceId, string targetId) =>
        new() { SourceInstanceID = sourceId, TargetInstanceID = targetId };

    // ─── 1. Initiates migration, sets fields ────────────────

    [Fact]
    public void Process_InitiatesMigration_SetsMigrationFields()
    {
        var state = TestFactory.MakeGameState(turn: 3);
        var source = TestFactory.MakeResource(cardId: 1, instanceId: "be_1", faceUp: true);
        var target = TestFactory.MakeResource(cardId: 2, instanceId: "be_2", faceUp: true);
        state.Player1Field.Backend[0] = source;
        state.Player1Field.Backend[1] = target;

        MigrateProcessor.Process(state, _game, 1, MakeReq("be_1", "be_2"), _cc);

        target.MigratingFrom.Should().Be("be_1");
        source.MigrationTarget.Should().Be("be_2");
        target.MigratingOnTurn.Should().Be(3);
    }

    // ─── 2-3. Face-down source or target → throws ───────────

    [Theory]
    [InlineData(false, true)]   // source face-down
    [InlineData(true,  false)]  // target face-down
    public void Process_FaceDown_Throws(bool sourceFaceUp, bool targetFaceUp)
    {
        var state = TestFactory.MakeGameState(turn: 3);
        var source = TestFactory.MakeResource(cardId: 1, instanceId: "be_1", faceUp: sourceFaceUp, deployLeft: sourceFaceUp ? 0 : 1);
        var target = TestFactory.MakeResource(cardId: 2, instanceId: "be_2", faceUp: targetFaceUp, deployLeft: targetFaceUp ? 0 : 1);
        state.Player1Field.Backend[0] = source;
        state.Player1Field.Backend[1] = target;

        var act = () => MigrateProcessor.Process(state, _game, 1, MakeReq("be_1", "be_2"), _cc);

        act.Should().Throw<GameRuleException>().WithMessage("*face-up*");
    }

    // ─── 4-7. Invalid migration state → throws ──────────────
    //
    //  source.MigrationTarget != null → source is already migrating (outbound)
    //  source.MigratingFrom   != null → source is already a destination (inbound)
    //  target.MigratingFrom   != null → target is already a destination (inbound)
    //  target.MigrationTarget != null → target is already migrating (outbound)

    [Theory]
    [InlineData("src_target", null,        null,       null,       "*already migrating*")]
    [InlineData(null,         "src_from",  null,       null,       "*migration destination*")]
    [InlineData(null,         null,        "tgt_from", null,       "*migration destination*")]
    [InlineData(null,         null,        null,       "tgt_target","*already migrating*")]
    public void Process_InvalidMigrationState_Throws(
        string? srcTarget, string? srcFrom, string? tgtFrom, string? tgtTarget, string msgPattern)
    {
        var state = TestFactory.MakeGameState(turn: 3);
        var source = TestFactory.MakeResource(cardId: 1, instanceId: "be_1", faceUp: true);
        source.MigrationTarget = srcTarget;
        source.MigratingFrom   = srcFrom;
        var target = TestFactory.MakeResource(cardId: 2, instanceId: "be_2", faceUp: true);
        target.MigratingFrom   = tgtFrom;
        target.MigrationTarget = tgtTarget;
        state.Player1Field.Backend[0] = source;
        state.Player1Field.Backend[1] = target;

        var act = () => MigrateProcessor.Process(state, _game, 1, MakeReq("be_1", "be_2"), _cc);

        act.Should().Throw<GameRuleException>().WithMessage(msgPattern);
    }

    // ─── 6. Target deploy turns < source → throws ───────────

    [Fact]
    public void Process_TargetDeployTurnsLessThanSource_Throws()
    {
        var state = TestFactory.MakeGameState(turn: 3);
        // Source has deployTurns=2 (cardNo=4), target has deployTurns=0 (cardNo=3)
        var source = TestFactory.MakeResource(cardId: 4, instanceId: "be_1", faceUp: true);
        var target = TestFactory.MakeResource(cardId: 3, instanceId: "be_2", faceUp: true);
        state.Player1Field.Backend[0] = source;
        state.Player1Field.Backend[1] = target;

        var act = () => MigrateProcessor.Process(
            state, _game, 1, MakeReq("be_1", "be_2"), _cc);

        act.Should().Throw<GameRuleException>().WithMessage("*deploy turns*");
    }

    // ─── 7. Generates migrate event ─────────────────────────

    [Fact]
    public void Process_GeneratesMigrateEvent()
    {
        var state = TestFactory.MakeGameState(turn: 3);
        var source = TestFactory.MakeResource(cardId: 1, instanceId: "be_1", faceUp: true);
        var target = TestFactory.MakeResource(cardId: 2, instanceId: "be_2", faceUp: true);
        state.Player1Field.Backend[0] = source;
        state.Player1Field.Backend[1] = target;

        var result = MigrateProcessor.Process(
            state, _game, 1, MakeReq("be_1", "be_2"), _cc);

        var evt = result.Events.First(e => e.EventType == WireActionTypes.Migrate);
        evt.EventData["sourceInstanceId"].Should().Be("be_1");
        evt.EventData["targetInstanceId"].Should().Be("be_2");
        evt.EventData["sourceCardId"].Should().Be(1L);
        evt.EventData["targetCardId"].Should().Be(2L);
    }
}
