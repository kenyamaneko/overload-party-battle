using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Models;
using OverloadParty.Battle.Npc;
using GD = OverloadParty.GameState;

namespace OverloadParty.Battle.Tests.Npc;

public class PriorityResolverTests
{
    /// <summary>PriorityResolver のテストに使う、コンピュート系リソース・Database・プラットフォームを登録したカードキャッシュを作る。</summary>
    /// <returns>TST-0001 / TST-0002 / TEST-0200 を登録したキャッシュ。</returns>
    private static TestCardCache Cc()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", tp: 600, av: 1400));
        cc.Add(TestFactory.DataCard(cardId: "TST-0002", subtype: "Database", yield: 400, av: 800));
        cc.Add(TestFactory.PlatformCard(cardId: "TEST-0200", name: "TestPlatform"));
        return cc;
    }

    /// <summary>PriorityResolver のテストに使う DecisionContext を作る。</summary>
    /// <param name="cc">紐づけるカードキャッシュ。</param>
    /// <param name="field">自分のフィールド。省略時は空。</param>
    /// <param name="oppField">相手のフィールド。省略時は空。</param>
    /// <param name="hand">手札。省略時は空。</param>
    /// <param name="budget">バジェット。</param>
    /// <returns>評価対象の DecisionContext。</returns>
    private static DecisionContext MakeCtx(
        TestCardCache cc,
        GD.Field? field = null, GD.OpponentField? oppField = null,
        List<GD.UndeployedCard>? hand = null, long budget = 5000) =>
        new(
            field ?? TestFactory.MakeWireField(),
            oppField ?? TestFactory.MakeWireOpponentField(),
            hand ?? [],
            budget,
            cc);

    /// <summary>効果優先度設定を持つ AiConfig を作る。</summary>
    /// <param name="priorities">カテゴリごとの効果優先度。省略時は空。</param>
    /// <returns>テスト用の AiConfig。</returns>
    private static AiConfig MakeConfig(Dictionary<string, EffectPriorityEntry>? priorities = null) =>
        new()
        {
            Model = "test",
            Faction = "SHE",
            EffectPriorities = priorities ?? new(),
            TargetSelection = new TargetSelectionConfig(),
        };

    /// <summary>どのカードに対しても効果なしを返す効果レジストリのダブル。</summary>
    private class NullEffectRegistry : IEffectRegistry
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

    /// <summary>(カード, トリガー) ごとに効果情報を差し込める効果レジストリのダブル。</summary>
    private class StubEffectRegistry : IEffectRegistry
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

    [Trait("対象", "効果カテゴリの優先度解決")]
    public class Resolve
    {
        [Fact(DisplayName = "budget_gain はバジェットが threshold 未満のとき、高優先度を返す")]
        public void BudgetGain_LowBudget_ReturnsHighPriority()
        {
            var cc = Cc();
            var ctx = MakeCtx(cc, budget: 500);
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
                EffectCategory.BudgetGain, info, ctx, config, cc);

            use.Should().BeTrue();
            pri.Should().Be(90);
        }

        [Fact(DisplayName = "budget_gain はバジェットが threshold 以上のとき、低優先度を返す")]
        public void BudgetGain_HighBudget_ReturnsLowPriority()
        {
            var cc = Cc();
            var ctx = MakeCtx(cc, budget: 5000);
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
                EffectCategory.BudgetGain, info, ctx, config, cc);

            use.Should().BeTrue();
            pri.Should().Be(40);
        }

        [Fact(DisplayName = "draw は手札が hand_threshold 未満のとき、高優先度を返す")]
        public void Draw_FewCards_ReturnsHighPriority()
        {
            var cc = Cc();
            var hand = new List<GD.UndeployedCard>
            {
                new() { InstanceID = "h1", CardID = "TST-0001" },
                new() { InstanceID = "h2", CardID = "TST-0001" },
            };
            var ctx = MakeCtx(cc, hand: hand);
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
                EffectCategory.Draw, info, ctx, config, cc);

            use.Should().BeTrue();
            pri.Should().Be(80);
        }

        [Fact(DisplayName = "draw は手札が hand_threshold 以上のとき、低優先度を返す")]
        public void Draw_ManyCards_ReturnsLowPriority()
        {
            var cc = Cc();
            var hand = Enumerable.Range(0, 5)
                .Select(i => new GD.UndeployedCard { InstanceID = $"h_{i}", CardID = "TST-0001" })
                .ToList();
            var ctx = MakeCtx(cc, hand: hand);
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
                EffectCategory.Draw, info, ctx, config, cc);

            use.Should().BeTrue();
            pri.Should().Be(30);
        }

        [Fact(DisplayName = "単純な優先度は設定の値をそのまま返す")]
        public void SimplePriority_ReturnsConfiguredValue()
        {
            var cc = Cc();
            var ctx = MakeCtx(cc);
            var config = MakeConfig(new()
            {
                ["deploy_free"] = new EffectPriorityEntry { Priority = 75 },
            });
            var info = new EffectInfo();

            var (pri, use) = PriorityResolver.Resolve(
                EffectCategory.DeployFree, info, ctx, config, cc);

            use.Should().BeTrue();
            pri.Should().Be(75);
        }

        [Fact(DisplayName = "設定に無いカテゴリは使用不可を返す")]
        public void CategoryNotInConfig_ReturnsNotUsable()
        {
            var cc = Cc();
            var ctx = MakeCtx(cc);
            var config = MakeConfig(new()); // 優先度設定なし
            var info = new EffectInfo();

            var (_, use) = PriorityResolver.Resolve(
                EffectCategory.BudgetGain, info, ctx, config, cc);

            use.Should().BeFalse();
        }

        [Fact(DisplayName = "リアクティブ系のカテゴリ (cancel_action) は使用不可を返す")]
        public void ReactiveCategory_NotUsable()
        {
            var cc = Cc();
            var ctx = MakeCtx(cc);
            var config = MakeConfig(new()
            {
                ["cancel_action"] = new EffectPriorityEntry { Priority = 99 },
            });
            var info = new EffectInfo();

            var (_, use) = PriorityResolver.Resolve(
                EffectCategory.CancelAction, info, ctx, config, cc);

            use.Should().BeFalse();
        }

        [Fact(DisplayName = "single_damage はターゲットが無いとき、使用不可を返す")]
        public void SingleDamage_NoTargets_NotUsable()
        {
            var cc = Cc();
            var ctx = MakeCtx(cc); // 相手フィールドは空
            var config = MakeConfig(new()
            {
                ["single_damage"] = new EffectPriorityEntry { Priority = 60 },
            });
            var info = new EffectInfo { TargetZone = Zones.Frontend };

            var (_, use) = PriorityResolver.Resolve(
                EffectCategory.SingleDamage, info, ctx, config, cc);

            use.Should().BeFalse();
        }

        [Fact(DisplayName = "single_damage はターゲットがあるとき、使用可で優先度を返す")]
        public void SingleDamage_WithTargets_Usable()
        {
            var cc = Cc();
            var oppField = TestFactory.MakeWireOpponentField();
            oppField.Frontend[0] = TestFactory.MakeWireResource(instanceId: "t1");
            var ctx = MakeCtx(cc, oppField: oppField);
            var config = MakeConfig(new()
            {
                ["single_damage"] = new EffectPriorityEntry { Priority = 60 },
            });
            var info = new EffectInfo { TargetZone = Zones.Frontend };

            var (pri, use) = PriorityResolver.Resolve(
                EffectCategory.SingleDamage, info, ctx, config, cc);

            use.Should().BeTrue();
            pri.Should().Be(60);
        }

        [Fact(DisplayName = "heal は被ダメージリソースが無いとき、使用不可を返す")]
        public void Heal_NoDamage_NotUsable()
        {
            var cc = Cc();
            var field = TestFactory.MakeWireField();
            field.Frontend[0] = TestFactory.MakeWireResource(damage: 0);
            var ctx = MakeCtx(cc, field: field);
            var config = MakeConfig(new()
            {
                ["heal"] = new EffectPriorityEntry { Priority = 50 },
            });
            var info = new EffectInfo();

            var (_, use) = PriorityResolver.Resolve(
                EffectCategory.Heal, info, ctx, config, cc);

            use.Should().BeFalse();
        }

        [Fact(DisplayName = "インサイト吸収は、相手リソースが 0 体のとき使用不可を返す")]
        public void InsightAbsorb_NoOpponentResources_NotUsable()
        {
            var cc = Cc();
            var ctx = MakeCtx(cc); // 相手フィールドは空
            var config = MakeConfig(new()
            {
                ["insight_absorb"] = new EffectPriorityEntry { Priority = 60 },
            });
            var info = new EffectInfo();

            var (_, use) = PriorityResolver.Resolve(
                EffectCategory.InsightAbsorb, info, ctx, config, cc);

            use.Should().BeFalse();
        }

        [Fact(DisplayName = "インサイト吸収は、相手リソースが 1 体のとき使用可を返す")]
        public void InsightAbsorb_WithOpponentResource_Usable()
        {
            var cc = Cc();
            var oppField = TestFactory.MakeWireOpponentField();
            oppField.Frontend[0] = TestFactory.MakeWireResource(instanceId: "t1");
            var ctx = MakeCtx(cc, oppField: oppField);
            var config = MakeConfig(new()
            {
                ["insight_absorb"] = new EffectPriorityEntry { Priority = 60 },
            });
            var info = new EffectInfo();

            var (_, use) = PriorityResolver.Resolve(
                EffectCategory.InsightAbsorb, info, ctx, config, cc);

            use.Should().BeTrue();
        }

        [Fact(DisplayName = "デバフは、相手リソースが 0 体のとき使用不可を返す")]
        public void Debuff_NoOpponentResources_NotUsable()
        {
            var cc = Cc();
            var ctx = MakeCtx(cc); // 相手フィールドは空
            var config = MakeConfig(new()
            {
                ["debuff"] = new EffectPriorityEntry { Priority = 55 },
            });
            var info = new EffectInfo();

            var (_, use) = PriorityResolver.Resolve(
                EffectCategory.Debuff, info, ctx, config, cc);

            use.Should().BeFalse();
        }

        [Fact(DisplayName = "デバフは、相手リソースが 1 体のとき使用可を返す")]
        public void Debuff_WithOpponentResource_Usable()
        {
            var cc = Cc();
            var oppField = TestFactory.MakeWireOpponentField();
            oppField.Frontend[0] = TestFactory.MakeWireResource(instanceId: "t1");
            var ctx = MakeCtx(cc, oppField: oppField);
            var config = MakeConfig(new()
            {
                ["debuff"] = new EffectPriorityEntry { Priority = 55 },
            });
            var info = new EffectInfo();

            var (_, use) = PriorityResolver.Resolve(
                EffectCategory.Debuff, info, ctx, config, cc);

            use.Should().BeTrue();
        }

        [Fact(DisplayName = "バフは、自分のリソースが 0 体のとき使用不可を返す")]
        public void Buff_NoOwnResources_NotUsable()
        {
            var cc = Cc();
            var ctx = MakeCtx(cc); // 自分のフィールドは空
            var config = MakeConfig(new()
            {
                ["buff"] = new EffectPriorityEntry { Priority = 45 },
            });
            var info = new EffectInfo();

            var (_, use) = PriorityResolver.Resolve(
                EffectCategory.Buff, info, ctx, config, cc);

            use.Should().BeFalse();
        }

        [Fact(DisplayName = "バフは、自分のリソースが 1 体のとき使用可を返す")]
        public void Buff_WithOwnResource_Usable()
        {
            var cc = Cc();
            var field = TestFactory.MakeWireField();
            field.Frontend[0] = TestFactory.MakeWireResource(instanceId: "m1");
            var ctx = MakeCtx(cc, field: field);
            var config = MakeConfig(new()
            {
                ["buff"] = new EffectPriorityEntry { Priority = 45 },
            });
            var info = new EffectInfo();

            var (_, use) = PriorityResolver.Resolve(
                EffectCategory.Buff, info, ctx, config, cc);

            use.Should().BeTrue();
        }

        [Fact(DisplayName = "リアクティブ公開は、相手の伏せサポートが無いとき使用不可を返す")]
        public void RevealReactive_NoFaceDownSupport_NotUsable()
        {
            var cc = Cc();
            var ctx = MakeCtx(cc); // 相手のサポートゾーンは空
            var config = MakeConfig(new()
            {
                ["reveal_reactive"] = new EffectPriorityEntry { Priority = 50 },
            });
            var info = new EffectInfo();

            var (_, use) = PriorityResolver.Resolve(
                EffectCategory.RevealReactive, info, ctx, config, cc);

            use.Should().BeFalse();
        }

        [Fact(DisplayName = "リアクティブ公開は、相手に裏向きサポートが 1 件あるとき使用可を返す")]
        public void RevealReactive_WithFaceDownSupport_Usable()
        {
            var cc = Cc();
            var oppField = TestFactory.MakeWireOpponentField();
            oppField.Support[0] = TestFactory.MakeHiddenSupport(instanceId: "s1", faceDown: true);
            var ctx = MakeCtx(cc, oppField: oppField);
            var config = MakeConfig(new()
            {
                ["reveal_reactive"] = new EffectPriorityEntry { Priority = 50 },
            });
            var info = new EffectInfo();

            var (_, use) = PriorityResolver.Resolve(
                EffectCategory.RevealReactive, info, ctx, config, cc);

            use.Should().BeTrue();
        }

        [Fact(DisplayName = "プラットフォーム破壊は、相手のプラットフォームが無いとき使用不可を返す")]
        public void DestroyPlatform_NoPlatform_NotUsable()
        {
            var cc = Cc();
            var ctx = MakeCtx(cc); // 相手のサポートゾーンは空
            var config = MakeConfig(new()
            {
                ["destroy_platform"] = new EffectPriorityEntry { Priority = 65 },
            });
            var info = new EffectInfo();

            var (_, use) = PriorityResolver.Resolve(
                EffectCategory.DestroyPlatform, info, ctx, config, cc);

            use.Should().BeFalse();
        }

        [Fact(DisplayName = "プラットフォーム破壊は、相手のプラットフォームが 1 件あるとき使用可を返す")]
        public void DestroyPlatform_WithPlatform_Usable()
        {
            var cc = Cc();
            var oppField = TestFactory.MakeWireOpponentField();
            oppField.Support[0] = TestFactory.MakeHiddenSupport(instanceId: "s1", cardId: "TEST-0200", faceDown: false);
            var ctx = MakeCtx(cc, oppField: oppField);
            var config = MakeConfig(new()
            {
                ["destroy_platform"] = new EffectPriorityEntry { Priority = 65 },
            });
            var info = new EffectInfo();

            var (_, use) = PriorityResolver.Resolve(
                EffectCategory.DestroyPlatform, info, ctx, config, cc);

            use.Should().BeTrue();
        }

        [Fact(DisplayName = "エントリの条件を満たさないとき、使用不可を返す")]
        public void WithEntryCondition_NotMet_NotUsable()
        {
            var cc = Cc();
            var ctx = MakeCtx(cc); // 相手フィールドは空
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
                EffectCategory.Debuff, info, ctx, config, cc);

            use.Should().BeFalse();
        }
    }

    [Trait("対象", "カード効果の評価")]
    public class Evaluate
    {
        [Fact(DisplayName = "効果情報が無いカードは使用不可を返す")]
        public void NoEffectInfo_ReturnsNotUsable()
        {
            var cc = Cc();
            var ctx = MakeCtx(cc);
            var config = MakeConfig();
            var effects = new NullEffectRegistry();

            var (_, use, _) = PriorityResolver.Evaluate(
                "UNKNOWN", TriggerType.Ignition, ctx, config, effects, cc);

            use.Should().BeFalse();
        }

        [Fact(DisplayName = "効果があるカードは設定の優先度を返す")]
        public void WithEffect_ReturnsConfigPriority()
        {
            var cc = Cc();
            var reg = new StubEffectRegistry();
            reg.SetEffectInfo("TST-0003", TriggerType.Ignition, new EffectInfo
            {
                TargetType = EffectTargetType.None,
            }.WithCategory(EffectCategory.BudgetGain));

            var ctx = MakeCtx(cc, budget: 500);
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
                "TST-0003", TriggerType.Ignition, ctx, config, reg, cc);

            use.Should().BeTrue();
            pri.Should().Be(90);
        }
    }

    [Trait("対象", "効果ターゲットの選択")]
    public class SelectTarget
    {
        [Fact(DisplayName = "解決可能な spec を持つカテゴリが無いとき、例外を投げる")]
        public void Throws_when_no_category_has_a_resolvable_spec()
        {
            var cc = Cc();
            var ctx = MakeCtx(cc);
            var info = new EffectInfo().WithCategory(EffectCategory.BudgetGain);

            var act = () => PriorityResolver.SelectTarget(info, ctx, new TargetSelectionConfig(), cc);

            act.Should().Throw<InvalidOperationException>();
        }

        [Fact(DisplayName = "カテゴリの spec が未設定のとき、例外を投げる")]
        public void Throws_when_category_spec_is_unconfigured()
        {
            var cc = Cc();
            var ctx = MakeCtx(cc);
            var info = new EffectInfo().WithCategory(EffectCategory.SingleDamage);

            var act = () => PriorityResolver.SelectTarget(info, ctx, new TargetSelectionConfig(), cc);

            act.Should().Throw<InvalidOperationException>();
        }
    }
}
