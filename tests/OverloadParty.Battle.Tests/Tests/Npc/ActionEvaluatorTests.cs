using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Models;
using OverloadParty.Battle.Npc;

namespace OverloadParty.Battle.Tests.Tests.Npc;

public class ActionEvaluatorTests
{
    private readonly TestCardCache _cc = new();
    private readonly NullEffectRegistry _effects = new();

    public ActionEvaluatorTests()
    {
        _cc.Add(TestFactory.ComputeCard(cardNo: 1, tp: 600, av: 1400, mc: 150));
        _cc.Add(TestFactory.DataCard(cardNo: 100, cardType: CardTypes.Database, yield: 400, av: 800, mc: 100));
        _cc.Add(TestFactory.PlatformCard(cardNo: 200, name: "TestPlatform"));
    }

    private DecisionContext MakeCtx(
        Field? field = null, Field? oppField = null,
        List<HandCard>? hand = null, long budget = 5000)
    {
        var ai = new StandardAi(_cc, _effects);
        return new DecisionContext(
            field ?? TestFactory.MakeField(),
            oppField ?? TestFactory.MakeField(),
            hand ?? [],
            budget,
            ai);
    }

    // ─── EvaluateCard ───────────────────────────────────────────

    [Fact]
    public void EvaluateCard_NoEffectInfo_ReturnsNotUsable()
    {
        var ctx = MakeCtx();

        var (priority, use, choice) = ActionEvaluator.EvaluateCard(
            999, TriggerType.Activate, ctx, _effects, _cc);

        use.Should().BeFalse();
        priority.Should().Be(0);
        choice.Should().BeNull();
    }

    [Fact]
    public void EvaluateCard_WithBudgetGainEffect_LowBudget_ReturnsHighPriority()
    {
        var reg = new StubEffectRegistry();
        reg.SetEffectInfo(10, TriggerType.Activate, new EffectInfo
        {
            TargetType = EffectTargetType.None,
        }.WithCategory(EffectCategory.BudgetGain));

        var ctx = MakeCtx(budget: 500); // below LowBudgetThreshold

        var (priority, use, _) = ActionEvaluator.EvaluateCard(
            10, TriggerType.Activate, ctx, reg, _cc);

        use.Should().BeTrue();
        priority.Should().Be(NpcParams.PriBudgetGainHigh);
    }

    [Fact]
    public void EvaluateCard_WithBudgetGainEffect_HighBudget_ReturnsLowPriority()
    {
        var reg = new StubEffectRegistry();
        reg.SetEffectInfo(10, TriggerType.Activate, new EffectInfo
        {
            TargetType = EffectTargetType.None,
        }.WithCategory(EffectCategory.BudgetGain));

        var ctx = MakeCtx(budget: 5000); // above LowBudgetThreshold

        var (priority, use, _) = ActionEvaluator.EvaluateCard(
            10, TriggerType.Activate, ctx, reg, _cc);

        use.Should().BeTrue();
        priority.Should().Be(NpcParams.PriBudgetGainLow);
    }

    [Fact]
    public void EvaluateCard_ChoiceTarget_PopulatesChoiceData()
    {
        var oppField = TestFactory.MakeField();
        oppField.Frontend[0] = TestFactory.MakeResource(instanceId: "target_1", maxAV: 500);

        var reg = new StubEffectRegistry();
        var info = new EffectInfo
        {
            TargetType = EffectTargetType.Choice,
            TargetZone = GameConstants.ZoneFrontend,
        }.WithCategory(EffectCategory.SingleDamage);
        reg.SetEffectInfo(10, TriggerType.Activate, info);

        var ctx = MakeCtx(oppField: oppField);

        var (_, use, choice) = ActionEvaluator.EvaluateCard(
            10, TriggerType.Activate, ctx, reg, _cc);

        use.Should().BeTrue();
        choice.Should().NotBeNull();
        choice!["instanceId"].Should().Be("target_1");
    }

    [Fact]
    public void EvaluateCard_ChoiceTarget_NoValidTarget_ReturnsNotUsable()
    {
        var reg = new StubEffectRegistry();
        var info = new EffectInfo
        {
            TargetType = EffectTargetType.Choice,
            TargetZone = GameConstants.ZoneFrontend,
        }.WithCategory(EffectCategory.SingleDamage);
        reg.SetEffectInfo(10, TriggerType.Activate, info);

        var ctx = MakeCtx(); // empty opponent field

        var (_, use, _) = ActionEvaluator.EvaluateCard(
            10, TriggerType.Activate, ctx, reg, _cc);

        use.Should().BeFalse();
    }

