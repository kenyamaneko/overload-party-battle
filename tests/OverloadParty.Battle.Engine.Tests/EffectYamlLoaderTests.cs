using System.Text.Json;
using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Engine;

[Trait("対象", "count 発動条件の min/max 解釈")]
public class EffectYamlLoaderTests
{
    private readonly TestCardCache _cc = new();
    private readonly Game _game = TestFactory.MakeGame();

    private static JsonElement Parse(string json) =>
        JsonDocument.Parse(json).RootElement;

    private EffectHandler LoadAndGetHandler(string cardId, string triggerStr, TriggerType trigger, string guardJson, string opsJson)
    {
        var card = new CardDefinition
        {
            CardId = cardId,
            CardName = "T",
            CardType = CardTypes.Compute,
            Faction = "Tuners",
            DeployTurns = 0,
            Effects =
            [
                new EffectDef
                {
                    Trigger = triggerStr,
                    Guard = [Parse(guardJson)],
                    Ops = [Parse(opsJson)],
                },
            ],
        };
        _cc.Add(card);

        var registry = new EffectRegistry();
        var custom = new CustomEffectRegistry();
        EffectYamlLoader.LoadEffectSources([card], registry, custom);

        return registry.Get(cardId, trigger)
            ?? throw new InvalidOperationException("handler not registered");
    }

    [Fact(DisplayName = "max のみ指定した count 発動条件は対象が 0 体でも成立し効果が実行される")]
    public void CountGuard_MaxOnly_AcceptsZeroResources()
    {
        // 「Tuners 3 体以下」(min 省略, max=3) のとき、Tuners が 0 体でも guard 通過することを検証
        var handler = LoadAndGetHandler(
            cardId: "TST-0001",
            triggerStr: "ignition",
            trigger: TriggerType.Ignition,
            guardJson: """
                {
                    "count": {
                        "selector": { "owner": "myself", "faction": "Tuners" },
                        "max": 3
                    }
                }
                """,
            opsJson: """{ "gain_budget": { "target": "myself", "amount": 100 } }""");

        var state = TestFactory.MakeGameState(p1Budget: 1000);
        // フィールドに Tuners 0 体

        var result = handler(new EffectContext
        {
            State = state,
            Game = _game,
            PlayerNum = 1,
            CardCache = _cc,
            Effects = new EffectRegistry(),
        });

        result.HasGuardFailed.Should().BeFalse("max-only count guard should accept 0 resources");
        state.Player1Budget.Should().Be(1100);
    }

    [Fact(DisplayName = "max のみ指定した count 発動条件は対象が max を超えると不成立で効果が実行されない")]
    public void CountGuard_MaxOnly_RejectsCountOverMax()
    {
        var handler = LoadAndGetHandler(
            cardId: "TST-0002",
            triggerStr: "ignition",
            trigger: TriggerType.Ignition,
            guardJson: """
                {
                    "count": {
                        "selector": { "owner": "myself", "faction": "Tuners" },
                        "max": 2
                    }
                }
                """,
            opsJson: """{ "gain_budget": { "target": "myself", "amount": 100 } }""");

        _cc.Add(TestFactory.ComputeCard(cardId: "TN-A", faction: "Tuners"));
        var state = TestFactory.MakeGameState(p1Budget: 1000);
        state.Player1Field.Frontend[0] = TestFactory.MakeResource(cardId: "TN-A", instanceId: "t1");
        state.Player1Field.Frontend[1] = TestFactory.MakeResource(cardId: "TN-A", instanceId: "t2");
        state.Player1Field.Frontend[2] = TestFactory.MakeResource(cardId: "TN-A", instanceId: "t3");
        // フィールドに Tuners 3 体 (max=2 を超過)

        var result = handler(new EffectContext
        {
            State = state,
            Game = _game,
            PlayerNum = 1,
            CardCache = _cc,
            Effects = new EffectRegistry(),
        });

        result.HasGuardFailed.Should().BeTrue("max-only count guard should reject when count exceeds max");
        state.Player1Budget.Should().Be(1000);
    }

