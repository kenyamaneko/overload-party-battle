using OverloadParty.Battle.Npc;

namespace OverloadParty.Battle.Tests.Npc;

[Trait("対象", "AI 設定の読み込み")]
public class AiConfigLoaderTests
{
    [Fact(DisplayName = "最小構成の設定から model と faction を読み込む")]
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

    [Fact(DisplayName = "構造化形式の deck を card_id と copies に読み込む")]
    public void LoadFromString_Deck_StructuredFormat()
    {
        var yaml = """
            model: test
            faction: SHE
            deck:
              - card_id: TST-0001
                copies: 3
              - card_id: TST-0002
                copies: 1
            """;

        var config = AiConfigLoader.LoadFromString(yaml);

        config.Deck.Should().HaveCount(2);
        config.Deck[0].CardId.Should().Be("TST-0001");
        config.Deck[0].Copies.Should().Be(3);
        config.Deck[1].CardId.Should().Be("TST-0002");
        config.Deck[1].Copies.Should().Be(1);
    }

    [Fact(DisplayName = "effect_priorities のスカラー値を優先度として読み込む")]
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

    [Fact(DisplayName = "effect_priorities のオブジェクト値を priority・low_priority・threshold に読み込む")]
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

    [Fact(DisplayName = "effect_priorities の condition を selector と min に読み込む")]
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

    [Fact(DisplayName = "deploy の priorities・zone_preferences と branch_choices を読み込む")]
    public void LoadFromString_DeployConfig_Priorities()
    {
        var yaml = """
            model: test
            faction: SHE
            branch_choices:
              TST-0003: use
            deploy:
              priorities:
                - card_id: TST-0001
                  priority: 80
                - card_type: Compute
                  priority: 50
              zone_preferences:
                Compute: [frontend, backend]
                DataResource: [backend]
            """;

        var config = AiConfigLoader.LoadFromString(yaml);

        config.Deploy.Priorities.Should().HaveCount(2);
        config.Deploy.Priorities[0].CardId.Should().Be("TST-0001");
        config.Deploy.Priorities[0].Priority.Should().Be(80);
        config.Deploy.Priorities[1].CardType.Should().Be("Compute");
        config.BranchChoices!["TST-0003"].Should().Be("use");
        config.Deploy.ZonePreferences!["Compute"].Should().Equal("frontend", "backend");
    }

    [Fact(DisplayName = "deploy の conditional_priorities を condition と fallback_priority ごと読み込む")]
    public void LoadFromString_ConditionalPriorities()
    {
        var yaml = """
            model: test
            faction: Tenki
            deploy:
              conditional_priorities:
                - card_id: TST-0004
                  priority: 90
                  condition:
                    selector: { owner: myself }
                    card_id: [TST-0005]
                    min: 1
                  fallback_priority: 70
            """;

        var config = AiConfigLoader.LoadFromString(yaml);

        var cp = config.Deploy.ConditionalPriorities![0];
        cp.CardId.Should().Be("TST-0004");
        cp.Priority.Should().Be(90);
        cp.FallbackPriority.Should().Be(70);
        cp.Condition.Selector!.Owner.Should().Be("myself");
        cp.Condition.CardId.Should().Equal("TST-0005");
        cp.Condition.Min.Should().Be(1);
    }

    [Fact(DisplayName = "game_phases の condition・target_selection・effect_priorities を読み込む")]
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
                    selector: { owner: myself }
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
        late.Condition.Count!.Selector!.Owner.Should().Be("myself");
        late.Condition.Count.Min.Should().Be(3);
        late.TargetSelection!.Attack.Should().NotBeNull();
        late.TargetSelection!.Attack!.OrderBy.Should().Be("tp_desc");
        late.EffectPriorities!["single_damage"].Priority.Should().Be(80);
    }

    [Fact(DisplayName = "scale_up の instance_family・conditional_family・max_maintenance_ratio を読み込む")]
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
                    selector: { owner: myself, card_type: DataResource, zone: backend }
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
        cf.Condition.Selector!.CardType.Should().Be("DataResource");
    }

    [Fact(DisplayName = "reactive の max_slots と priorities を読み込む")]
    public void LoadFromString_Reactive()
    {
        var yaml = """
            model: test
            faction: SHE
            reactive:
              max_slots: 2
              priorities:
                TST-0006: 90
                TST-0007: 70
            """;

        var config = AiConfigLoader.LoadFromString(yaml);

        config.Reactive!.MaxSlots.Should().Be(2);
        config.Reactive.Priorities["TST-0006"].Should().Be(90);
        config.Reactive.Priorities["TST-0007"].Should().Be(70);
    }

    [Fact(DisplayName = "スカラーとオブジェクトが混在する effect_priorities を両方読み込む")]
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

    [Fact(DisplayName = "ディレクトリ内の全 YAML を読み込み、8 件の設定を得る")]
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