    [Fact]
    public void EvaluateCard_ConditionNotMet_ReturnsNotUsable()
    {
        var reg = new StubEffectRegistry();
        var info = new EffectInfo
        {
            TargetType = EffectTargetType.None,
        }.WithCategory(EffectCategory.BudgetGain);
        info.Conditions.Add(new EffectCondition { Type = "min_budget", Value = 3000 });
        reg.SetEffectInfo(10, TriggerType.Activate, info);

        var ctx = MakeCtx(budget: 1000); // below min_budget condition

        var (_, use, _) = ActionEvaluator.EvaluateCard(
            10, TriggerType.Activate, ctx, reg, _cc);

        use.Should().BeFalse();
    }

    // ─── EvaluateCategory ───────────────────────────────────────

    [Fact]
    public void EvaluateCategory_BudgetGain_LowBudget_ReturnsHighPriority()
    {
        var ctx = MakeCtx(budget: 500);
        var info = new EffectInfo();

        var (pri, use) = ActionEvaluator.EvaluateCategory(EffectCategory.BudgetGain, info, ctx);

        use.Should().BeTrue();
        pri.Should().Be(NpcParams.PriBudgetGainHigh);
    }

    [Fact]
    public void EvaluateCategory_BudgetGain_HighBudget_ReturnsLowPriority()
    {
        var ctx = MakeCtx(budget: 5000);
        var info = new EffectInfo();

        var (pri, use) = ActionEvaluator.EvaluateCategory(EffectCategory.BudgetGain, info, ctx);

        use.Should().BeTrue();
        pri.Should().Be(NpcParams.PriBudgetGainLow);
    }

    [Theory]
    [InlineData(EffectCategory.BudgetPenalty)]
    [InlineData(EffectCategory.InsightGain)]
    [InlineData(EffectCategory.DeployFree)]
    [InlineData(EffectCategory.RecoverCard)]
    public void EvaluateCategory_AlwaysUsableCategories_ReturnsUsableWithCorrectPriority(EffectCategory cat)
    {
        var ctx = MakeCtx();
        var info = new EffectInfo();

        var (pri, use) = ActionEvaluator.EvaluateCategory(cat, info, ctx);

        use.Should().BeTrue();
        int expectedPri = cat switch
        {
            EffectCategory.BudgetPenalty => NpcParams.PriBudgetPenalty,
            EffectCategory.InsightGain => NpcParams.PriInsightGain,
            EffectCategory.DeployFree => NpcParams.PriDeployFree,
            EffectCategory.RecoverCard => NpcParams.PriRecoverCard,
            _ => throw new ArgumentException($"Unexpected category: {cat}"),
        };
        pri.Should().Be(expectedPri);
    }

    [Fact]
    public void EvaluateCategory_InsightAbsorb_WithOppResources_Usable()
    {
        var oppField = TestFactory.MakeField();
        oppField.Frontend[0] = TestFactory.MakeResource(instanceId: "opp1");
        var ctx = MakeCtx(oppField: oppField);
        var info = new EffectInfo();

        var (pri, use) = ActionEvaluator.EvaluateCategory(EffectCategory.InsightAbsorb, info, ctx);

        use.Should().BeTrue();
        pri.Should().Be(NpcParams.PriInsightAbsorb);
    }

    [Theory]
    [InlineData(EffectCategory.InsightAbsorb)]
    [InlineData(EffectCategory.Debuff)]
    public void EvaluateCategory_NeedsOppResources_EmptyOppField_NotUsable(EffectCategory cat)
    {
        var ctx = MakeCtx();
        var info = new EffectInfo();

        var (_, use) = ActionEvaluator.EvaluateCategory(cat, info, ctx);

        use.Should().BeFalse();
    }

    [Theory]
    [InlineData(2, true, "PriDrawHigh")]
    [InlineData(3, true, "PriDrawHigh")]
    [InlineData(4, true, "PriDrawLow")]
    [InlineData(6, true, "PriDrawLow")]
    public void EvaluateCategory_Draw_PriorityDependsOnHandSize(
        int handSize, bool expectedUse, string expectedPriLabel)
    {
        var hand = Enumerable.Range(0, handSize)
            .Select(i => new HandCard { InstanceID = $"h_{i}", CardID = 1 })
            .ToList();
        var ctx = MakeCtx(hand: hand);
        var info = new EffectInfo();

        var (pri, use) = ActionEvaluator.EvaluateCategory(EffectCategory.Draw, info, ctx);

        use.Should().Be(expectedUse);
        int expectedPri = expectedPriLabel switch
        {
            "PriDrawHigh" => NpcParams.PriDrawHigh,
            "PriDrawLow" => NpcParams.PriDrawLow,
            _ => 0,
        };
        pri.Should().Be(expectedPri);
    }

