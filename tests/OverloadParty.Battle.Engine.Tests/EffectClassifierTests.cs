using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Effects.Ops;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Effects;

public class EffectClassifierTests
{
    [Trait("対象", "バジェット効果の分類")]
    public class BudgetOps
    {
        [Fact(DisplayName = "GainBudgetOp は BudgetGain カテゴリだけに分類される")]
        public void GainBudget()
        {
            var ops = new IEffectOp[] { new GainBudgetOp(PlayerRef.Myself, new StaticAmount(500)) };
            var info = EffectClassifier.ClassifyOps(ops);

            info.HasCategory(EffectCategory.BudgetGain).Should().BeTrue();
            info.Categories.Should().ContainSingle();
        }

        [Fact(DisplayName = "LoseBudgetOp は BudgetPenalty に分類される")]
        public void LoseBudget()
        {
            var ops = new IEffectOp[] { new LoseBudgetOp(PlayerRef.Opponent, new StaticAmount(300)) };
            var info = EffectClassifier.ClassifyOps(ops);

            info.HasCategory(EffectCategory.BudgetPenalty).Should().BeTrue();
        }
    }

    [Trait("対象", "インサイト効果の分類")]
    public class InsightOps
    {
        [Fact(DisplayName = "GainInsightOp は InsightGain に分類される")]
        public void GainInsight()
        {
            var ops = new IEffectOp[] { new GainInsightOp(new StaticAmount(200)) };
            var info = EffectClassifier.ClassifyOps(ops);

            info.HasCategory(EffectCategory.InsightGain).Should().BeTrue();
        }

        [Fact(DisplayName = "AbsorbInsightOp は InsightAbsorb に分類される")]
        public void AbsorbInsight()
        {
            var ops = new IEffectOp[] { new AbsorbInsightOp(new StaticAmount(150)) };
            var info = EffectClassifier.ClassifyOps(ops);

            info.HasCategory(EffectCategory.InsightAbsorb).Should().BeTrue();
        }
    }

    [Trait("対象", "ダメージ効果の分類")]
    public class DamageOps
    {
        [Fact(DisplayName = "選択による単体ダメージは SingleDamage に分類され、対象種別 Choice・対象ゾーンはフロントエンドになる")]
        public void ByChoice_SingleDamage()
        {
            var sel = new ByChoiceSelector { Zone = Zones.Frontend, Owner = "opponent" };
            var ops = new IEffectOp[] { new DealDamageOp(sel, new StaticAmount(400)) };
            var info = EffectClassifier.ClassifyOps(ops);

            info.HasCategory(EffectCategory.SingleDamage).Should().BeTrue();
            info.TargetType.Should().Be(EffectTargetType.Choice);
            info.TargetZone.Should().Be(Zones.Frontend);
        }

        [Fact(DisplayName = "相手全体へのダメージは AoEDamage に分類され、対象種別が AllOpp になる")]
        public void AllOpponent_AoE()
        {
            var sel = new AllOpponentSelector();
            var ops = new IEffectOp[] { new DealDamageOp(sel, new StaticAmount(200)) };
            var info = EffectClassifier.ClassifyOps(ops);

            info.HasCategory(EffectCategory.AoEDamage).Should().BeTrue();
            info.TargetType.Should().Be(EffectTargetType.AllOpp);
        }

        [Fact(DisplayName = "発動元自身を対象にしたダメージは対象種別が Myself になる")]
        public void SourceSelector_Self()
        {
            var ops = new IEffectOp[] { new DealDamageOp(SourceSelector.Instance, new StaticAmount(100)) };
            var info = EffectClassifier.ClassifyOps(ops);

            info.TargetType.Should().Be(EffectTargetType.Myself);
        }
    }

    [Trait("対象", "バフ・デバフ効果の分類")]
    public class BuffDebuffOps
    {
        [Fact(DisplayName = "発動元自身へのバフは Buff に分類され、対象種別が Myself になる")]
        public void Buff_SourceSelector()
        {
            var ops = new IEffectOp[] { new ApplyBuffOp(SourceSelector.Instance, "buff_tp", new StaticAmount(200), "this_turn") };
            var info = EffectClassifier.ClassifyOps(ops);

            info.HasCategory(EffectCategory.Buff).Should().BeTrue();
            info.TargetType.Should().Be(EffectTargetType.Myself);
        }

