using System.Text.Json;
using ApiCard = OverloadParty.ApiCard;
using OverloadParty.Battle.Data;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Data;

public class CardDefinitionMapperTests
{
    private static ApiCard.CardDefinition ParseWireCard(string json) =>
        JsonSerializer.Deserialize<ApiCard.CardDefinition>(json)!;

    [Trait("対象", "Compute カードの stats マッピング")]
    public class Computeカードのstatsマッピング
    {
        [Fact(DisplayName = "throughput 400・availability 800・sla_penalty 300・maintenance_cost 100 の stats を持つとき、同値の ComputeStats を持つカード定義になる")]
        public void MapsComputeStats()
        {
            var wire = ParseWireCard("""
            {
              "card_id": "TST-COMPUTE", "card_name": "test compute", "resource_label": "VM", "faction": "Neutral",
              "card_type": "Compute", "deploy_turns": 0, "resizable": false, "elastic": false,
              "elastic_increment": 0, "free_tier": 0, "cost_per_request": 0,
              "stats": { "throughput": 400, "availability": 800, "sla_penalty": 300, "maintenance_cost": 100 },
              "effect_text": "", "restriction": "unlimited", "is_active": true
            }
            """);

            var card = CardDefinitionMapper.ToCardDefinition(wire);

            card.ComputeStats.Should().NotBeNull();
            card.ComputeStats!.Throughput.Should().Be(400);
            card.ComputeStats.Availability.Should().Be(800);
            card.ComputeStats.SLAPenalty.Should().Be(300);
            card.ComputeStats.MaintenanceCost.Should().Be(100);
            card.DataResourceStats.Should().BeNull();
        }

        [Fact(DisplayName = "契約上定義済みだが battle 未使用の throughput_max を stats に含むとき、マッピングに成功する")]
        public void IgnoresContractKnownUnusedKey()
        {
            var wire = ParseWireCard("""
            {
              "card_id": "TST-COMPUTE-MAX", "card_name": "test compute", "resource_label": "VM", "faction": "Neutral",
              "card_type": "Compute", "deploy_turns": 0, "resizable": false, "elastic": false,
              "elastic_increment": 0, "free_tier": 0, "cost_per_request": 0,
              "stats": { "throughput": 400, "throughput_max": 900, "availability": 800, "sla_penalty": 300, "maintenance_cost": 100 },
              "effect_text": "", "restriction": "unlimited", "is_active": true
            }
            """);

            var card = CardDefinitionMapper.ToCardDefinition(wire);

            card.ComputeStats.Should().NotBeNull();
            card.ComputeStats!.Throughput.Should().Be(400);
        }
    }

    [Trait("対象", "DataResource カードの stats マッピング")]
    public class DataResourceカードのstatsマッピング
    {
        [Fact(DisplayName = "yield 200・availability 700・sla_penalty 500・maintenance_cost 100 の stats を持つとき、同値の DataResourceStats を持つカード定義になる")]
        public void MapsDataResourceStats()
        {
            var wire = ParseWireCard("""
            {
              "card_id": "TST-DATA", "card_name": "test data", "resource_label": "Database", "faction": "Neutral",
              "card_type": "DataResource", "deploy_turns": 0, "resizable": false, "elastic": false,
              "elastic_increment": 0, "free_tier": 0, "cost_per_request": 0,
              "stats": { "yield": 200, "availability": 700, "sla_penalty": 500, "maintenance_cost": 100 },
              "effect_text": "", "restriction": "unlimited", "is_active": true
            }
            """);

            var card = CardDefinitionMapper.ToCardDefinition(wire);

            card.DataResourceStats.Should().NotBeNull();
            card.DataResourceStats!.Yield.Should().Be(200);
            card.DataResourceStats.Availability.Should().Be(700);
            card.DataResourceStats.SLAPenalty.Should().Be(500);
            card.DataResourceStats.MaintenanceCost.Should().Be(100);
            card.ComputeStats.Should().BeNull();
        }
    }

    [Trait("対象", "サポートカードの stats マッピング")]
    public class サポートカードのstatsマッピング
    {
        [Fact(DisplayName = "stats が空オブジェクトのとき、Compute・DataResource いずれのステータスも持たない")]
        public void MapsNoStats()
        {
            var wire = ParseWireCard("""
            {
              "card_id": "TST-SUPPORT", "card_name": "test support", "resource_label": "", "faction": "Neutral",
              "card_type": "Attachment", "deploy_turns": 0, "resizable": false, "elastic": false,
              "elastic_increment": 0, "free_tier": 0, "cost_per_request": 0,
              "stats": {},
              "effect_text": "", "restriction": "unlimited", "is_active": true
            }
            """);

            var card = CardDefinitionMapper.ToCardDefinition(wire);

            card.ComputeStats.Should().BeNull();
            card.DataResourceStats.Should().BeNull();
        }
    }