    [Theory]
    [InlineData(2, true, "PriSearchHigh")]
    [InlineData(3, true, "PriSearchHigh")]
    [InlineData(4, true, "PriSearchLow")]
    public void EvaluateCategory_Search_PriorityDependsOnHandSize(
        int handSize, bool expectedUse, string expectedPriLabel)
    {
        var hand = Enumerable.Range(0, handSize)
            .Select(i => new HandCard { InstanceID = $"h_{i}", CardID = 1 })
            .ToList();
        var ctx = MakeCtx(hand: hand);
        var info = new EffectInfo();

        var (pri, use) = ActionEvaluator.EvaluateCategory(EffectCategory.Search, info, ctx);

        use.Should().Be(expectedUse);
        int expectedPri = expectedPriLabel switch
        {
            "PriSearchHigh" => NpcParams.PriSearchHigh,
            "PriSearchLow" => NpcParams.PriSearchLow,
            _ => 0,
        };
        pri.Should().Be(expectedPri);
    }

    [Fact]
    public void EvaluateCategory_AoEDamage_TwoOrMoreTargets_Usable()
    {
        var oppField = TestFactory.MakeField();
        oppField.Frontend[0] = TestFactory.MakeResource(instanceId: "t1");
        oppField.Frontend[1] = TestFactory.MakeResource(instanceId: "t2");
        var ctx = MakeCtx(oppField: oppField);
        var info = new EffectInfo { TargetZone = GameConstants.ZoneFrontend };

        var (pri, use) = ActionEvaluator.EvaluateCategory(EffectCategory.AoEDamage, info, ctx);

        use.Should().BeTrue();
        pri.Should().Be(NpcParams.PriAoEDamage);
    }

    [Fact]
    public void EvaluateCategory_AoEDamage_OneTarget_NotUsable()
    {
        var oppField = TestFactory.MakeField();
        oppField.Frontend[0] = TestFactory.MakeResource(instanceId: "t1");
        var ctx = MakeCtx(oppField: oppField);
        var info = new EffectInfo { TargetZone = GameConstants.ZoneFrontend };

        var (_, use) = ActionEvaluator.EvaluateCategory(EffectCategory.AoEDamage, info, ctx);

        use.Should().BeFalse();
    }

    [Fact]
    public void EvaluateCategory_SingleDamage_WithTarget_Usable()
    {
        var oppField = TestFactory.MakeField();
        oppField.Frontend[0] = TestFactory.MakeResource(instanceId: "t1");
        var ctx = MakeCtx(oppField: oppField);
        var info = new EffectInfo { TargetZone = GameConstants.ZoneFrontend };

        var (pri, use) = ActionEvaluator.EvaluateCategory(EffectCategory.SingleDamage, info, ctx);

        use.Should().BeTrue();
        pri.Should().Be(NpcParams.PriSingleDamage);
    }

    [Fact]
    public void EvaluateCategory_SingleDamage_NoTarget_NotUsable()
    {
        var ctx = MakeCtx();
        var info = new EffectInfo { TargetZone = GameConstants.ZoneFrontend };

        var (_, use) = ActionEvaluator.EvaluateCategory(EffectCategory.SingleDamage, info, ctx);

        use.Should().BeFalse();
    }

    [Fact]
    public void EvaluateCategory_Debuff_WithOppResources_Usable()
    {
        var oppField = TestFactory.MakeField();
        oppField.Frontend[0] = TestFactory.MakeResource(instanceId: "t1");
        var ctx = MakeCtx(oppField: oppField);
        var info = new EffectInfo();

        var (pri, use) = ActionEvaluator.EvaluateCategory(EffectCategory.Debuff, info, ctx);

        use.Should().BeTrue();
        pri.Should().Be(NpcParams.PriDebuff);
    }


    [Fact]
    public void EvaluateCategory_Buff_WithOwnResources_Usable()
    {
        var field = TestFactory.MakeField();
        field.Frontend[0] = TestFactory.MakeResource(instanceId: "own1");
        var ctx = MakeCtx(field: field);
        var info = new EffectInfo();

        var (pri, use) = ActionEvaluator.EvaluateCategory(EffectCategory.Buff, info, ctx);

        use.Should().BeTrue();
        pri.Should().Be(NpcParams.PriBuff);
    }

