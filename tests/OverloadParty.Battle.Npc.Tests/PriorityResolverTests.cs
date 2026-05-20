using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Models;
using OverloadParty.Battle.Npc;

namespace OverloadParty.Battle.Tests.Npc;

public class PriorityResolverTests
{
    private readonly TestCardCache _cc = new();

    public PriorityResolverTests()
    {
        _cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", tp: 600, av: 1400));
        _cc.Add(TestFactory.DataCard(cardId: "TST-0002", subtype: "Database", yield: 400, av: 800));
        _cc.Add(TestFactory.PlatformCard(cardId: "TEST-0200", name: "TestPlatform"));
    }

    private DecisionContext MakeCtx(
        Field? field = null, Field? oppField = null,
        List<UndeployedCard>? hand = null, long budget = 5000)
    {
        return new DecisionContext(
            field ?? TestFactory.MakeField(),
            oppField ?? TestFactory.MakeField(),
            hand ?? [],
            budget,
            _cc);
    }

    private static AiConfig MakeConfig(Dictionary<string, EffectPriorityEntry>? priorities = null)
    {
        return new AiConfig
        {
            Model = "test",
            Faction = "SHE",
            EffectPriorities = priorities ?? new(),
            TargetSelection = new TargetSelectionConfig(),
        };
    }

    // ─── Resolve ────────────────────────────────────────────────

    [Fact]
    public void Resolve_BudgetGain_LowBudget_ReturnsHighPriority()
    {
        var ctx = MakeCtx(budget: 500);
        var config = MakeConfig(new()
        {
            ["budget_gain"] = new EffectPriorityEntry
            {
                Priority = 90,
                LowPriority = 40,
                Threshold = 1500,
            },
        });
        var info = new EffectInfo();

        var (pri, use) = PriorityResolver.Resolve(
            EffectCategory.BudgetGain, info, ctx, config, _cc);

        use.Should().BeTrue();
        pri.Should().Be(90);
    }

    [Fact]
    public void Resolve_BudgetGain_HighBudget_ReturnsLowPriority()
    {
        var ctx = MakeCtx(budget: 5000);
        var config = MakeConfig(new()
        {
            ["budget_gain"] = new EffectPriorityEntry
            {
                Priority = 90,
                LowPriority = 40,
                Threshold = 1500,
            },
        });
        var info = new EffectInfo();

        var (pri, use) = PriorityResolver.Resolve(
            EffectCategory.BudgetGain, info, ctx, config, _cc);

        use.Should().BeTrue();
        pri.Should().Be(40);
    }

    [Fact]
    public void Resolve_Draw_FewCards_ReturnsHighPriority()
    {
        var hand = new List<UndeployedCard>
        {
            new() { InstanceID = "h1", CardID = "TST-0001" },
            new() { InstanceID = "h2", CardID = "TST-0001" },
        };
        var ctx = MakeCtx(hand: hand);
        var config = MakeConfig(new()
        {
            ["draw"] = new EffectPriorityEntry
            {
                Priority = 80,
                LowPriority = 30,
                HandThreshold = 3,
            },
        });
        var info = new EffectInfo();

        var (pri, use) = PriorityResolver.Resolve(
            EffectCategory.Draw, info, ctx, config, _cc);

        use.Should().BeTrue();
        pri.Should().Be(80);
    }

    [Fact]
    public void Resolve_Draw_ManyCards_ReturnsLowPriority()
    {
        var hand = Enumerable.Range(0, 5)
            .Select(i => new UndeployedCard { InstanceID = $"h_{i}", CardID = "TST-0001" })
            .ToList();
        var ctx = MakeCtx(hand: hand);
        var config = MakeConfig(new()
        {
            ["draw"] = new EffectPriorityEntry
            {
                Priority = 80,
                LowPriority = 30,
                HandThreshold = 3,
            },
        });
        var info = new EffectInfo();

        var (pri, use) = PriorityResolver.Resolve(
            EffectCategory.Draw, info, ctx, config, _cc);

        use.Should().BeTrue();
        pri.Should().Be(30);
    }

    [Fact]
    public void Resolve_SimplePriority_ReturnsConfiguredValue()
    {
        var ctx = MakeCtx();
        var config = MakeConfig(new()
        {
            ["deploy_free"] = new EffectPriorityEntry { Priority = 75 },
        });
        var info = new EffectInfo();

        var (pri, use) = PriorityResolver.Resolve(
            EffectCategory.DeployFree, info, ctx, config, _cc);

        use.Should().BeTrue();
        pri.Should().Be(75);
    }

    [Fact]
    public void Resolve_CategoryNotInConfig_ReturnsNotUsable()
    {
        var ctx = MakeCtx();
        var config = MakeConfig(new()); // empty priorities
        var info = new EffectInfo();

        var (_, use) = PriorityResolver.Resolve(
            EffectCategory.BudgetGain, info, ctx, config, _cc);

        use.Should().BeFalse();
    }

    [Fact]
    public void Resolve_ReactiveCategory_NotUsable()
    {
        var ctx = MakeCtx();
        var config = MakeConfig(new()
        {
            ["cancel_action"] = new EffectPriorityEntry { Priority = 99 },
        });
        var info = new EffectInfo();

        var (_, use) = PriorityResolver.Resolve(
            EffectCategory.CancelAction, info, ctx, config, _cc);

        use.Should().BeFalse();
    }

