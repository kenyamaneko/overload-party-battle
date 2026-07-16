using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Models;
using OverloadParty.Battle.Npc;
using GD = OverloadParty.GameState;

namespace OverloadParty.Battle.Tests.Npc;

public class PriorityResolverTests
{
    /// <summary>Shared setup for PriorityResolver tests (seeded card cache, decision-context/config builders, and effect-registry doubles).</summary>
    public abstract class Base
    {
        protected readonly TestCardCache _cc = new();

        protected Base()
        {
            _cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", tp: 600, av: 1400));
            _cc.Add(TestFactory.DataCard(cardId: "TST-0002", subtype: "Database", yield: 400, av: 800));
            _cc.Add(TestFactory.PlatformCard(cardId: "TEST-0200", name: "TestPlatform"));
        }

        protected DecisionContext MakeCtx(
            GD.Field? field = null, GD.OpponentField? oppField = null,
            List<GD.UndeployedCard>? hand = null, long budget = 5000)
        {
            return new DecisionContext(
                field ?? TestFactory.MakeWireField(),
                oppField ?? TestFactory.MakeWireOpponentField(),
                hand ?? [],
                budget,
                _cc);
        }

        /// <summary>Builds an AiConfig with the given effect priorities and default target selection.</summary>
        protected static AiConfig MakeConfig(Dictionary<string, EffectPriorityEntry>? priorities = null)
        {
            return new AiConfig
            {
                Model = "test",
                Faction = "SHE",
                EffectPriorities = priorities ?? new(),
                TargetSelection = new TargetSelectionConfig(),
            };
        }

        /// <summary>Effect registry double that reports no effects for any card.</summary>
        protected class NullEffectRegistry : IEffectRegistry
        {
            public EffectHandler? Get(string cardId, TriggerType trigger) => null;
            public bool Has(string cardId, TriggerType trigger) => false;
            public BudgetRequirement? GetBudgetRequirement(string cardId, TriggerType trigger) => null;
            public EffectInfo? GetEffectInfo(string cardId, TriggerType trigger) => null;
            public List<string>? GetChoiceOptions(string cardId, TriggerType trigger) => null;
            public IEffectOp[]? GetOps(string cardId, TriggerType trigger) => null;
            public void RegisterPassive(string cardId, PassiveEffectDef def) { }
            public IReadOnlyList<PassiveEffectDef> GetPassives(string cardId) => [];
        }

        /// <summary>Effect registry double whose effect infos are seeded per (card, trigger).</summary>
        protected class StubEffectRegistry : IEffectRegistry
        {
            private readonly Dictionary<(string, TriggerType), EffectInfo> _infos = new();

            public void SetEffectInfo(string cardId, TriggerType trigger, EffectInfo info)
                => _infos[(cardId, trigger)] = info;

            public EffectHandler? Get(string cardId, TriggerType trigger) => null;
            public bool Has(string cardId, TriggerType trigger) => _infos.ContainsKey((cardId, trigger));
            public EffectInfo? GetEffectInfo(string cardId, TriggerType trigger) =>
                _infos.GetValueOrDefault((cardId, trigger));
            public BudgetRequirement? GetBudgetRequirement(string cardId, TriggerType trigger) => null;
            public List<string>? GetChoiceOptions(string cardId, TriggerType trigger) => null;
            public IEffectOp[]? GetOps(string cardId, TriggerType trigger) => null;
            public void RegisterPassive(string cardId, PassiveEffectDef def) { }
            public IReadOnlyList<PassiveEffectDef> GetPassives(string cardId) => [];
        }
    }

    [Trait("対象", "効果カテゴリの優先度解決")]
    public class Resolve : Base
    {
        [Fact(DisplayName = "budget_gain はバジェットが threshold 未満のとき、高優先度を返す")]
        public void BudgetGain_LowBudget_ReturnsHighPriority()
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

        [Fact(DisplayName = "budget_gain はバジェットが threshold 以上のとき、低優先度を返す")]
        public void BudgetGain_HighBudget_ReturnsLowPriority()
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