    [Trait("対象", "Compute カードの stats 異常系")]
    public class Computeカードのstats異常系
    {
        [Theory(DisplayName = "Compute カードの stats が不正なとき、原因ごとに対応するメッセージの例外になる")]
        [InlineData(
            "必須キーが欠落しているとき",
            """{ "throughput": 400, "availability": 800, "sla_penalty": 300 }""",
            "*missing required key 'maintenance_cost'*")]
        [InlineData(
            "契約に無い未知のキーを含むとき",
            """{ "throughput": 400, "availability": 800, "sla_penalty": 300, "maintenance_cost": 100, "unknown_key": 1 }""",
            "*unrecognized key 'unknown_key'*")]
        [InlineData(
            "stats が空オブジェクトのとき",
            "{}",
            "*missing required key 'throughput'*")]
        public void Throws(string caseLabel, string statsJson, string expectedMessagePattern)
        {
            var wire = ParseWireCard($$"""
            {
              "card_id": "TST-COMPUTE-BAD", "card_name": "test compute", "resource_label": "VM", "faction": "Neutral",
              "card_type": "Compute", "deploy_turns": 0, "resizable": false, "elastic": false,
              "elastic_increment": 0, "free_tier": 0, "cost_per_request": 0,
              "stats": {{statsJson}},
              "effect_text": "", "restriction": "unlimited", "is_active": true
            }
            """);

            var act = () => CardDefinitionMapper.ToCardDefinition(wire);

            act.Should().Throw<InvalidOperationException>(caseLabel).WithMessage(expectedMessagePattern);
        }
    }

    [Trait("対象", "効果配列のマッピング")]
    public class Effects配列のマッピング
    {
        [Fact(DisplayName = "effects 配列を持つとき、trigger と ops を持つ効果定義のリストにマッピングする")]
        public void MapsEffectsArray()
        {
            var wire = ParseWireCard("""
            {
              "card_id": "TST-EFFECT", "card_name": "test effect", "resource_label": "", "faction": "Neutral",
              "card_type": "Attachment", "deploy_turns": 0, "resizable": false, "elastic": false,
              "elastic_increment": 0, "free_tier": 0, "cost_per_request": 0,
              "stats": {},
              "effect_text": "", "restriction": "unlimited", "is_active": true,
              "effects": [ { "trigger": "on_deploy", "ops": [ { "target_shield": {} } ] } ]
            }
            """);

            var card = CardDefinitionMapper.ToCardDefinition(wire);

            card.Effects.Should().ContainSingle();
            card.Effects![0].Trigger.Should().Be("on_deploy");
            card.Effects[0].Ops.Should().NotBeNull();
        }

        [Fact(DisplayName = "effects キーを持たないとき、効果定義は null になる")]
        public void MapsAbsentEffectsToNull()
        {
            var wire = ParseWireCard("""
            {
              "card_id": "TST-NOEFFECT", "card_name": "test no effect", "resource_label": "VM", "faction": "Neutral",
              "card_type": "Compute", "deploy_turns": 0, "resizable": false, "elastic": false,
              "elastic_increment": 0, "free_tier": 0, "cost_per_request": 0,
              "stats": { "throughput": 400, "availability": 800, "sla_penalty": 300, "maintenance_cost": 100 },
              "effect_text": "", "restriction": "unlimited", "is_active": true
            }
            """);

            var card = CardDefinitionMapper.ToCardDefinition(wire);

            card.Effects.Should().BeNull();
        }
    }

    [Trait("対象", "施策のマッピング")]
    public class 施策のマッピング
    {
        [Fact(DisplayName = "施策の effect (単一オブジェクト) を持つとき、ops を含む効果定義にマッピングする")]
        public void MapsInitiativeEffect()
        {
            var wire = JsonSerializer.Deserialize<ApiCard.Initiative>("""
            {
              "initiative_id": "TST-IN-01", "product_id": "PD-TEST", "kind": "routine",
              "name": "test initiative", "insight_cost": 100, "effect_text": "",
              "effect": { "ops": [ { "gain_insight": { "amount": 100 } } ] }, "is_active": true
            }
            """)!;

            var initiative = CardDefinitionMapper.ToInitiative(wire);

            initiative.InitiativeId.Should().Be("TST-IN-01");
            initiative.Kind.Should().Be(InitiativeKinds.Routine);
            initiative.Effect.Ops.Should().NotBeNull();
        }
    }

    [Trait("対象", "カード定義マッパー")]
    public class 実データ全件のマッピング
    {
        [Fact(DisplayName = "実 cards_gen.json の全カードをマップすると、Compute と DataResource の全カードで可用性が 1 以上になる")]
        public void AllRealComputeAndDataResourceCardsHaveAvailability()
        {
            var cards = TestEffectSetup.Get().CardCache.All().Values;

            cards.Where(c => c.IsComputeType || c.IsDataResource)
                .Should().OnlyContain(c => c.BaseAvailability >= 1);
        }
    }
}
