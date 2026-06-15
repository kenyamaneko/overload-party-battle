using System.Text.Json;
using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Engine;

public class UseInitiativeProcessorTests
{
    private const string ProductId = "PD-TST";
    private const string RoutineId = "IN-TST-R";
    private const string SpecialId = "IN-TST-S";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    private readonly TestCardCache _cc = new();
    private readonly Game _game = TestFactory.MakeGame();

    public UseInitiativeProcessorTests()
    {
        _cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", tp: 600, av: 1400, deployTurns: 0));
        _cc.Add(TestFactory.DataCard(cardId: "TST-0002", yield: 400));
    }

    private static EffectDef Effect(string json) =>
        JsonSerializer.Deserialize<EffectDef>(json, JsonOpts)!;

    /// <summary>同一プロダクトに属する routine / special を 1 つずつ持つテスト施策セットを作る。</summary>
    private static List<Initiative> Initiatives(
        long routineCost, string routineJson,
        long specialCost, string specialJson)
    {
        return
        [
            new Initiative { InitiativeId = RoutineId, ProductId = ProductId, Kind = InitiativeKinds.Routine, Name = "R", InsightCost = routineCost, Effect = Effect(routineJson) },
            new Initiative { InitiativeId = SpecialId, ProductId = ProductId, Kind = InitiativeKinds.Special, Name = "S", InsightCost = specialCost, Effect = Effect(specialJson) },
        ];
    }

    private static (IEffectRegistry Effects, InitiativeCatalog Catalog) Setup(List<Initiative> initiatives)
    {
        var registry = new EffectRegistry();
        var customs = new CustomEffectRegistry();
        InitiativeEffects.LoadIntoRegistry(initiatives, registry, customs);
        return (registry, new InitiativeCatalog(initiatives));
    }

    // routine / special それぞれに無害な効果を持つ標準施策セット。
    private static List<Initiative> StandardInitiatives() => Initiatives(
        routineCost: 100,
        routineJson: """{"ops":[{"gain_budget":{"target":"myself","amount":50}}]}""",
        specialCost: 200,
        specialJson: """{"ops":[{"gain_budget":{"target":"myself","amount":50}}]}""");

    private ActionResult Use(BattleGameState state, string kind, IEffectRegistry effects, InitiativeCatalog catalog,
        Dictionary<string, object>? choice = null) =>
        UseInitiativeProcessor.Process(
            state, _game, 1, new UseInitiativeRequest { Kind = kind, ChoiceData = choice },
            _cc, effects, catalog);

    private BattleGameState MakeState(long turn = 3, long insight = 1000)
    {
        var state = TestFactory.MakeGameState(turn: turn, phase: Phase.Main);
        state.Player1ProductId = ProductId;
        state.Player1RoutineId = RoutineId;
        state.Player1SpecialId = SpecialId;
        state.Player1InsightPool = insight;
        return state;
    }

    // ─── ルール強制 ────────────────────────────────────────────

    [Fact]
    public void FirstTurn_Throws()
    {
        var (effects, catalog) = Setup(StandardInitiatives());
        var state = MakeState(turn: 1);

        var act = () => Use(state, InitiativeKinds.Routine, effects, catalog);

        act.Should().Throw<GameRuleException>().WithMessage("*first turn*");
    }

    [Fact]
    public void UnknownInitiative_Throws()
    {
        var (effects, catalog) = Setup(StandardInitiatives());
        var state = MakeState();
        state.Player1RoutineId = "IN-NOPE";

        var act = () => Use(state, InitiativeKinds.Routine, effects, catalog);

        act.Should().Throw<GameRuleException>().WithMessage("*not found*");
    }

    [Fact]
    public void InitiativeKindMismatch_Throws()
    {
        // routine スロットにスペシャル施策の ID がセットされている場合は弾く。
        var (effects, catalog) = Setup(StandardInitiatives());
        var state = MakeState();
        state.Player1RoutineId = SpecialId;

        var act = () => Use(state, InitiativeKinds.Routine, effects, catalog);

        act.Should().Throw<GameRuleException>().WithMessage("*is not a routine*");
    }

    [Fact]
    public void Routine_OncePerTurn_Throws()
    {
        var (effects, catalog) = Setup(StandardInitiatives());
        var state = MakeState();

        Use(state, InitiativeKinds.Routine, effects, catalog);
        var act = () => Use(state, InitiativeKinds.Routine, effects, catalog);

        act.Should().Throw<GameRuleException>().WithMessage("*routine already used*");
    }

    [Fact]
    public void Special_OncePerGame_Throws()
    {
        var (effects, catalog) = Setup(StandardInitiatives());
        var state = MakeState();

        Use(state, InitiativeKinds.Special, effects, catalog);
        var act = () => Use(state, InitiativeKinds.Special, effects, catalog);

        act.Should().Throw<GameRuleException>().WithMessage("*special already used*");
    }

    [Fact]
    public void InsufficientInsight_Throws()
    {
        var (effects, catalog) = Setup(StandardInitiatives());
        var state = MakeState(insight: 50); // routine cost is 100

        var act = () => Use(state, InitiativeKinds.Routine, effects, catalog);

        act.Should().Throw<GameRuleException>().WithMessage("*insufficient insight*");
        state.Player1InsightPool.Should().Be(50, "failed initiative must not consume insight");
    }

    [Fact]
    public void InsightCost_Deducted()
    {
        var (effects, catalog) = Setup(StandardInitiatives());
        var state = MakeState(insight: 1000);

        Use(state, InitiativeKinds.Routine, effects, catalog);

        state.Player1InsightPool.Should().Be(900);
    }

