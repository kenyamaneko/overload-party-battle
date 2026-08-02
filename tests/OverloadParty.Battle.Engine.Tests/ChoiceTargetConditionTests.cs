using System.Text.Json;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Engine;

[Trait("対象", "プレイヤーが選んだ対象への効果の適用条件")]
public class ChoiceTargetConditionTests
{
    private const string SourceCardId = "TST-9000";
    private const string ChosenCardId = "TST-9001";

    private const string ZoneAndFactionAndSubtype = """
        { "owner": "myself", "zone": "backend", "faction": "Tuners", "subtype": "Container", "pick": "choice" }
        """;

    private const string ZoneAndFaction = """
        { "owner": "myself", "zone": "backend", "faction": "Tuners", "pick": "choice" }
        """;

    /// <summary>指定の適用条件を持つ回復の起動効果を、指定の属性のリソースを選んで発動する。</summary>
    /// <param name="targetCondition">起動効果の適用条件 (カード定義の selector と同じ形式)。</param>
    /// <param name="zone">選ぶリソースを置くゾーン。</param>
    /// <param name="faction">選ぶリソースの陣営。</param>
    /// <param name="subtype">選ぶリソースのサブタイプ。</param>
    /// <returns>発動後に選んだリソースへ残るダメージ。</returns>
    private static long UseHealIgnition(string targetCondition, string zone, string faction, string subtype)
    {
        var source = TestFactory.ComputeCard(cardId: SourceCardId, faction: "Tuners");
        source.Effects =
        [
            new EffectDef
            {
                Trigger = "ignition",
                Ops =
                [
                    JsonDocument.Parse(
                        $$"""{ "heal_damage": { "selector": {{targetCondition}}, "amount": 300 } }""").RootElement,
                ],
            },
        ];

        var cc = new TestCardCache();
        cc.Add(source);
        cc.Add(TestFactory.ComputeCard(cardId: ChosenCardId, subtype: subtype, faction: faction));

        var effects = new EffectRegistry();
        EffectYamlLoader.LoadEffectSources([source], effects, new CustomEffectRegistry());

        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
        state.Player1Field.Backend[2] = TestFactory.MakeResource(cardId: SourceCardId, instanceId: "src");

        var chosen = TestFactory.MakeResource(cardId: ChosenCardId, instanceId: "chosen", damage: 500);
        var slotsByZone = new Dictionary<string, Zone<DeployedResource>>
        {
            ["frontend"] = state.Player1Field.Frontend,
            ["backend"] = state.Player1Field.Backend,
        };
        slotsByZone[zone][0] = chosen;

        UseIgnitionProcessor.Process(
            state,
            TestFactory.MakeGame(),
            1,
            new UseIgnitionRequest
            {
                InstanceID = "src",
                ChoiceData = new Dictionary<string, object> { ["instanceId"] = "chosen" },
            },
            cc,
            effects);

        return chosen.Damage;
    }

    [Fact(DisplayName = "バックエンドの Tuners の Container に限定した回復効果で、ゾーン・陣営・サブタイプがすべて一致するリソースを選ぶと、そのダメージが 500 から 200 に減る")]
    public void ChoiceTarget_MatchesEveryCondition_Heals()
    {
        long damage = UseHealIgnition(
            ZoneAndFactionAndSubtype, zone: "backend", faction: "Tuners", subtype: "Container");

        damage.Should().Be(200);
    }

    [Theory(DisplayName = "バックエンドの Tuners の Container に限定した回復効果で、ゾーン・陣営・サブタイプのどれかが一致しないリソースを選ぶと、そのダメージが 500 のまま減らない")]
    [InlineData("frontend", "Tuners", "Container")]
    [InlineData("backend", "SHE", "Container")]
    [InlineData("backend", "Tuners", "VM")]
    public void ChoiceTarget_MissesAnyCondition_DoesNotHeal(string zone, string faction, string subtype)
    {
        long damage = UseHealIgnition(ZoneAndFactionAndSubtype, zone, faction, subtype);

        damage.Should().Be(500);
    }

    [Fact(DisplayName = "バックエンドの Tuners に限定しサブタイプを限定しない回復効果では、Container 以外のサブタイプのリソースを選んでも、そのダメージが 500 から 200 に減る")]
    public void ChoiceTarget_NoSubtypeCondition_HealsAnySubtype()
    {
        long damage = UseHealIgnition(ZoneAndFaction, zone: "backend", faction: "Tuners", subtype: "VM");

        damage.Should().Be(200);
    }
}
