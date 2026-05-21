using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Effects.Ops;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Effects;

/// <summary>
/// Tests for EffectClassifier — classifies IEffectOp pipelines into
/// EffectCategory values used by the NPC AI.
/// </summary>
public class EffectClassifierTests
{
    // ─── Budget Ops ───────────────────────────────────────────

    [Fact]
    public void Classify_GainBudget()
    {
        var ops = new IEffectOp[] { new GainBudgetOp(PlayerRef.Myself, new StaticAmount(500)) };
        var info = EffectClassifier.ClassifyOps(ops);

        info.HasCategory(EffectCategory.BudgetGain).Should().BeTrue();
        info.Categories.Should().ContainSingle();
    }

    [Fact]
    public void Classify_LoseBudget()
    {
        var ops = new IEffectOp[] { new LoseBudgetOp(PlayerRef.Opponent, new StaticAmount(300)) };
        var info = EffectClassifier.ClassifyOps(ops);

        info.HasCategory(EffectCategory.BudgetPenalty).Should().BeTrue();
    }

    // ─── Insight Ops ──────────────────────────────────────────

    [Fact]
    public void Classify_GainInsight()
    {
        var ops = new IEffectOp[] { new GainInsightOp(new StaticAmount(200)) };
        var info = EffectClassifier.ClassifyOps(ops);

        info.HasCategory(EffectCategory.InsightGain).Should().BeTrue();
    }

    [Fact]
    public void Classify_AbsorbInsight()
    {
        var ops = new IEffectOp[] { new AbsorbInsightOp(new StaticAmount(150)) };
        var info = EffectClassifier.ClassifyOps(ops);

        info.HasCategory(EffectCategory.InsightAbsorb).Should().BeTrue();
    }

    // ─── Damage Ops ───────────────────────────────────────────

    [Fact]
    public void Classify_DealDamage_ByChoice_SingleDamage()
    {
        var sel = new ByChoiceSelector { Zone = Zones.Frontend, Owner = "opponent" };
        var ops = new IEffectOp[] { new DealDamageOp(sel, new StaticAmount(400)) };
        var info = EffectClassifier.ClassifyOps(ops);

        info.HasCategory(EffectCategory.SingleDamage).Should().BeTrue();
        info.TargetType.Should().Be(EffectTargetType.Choice);
        info.TargetZone.Should().Be(Zones.Frontend);
    }

    [Fact]
    public void Classify_DealDamage_AllOpponent_AoE()
    {
        var sel = new AllOpponentSelector();
        var ops = new IEffectOp[] { new DealDamageOp(sel, new StaticAmount(200)) };
        var info = EffectClassifier.ClassifyOps(ops);

        info.HasCategory(EffectCategory.AoEDamage).Should().BeTrue();
        info.TargetType.Should().Be(EffectTargetType.AllOpp);
    }

    [Fact]
    public void Classify_DealDamage_SourceSelector_Self()
    {
        var ops = new IEffectOp[] { new DealDamageOp(SourceSelector.Instance, new StaticAmount(100)) };
        var info = EffectClassifier.ClassifyOps(ops);

        info.TargetType.Should().Be(EffectTargetType.Myself);
    }

    // ─── Buff / Debuff ────────────────────────────────────────

    [Fact]
    public void Classify_Buff_SourceSelector()
    {
        var ops = new IEffectOp[] { new ApplyBuffOp(SourceSelector.Instance, "buff_tp", new StaticAmount(200), "this_turn") };
        var info = EffectClassifier.ClassifyOps(ops);

        info.HasCategory(EffectCategory.Buff).Should().BeTrue();
        info.TargetType.Should().Be(EffectTargetType.Myself);
    }

    [Fact]
    public void Classify_Debuff_OpponentChoice()
    {
        var sel = new ByChoiceSelector { Owner = "opponent" };
        var ops = new IEffectOp[] { new ApplyBuffOp(sel, "debuff_tp", new StaticAmount(100), "this_turn") };
        var info = EffectClassifier.ClassifyOps(ops);

        info.HasCategory(EffectCategory.Debuff).Should().BeTrue();
        info.TargetType.Should().Be(EffectTargetType.Choice);
    }