    [Fact]
    public void EvaluateCategory_Buff_EmptyField_NotUsable()
    {
        var ctx = MakeCtx();
        var info = new EffectInfo();

        var (_, use) = ActionEvaluator.EvaluateCategory(EffectCategory.Buff, info, ctx);

        use.Should().BeFalse();
    }

    [Fact]
    public void EvaluateCategory_Heal_WithDamagedResource_Usable()
    {
        var field = TestFactory.MakeField();
        field.Frontend[0] = TestFactory.MakeResource(instanceId: "dmg", damage: 300);
        var ctx = MakeCtx(field: field);
        var info = new EffectInfo();

        var (pri, use) = ActionEvaluator.EvaluateCategory(EffectCategory.Heal, info, ctx);

        use.Should().BeTrue();
        pri.Should().Be(NpcParams.PriHeal);
    }

    [Fact]
    public void EvaluateCategory_Heal_NoDamage_NotUsable()
    {
        var field = TestFactory.MakeField();
        field.Frontend[0] = TestFactory.MakeResource(instanceId: "healthy", damage: 0);
        var ctx = MakeCtx(field: field);
        var info = new EffectInfo();

        var (_, use) = ActionEvaluator.EvaluateCategory(EffectCategory.Heal, info, ctx);

        use.Should().BeFalse();
    }


    [Fact]
    public void EvaluateCategory_RevealReactive_WithFaceDownSupport_Usable()
    {
        var oppField = TestFactory.MakeField();
        oppField.Support[0] = new SupportInstance { InstanceID = "sup1", FaceUp = false };
        var ctx = MakeCtx(oppField: oppField);
        var info = new EffectInfo();

        var (pri, use) = ActionEvaluator.EvaluateCategory(EffectCategory.RevealReactive, info, ctx);

        use.Should().BeTrue();
        pri.Should().Be(NpcParams.PriRevealReactive);
    }

    [Fact]
    public void EvaluateCategory_RevealReactive_AllFaceUp_NotUsable()
    {
        var oppField = TestFactory.MakeField();
        oppField.Support[0] = new SupportInstance { InstanceID = "sup1", FaceUp = true };
        var ctx = MakeCtx(oppField: oppField);
        var info = new EffectInfo();

        var (_, use) = ActionEvaluator.EvaluateCategory(EffectCategory.RevealReactive, info, ctx);

        use.Should().BeFalse();
    }

    [Fact]
    public void EvaluateCategory_DestroyPlatform_WithPlatform_Usable()
    {
        var oppField = TestFactory.MakeField();
        oppField.Support[0] = new SupportInstance { InstanceID = "plat1", CardID = 200, FaceUp = true };
        var ctx = MakeCtx(oppField: oppField);
        var info = new EffectInfo();

        var (pri, use) = ActionEvaluator.EvaluateCategory(EffectCategory.DestroyPlatform, info, ctx);

        use.Should().BeTrue();
        pri.Should().Be(NpcParams.PriDestroyPlatform);
    }

    [Fact]
    public void EvaluateCategory_DestroyPlatform_NoPlatform_NotUsable()
    {
        var ctx = MakeCtx();
        var info = new EffectInfo();

        var (_, use) = ActionEvaluator.EvaluateCategory(EffectCategory.DestroyPlatform, info, ctx);

        use.Should().BeFalse();
    }

    [Theory]
    [InlineData(EffectCategory.CancelAction)]
    [InlineData(EffectCategory.Survive)]
    public void EvaluateCategory_ReactiveCategories_NotProactivelyUsed(EffectCategory cat)
    {
        var ctx = MakeCtx();
        var info = new EffectInfo();

        var (_, use) = ActionEvaluator.EvaluateCategory(cat, info, ctx);

        use.Should().BeFalse();
    }

    // ─── CheckConditions ────────────────────────────────────────

    [Fact]
    public void CheckConditions_EmptyConditions_ReturnsTrue()
    {
        var ctx = MakeCtx();

        var result = ActionEvaluator.CheckConditions([], ctx, _cc);

        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("min_budget", 2000, 3000, true)]
    [InlineData("min_budget", 2000, 1000, false)]
    [InlineData("max_budget", 2000, 1000, true)]
    [InlineData("max_budget", 2000, 3000, false)]
    public void CheckConditions_BudgetCondition_ReturnsExpected(
        string condType, long condValue, long budget, bool expected)
    {
        var ctx = MakeCtx(budget: budget);
        var conditions = new List<EffectCondition>
        {
            new() { Type = condType, Value = condValue },
        };

        ActionEvaluator.CheckConditions(conditions, ctx, _cc).Should().Be(expected);
    }