    [Fact(DisplayName = "min のみ指定した count 発動条件は対象が min 未満だと不成立で効果が実行されない")]
    public void CountGuard_MinOnly_RequiresAtLeastMin()
    {
        // min 指定だけの count guard は従来通り min 以上必要 (0 体なら失敗)
        var handler = LoadAndGetHandler(
            cardId: "TST-0003",
            triggerStr: "ignition",
            trigger: TriggerType.Ignition,
            guardJson: """
                {
                    "count": {
                        "selector": { "owner": "myself", "faction": "Tuners" },
                        "min": 2
                    }
                }
                """,
            opsJson: """{ "gain_budget": { "target": "myself", "amount": 100 } }""");

        var state = TestFactory.MakeGameState(p1Budget: 1000);
        // フィールドに Tuners 0 体 (min=2 を満たさない)

        var result = handler(new EffectContext
        {
            State = state,
            Game = _game,
            PlayerNum = 1,
            CardCache = _cc,
            Effects = new EffectRegistry(),
        });

        result.HasGuardFailed.Should().BeTrue("min=2 should reject when count is 0");
        state.Player1Budget.Should().Be(1000);
    }
}

[Trait("対象", "パッシブ効果の分類と不正な定義の検出")]
public class PassiveClassificationTests
{
    private readonly TestCardCache _cc = new();
    private readonly Game _game = TestFactory.MakeGame();

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Fact(DisplayName = "on_field_change に apply_buff 以外の op を併記した定義は読み込みが失敗する")]
    public void OnFieldChangeWithNonApplyBuffOp_ThrowsOnLoad()
    {
        var card = TestFactory.ComputeCard(cardId: "TST-9101");
        card.Effects =
        [
            new EffectDef
            {
                Trigger = "on_field_change",
                Ops = [Parse("""{ "gain_budget": { "target": "myself", "amount": 100 } }""")],
            },
        ];

        var act = () => EffectYamlLoader.LoadEffectSources([card], new EffectRegistry(), new CustomEffectRegistry());

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact(DisplayName = "apply_buff の duration に continuous と書いた定義は読み込みが失敗する")]
    public void ApplyBuffWithContinuousDuration_ThrowsOnLoad()
    {
        var card = TestFactory.ComputeCard(cardId: "TST-9102");
        card.Effects =
        [
            new EffectDef
            {
                Trigger = "ignition",
                Ops = [Parse("""{"apply_buff":{"selector":"source","buff":"tp","amount":200,"duration":"continuous"}}""")],
            },
        ];

        var act = () => EffectYamlLoader.LoadEffectSources([card], new EffectRegistry(), new CustomEffectRegistry());

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact(DisplayName = "イベント系 while_on_field で selector が source でない定義は読み込みが失敗する")]
    public void EventWhileOnFieldWithNonSourceSelector_ThrowsOnLoad()
    {
        var card = TestFactory.ComputeCard(cardId: "TST-9103");
        card.Effects =
        [
            new EffectDef
            {
                Trigger = "on_deploy",
                Choice = new Dictionary<string, List<JsonElement>>
                {
                    ["standard"] =
                    [
                        Parse("""{"apply_buff":{"selector":{"owner":"myself"},"buff":"maintenance_reduction","amount":200,"duration":"while_on_field"}}"""),
                    ],
                },
            },
        ];

        var act = () => EffectYamlLoader.LoadEffectSources([card], new EffectRegistry(), new CustomEffectRegistry());

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact(DisplayName = "on_deploy の while_on_field 専用 def と他 op の def が並ぶカードは、前者だけがパッシブ効果として登録され後者はイベントとして残る")]
    public void MixedOnDeployDefs_OnlyWhileOnFieldDefBecomesPassive()
    {
        var card = TestFactory.ComputeCard(cardId: "TST-9104");
        card.Effects =
        [
            new EffectDef
            {
                Trigger = "on_deploy",
                Ops = [Parse("""{"apply_buff":{"selector":"source","buff":"count_multiplier","amount":2,"duration":"while_on_field"}}""")],
            },
            new EffectDef
            {
                Trigger = "on_deploy",
                Ops = [Parse("""{ "draw": { "count": 1 } }""")],
            },
        ];
        var registry = new EffectRegistry();

        EffectYamlLoader.LoadEffectSources([card], registry, new CustomEffectRegistry());

        registry.GetPassives("TST-9104").Should().ContainSingle();
        registry.Has("TST-9104", TriggerType.OnDeploy).Should().BeTrue();

        var state = TestFactory.MakeGameState();
        state.Player1Repository.Add(new UndeployedCard { InstanceID = "repo_1", CardID = "TST-9104" });
        var handler = registry.Get("TST-9104", TriggerType.OnDeploy)!;
        handler(new EffectContext
        {
            State = state,
            Game = _game,
            PlayerNum = 1,
            CardCache = _cc,
            Effects = registry,
        });

        state.Player1Hand.Should().ContainSingle(c => c.InstanceID == "repo_1");
    }
}