        [Fact(DisplayName = "相手を選択するデバフは Debuff に分類され、対象種別が Choice になる")]
        public void Debuff_OpponentChoice()
        {
            var sel = new ByChoiceSelector { Owner = "opponent" };
            var ops = new IEffectOp[] { new ApplyBuffOp(sel, "debuff_tp", new StaticAmount(100), "this_turn") };
            var info = EffectClassifier.ClassifyOps(ops);

            info.HasCategory(EffectCategory.Debuff).Should().BeTrue();
            info.TargetType.Should().Be(EffectTargetType.Choice);
        }

        [Fact(DisplayName = "相手全体へのデバフは Debuff に分類され、対象種別が AllOpp になる")]
        public void Debuff_AllOpponent()
        {
            var ops = new IEffectOp[] { new ApplyBuffOp(new AllOpponentSelector(), "debuff_tp", new StaticAmount(50), "this_turn") };
            var info = EffectClassifier.ClassifyOps(ops);

            info.HasCategory(EffectCategory.Debuff).Should().BeTrue();
            info.TargetType.Should().Be(EffectTargetType.AllOpp);
        }
    }

    [Trait("対象", "回復効果の分類")]
    public class HealOps
    {
        [Fact(DisplayName = "HealDamageOp は Heal に分類される")]
        public void HealDamage()
        {
            var ops = new IEffectOp[] { new HealDamageOp(SourceSelector.Instance, new StaticAmount(500)) };
            var info = EffectClassifier.ClassifyOps(ops);

            info.HasCategory(EffectCategory.Heal).Should().BeTrue();
        }
    }

    [Trait("対象", "単一 op のカテゴリ分類")]
    public class SingleOps
    {
        [Theory(DisplayName = "各 op は対応する EffectCategory に分類される")]
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

    [Trait("対象", "発動条件の分類")]
    public class Conditions
    {
        [Fact(DisplayName = "MinBudgetGuard は値 1000 の min_budget 条件として分類される")]
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

        [Fact(DisplayName = "MaxBudgetGuard は値 2000 の max_budget 条件として分類される")]
        public void RequireMaxBudget_AddsCondition()
        {
            var block = new BuiltBlock { Guards = [new MaxBudgetGuard(2000)] };
            var info = EffectClassifier.ClassifyBlock(block);

            info.Conditions.Should().ContainSingle();
            info.Conditions[0].Type.Should().Be("max_budget");
            info.Conditions[0].Value.Should().Be(2000);
        }

        [Fact(DisplayName = "ResourceCountGuard は owner myself・faction SHE・min 3 を持つ resource_count 条件になる")]
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

        [Fact(DisplayName = "max のみ指定した ResourceCountGuard は min null・max 3 の resource_count 条件になる")]
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

    [Trait("対象", "分岐 op の分類")]
    public class Branching
    {
        [Fact(DisplayName = "BranchOnChoiceOp は分岐と判定され、分岐先の Heal カテゴリを取り込む")]
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

        [Fact(DisplayName = "IfConditionOp は then 分岐のカテゴリを取り込む")]
        public void IfCondition_MergesThenBranch()
        {
            var then = new List<IEffectOp> { new GainBudgetOp(PlayerRef.Myself, new StaticAmount(500)) };
            var ops = new IEffectOp[] { new IfConditionOp(_ => true, then) };
            var info = EffectClassifier.ClassifyOps(ops);

            info.HasCategory(EffectCategory.BudgetGain).Should().BeTrue();
        }
    }

    [Trait("対象", "CustomFnTaggedOp のメタデータ分類")]
    public class CustomFnTaggedOps
    {
        [Fact(DisplayName = "CustomFnTaggedOp のカテゴリ・対象種別・対象ゾーンはメタデータから決まる")]
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

    [Trait("対象", "複数 op の分類")]
    public class MultipleOps
    {
        [Fact(DisplayName = "複数 op のカテゴリはすべて集約される")]
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

        [Fact(DisplayName = "同じカテゴリの op を重ねてもカテゴリは 1 つになる")]
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