    [Fact]
    public void CheckConditions_FactionCount_Met_ReturnsTrue()
    {
        var field = TestFactory.MakeField();
        field.Frontend[0] = TestFactory.MakeResource(cardId: 1, instanceId: "r1");
        field.Frontend[1] = TestFactory.MakeResource(cardId: 1, instanceId: "r2");
        var ctx = MakeCtx(field: field);
        var conditions = new List<EffectCondition>
        {
            new() { Type = "faction_count", Value = 2, Faction = "SD" },
        };

        ActionEvaluator.CheckConditions(conditions, ctx, _cc).Should().BeTrue();
    }

    [Fact]
    public void CheckConditions_FactionCount_NotMet_ReturnsFalse()
    {
        var field = TestFactory.MakeField();
        field.Frontend[0] = TestFactory.MakeResource(cardId: 1, instanceId: "r1");
        var ctx = MakeCtx(field: field);
        var conditions = new List<EffectCondition>
        {
            new() { Type = "faction_count", Value = 3, Faction = "SD" },
        };

        ActionEvaluator.CheckConditions(conditions, ctx, _cc).Should().BeFalse();
    }

    [Fact]
    public void CheckConditions_OpponentBackend_Met_ReturnsTrue()
    {
        var oppField = TestFactory.MakeField();
        oppField.Backend[0] = TestFactory.MakeResource(instanceId: "opp_be", faceUp: true);
        var ctx = MakeCtx(oppField: oppField);
        var conditions = new List<EffectCondition>
        {
            new() { Type = "opponent_backend" },
        };

        ActionEvaluator.CheckConditions(conditions, ctx, _cc).Should().BeTrue();
    }

    [Fact]
    public void CheckConditions_OpponentBackend_NotMet_ReturnsFalse()
    {
        var ctx = MakeCtx(); // empty opponent field
        var conditions = new List<EffectCondition>
        {
            new() { Type = "opponent_backend" },
        };

        ActionEvaluator.CheckConditions(conditions, ctx, _cc).Should().BeFalse();
    }

    [Fact]
    public void CheckConditions_UnknownType_TreatedAsTrue()
    {
        var ctx = MakeCtx();
        var conditions = new List<EffectCondition>
        {
            new() { Type = "unknown_condition_type", Value = 999 },
        };

        ActionEvaluator.CheckConditions(conditions, ctx, _cc).Should().BeTrue();
    }

    [Fact]
    public void CheckConditions_MultipleConditions_AllMustPass()
    {
        var ctx = MakeCtx(budget: 3000);
        var conditions = new List<EffectCondition>
        {
            new() { Type = "min_budget", Value = 2000 },
            new() { Type = "max_budget", Value = 1000 }, // fails
        };

        ActionEvaluator.CheckConditions(conditions, ctx, _cc).Should().BeFalse();
    }

    // ─── SelectTarget ───────────────────────────────────────────

    [Fact]
    public void SelectTarget_SingleDamage_ReturnsWeakestOpponent()
    {
        var oppField = TestFactory.MakeField();
        oppField.Frontend[0] = TestFactory.MakeResource(instanceId: "strong", maxAV: 2000);
        oppField.Frontend[1] = TestFactory.MakeResource(instanceId: "weak", maxAV: 400);

        var info = new EffectInfo
        {
            TargetZone = GameConstants.ZoneFrontend,
        }.WithCategory(EffectCategory.SingleDamage);
        var ctx = MakeCtx(oppField: oppField);

        var target = ActionEvaluator.SelectTarget(info, ctx, _cc);

        target.Should().Be("weak");
    }

    [Fact]
    public void SelectTarget_Debuff_ReturnsStrongestOpponent()
    {
        var oppField = TestFactory.MakeField();
        oppField.Frontend[0] = TestFactory.MakeResource(cardId: 1, instanceId: "low_tp", currentTP: 300);
        oppField.Frontend[1] = TestFactory.MakeResource(cardId: 1, instanceId: "high_tp", currentTP: 900);

        var info = new EffectInfo
        {
            TargetZone = GameConstants.ZoneFrontend,
        }.WithCategory(EffectCategory.Debuff);
        var ctx = MakeCtx(oppField: oppField);

        var target = ActionEvaluator.SelectTarget(info, ctx, _cc);

        target.Should().Be("high_tp");
    }

