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
        var ops = new IEffectOp[] { new GainBudgetOp(PlayerRef.Self, new StaticAmount(500)) };
        var info = EffectClassifier.ClassifyOps(ops);

        Assert.True(info.HasCategory(EffectCategory.BudgetGain));
        Assert.Single(info.Categories);
    }

    [Fact]
    public void Classify_LoseBudget()
    {
        var ops = new IEffectOp[] { new LoseBudgetOp(PlayerRef.Opponent, new StaticAmount(300)) };
        var info = EffectClassifier.ClassifyOps(ops);

        Assert.True(info.HasCategory(EffectCategory.BudgetPenalty));
    }

    // ─── Insight Ops ──────────────────────────────────────────

    [Fact]
    public void Classify_GainInsight()
    {
        var ops = new IEffectOp[] { new GainInsightOp(new StaticAmount(200)) };
        var info = EffectClassifier.ClassifyOps(ops);

        Assert.True(info.HasCategory(EffectCategory.InsightGain));
    }

    [Fact]
    public void Classify_AbsorbInsight()
    {
        var ops = new IEffectOp[] { new AbsorbInsightOp(new StaticAmount(150)) };
        var info = EffectClassifier.ClassifyOps(ops);

        Assert.True(info.HasCategory(EffectCategory.InsightAbsorb));
    }

    // ─── Damage Ops ───────────────────────────────────────────

    [Fact]
    public void Classify_DealDamage_ByChoice_SingleDamage()
    {
        var sel = new ByChoiceSelector { Zone = "frontend", Owner = "opponent" };
        var ops = new IEffectOp[] { new DealDamageOp(sel, new StaticAmount(400)) };
        var info = EffectClassifier.ClassifyOps(ops);

        Assert.True(info.HasCategory(EffectCategory.SingleDamage));
        Assert.Equal(EffectTargetType.Choice, info.TargetType);
        Assert.Equal("frontend", info.TargetZone);
    }

    [Fact]
    public void Classify_DealDamage_AllOpponent_AoE()
    {
        var sel = new AllOpponentSelector();
        var ops = new IEffectOp[] { new DealDamageOp(sel, new StaticAmount(200)) };
        var info = EffectClassifier.ClassifyOps(ops);

        Assert.True(info.HasCategory(EffectCategory.AoEDamage));
        Assert.Equal(EffectTargetType.AllOpp, info.TargetType);
    }

    [Fact]
    public void Classify_DealDamage_SourceSelector_Self()
    {
        var ops = new IEffectOp[] { new DealDamageOp(SourceSelector.Instance, new StaticAmount(100)) };
        var info = EffectClassifier.ClassifyOps(ops);

        Assert.Equal(EffectTargetType.Self, info.TargetType);
    }

    // ─── Buff / Debuff ────────────────────────────────────────

    [Fact]
    public void Classify_Buff_SourceSelector()
    {
        var ops = new IEffectOp[] { new ApplyBuffOp(SourceSelector.Instance, "buff_tp", new StaticAmount(200), "this_turn") };
        var info = EffectClassifier.ClassifyOps(ops);

        Assert.True(info.HasCategory(EffectCategory.Buff));
        Assert.Equal(EffectTargetType.Self, info.TargetType);
    }

    [Fact]
    public void Classify_Debuff_OpponentChoice()
    {
        var sel = new ByChoiceSelector { Owner = "opponent" };
        var ops = new IEffectOp[] { new ApplyBuffOp(sel, "debuff_tp", new StaticAmount(100), "this_turn") };
        var info = EffectClassifier.ClassifyOps(ops);

        Assert.True(info.HasCategory(EffectCategory.Debuff));
        Assert.Equal(EffectTargetType.Choice, info.TargetType);
    }

    [Fact]
    public void Classify_Debuff_AllOpponent()
    {
        var ops = new IEffectOp[] { new ApplyBuffOp(new AllOpponentSelector(), "debuff_tp", new StaticAmount(50), "this_turn") };
        var info = EffectClassifier.ClassifyOps(ops);

        Assert.True(info.HasCategory(EffectCategory.Debuff));
        Assert.Equal(EffectTargetType.AllOpp, info.TargetType);
    }

    // ─── Heal ─────────────────────────────────────────────────

    [Fact]
    public void Classify_HealDamage()
    {
        var ops = new IEffectOp[] { new HealDamageOp(SourceSelector.Instance, new StaticAmount(500)) };
        var info = EffectClassifier.ClassifyOps(ops);

        Assert.True(info.HasCategory(EffectCategory.Heal));
    }

    // ─── Card Movement ────────────────────────────────────────

    [Fact]
    public void Classify_DrawCards()
    {
        var ops = new IEffectOp[] { new DrawCardsOp(2) };
        var info = EffectClassifier.ClassifyOps(ops);

        Assert.True(info.HasCategory(EffectCategory.Draw));
    }

    [Fact]
    public void Classify_SearchRepo()
    {
        var ops = new IEffectOp[] { new SearchRepoOp() };
        var info = EffectClassifier.ClassifyOps(ops);

        Assert.True(info.HasCategory(EffectCategory.Search));
    }

    [Fact]
    public void Classify_DeployFromHand()
    {
        var ops = new IEffectOp[] { new DeployFromHandOp() };
        var info = EffectClassifier.ClassifyOps(ops);

        Assert.True(info.HasCategory(EffectCategory.DeployFree));
    }

    [Fact]
    public void Classify_DeployFromRepo()
    {
        var ops = new IEffectOp[] { new DeployFromRepoOp() };
        var info = EffectClassifier.ClassifyOps(ops);

        Assert.True(info.HasCategory(EffectCategory.DeployFree));
    }

    [Fact]
    public void Classify_TrashToHand()
    {
        var ops = new IEffectOp[] { new TrashToHandOp() };
        var info = EffectClassifier.ClassifyOps(ops);

        Assert.True(info.HasCategory(EffectCategory.RecoverCard));
    }

    // ─── Field Ops ────────────────────────────────────────────

    [Fact]
    public void Classify_RevealTrap()
    {
        var ops = new IEffectOp[] { new RevealTrapOp() };
        var info = EffectClassifier.ClassifyOps(ops);

        Assert.True(info.HasCategory(EffectCategory.RevealTrap));
    }

    [Fact]
    public void Classify_DestroyPlatform()
    {
        var ops = new IEffectOp[] { new DestroyPlatformOp() };
        var info = EffectClassifier.ClassifyOps(ops);

        Assert.True(info.HasCategory(EffectCategory.DestroyPlatform));
    }

    // ─── Reactive Control ─────────────────────────────────────

    [Fact]
    public void Classify_CancelAction()
    {
        var ops = new IEffectOp[] { SetCancelActionOp.Instance };
        var info = EffectClassifier.ClassifyOps(ops);

        Assert.True(info.HasCategory(EffectCategory.CancelAction));
    }

    [Fact]
    public void Classify_SurviveDestruction()
    {
        var ops = new IEffectOp[] { new SurviveDestructionOp(1) };
        var info = EffectClassifier.ClassifyOps(ops);

        Assert.True(info.HasCategory(EffectCategory.Survive));
    }

    // ─── Conditions ───────────────────────────────────────────

    [Fact]
    public void Classify_RequireBudget_AddsCondition()
    {
        var ops = new IEffectOp[] { new RequireBudgetOp(1000), new GainBudgetOp(PlayerRef.Self, new StaticAmount(500)) };
        var info = EffectClassifier.ClassifyOps(ops);

        Assert.Single(info.Conditions);
        Assert.Equal("min_budget", info.Conditions[0].Type);
        Assert.Equal(1000, info.Conditions[0].Value);
        Assert.True(info.HasCategory(EffectCategory.BudgetGain));
    }

    [Fact]
    public void Classify_RequireMaxBudget_AddsCondition()
    {
        var ops = new IEffectOp[] { new RequireMaxBudgetOp(2000) };
        var info = EffectClassifier.ClassifyOps(ops);

        Assert.Single(info.Conditions);
        Assert.Equal("max_budget", info.Conditions[0].Type);
        Assert.Equal(2000, info.Conditions[0].Value);
    }

    [Fact]
    public void Classify_RequireFactionCount_AddsCondition()
    {
        var ops = new IEffectOp[] { new RequireFactionCountOp("SD", 3) };
        var info = EffectClassifier.ClassifyOps(ops);

        Assert.Single(info.Conditions);
        Assert.Equal("faction_count", info.Conditions[0].Type);
        Assert.Equal(3, info.Conditions[0].Value);
        Assert.Equal("SD", info.Conditions[0].Faction);
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

        Assert.True(info.HasBranch);
        Assert.True(info.HasCategory(EffectCategory.Heal));
    }

    [Fact]
    public void Classify_IfCondition_MergesThenBranch()
    {
        var then = new List<IEffectOp> { new GainBudgetOp(PlayerRef.Self, new StaticAmount(500)) };
        var ops = new IEffectOp[] { new IfConditionOp(_ => true, then) };
        var info = EffectClassifier.ClassifyOps(ops);

        Assert.True(info.HasCategory(EffectCategory.BudgetGain));
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
                Target = EffectTargetType.Self,
                Zone = "backend",
            }
        };
        var info = EffectClassifier.ClassifyOps(ops);

        Assert.True(info.HasCategory(EffectCategory.Draw));
        Assert.True(info.HasCategory(EffectCategory.Search));
        Assert.Equal(EffectTargetType.Self, info.TargetType);
        Assert.Equal("backend", info.TargetZone);
    }

    // ─── Multiple Ops ─────────────────────────────────────────

    [Fact]
    public void Classify_MultipleOps_CombinesCategories()
    {
        var ops = new IEffectOp[]
        {
            new GainBudgetOp(PlayerRef.Self, new StaticAmount(500)),
            new DrawCardsOp(1),
            new HealDamageOp(SourceSelector.Instance, new StaticAmount(200)),
        };
        var info = EffectClassifier.ClassifyOps(ops);

        Assert.Equal(3, info.Categories.Count);
        Assert.True(info.HasCategory(EffectCategory.BudgetGain));
        Assert.True(info.HasCategory(EffectCategory.Draw));
        Assert.True(info.HasCategory(EffectCategory.Heal));
    }

    /// <summary>
    /// Same category should not be duplicated.
    /// </summary>
    [Fact]
    public void Classify_NoDuplicateCategories()
    {
        var ops = new IEffectOp[]
        {
            new GainBudgetOp(PlayerRef.Self, new StaticAmount(100)),
            new GainBudgetOp(PlayerRef.Self, new StaticAmount(200)),
        };
        var info = EffectClassifier.ClassifyOps(ops);

        Assert.Single(info.Categories);
    }
}
