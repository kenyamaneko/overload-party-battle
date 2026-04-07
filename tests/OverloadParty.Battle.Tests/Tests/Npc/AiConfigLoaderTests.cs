using OverloadParty.Battle.Npc;

namespace OverloadParty.Battle.Tests.Npc;

public class AiConfigLoaderTests
{
    [Fact]
    public void LoadFromString_MinimalConfig_DeserializesModel()
    {
        var yaml = """
            model: SHE-easy
            faction: SHE
            """;

        var config = AiConfigLoader.LoadFromString(yaml);

        config.Model.Should().Be("SHE-easy");
        config.Faction.Should().Be("SHE");
    }

    [Fact]
    public void LoadFromString_Deck_StructuredFormat()
    {
        var yaml = """
            model: test
            faction: SHE
            deck:
              - card_id: SH-0001
                copies: 3
              - card_id: SH-0002
                copies: 1
            """;

        var config = AiConfigLoader.LoadFromString(yaml);

        config.Deck.Should().HaveCount(2);
        config.Deck[0].CardId.Should().Be("SH-0001");
        config.Deck[0].Copies.Should().Be(3);
        config.Deck[1].CardId.Should().Be("SH-0002");
        config.Deck[1].Copies.Should().Be(1);
    }

    [Fact]
    public void LoadFromString_EffectPriority_ScalarInt()
    {
        var yaml = """
            model: test
            faction: SHE
            effect_priorities:
              deploy_free: 75
              single_damage: 60
            """;

        var config = AiConfigLoader.LoadFromString(yaml);

        config.EffectPriorities["deploy_free"].Priority.Should().Be(75);
        config.EffectPriorities["single_damage"].Priority.Should().Be(60);
    }

    [Fact]
    public void LoadFromString_EffectPriority_ObjectWithThreshold()
    {
        var yaml = """
            model: test
            faction: SHE
            effect_priorities:
              budget_gain:
                priority: 90
                low_priority: 40
                threshold: 1500
            """;

        var config = AiConfigLoader.LoadFromString(yaml);

        var entry = config.EffectPriorities["budget_gain"];
        entry.Priority.Should().Be(90);
        entry.LowPriority.Should().Be(40);
        entry.Threshold.Should().Be(1500);
    }

    [Fact]
    public void LoadFromString_EffectPriority_ObjectWithCondition()
    {
        var yaml = """
            model: test
            faction: SHE
            effect_priorities:
              debuff:
                priority: 55
                condition:
                  selector: { owner: opponent }
                  min: 1
            """;

        var config = AiConfigLoader.LoadFromString(yaml);

        var entry = config.EffectPriorities["debuff"];
        entry.Priority.Should().Be(55);
        entry.Condition.Should().NotBeNull();
        entry.Condition!.Selector!.Owner.Should().Be("opponent");
        entry.Condition.Min.Should().Be(1);
    }

    [Fact]
    public void LoadFromString_DeployConfig_Priorities()
    {
        var yaml = """
            model: test
            faction: SHE
            deploy:
              priorities:
                - card_id: SH-0001
                  priority: 80
                - card_type: compute
                  priority: 50
              choices:
                SH-0006: use
              zone_preferences:
                compute: [frontend, backend]
                data: [backend]
            """;

        var config = AiConfigLoader.LoadFromString(yaml);

        config.Deploy.Priorities.Should().HaveCount(2);
        config.Deploy.Priorities[0].CardId.Should().Be("SH-0001");
        config.Deploy.Priorities[0].Priority.Should().Be(80);
        config.Deploy.Priorities[1].CardType.Should().Be("compute");
        config.Deploy.Choices!["SH-0006"].Should().Be("use");
        config.Deploy.ZonePreferences!["compute"].Should().Equal("frontend", "backend");
    }