    [Fact]
    public void SelectTarget_Heal_ReturnsMostDamagedOwn()
    {
        var field = TestFactory.MakeField();
        field.Frontend[0] = TestFactory.MakeResource(instanceId: "light_dmg", damage: 100);
        field.Frontend[1] = TestFactory.MakeResource(instanceId: "heavy_dmg", damage: 800);

        var info = new EffectInfo().WithCategory(EffectCategory.Heal);
        var ctx = MakeCtx(field: field);

        var target = ActionEvaluator.SelectTarget(info, ctx, _cc);

        target.Should().Be("heavy_dmg");
    }

    [Fact]
    public void SelectTarget_Buff_ReturnsStrongestOwn()
    {
        var field = TestFactory.MakeField();
        field.Frontend[0] = TestFactory.MakeResource(cardId: 1, instanceId: "low_val", currentTP: 200);
        field.Frontend[1] = TestFactory.MakeResource(cardId: 1, instanceId: "high_val", currentTP: 800);

        var info = new EffectInfo
        {
            TargetZone = GameConstants.ZoneFrontend,
        }.WithCategory(EffectCategory.Buff);
        var ctx = MakeCtx(field: field);

        var target = ActionEvaluator.SelectTarget(info, ctx, _cc);

        target.Should().Be("high_val");
    }

    [Fact]
    public void SelectTarget_DestroyPlatform_ReturnsFirstPlatform()
    {
        var oppField = TestFactory.MakeField();
        oppField.Support[0] = new SupportInstance { InstanceID = "plat_1", CardID = 200, FaceUp = true };

        var info = new EffectInfo().WithCategory(EffectCategory.DestroyPlatform);
        var ctx = MakeCtx(oppField: oppField);

        var target = ActionEvaluator.SelectTarget(info, ctx, _cc);

        target.Should().Be("plat_1");
    }

    [Fact]
    public void SelectTarget_Fallback_ReturnsWeakestOpponent()
    {
        var oppField = TestFactory.MakeField();
        oppField.Frontend[0] = TestFactory.MakeResource(instanceId: "strong", maxAV: 2000);
        oppField.Frontend[1] = TestFactory.MakeResource(instanceId: "weak", maxAV: 300);

        // An info with no damage/debuff/heal/buff/platform category triggers fallback
        var info = new EffectInfo
        {
            TargetZone = GameConstants.ZoneFrontend,
        }.WithCategory(EffectCategory.InsightGain);
        var ctx = MakeCtx(oppField: oppField);

        var target = ActionEvaluator.SelectTarget(info, ctx, _cc);

        target.Should().Be("weak");
    }

    // ─── Test doubles ───────────────────────────────────────────

    private class NullEffectRegistry : IEffectRegistry
    {
        public EffectHandler? Get(long cardNo, TriggerType trigger) => null;
        public bool Has(long cardNo, TriggerType trigger) => false;
        public BudgetRequirement? GetBudgetRequirement(long cardNo, TriggerType trigger) => null;
        public EffectInfo? GetEffectInfo(long cardNo, TriggerType trigger) => null;
        public List<string>? GetChoiceOptions(long cardNo, TriggerType trigger) => null;
    }

    private class StubEffectRegistry : IEffectRegistry
    {
        private readonly Dictionary<(long, TriggerType), EffectInfo> _infos = new();

        public void SetEffectInfo(long cardNo, TriggerType trigger, EffectInfo info)
        {
            _infos[(cardNo, trigger)] = info;
        }

        public EffectHandler? Get(long cardNo, TriggerType trigger) => null;
        public bool Has(long cardNo, TriggerType trigger) => _infos.ContainsKey((cardNo, trigger));
        public BudgetRequirement? GetBudgetRequirement(long cardNo, TriggerType trigger) => null;
        public EffectInfo? GetEffectInfo(long cardNo, TriggerType trigger) =>
            _infos.GetValueOrDefault((cardNo, trigger));
        public List<string>? GetChoiceOptions(long cardNo, TriggerType trigger) => null;
    }
}

/// <summary>
/// Extension to fluently add categories to EffectInfo in tests.
/// </summary>
internal static class EffectInfoTestExtensions
{
    public static EffectInfo WithCategory(this EffectInfo info, EffectCategory cat)
    {
        info.Categories.Add(cat);
        return info;
    }
}
