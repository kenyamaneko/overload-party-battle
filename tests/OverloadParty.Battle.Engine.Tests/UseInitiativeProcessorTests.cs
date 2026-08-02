using System.Text.Json;
using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Engine;

public class UseInitiativeProcessorTests
{
    /// <summary>Shared setup for UseInitiativeProcessor tests (initiative factories, registry setup, and state/use helpers).</summary>
    public abstract class Base
    {
        protected const string ProductId = "PD-TST";
        protected const string RoutineId = "IN-TST-R";
        protected const string SpecialId = "IN-TST-S";

        protected static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true,
        };

        protected readonly TestCardCache _cc = new();
        protected readonly Game _game = TestFactory.MakeGame();

        protected Base()
        {
            _cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", tp: 600, av: 1400, deployTurns: 0));
            _cc.Add(TestFactory.DataCard(cardId: "TST-0002", yield: 400));
        }

        /// <summary>Deserializes an EffectDef from snake_case JSON.</summary>
        protected static EffectDef Effect(string json) =>
            JsonSerializer.Deserialize<EffectDef>(json, JsonOpts)!;

        /// <summary>同一プロダクトに属する routine / special を 1 つずつ持つテスト施策セットを作る。</summary>
        protected static List<Initiative> Initiatives(
            long routineCost, string routineJson,
            long specialCost, string specialJson)
        {
            return
            [
                new Initiative { InitiativeId = RoutineId, ProductId = ProductId, Kind = InitiativeKinds.Routine, Name = "R", InsightCost = routineCost, Effect = Effect(routineJson) },
                new Initiative { InitiativeId = SpecialId, ProductId = ProductId, Kind = InitiativeKinds.Special, Name = "S", InsightCost = specialCost, Effect = Effect(specialJson) },
            ];
        }

        /// <summary>Loads initiatives into a fresh registry and returns it alongside their catalog.</summary>
        protected static (IEffectRegistry Effects, InitiativeCatalog Catalog) Setup(List<Initiative> initiatives)
        {
            var registry = new EffectRegistry();
            var customs = new CustomEffectRegistry();
            InitiativeEffects.LoadIntoRegistry(initiatives, registry, customs);
            return (registry, new InitiativeCatalog(initiatives));
        }

        /// <summary>routine / special それぞれに無害な効果を持つ標準施策セットを作る。</summary>
        protected static List<Initiative> StandardInitiatives() => Initiatives(
            routineCost: 100,
            routineJson: """{"ops":[{"gain_budget":{"target":"myself","amount":50}}]}""",
            specialCost: 200,
            specialJson: """{"ops":[{"gain_budget":{"target":"myself","amount":50}}]}""");

        /// <summary>Processes a UseInitiative request for player 1 with the given kind and optional choice.</summary>
        protected ActionResult Use(BattleGameState state, string kind, IEffectRegistry effects, InitiativeCatalog catalog,
            Dictionary<string, object>? choice = null) =>
            UseInitiativeProcessor.Process(
                state, _game, 1, new UseInitiativeRequest { Kind = kind, ChoiceData = choice },
                _cc, effects, catalog);