        [Fact(DisplayName = "draw は手札が hand_threshold 未満のとき、高優先度を返す")]
        public void Draw_FewCards_ReturnsHighPriority()
        {
            var hand = new List<GD.UndeployedCard>
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

        [Fact(DisplayName = "draw は手札が hand_threshold 以上のとき、低優先度を返す")]
        public void Draw_ManyCards_ReturnsLowPriority()
        {
            var hand = Enumerable.Range(0, 5)
                .Select(i => new GD.UndeployedCard { InstanceID = $"h_{i}", CardID = "TST-0001" })
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

        [Fact(DisplayName = "単純な優先度は設定の値をそのまま返す")]
        public void SimplePriority_ReturnsConfiguredValue()
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

        [Fact(DisplayName = "設定に無いカテゴリは使用不可を返す")]
        public void CategoryNotInConfig_ReturnsNotUsable()
        {
            var ctx = MakeCtx();
            var config = MakeConfig(new()); // empty priorities
            var info = new EffectInfo();

            var (_, use) = PriorityResolver.Resolve(
                EffectCategory.BudgetGain, info, ctx, config, _cc);

            use.Should().BeFalse();
        }

        [Fact(DisplayName = "リアクティブ系のカテゴリ (cancel_action) は使用不可を返す")]
        public void ReactiveCategory_NotUsable()
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

        [Fact(DisplayName = "single_damage はターゲットが無いとき、使用不可を返す")]
        public void SingleDamage_NoTargets_NotUsable()
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

        [Fact(DisplayName = "single_damage はターゲットがあるとき、使用可で優先度を返す")]
        public void SingleDamage_WithTargets_Usable()
        {
            var oppField = TestFactory.MakeWireOpponentField();
            oppField.Frontend[0] = TestFactory.MakeWireResource(instanceId: "t1");
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

        [Fact(DisplayName = "heal は被ダメージリソースが無いとき、使用不可を返す")]
        public void Heal_NoDamage_NotUsable()
        {
            var field = TestFactory.MakeWireField();
            field.Frontend[0] = TestFactory.MakeWireResource(damage: 0);
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

        [Fact(DisplayName = "エントリの条件を満たさないとき、使用不可を返す")]
        public void WithEntryCondition_NotMet_NotUsable()
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
    }

    [Trait("対象", "カード効果の評価")]
    public class Evaluate : Base
    {
        [Fact(DisplayName = "効果情報が無いカードは使用不可を返す")]
        public void NoEffectInfo_ReturnsNotUsable()
        {
            var ctx = MakeCtx();
            var config = MakeConfig();
            var effects = new NullEffectRegistry();

            var (_, use, _) = PriorityResolver.Evaluate(
                "UNKNOWN", TriggerType.Ignition, ctx, config, effects, _cc);

            use.Should().BeFalse();
        }

        [Fact(DisplayName = "効果があるカードは設定の優先度を返す")]
        public void WithEffect_ReturnsConfigPriority()
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
    }

    [Trait("対象", "効果ターゲットの選択")]
    public class SelectTarget : Base
    {
        [Fact(DisplayName = "解決可能な spec を持つカテゴリが無いとき、例外を投げる")]
        public void Throws_when_no_category_has_a_resolvable_spec()
        {
            var ctx = MakeCtx();
            var info = new EffectInfo().WithCategory(EffectCategory.BudgetGain);

            var act = () => PriorityResolver.SelectTarget(info, ctx, new TargetSelectionConfig(), _cc);

            act.Should().Throw<InvalidOperationException>();
        }

        [Fact(DisplayName = "カテゴリの spec が未設定のとき、例外を投げる")]
        public void Throws_when_category_spec_is_unconfigured()
        {
            var ctx = MakeCtx();
            var info = new EffectInfo().WithCategory(EffectCategory.SingleDamage);

            var act = () => PriorityResolver.SelectTarget(info, ctx, new TargetSelectionConfig(), _cc);

            act.Should().Throw<InvalidOperationException>();
        }
    }
}