    [Fact]
    public void Classify_Debuff_AllOpponent()
    {
        var ops = new IEffectOp[] { new ApplyBuffOp(new AllOpponentSelector(), "debuff_tp", new StaticAmount(50), "this_turn") };
        var info = EffectClassifier.ClassifyOps(ops);

        info.HasCategory(EffectCategory.Debuff).Should().BeTrue();
        info.TargetType.Should().Be(EffectTargetType.AllOpp);
    }

    // ─── Heal ─────────────────────────────────────────────────

    [Fact]
    public void Classify_HealDamage()
    {
        var ops = new IEffectOp[] { new HealDamageOp(SourceSelector.Instance, new StaticAmount(500)) };
        var info = EffectClassifier.ClassifyOps(ops);

        info.HasCategory(EffectCategory.Heal).Should().BeTrue();
    }

    // ─── Card Movement / Field Ops / Reactive Control ─────────

    [Theory]
    [InlineData("draw", EffectCategory.Draw)]
    [InlineData("search", EffectCategory.Search)]
    [InlineData("deployhand", EffectCategory.DeployFree)]
    [InlineData("deployrepo", EffectCategory.DeployFree)]
    [InlineData("trashtohand", EffectCategory.RecoverCard)]
    [InlineData("reveal", EffectCategory.RevealReactive)]
    [InlineData("destroyplat", EffectCategory.DestroyPlatform)]
    [InlineData("cancel", EffectCategory.CancelAction)]
    [InlineData("survive", EffectCategory.Survive)]
    public void Classify_SingleOp_HasExpectedCategory(string opKey, EffectCategory expected)
    {
        IEffectOp op = opKey switch
        {
            "draw" => new DrawCardsOp(1),
            "search" => new SearchRepoOp(),
            "deployhand" => new DeployFromHandOp(),
            "deployrepo" => new DeployFromRepoOp(),
            "trashtohand" => new TrashToHandOp(),
            "reveal" => new RevealReactiveOp(),
            "destroyplat" => new DestroyPlatformOp(),
            "cancel" => SetCancelActionOp.Instance,
            _ => new SurviveDestructionOp(1),
        };
        var info = EffectClassifier.ClassifyOps([op]);

        info.HasCategory(expected).Should().BeTrue();
    }

    // ─── Conditions ───────────────────────────────────────────

    [Fact]
    public void Classify_RequireBudget_AddsCondition()
    {
        var ops = new IEffectOp[] { new RequireBudgetOp(1000), new GainBudgetOp(PlayerRef.Myself, new StaticAmount(500)) };
        var info = EffectClassifier.ClassifyOps(ops);

        info.Conditions.Should().ContainSingle();
        info.Conditions[0].Type.Should().Be("min_budget");
        info.Conditions[0].Value.Should().Be(1000);
        info.HasCategory(EffectCategory.BudgetGain).Should().BeTrue();
    }

    [Fact]
    public void Classify_RequireMaxBudget_AddsCondition()
    {
        var ops = new IEffectOp[] { new RequireMaxBudgetOp(2000) };
        var info = EffectClassifier.ClassifyOps(ops);

        info.Conditions.Should().ContainSingle();
        info.Conditions[0].Type.Should().Be("max_budget");
        info.Conditions[0].Value.Should().Be(2000);
    }

    [Fact]
    public void Classify_ResourceCountGuard_AddsCondition()
    {
        var ops = new IEffectOp[]
        {
            new ResourceCountGuardOp(
                owner: "myself", zone: null, faction: "SHE",
                cardTypes: null, cardIds: null, min: 3, max: null),
        };
        var info = EffectClassifier.ClassifyOps(ops);

        info.Conditions.Should().ContainSingle();
        info.Conditions[0].Type.Should().Be("resource_count");
        info.Conditions[0].Owner.Should().Be("myself");
        info.Conditions[0].Faction.Should().Be("SHE");
        info.Conditions[0].Min.Should().Be(3);
        info.Conditions[0].Max.Should().BeNull();
    }

