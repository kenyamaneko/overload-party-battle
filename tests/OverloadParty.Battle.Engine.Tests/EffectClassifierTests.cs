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
    /// <summary>Tests classification of budget ops.</summary>
    public class BudgetOps
    {
        [Fact]
        public void GainBudget()
        {
            var ops = new IEffectOp[] { new GainBudgetOp(PlayerRef.Myself, new StaticAmount(500)) };
            var info = EffectClassifier.ClassifyOps(ops);

            info.HasCategory(EffectCategory.BudgetGain).Should().BeTrue();
            info.Categories.Should().ContainSingle();
        }

        [Fact]
        public void LoseBudget()
        {
            var ops = new IEffectOp[] { new LoseBudgetOp(PlayerRef.Opponent, new StaticAmount(300)) };
            var info = EffectClassifier.ClassifyOps(ops);

            info.HasCategory(EffectCategory.BudgetPenalty).Should().BeTrue();
        }
    }

    /// <summary>Tests classification of insight ops.</summary>
    public class InsightOps
    {
        [Fact]
        public void GainInsight()
        {
            var ops = new IEffectOp[] { new GainInsightOp(new StaticAmount(200)) };
            var info = EffectClassifier.ClassifyOps(ops);

            info.HasCategory(EffectCategory.InsightGain).Should().BeTrue();
        }

        [Fact]
        public void AbsorbInsight()
        {
            var ops = new IEffectOp[] { new AbsorbInsightOp(new StaticAmount(150)) };
            var info = EffectClassifier.ClassifyOps(ops);

            info.HasCategory(EffectCategory.InsightAbsorb).Should().BeTrue();
        }
    }

    /// <summary>Tests classification of damage ops.</summary>
    public class DamageOps
    {
        [Fact]
        public void ByChoice_SingleDamage()
        {
            var sel = new ByChoiceSelector { Zone = Zones.Frontend, Owner = "opponent" };
            var ops = new IEffectOp[] { new DealDamageOp(sel, new StaticAmount(400)) };
            var info = EffectClassifier.ClassifyOps(ops);

            info.HasCategory(EffectCategory.SingleDamage).Should().BeTrue();
            info.TargetType.Should().Be(EffectTargetType.Choice);
            info.TargetZone.Should().Be(Zones.Frontend);
        }

        [Fact]
        public void AllOpponent_AoE()
        {
            var sel = new AllOpponentSelector();
            var ops = new IEffectOp[] { new DealDamageOp(sel, new StaticAmount(200)) };
            var info = EffectClassifier.ClassifyOps(ops);

            info.HasCategory(EffectCategory.AoEDamage).Should().BeTrue();
            info.TargetType.Should().Be(EffectTargetType.AllOpp);
        }

        [Fact]
        public void SourceSelector_Self()
        {
            var ops = new IEffectOp[] { new DealDamageOp(SourceSelector.Instance, new StaticAmount(100)) };
            var info = EffectClassifier.ClassifyOps(ops);

            info.TargetType.Should().Be(EffectTargetType.Myself);
        }
    }

    /// <summary>Tests classification of buff and debuff ops.</summary>
    public class BuffDebuffOps
    {
        [Fact]
        public void Buff_SourceSelector()
        {
            var ops = new IEffectOp[] { new ApplyBuffOp(SourceSelector.Instance, "buff_tp", new StaticAmount(200), "this_turn") };
            var info = EffectClassifier.ClassifyOps(ops);

            info.HasCategory(EffectCategory.Buff).Should().BeTrue();
            info.TargetType.Should().Be(EffectTargetType.Myself);
        }

        [Fact]
        public void Debuff_OpponentChoice()
        {
            var sel = new ByChoiceSelector { Owner = "opponent" };
            var ops = new IEffectOp[] { new ApplyBuffOp(sel, "debuff_tp", new StaticAmount(100), "this_turn") };
            var info = EffectClassifier.ClassifyOps(ops);

            info.HasCategory(EffectCategory.Debuff).Should().BeTrue();
            info.TargetType.Should().Be(EffectTargetType.Choice);
        }

        [Fact]
        public void Debuff_AllOpponent()
        {
            var ops = new IEffectOp[] { new ApplyBuffOp(new AllOpponentSelector(), "debuff_tp", new StaticAmount(50), "this_turn") };
            var info = EffectClassifier.ClassifyOps(ops);

            info.HasCategory(EffectCategory.Debuff).Should().BeTrue();
            info.TargetType.Should().Be(EffectTargetType.AllOpp);
        }
    }

    /// <summary>Tests classification of heal ops.</summary>
    public class HealOps
    {
        [Fact]
        public void HealDamage()
        {
            var ops = new IEffectOp[] { new HealDamageOp(SourceSelector.Instance, new StaticAmount(500)) };
            var info = EffectClassifier.ClassifyOps(ops);

            info.HasCategory(EffectCategory.Heal).Should().BeTrue();
        }
    }

    /// <summary>Tests classification of single card-movement, field, and reactive-control ops.</summary>
    public class SingleOps
    {
        [Theory]
        [InlineData("draw", EffectCategory.Draw)]
        [InlineData("search", EffectCategory.Search)]
        [InlineData("deployrepo", EffectCategory.DeployFree)]
        [InlineData("trashtohand", EffectCategory.RecoverCard)]
        [InlineData("reveal", EffectCategory.RevealReactive)]
        [InlineData("destroyplat", EffectCategory.DestroyPlatform)]
        [InlineData("cancel", EffectCategory.CancelAction)]
        [InlineData("survive", EffectCategory.Survive)]
        public void HasExpectedCategory(string opKey, EffectCategory expected)
        {
            IEffectOp op = opKey switch
            {
                "draw" => new DrawCardsOp(1),
                "search" => new SearchRepoOp(),
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
    }

    /// <summary>Tests classification of guard conditions on a BuiltBlock.</summary>
    public class Conditions
    {
        [Fact]
        public void RequireBudget_AddsCondition()
        {
            var block = new BuiltBlock
            {
                Guards = [new MinBudgetGuard(1000)],
                Ops = [new GainBudgetOp(PlayerRef.Myself, new StaticAmount(500))],
            };
            var info = EffectClassifier.ClassifyBlock(block);

            info.Conditions.Should().ContainSingle();
            info.Conditions[0].Type.Should().Be("min_budget");
            info.Conditions[0].Value.Should().Be(1000);
            info.HasCategory(EffectCategory.BudgetGain).Should().BeTrue();
        }

        [Fact]
        public void RequireMaxBudget_AddsCondition()
        {
            var block = new BuiltBlock { Guards = [new MaxBudgetGuard(2000)] };
            var info = EffectClassifier.ClassifyBlock(block);

            info.Conditions.Should().ContainSingle();
            info.Conditions[0].Type.Should().Be("max_budget");
            info.Conditions[0].Value.Should().Be(2000);
        }

        [Fact]
        public void ResourceCountGuard_AddsCondition()
        {
            var block = new BuiltBlock
            {
                Guards =
                [
                    new ResourceCountGuard(
                        owner: "myself", zone: null, faction: "SHE",
                        cardTypes: null, subtypes: null, cardIds: null, min: 3, max: null),
                ],
            };
            var info = EffectClassifier.ClassifyBlock(block);

            info.Conditions.Should().ContainSingle();
            info.Conditions[0].Type.Should().Be("resource_count");
            info.Conditions[0].Owner.Should().Be("myself");
            info.Conditions[0].Faction.Should().Be("SHE");
            info.Conditions[0].Min.Should().Be(3);
            info.Conditions[0].Max.Should().BeNull();
        }

        [Fact]
        public void ResourceCountGuard_MaxOnly_PreservesShape()
        {
            var block = new BuiltBlock
            {
                Guards =
                [
                    new ResourceCountGuard(
                        owner: "myself", zone: null, faction: "Tuners",
                        cardTypes: null, subtypes: null, cardIds: null, min: null, max: 3),
                ],
            };
            var info = EffectClassifier.ClassifyBlock(block);

            info.Conditions.Should().ContainSingle();
            info.Conditions[0].Type.Should().Be("resource_count");
            info.Conditions[0].Min.Should().BeNull();
            info.Conditions[0].Max.Should().Be(3);
        }
    }

    /// <summary>Tests classification of branching ops.</summary>
    public class Branching
    {
        [Fact]
        public void BranchOnChoice_MergesCategories()
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
        public void IfCondition_MergesThenBranch()
        {
            var then = new List<IEffectOp> { new GainBudgetOp(PlayerRef.Myself, new StaticAmount(500)) };
            var ops = new IEffectOp[] { new IfConditionOp(_ => true, then) };
            var info = EffectClassifier.ClassifyOps(ops);

            info.HasCategory(EffectCategory.BudgetGain).Should().BeTrue();
        }
    }

    /// <summary>Tests classification of CustomFnTagged ops via their metadata.</summary>
    public class CustomFnTaggedOps
    {
        [Fact]
        public void UsesMetadata()
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
    }

    /// <summary>Tests classification across multiple ops in a pipeline.</summary>
    public class MultipleOps
    {
        [Fact]
        public void CombinesCategories()
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
        public void NoDuplicateCategories()
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
}