[Trait("対象", "効果定義の不正値の読み込み拒否")]
public class UnknownValueRejectionTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    private static CardDefinition MakeCard(string cardId, string? guardJson, string opsJson) => new()
    {
        CardId = cardId,
        CardName = "T",
        CardType = CardTypes.Compute,
        DeployTurns = 0,
        Effects =
        [
            new EffectDef
            {
                Trigger = "ignition",
                Guard = guardJson is null ? null : [Parse(guardJson)],
                Ops = [Parse(opsJson)],
            },
        ],
    };

    [Theory(DisplayName = "未知の値を含む効果定義は、読み込みが失敗する")]
    [InlineData("未知の op 名", null, """{ "TST_unknown_op": {} }""", "Unknown op")]
    [InlineData("selector の未知キーワード", null, """{"apply_buff":{"selector":"TST_unknown_keyword","buff":"tp","amount":100,"duration":"this_turn"}}""", "Unknown selector keyword")]
    [InlineData("selector の未知 owner", null, """{"apply_buff":{"selector":{"owner":"TST_unknown_owner"},"buff":"tp","amount":100,"duration":"this_turn"}}""", "Unknown selector owner")]
    [InlineData("未知のバフ種別", null, """{"apply_buff":{"selector":"source","buff":"TST_unknown_buff","amount":100,"duration":"this_turn"}}""", "Unknown buff type")]
    [InlineData("未知の効果量形式", null, """{"gain_budget":{"target":"myself","amount":{}}}""", "Unknown amount format")]
    [InlineData("不正な ref 表記", null, """{"gain_budget":{"target":"myself","amount":{"ref":"onlydot"}}}""", "Invalid ref format")]
    [InlineData("未知の発動条件種別", """{"TST_unknown_guard_key":{}}""", """{"gain_budget":{"target":"myself","amount":100}}""", "Unknown guard type")]
    public void UnknownValue_ThrowsWithFragment(string caseName, string? guardJson, string opsJson, string expectedMessageFragment)
    {
        var card = MakeCard(caseName, guardJson, opsJson);

        var act = () => EffectYamlLoader.LoadEffectSources([card], new EffectRegistry(), new CustomEffectRegistry());

        act.Should().Throw<InvalidOperationException>().WithMessage($"*{expectedMessageFragment}*");
    }

    [Fact(DisplayName = "未知のトリガー名を含む定義は、読み込みが失敗する")]
    public void UnknownTrigger_Throws()
    {
        var card = TestFactory.ComputeCard(cardId: "TST-9200");
        card.Effects =
        [
            new EffectDef
            {
                Trigger = "TST_unknown_trigger",
                Ops = [Parse("""{"gain_budget":{"target":"myself","amount":100}}""")],
            },
        ];

        var act = () => EffectYamlLoader.LoadEffectSources([card], new EffectRegistry(), new CustomEffectRegistry());

        act.Should().Throw<InvalidOperationException>().WithMessage("*Unknown trigger*");
    }

    [Fact(DisplayName = "未知の use_limit を含む定義は、読み込みが失敗する")]
    public void UnknownUseLimit_Throws()
    {
        var card = TestFactory.ComputeCard(cardId: "TST-9201");
        card.Effects =
        [
            new EffectDef
            {
                Trigger = "on_deploy",
                UseLimit = "TST_unknown_limit",
                Ops = [Parse("""{"gain_budget":{"target":"myself","amount":100}}""")],
            },
        ];

        var act = () => EffectYamlLoader.LoadEffectSources([card], new EffectRegistry(), new CustomEffectRegistry());

        act.Should().Throw<InvalidOperationException>().WithMessage("*Unknown use_limit*");
    }

    [Fact(DisplayName = "id の無い効果定義は、読み込みが失敗する")]
    public void MissingId_Throws()
    {
        // 従属ブロック (after 付き) が存在するとき、ルートブロックには id が必須。
        var card = TestFactory.ComputeCard(cardId: "TST-9202");
        card.Effects =
        [
            new EffectDef
            {
                Trigger = "ignition",
                Ops = [Parse("""{"gain_budget":{"target":"myself","amount":100}}""")],
            },
            new EffectDef
            {
                Trigger = "ignition",
                After = "root",
                Ops = [Parse("""{"gain_budget":{"target":"myself","amount":50}}""")],
            },
        ];

        var act = () => EffectYamlLoader.LoadEffectSources([card], new EffectRegistry(), new CustomEffectRegistry());

        act.Should().Throw<InvalidOperationException>().WithMessage("*must have an id*");
    }
}