        /// <summary>Builds a Main-phase state seeded with player 1's routine/special ids and insight pool.</summary>
        protected BattleGameState MakeState(long turn = 3, long insight = 1000)
        {
            var state = TestFactory.MakeGameState(turn: turn, phase: Phase.Main);
            state.Player1RoutineId = RoutineId;
            state.Player1SpecialId = SpecialId;
            state.Player1InsightPool = insight;
            return state;
        }
    }

    [Trait("対象", "施策の使用ルール")]
    public class RuleEnforcement : Base
    {
        [Fact(DisplayName = "初回ターンに施策を使用しようとすると拒否される")]
        public void FirstTurn_Throws()
        {
            var (effects, catalog) = Setup(StandardInitiatives());
            var state = MakeState(turn: 1);

            var act = () => Use(state, InitiativeKinds.Routine, effects, catalog);

            act.Should().Throw<GameRuleException>().WithMessage("*first turn*");
        }

        [Fact(DisplayName = "存在しない施策 ID を使用しようとすると拒否される")]
        public void UnknownInitiative_Throws()
        {
            var (effects, catalog) = Setup(StandardInitiatives());
            var state = MakeState();
            state.Player1RoutineId = "IN-NOPE";

            var act = () => Use(state, InitiativeKinds.Routine, effects, catalog);

            act.Should().Throw<GameRuleException>().WithMessage("*not found*");
        }

        [Fact(DisplayName = "ルーチン枠にスペシャルの施策がセットされていると拒否される")]
        public void InitiativeKindMismatch_Throws()
        {
            // routine スロットにスペシャル施策の ID がセットされている場合は弾く。
            var (effects, catalog) = Setup(StandardInitiatives());
            var state = MakeState();
            state.Player1RoutineId = SpecialId;

            var act = () => Use(state, InitiativeKinds.Routine, effects, catalog);

            act.Should().Throw<GameRuleException>().WithMessage("*is not a routine*");
        }

        [Fact(DisplayName = "ルーチンを同一ターンに 2 回使用しようとすると拒否される")]
        public void Routine_OncePerTurn_Throws()
        {
            var (effects, catalog) = Setup(StandardInitiatives());
            var state = MakeState();

            Use(state, InitiativeKinds.Routine, effects, catalog);
            var act = () => Use(state, InitiativeKinds.Routine, effects, catalog);

            act.Should().Throw<GameRuleException>().WithMessage("*routine already used*");
        }

        [Fact(DisplayName = "スペシャルを 1 ゲームに 2 回使用しようとすると拒否される")]
        public void Special_OncePerGame_Throws()
        {
            var (effects, catalog) = Setup(StandardInitiatives());
            var state = MakeState();

            Use(state, InitiativeKinds.Special, effects, catalog);
            var act = () => Use(state, InitiativeKinds.Special, effects, catalog);

            act.Should().Throw<GameRuleException>().WithMessage("*special already used*");
        }

        [Fact(DisplayName = "インサイトが施策コストに満たないとき拒否されインサイトプールが消費されない")]
        public void InsufficientInsight_Throws()
        {
            var (effects, catalog) = Setup(StandardInitiatives());
            var state = MakeState(insight: 50); // routine cost is 100

            var act = () => Use(state, InitiativeKinds.Routine, effects, catalog);

            act.Should().Throw<GameRuleException>().WithMessage("*insufficient insight*");
            state.Player1InsightPool.Should().Be(50, "failed initiative must not consume insight");
        }

        [Fact(DisplayName = "ルーチンを使用するとコスト 100 が引かれインサイトプールが 900 になる")]
        public void InsightCost_Deducted()
        {
            var (effects, catalog) = Setup(StandardInitiatives());
            var state = MakeState(insight: 1000);

            Use(state, InitiativeKinds.Routine, effects, catalog);

            state.Player1InsightPool.Should().Be(900);
        }

        [Fact(DisplayName = "施策を使用すると施策の使用イベントに種別とプロダクトと施策情報が載る")]
        public void Use_EmitsInitiativeEvent()
        {
            var (effects, catalog) = Setup(StandardInitiatives());
            var state = MakeState();

            var result = Use(state, InitiativeKinds.Routine, effects, catalog);

            var evt = result.Events.Should().ContainSingle(e => e.EventType == EventTypes.UseInitiative).Subject;
            var data = evt.EventData.Should().BeOfType<UseInitiativeEventData>().Subject;
            data.Kind.Should().Be(InitiativeKinds.Routine);
            data.ProductId.Should().Be(ProductId);
            data.InitiativeId.Should().Be(RoutineId);
            data.InitiativeName.Should().Be("R");
            data.InsightCost.Should().Be(100);
        }
    }

    [Trait("対象", "対象を選ぶ施策のコスト")]
    public class ChoiceRequiredCost : Base
    {
        /// <summary>相手のフロントエンドから選んだ 1 体に 300 ダメージを与える routine を用意する。</summary>
        /// <returns>効果レジストリと施策カタログ。</returns>
        private static (IEffectRegistry Effects, InitiativeCatalog Catalog) SetupChoiceRoutine() => Setup(Initiatives(
            routineCost: 100,
            routineJson: """{"ops":[{"deal_damage":{"selector":{"owner":"opponent","zone":"frontend","pick":"choice"},"amount":300}}]}""",
            specialCost: 0,
            specialJson: """{"ops":[{"gain_budget":{"target":"myself","amount":0}}]}"""));

        [Fact(DisplayName = "対象を選ぶ施策を選択なしで使用すると拒否され、インサイトも使用回数も消費されない")]
        public void ChoiceRoutine_WithoutChoice_IsRejectedAndConsumesNothing()
        {
            var (effects, catalog) = SetupChoiceRoutine();
            var state = MakeState(insight: 1000);
            state.Player2Field.Frontend[0] = TestFactory.MakeResource(instanceId: "o1", faceUp: true);

            var act = () => Use(state, InitiativeKinds.Routine, effects, catalog);

            act.Should().Throw<GameRuleException>().WithMessage("*requires a target choice*");
            state.Player1InsightPool.Should().Be(1000);
            state.GetRoutineUsedThisTurn(1).Should().BeFalse();
            state.Player2Field.Frontend[0]!.Damage.Should().Be(0);
        }

        [Fact(DisplayName = "対象を選ぶ施策を選択付きで使用すると、選んだリソースに 300 ダメージが入りインサイトが 100 減る")]
        public void ChoiceRoutine_WithChoice_AppliesDamageAndPaysCost()
        {
            var (effects, catalog) = SetupChoiceRoutine();
            var state = MakeState(insight: 1000);
            state.Player2Field.Frontend[0] = TestFactory.MakeResource(instanceId: "o1", faceUp: true);

            Use(state, InitiativeKinds.Routine, effects, catalog,
                new Dictionary<string, object> { ["instanceId"] = "o1" });

            state.Player2Field.Frontend[0]!.Damage.Should().Be(300);
            state.Player1InsightPool.Should().Be(900);
            state.GetRoutineUsedThisTurn(1).Should().BeTrue();
        }

        /// <summary>自分のリソースから選んだ 1 体の可用性を 300 回復する routine を用意する。</summary>
        /// <returns>効果レジストリと施策カタログ。</returns>
        private static (IEffectRegistry Effects, InitiativeCatalog Catalog) SetupHealChoiceRoutine() => Setup(Initiatives(
            routineCost: 300,
            routineJson: """{"ops":[{"heal_damage":{"selector":{"owner":"myself","pick":"choice"},"amount":300}}]}""",
            specialCost: 0,
            specialJson: """{"ops":[{"gain_budget":{"target":"myself","amount":0}}]}"""));

        [Fact(DisplayName = "回復する対象を選ぶ施策を選択なしで使用すると拒否され、インサイトも使用回数も消費されない")]
        public void HealChoiceRoutine_WithoutChoice_IsRejectedAndConsumesNothing()
        {
            var (effects, catalog) = SetupHealChoiceRoutine();
            var state = MakeState(insight: 1000);
            var wounded = TestFactory.MakeResource(instanceId: "m1", faceUp: true, damage: 500);
            state.Player1Field.Frontend[0] = wounded;

            var act = () => Use(state, InitiativeKinds.Routine, effects, catalog);

            act.Should().Throw<GameRuleException>().WithMessage("*requires a target choice*");
            state.Player1InsightPool.Should().Be(1000);
            state.GetRoutineUsedThisTurn(1).Should().BeFalse();
            wounded.Damage.Should().Be(500);
        }

        [Fact(DisplayName = "回復する対象を選ぶ施策を選択付きで使用すると、選んだリソースのダメージが 300 回復する")]
        public void HealChoiceRoutine_WithChoice_HealsChosenResource()
        {
            var (effects, catalog) = SetupHealChoiceRoutine();
            var state = MakeState(insight: 1000);
            var wounded = TestFactory.MakeResource(instanceId: "m1", faceUp: true, damage: 500);
            state.Player1Field.Frontend[0] = wounded;

            Use(state, InitiativeKinds.Routine, effects, catalog,
                new Dictionary<string, object> { ["instanceId"] = "m1" });

            wounded.Damage.Should().Be(200);
            state.Player1InsightPool.Should().Be(700);
        }

        [Fact(DisplayName = "伏せリアクティブを確認する施策は、相手に伏せカードが 2 枚あるとき選択待ちへ遷移する")]
        public void PeekRoutine_MultipleFaceDown_SuspendsForChoice()
        {
            var (effects, catalog) = Setup(Initiatives(
                routineCost: 150,
                routineJson: """{"ops":[{"peek_reactive":{}}]}""",
                specialCost: 0,
                specialJson: """{"ops":[{"gain_budget":{"target":"myself","amount":0}}]}"""));
            var state = MakeState(insight: 1000);
            state.Player2Field.Support[0] = new DeployedSupport { InstanceID = "sup_1", CardID = "TST-0400", FaceUp = false };
            state.Player2Field.Support[1] = new DeployedSupport { InstanceID = "sup_2", CardID = "TST-0400", FaceUp = false };

            Use(state, InitiativeKinds.Routine, effects, catalog);

            state.PendingEffectChoice.Should().NotBeNull();
            state.PendingEffectChoice!.ChoiceKind.Should().Be(ChoiceKinds.FaceDownReactive);
            state.PendingEffectChoice.Candidates.Should().Equal("sup_1", "sup_2");
        }

        [Fact(DisplayName = "伏せリアクティブを確認する施策の選択を解決すると、選んだ 2 枚目だけを覗き見る")]
        public void PeekRoutine_ResolvingChoice_PeeksChosenCard()
        {
            var (effects, catalog) = Setup(Initiatives(
                routineCost: 150,
                routineJson: """{"ops":[{"peek_reactive":{}}]}""",
                specialCost: 0,
                specialJson: """{"ops":[{"gain_budget":{"target":"myself","amount":0}}]}"""));
            var state = MakeState(insight: 1000);
            state.Player2Field.Support[0] = new DeployedSupport { InstanceID = "sup_1", CardID = "TST-0400", FaceUp = false };
            state.Player2Field.Support[1] = new DeployedSupport { InstanceID = "sup_2", CardID = "TST-0400", FaceUp = false };

            Use(state, InitiativeKinds.Routine, effects, catalog);
            ResolvePendingChoiceProcessor.Process(
                state, _game, 1, new ResolvePendingChoiceRequest { ChosenId = "sup_2" },
                _cc, effects, new FakeClock());

            state.Player2Field.Support[1]!.PeekedBy.Should().Contain(1);
            state.Player2Field.Support[0]!.PeekedBy.Should().BeEmpty();
            state.PendingEffectChoice.Should().BeNull();
        }
    }

    [Trait("対象", "施策の効果")]
    public class Effects : Base
    {
        [Fact(DisplayName = "相手の表向きリソース 1 体につき相手のバジェットが 100 減り裏向きは数えない")]
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

        [Fact(DisplayName = "count_multiplier 2 の一時効果を持つリソース 1 体は、2 体分として数えられる")]
        public void Routine_LoseBudgetPerOpponentCount_CountMultiplierCountsAsMultipleUnits()
        {
            var initiatives = Initiatives(
                routineCost: 400,
                routineJson: """{"ops":[{"lose_budget":{"target":"opponent","amount":{"base":0,"per":{"count":{"owner":"opponent"},"value":100}}}}]}""",
                specialCost: 0,
                specialJson: """{"ops":[{"gain_budget":{"target":"myself","amount":0}}]}""");
            var (effects, catalog) = Setup(initiatives);

            var state = MakeState();
            var counted = TestFactory.MakeResource(instanceId: "o1", faceUp: true);
            counted.TemporaryEffects.Add(new TemporaryEffect { EffectType = "count_multiplier", Value = 2 });
            state.Player2Field.Frontend[0] = counted;

            Use(state, InitiativeKinds.Routine, effects, catalog);

            state.Player2Budget.Should().Be(5000 - 200);
        }

        [Fact(DisplayName = "count_multiplier の無いリソースは、1 体分として数えられる")]
        public void Routine_LoseBudgetPerOpponentCount_NoMultiplierCountsAsOneUnit()
        {
            var initiatives = Initiatives(
                routineCost: 400,
                routineJson: """{"ops":[{"lose_budget":{"target":"opponent","amount":{"base":0,"per":{"count":{"owner":"opponent"},"value":100}}}}]}""",
                specialCost: 0,
                specialJson: """{"ops":[{"gain_budget":{"target":"myself","amount":0}}]}""");
            var (effects, catalog) = Setup(initiatives);

            var state = MakeState();
            state.Player2Field.Frontend[0] = TestFactory.MakeResource(instanceId: "o1", faceUp: true);

            Use(state, InitiativeKinds.Routine, effects, catalog);

            state.Player2Budget.Should().Be(5000 - 100);
        }

        [Fact(DisplayName = "インサイトプール全量を 1.5 倍のバジェットに変換しプールが 0 になる")]
        public void Special_ConvertAllInsight()
        {
            // 大感謝セール: Insight プール全量を 1.5 倍の Budget に変換しプールを 0 に。
            var initiatives = Initiatives(
                routineCost: 0,
                routineJson: """{"ops":[{"gain_budget":{"target":"myself","amount":0}}]}""",
                specialCost: 0,
                specialJson: """{"ops":[{"convert_all_insight":{"rate_percent":150}}]}""");
            var (effects, catalog) = Setup(initiatives);
            var state = MakeState(insight: 1000);

            Use(state, InitiativeKinds.Special, effects, catalog);

            state.Player1Budget.Should().Be(5000 + 1500);
            state.Player1InsightPool.Should().Be(0);
        }

        [Fact(DisplayName = "相手の表向きリソース全体を休止にし裏向きは対象外になる")]
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

        [Fact(DisplayName = "選んだ味方リソース 1 体のダメージを 300 回復する")]
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

        [Fact(DisplayName = "自分の表向きリソース 1 体につきバジェットが 150 増える")]
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
}