    [Fact]
    public void Resolve_SingleDamage_NoTargets_NotUsable()
    {
        var ctx = MakeCtx(); // empty opponent field
        var config = MakeConfig(new()
        {
            ["single_damage"] = new EffectPriorityEntry { Priority = 60 },
        });
        var info = new EffectInfo { TargetZone = Zones.Frontend };

        var (_, use) = PriorityResolver.Resolve(
            EffectCategory.SingleDamage, info, ctx, config, _cc);

        use.Should().BeFalse();
    }

    [Fact]
    public void Resolve_SingleDamage_WithTargets_Usable()
    {
        var oppField = TestFactory.MakeField();
        oppField.Frontend[0] = TestFactory.MakeResource(instanceId: "t1");
        var ctx = MakeCtx(oppField: oppField);
        var config = MakeConfig(new()
        {
            ["single_damage"] = new EffectPriorityEntry { Priority = 60 },
        });
        var info = new EffectInfo { TargetZone = Zones.Frontend };

        var (pri, use) = PriorityResolver.Resolve(
            EffectCategory.SingleDamage, info, ctx, config, _cc);

        use.Should().BeTrue();
        pri.Should().Be(60);
    }

    [Fact]
    public void Resolve_Heal_NoDamage_NotUsable()
    {
        var field = TestFactory.MakeField();
        field.Frontend[0] = TestFactory.MakeResource(damage: 0);
        var ctx = MakeCtx(field: field);
        var config = MakeConfig(new()
        {
            ["heal"] = new EffectPriorityEntry { Priority = 50 },
        });
        var info = new EffectInfo();

        var (_, use) = PriorityResolver.Resolve(
            EffectCategory.Heal, info, ctx, config, _cc);

        use.Should().BeFalse();
    }

    [Fact]
    public void Resolve_WithEntryCondition_NotMet_NotUsable()
    {
        var ctx = MakeCtx(); // empty opponent field
        var config = MakeConfig(new()
        {
            ["debuff"] = new EffectPriorityEntry
            {
                Priority = 55,
                Condition = new ConditionDef
                {
                    Selector = new SelectorDef { Owner = "opponent" },
                    Min = 1,
                },
            },
        });
        var info = new EffectInfo();

        var (_, use) = PriorityResolver.Resolve(
            EffectCategory.Debuff, info, ctx, config, _cc);

        use.Should().BeFalse();
    }

    // ─── Evaluate (full card evaluation) ────────────────────────

    [Fact]
    public void Evaluate_NoEffectInfo_ReturnsNotUsable()
    {
        var ctx = MakeCtx();
        var config = MakeConfig();
        var effects = new NullEffectRegistry();

        var (_, use, _) = PriorityResolver.Evaluate(
            "UNKNOWN", TriggerType.Ignition, ctx, config, effects, _cc);

        use.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_WithEffect_ReturnsConfigPriority()
    {
        var reg = new StubEffectRegistry();
        reg.SetEffectInfo("TST-0003", TriggerType.Ignition, new EffectInfo
        {
            TargetType = EffectTargetType.None,
        }.WithCategory(EffectCategory.BudgetGain));

        var ctx = MakeCtx(budget: 500);
        var config = MakeConfig(new()
        {
            ["budget_gain"] = new EffectPriorityEntry
            {
                Priority = 90,
                LowPriority = 40,
                Threshold = 1500,
            },
        });

        var (pri, use, _) = PriorityResolver.Evaluate(
            "TST-0003", TriggerType.Ignition, ctx, config, reg, _cc);

        use.Should().BeTrue();
        pri.Should().Be(90);
    }

    // ─── SelectTarget ───────────────────────────────────────────

    [Fact]
    public void SelectTarget_throws_when_no_category_has_a_resolvable_spec()
    {
        var ctx = MakeCtx();
        var info = new EffectInfo().WithCategory(EffectCategory.BudgetGain);

        var act = () => PriorityResolver.SelectTarget(info, ctx, new TargetSelectionConfig(), _cc);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void SelectTarget_throws_when_category_spec_is_unconfigured()
    {
        var ctx = MakeCtx();
        var info = new EffectInfo().WithCategory(EffectCategory.SingleDamage);

        var act = () => PriorityResolver.SelectTarget(info, ctx, new TargetSelectionConfig(), _cc);

        act.Should().Throw<InvalidOperationException>();
    }

    // ─── Test doubles ───────────────────────────────────────────

    private class NullEffectRegistry : IEffectRegistry
    {
        public EffectHandler? Get(string cardId, TriggerType trigger) => null;
        public bool Has(string cardId, TriggerType trigger) => false;
        public BudgetRequirement? GetBudgetRequirement(string cardId, TriggerType trigger) => null;
        public EffectInfo? GetEffectInfo(string cardId, TriggerType trigger) => null;
        public List<string>? GetChoiceOptions(string cardId, TriggerType trigger) => null;
        public IEffectOp[]? GetOps(string cardId, TriggerType trigger) => null;
    }

    private class StubEffectRegistry : IEffectRegistry
    {
        private readonly Dictionary<(string, TriggerType), EffectInfo> _infos = new();

        public void SetEffectInfo(string cardId, TriggerType trigger, EffectInfo info)
            => _infos[(cardId, trigger)] = info;

        public EffectHandler? Get(string cardId, TriggerType trigger) => null;
        public bool Has(string cardId, TriggerType trigger) => _infos.ContainsKey((cardId, trigger));
        public BudgetRequirement? GetBudgetRequirement(string cardId, TriggerType trigger) => null;
        public EffectInfo? GetEffectInfo(string cardId, TriggerType trigger) =>
            _infos.GetValueOrDefault((cardId, trigger));
        public List<string>? GetChoiceOptions(string cardId, TriggerType trigger) => null;
        public IEffectOp[]? GetOps(string cardId, TriggerType trigger) => null;
    }
}
