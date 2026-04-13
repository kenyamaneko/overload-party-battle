using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Engine;

public class MonetizeProcessorTests
{
    private readonly TestCardCache _cc = new();
    private readonly Game _game = TestFactory.MakeGame();

    public MonetizeProcessorTests()
    {
        // Backend compute card: TP=600
        _cc.Add(TestFactory.ComputeCard(cardId: "SH-0001", tp: 600));
        // Second backend compute card: TP=400
        _cc.Add(TestFactory.ComputeCard(cardId: "TEST-0002", tp: 400, name: "SmallCompute"));
        // Database card (data type, not compute)
        _cc.Add(TestFactory.DataCard(cardId: "NT-0009", subtype: "Database"));
    }

    private static MonetizeRequest MakeReq(params MonetizeDistribution[] dists) =>
        new() { Distributions = [..dists] };

    private static MonetizeDistribution Dist(string instanceId, long amount) =>
        new() { InstanceID = instanceId, Amount = amount };

    // ─── 1. Basic monetize transfers insight to budget ─────────

    [Fact]
    public void Process_BasicMonetize_TransfersInsightToBudget()
    {
        var state = TestFactory.MakeGameState(turn: 2);
        var resource = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "be_1", faceUp: true);
        state.Player1Field.Backend[0] = resource;
        state.SetInsightPool(1, 500);

        var result = MonetizeProcessor.Process(
            state, _game, 1, MakeReq(Dist("be_1", 300)), _cc);

        state.GetInsightPool(1).Should().Be(200);
        state.GetBudget(1).Should().Be(5300);
        resource.MonetizedAmount.Should().Be(300);
    }

    // ─── 2. First turn → throws ──────────────────────────────

    [Fact]
    public void Process_FirstTurn_Throws()
    {
        var state = TestFactory.MakeGameState(turn: 1);
        state.Player1Field.Backend[0] = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "be_1", faceUp: true);
        state.SetInsightPool(1, 500);

        var act = () => MonetizeProcessor.Process(
            state, _game, 1, MakeReq(Dist("be_1", 100)), _cc);

        act.Should().Throw<GameRuleException>().WithMessage("*first turn*");
    }

    // ─── 3. Empty distributions → throws ─────────────────────

    [Fact]
    public void Process_EmptyDistributions_Throws()
    {
        var state = TestFactory.MakeGameState(turn: 2);
        state.SetInsightPool(1, 500);

        var act = () => MonetizeProcessor.Process(
            state, _game, 1, new MonetizeRequest { Distributions = [] }, _cc);

        act.Should().Throw<GameRuleException>();
    }

    // ─── 4. Frontend compute → throws ────────────────────────

    [Fact]
    public void Process_FrontendCompute_Throws()
    {
        var state = TestFactory.MakeGameState(turn: 2);
        var resource = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "fe_1", faceUp: true);
        state.Player1Field.Frontend[0] = resource;
        state.SetInsightPool(1, 500);

        var act = () => MonetizeProcessor.Process(
            state, _game, 1, MakeReq(Dist("fe_1", 100)), _cc);

        act.Should().Throw<GameRuleException>().WithMessage("*backend*");
    }

    // ─── 5. Non-compute type → throws ────────────────────────

    [Fact]
    public void Process_NonComputeType_Throws()
    {
        var state = TestFactory.MakeGameState(turn: 2);
        var resource = TestFactory.MakeResource(cardId: "NT-0009", instanceId: "be_1", faceUp: true);
        state.Player1Field.Backend[0] = resource;
        state.SetInsightPool(1, 500);

        var act = () => MonetizeProcessor.Process(
            state, _game, 1, MakeReq(Dist("be_1", 100)), _cc);

        act.Should().Throw<GameRuleException>().WithMessage("*compute*");
    }

    // ─── 6. Exceeds insight pool → throws ────────────────────

    [Fact]
    public void Process_ExceedsInsightPool_Throws()
    {
        var state = TestFactory.MakeGameState(turn: 2);
        var resource = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "be_1", faceUp: true);
        state.Player1Field.Backend[0] = resource;
        state.SetInsightPool(1, 100);

        var act = () => MonetizeProcessor.Process(
            state, _game, 1, MakeReq(Dist("be_1", 200)), _cc);

        act.Should().Throw<GameRuleException>().WithMessage("*exceeds insight pool*");
    }

    // ─── 7. Exceeds throughput capacity → throws ─────────────

    [Fact]
    public void Process_ExceedsThroughputCapacity_Throws()
    {
        var state = TestFactory.MakeGameState(turn: 2);
        var resource = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "be_1", faceUp: true);
        state.Player1Field.Backend[0] = resource;
        state.SetInsightPool(1, 5000);

        // TP=600 (Small rank, no family → effective TP = 600*1 = 600), requesting 700
        var act = () => MonetizeProcessor.Process(
            state, _game, 1, MakeReq(Dist("be_1", 700)), _cc);

        act.Should().Throw<GameRuleException>().WithMessage("*exceeds remaining capacity*");
    }

    // ─── 8. Negative amount → throws ─────────────────────────

    [Fact]
    public void Process_NegativeAmount_Throws()
    {
        var state = TestFactory.MakeGameState(turn: 2);
        var resource = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "be_1", faceUp: true);
        state.Player1Field.Backend[0] = resource;
        state.SetInsightPool(1, 500);

        var act = () => MonetizeProcessor.Process(
            state, _game, 1, MakeReq(Dist("be_1", -10)), _cc);

        act.Should().Throw<GameRuleException>();
    }

    // ─── 9. Multiple distributions all applied ───────────────

    [Fact]
    public void Process_MultipleDistributions_AllApplied()
    {
        var state = TestFactory.MakeGameState(turn: 2);
        var res1 = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "be_1", faceUp: true);
        var res2 = TestFactory.MakeResource(cardId: "TEST-0002", instanceId: "be_2", faceUp: true,
            maxTP: 400, currentTP: 400);
        state.Player1Field.Backend[0] = res1;
        state.Player1Field.Backend[1] = res2;
        state.SetInsightPool(1, 800);

        var result = MonetizeProcessor.Process(
            state, _game, 1, MakeReq(Dist("be_1", 300), Dist("be_2", 200)), _cc);

        res1.MonetizedAmount.Should().Be(300);
        res2.MonetizedAmount.Should().Be(200);
        state.GetInsightPool(1).Should().Be(300);
        state.GetBudget(1).Should().Be(5500);
        result.Events.Should().Contain(e => e.EventType == ActionTypes.Monetize);
    }
}