    [Fact]
    public void Classify_ResourceCountGuard_MaxOnly_PreservesShape()
    {
        var ops = new IEffectOp[]
        {
            new ResourceCountGuardOp(
                owner: "myself", zone: null, faction: "Tuners",
                cardTypes: null, cardIds: null, min: null, max: 3),
        };
        var info = EffectClassifier.ClassifyOps(ops);

        info.Conditions.Should().ContainSingle();
        info.Conditions[0].Type.Should().Be("resource_count");
        info.Conditions[0].Min.Should().BeNull();
        info.Conditions[0].Max.Should().Be(3);
    }

    // ─── Branching ────────────────────────────────────────────

    [Fact]
    public void Classify_BranchOnChoice_MergesCategories()
    {
        var branches = new Dictionary<string, List<IEffectOp>>
        {
            ["attack"] = [new DealDamageOp(SourceSelector.Instance, new StaticAmount(200))],
            ["heal"] = [new HealDamageOp(SourceSelector.Instance, new StaticAmount(300))],
        };
        var ops = new IEffectOp[] { new BranchOnChoiceOp(branches) };
        var info = EffectClassifier.ClassifyOps(ops);

        info.HasBranch.Should().BeTrue();
        info.HasCategory(EffectCategory.Heal).Should().BeTrue();
    }

    [Fact]
    public void Classify_IfCondition_MergesThenBranch()
    {
        var then = new List<IEffectOp> { new GainBudgetOp(PlayerRef.Myself, new StaticAmount(500)) };
        var ops = new IEffectOp[] { new IfConditionOp(_ => true, then) };
        var info = EffectClassifier.ClassifyOps(ops);

        info.HasCategory(EffectCategory.BudgetGain).Should().BeTrue();
    }

    // ─── CustomFnTagged ───────────────────────────────────────

    [Fact]
    public void Classify_CustomFnTagged_UsesMetadata()
    {
        var ops = new IEffectOp[]
        {
            new CustomFnTaggedOp(_ => { })
            {
                Categories = [EffectCategory.Draw, EffectCategory.Search],
                Target = EffectTargetType.Myself,
                Zone = Zones.Backend,
            }
        };
        var info = EffectClassifier.ClassifyOps(ops);

        info.HasCategory(EffectCategory.Draw).Should().BeTrue();
        info.HasCategory(EffectCategory.Search).Should().BeTrue();
        info.TargetType.Should().Be(EffectTargetType.Myself);
        info.TargetZone.Should().Be(Zones.Backend);
    }

    // ─── Multiple Ops ─────────────────────────────────────────

    [Fact]
    public void Classify_MultipleOps_CombinesCategories()
    {
        var ops = new IEffectOp[]
        {
            new GainBudgetOp(PlayerRef.Myself, new StaticAmount(500)),
            new DrawCardsOp(1),
            new HealDamageOp(SourceSelector.Instance, new StaticAmount(200)),
        };
        var info = EffectClassifier.ClassifyOps(ops);

        info.Categories.Should().HaveCount(3);
        info.HasCategory(EffectCategory.BudgetGain).Should().BeTrue();
        info.HasCategory(EffectCategory.Draw).Should().BeTrue();
        info.HasCategory(EffectCategory.Heal).Should().BeTrue();
    }

    /// <summary>
    /// Same category should not be duplicated.
    /// </summary>
    [Fact]
    public void Classify_NoDuplicateCategories()
    {
        var ops = new IEffectOp[]
        {
            new GainBudgetOp(PlayerRef.Myself, new StaticAmount(100)),
            new GainBudgetOp(PlayerRef.Myself, new StaticAmount(200)),
        };
        var info = EffectClassifier.ClassifyOps(ops);

        info.Categories.Should().ContainSingle();
    }
}