    [Fact]
    public void Use_EmitsInitiativeEvent()
    {
        var (effects, catalog) = Setup(StandardInitiatives());
        var state = MakeState();

        var result = Use(state, InitiativeKinds.Routine, effects, catalog);

        var evt = result.Events.Should().ContainSingle(e => e.EventType == EventTypes.UseInitiative).Subject;
        evt.EventData.Should().BeOfType<UseInitiativeEventData>()
            .Which.Kind.Should().Be(InitiativeKinds.Routine);
    }

    // ─── 効果 ──────────────────────────────────────────────────

    [Fact]
    public void Routine_LoseBudgetPerOpponentFaceUpCount()
    {
        // 物欲刺激: 相手の表向きリソース 1 体につき相手 Budget -100。
        var initiatives = Initiatives(
            routineCost: 400,
            routineJson: """{"ops":[{"lose_budget":{"target":"opponent","amount":{"base":0,"per":{"count":{"owner":"opponent"},"value":100}}}}]}""",
            specialCost: 0,
            specialJson: """{"ops":[{"gain_budget":{"target":"myself","amount":0}}]}""");
        var (effects, catalog) = Setup(initiatives);

        var state = MakeState();
        state.Player2Field.Frontend[0] = TestFactory.MakeResource(instanceId: "o1", faceUp: true);
        state.Player2Field.Frontend[1] = TestFactory.MakeResource(instanceId: "o2", faceUp: true);
        state.Player2Field.Backend[0] = TestFactory.MakeResource(instanceId: "o3", faceUp: false); // 裏向きは数えない

        Use(state, InitiativeKinds.Routine, effects, catalog);

        state.Player2Budget.Should().Be(5000 - 200);
    }

    [Fact]
    public void Special_ConvertAllInsight()
    {
        // 大感謝セール: Insight プール全量を 1.5 倍の Budget に変換しプールを 0 に。
        var initiatives = Initiatives(
            routineCost: 0,
            routineJson: """{"ops":[{"gain_budget":{"target":"myself","amount":0}}]}""",
            specialCost: 0,
            specialJson: """{"custom":"convert_all_insight","meta":{"multiplier_percent":150}}""");
        var (effects, catalog) = Setup(initiatives);
        var state = MakeState(insight: 1000);

        Use(state, InitiativeKinds.Special, effects, catalog);

        state.Player1Budget.Should().Be(5000 + 1500);
        state.Player1InsightPool.Should().Be(0);
    }

    [Fact]
    public void Special_DormantAllOpponentFaceUp()
    {
        // お裾分け: 相手の表向きリソース全体を休止に。
        var initiatives = Initiatives(
            routineCost: 0,
            routineJson: """{"ops":[{"gain_budget":{"target":"myself","amount":0}}]}""",
            specialCost: 600,
            specialJson: """{"ops":[{"apply_buff":{"selector":{"owner":"opponent"},"buff":"dormant","amount":1,"duration":"until_next_turn_end"}}]}""");
        var (effects, catalog) = Setup(initiatives);

        var state = MakeState();
        var faceUp = TestFactory.MakeResource(instanceId: "o1", faceUp: true);
        var faceDown = TestFactory.MakeResource(instanceId: "o2", faceUp: false);
        state.Player2Field.Frontend[0] = faceUp;
        state.Player2Field.Frontend[1] = faceDown;

        Use(state, InitiativeKinds.Special, effects, catalog);

        FieldHelpers.HasTemporaryEffect(faceUp, BuffTypes.Dormant).Should().BeTrue();
        FieldHelpers.HasTemporaryEffect(faceDown, BuffTypes.Dormant).Should().BeFalse();
    }

    [Fact]
    public void Routine_HealChosenAlly()
    {
        // 焼きたてのお菓子: 味方 1 体の可用性を 300 回復。
        var initiatives = Initiatives(
            routineCost: 300,
            routineJson: """{"ops":[{"heal_damage":{"selector":{"owner":"myself","pick":"choice"},"amount":300}}]}""",
            specialCost: 0,
            specialJson: """{"ops":[{"gain_budget":{"target":"myself","amount":0}}]}""");
        var (effects, catalog) = Setup(initiatives);

        var state = MakeState();
        var ally = TestFactory.MakeResource(instanceId: "a1", faceUp: true, maxAV: 1400, currentAV: 800, damage: 600);
        state.Player1Field.Frontend[0] = ally;

        Use(state, InitiativeKinds.Routine, effects, catalog,
            choice: new Dictionary<string, object> { ["instanceId"] = "a1" });

        ally.Damage.Should().Be(300);
    }

    [Fact]
    public void Special_GainBudgetPerOwnFaceUpCount()
    {
        // フェス開催: 自分の表向きリソース 1 体につき Budget +150。
        var initiatives = Initiatives(
            routineCost: 0,
            routineJson: """{"ops":[{"gain_budget":{"target":"myself","amount":0}}]}""",
            specialCost: 500,
            specialJson: """{"ops":[{"gain_budget":{"target":"myself","amount":{"base":0,"per":{"count":{"owner":"myself"},"value":150}}}}]}""");
        var (effects, catalog) = Setup(initiatives);

        var state = MakeState();
        state.Player1Field.Frontend[0] = TestFactory.MakeResource(instanceId: "m1", faceUp: true);
        state.Player1Field.Backend[0] = TestFactory.MakeResource(instanceId: "m2", faceUp: true);

        Use(state, InitiativeKinds.Special, effects, catalog);

        state.Player1Budget.Should().Be(5000 + 300);
    }
}