    [Fact]
    public void LoadFromString_ConditionalPriorities()
    {
        var yaml = """
            model: test
            faction: Tenki
            deploy:
              conditional_priorities:
                - card_id: TK-0005
                  priority: 90
                  condition:
                    selector: { owner: self }
                    card_id: [TK-0010]
                    min: 1
                  fallback_priority: 70
            """;

        var config = AiConfigLoader.LoadFromString(yaml);

        var cp = config.Deploy.ConditionalPriorities![0];
        cp.CardId.Should().Be("TK-0005");
        cp.Priority.Should().Be(90);
        cp.FallbackPriority.Should().Be(70);
        cp.Condition.Selector!.Owner.Should().Be("self");
        cp.Condition.CardId.Should().Equal("TK-0010");
        cp.Condition.Min.Should().Be(1);
    }

    [Fact]
    public void LoadFromString_GamePhases()
    {
        var yaml = """
            model: test
            faction: SHE
            game_phases:
              late:
                condition:
                  turn_min: 6
                  count:
                    selector: { owner: self }
                    min: 3
                target_selection:
                  attack:
                    selector: { owner: opponent }
                    order_by: tp_desc
                effect_priorities:
                  single_damage: 80
            """;

        var config = AiConfigLoader.LoadFromString(yaml);

        var late = config.GamePhases!.Late!;
        late.Condition.TurnMin.Should().Be(6);
        late.Condition.Count!.Selector!.Owner.Should().Be("self");
        late.Condition.Count.Min.Should().Be(3);
        late.TargetSelection!.Attack.Should().NotBeNull();
        late.TargetSelection!.Attack!.OrderBy.Should().Be("tp_desc");
        late.EffectPriorities!["single_damage"].Priority.Should().Be(80);
    }

    [Fact]
    public void LoadFromString_ScaleUp()
    {
        var yaml = """
            model: test
            faction: Tenki
            scale_up:
              instance_family: R
              conditional_family:
                - family: M
                  condition:
                    selector: { owner: self, card_type: data, zone: backend }
                    min: 2
              max_maintenance_ratio: 0.6
              order_by: tp_desc
            """;

        var config = AiConfigLoader.LoadFromString(yaml);

        config.ScaleUp.InstanceFamily.Should().Be("R");
        config.ScaleUp.MaxMaintenanceRatio.Should().Be(0.6);
        var cf = config.ScaleUp.ConditionalFamily![0];
        cf.Family.Should().Be("M");
        cf.Condition.Selector!.Zone.Should().Be("backend");
        cf.Condition.Selector!.CardType.Should().Be("data");
    }

    [Fact]
    public void LoadFromString_Reactive()
    {
        var yaml = """
            model: test
            faction: SHE
            reactive:
              max_slots: 2
              priorities:
                NT-0025: 90
                NT-0024: 70
            """;

        var config = AiConfigLoader.LoadFromString(yaml);

        config.Reactive!.MaxSlots.Should().Be(2);
        config.Reactive.Priorities["NT-0025"].Should().Be(90);
        config.Reactive.Priorities["NT-0024"].Should().Be(70);
    }

    [Fact]
    public void LoadFromString_MixedEffectPriorities_ScalarAndObject()
    {
        var yaml = """
            model: test
            faction: SHE
            effect_priorities:
              deploy_free: 75
              budget_gain:
                priority: 90
                low_priority: 40
                threshold: 1500
              single_damage: 60
              draw:
                priority: 80
                low_priority: 30
                hand_threshold: 3
            """;

        var config = AiConfigLoader.LoadFromString(yaml);

        config.EffectPriorities["deploy_free"].Priority.Should().Be(75);
        config.EffectPriorities["deploy_free"].LowPriority.Should().BeNull();
        config.EffectPriorities["budget_gain"].Priority.Should().Be(90);
        config.EffectPriorities["budget_gain"].Threshold.Should().Be(1500);
        config.EffectPriorities["single_damage"].Priority.Should().Be(60);
        config.EffectPriorities["draw"].HandThreshold.Should().Be(3);
    }

    [Fact]
    public void LoadAll_LoadsAllYamlFiles()
    {
        var dir = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "OverloadParty.Battle.Npc", "Data");

        if (!Directory.Exists(dir))
        {
            return; // skip if data dir not reachable
        }

        var configs = AiConfigLoader.LoadAll(dir);

        configs.Should().ContainKey("SHE-easy");
        configs.Should().ContainKey("Tenki-hard");
        configs.Count.Should().Be(8);
    }
}
