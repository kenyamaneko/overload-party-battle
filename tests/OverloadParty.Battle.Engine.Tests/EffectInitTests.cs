using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Effects.Ops;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Effects;

public class EffectRegistrationTests
{
    /// <summary>Shared setup for effect-registration tests (a populated registry, card cache, and game).</summary>
    public abstract class Base
    {
        protected readonly EffectRegistry _registry;
        protected readonly ICardCache _cardCache;
        protected readonly Game _game;

        /// <summary>Builds a populated effect registry, its backing card cache, and a fresh game.</summary>
        protected Base()
        {
            (_registry, _cardCache) = TestEffectSetup.Get();
            _game = TestFactory.MakeGame();
        }

        /// <summary>
        /// Runs the handler registered for the given card and trigger and returns its result.
        /// </summary>
        /// <param name="state">Game state the effect mutates.</param>
        /// <param name="cardId">Card whose handler is executed.</param>
        /// <param name="trigger">Trigger under which the handler is registered.</param>
        /// <param name="playerNum">Player firing the effect.</param>
        /// <returns>The result returned by the handler.</returns>
        protected EffectResult ExecuteEffect(BattleGameState state, string cardId, TriggerType trigger, long playerNum)
        {
            var handler = _registry.Get(cardId, trigger)
                ?? throw new InvalidOperationException($"{cardId} {trigger} handler not registered");

            var ctx = new EffectContext
            {
                State = state,
                Game = _game,
                PlayerNum = playerNum,
                CardCache = _cardCache,
                Effects = new EffectRegistry(),
            };

            return handler(ctx);
        }

        /// <summary>Selector that returns a fixed list of resources regardless of context.</summary>
        protected class FixedSelector(List<DeployedResource> targets) : ISelector
        {
            /// <summary>Returns the fixed target list.</summary>
            /// <param name="ctx">Op context (ignored).</param>
            /// <returns>The fixed list of targets.</returns>
            public List<DeployedResource> Select(OpContext ctx) => targets;
        }
    }

    [Trait("対象", "レジストリの登録件数")]
    public class RegistrationCount : Base
    {
        [Fact(DisplayName = "起動時にレジストリへ 40 件を超えるハンドラが登録される")]
        public void PopulatesRegistry_WithManyHandlers()
        {
            _registry.RegistrationCount.Should().BeGreaterThan(40,
                "EffectInit registers handlers for 50+ card/trigger combinations");
        }
    }

    [Trait("対象", "SHE 陣営カードの登録")]
    public class SheFactionRegistration : Base
    {
        // 各 TriggerType 種別の代表 1 件のみを残す (load の動線確認が目的、
        // カード固有挙動は別途 Card{NN}_* / SH{NN}_* Fact で検証)
        [Theory(DisplayName = "SHE 陣営のカードが指定トリガーのハンドラを登録済みである")]
        [InlineData("SH-0006", TriggerType.OnDeploy)]
        [InlineData("SH-0008", TriggerType.OnDestroy)]
        [InlineData("SH-0009", TriggerType.Ignition)]
        [InlineData("SH-0014", TriggerType.OnIncident)]
        [InlineData("SH-0021", TriggerType.OnDamaged)]
        [InlineData("SH-0005", TriggerType.OnFieldChange)]
        public void Cards_AreRegistered(string cardId, TriggerType trigger)
        {
            _registry.Has(cardId, trigger).Should().BeTrue(
                $"card #{cardId} should have a {trigger} handler");
            _registry.Get(cardId, trigger).Should().NotBeNull();
        }
    }

    [Trait("対象", "天気使い 陣営カードの登録")]
    public class TenkiFactionRegistration : Base
    {
        [Theory(DisplayName = "天気使い 陣営のカードが指定トリガーのハンドラを登録済みである")]
        [InlineData("TK-0008", TriggerType.OnDestroy)]
        [InlineData("TK-0010", TriggerType.OnDeploy)]
        [InlineData("TK-0014", TriggerType.OnIncident)]
        [InlineData("TK-0020", TriggerType.Ignition)]
        [InlineData("NT-0027", TriggerType.OnAttackDeclared)]
        public void Cards_AreRegistered(string cardId, TriggerType trigger)
        {
            _registry.Has(cardId, trigger).Should().BeTrue(
                $"card #{cardId} should have a {trigger} handler");
            _registry.Get(cardId, trigger).Should().NotBeNull();
        }
    }

    [Trait("対象", "しゅがーらぼ 陣営カードの登録")]
    public class SugarFactionRegistration : Base
    {
        [Theory(DisplayName = "しゅがーらぼ 陣営のカードが指定トリガーのハンドラを登録済みである")]
        [InlineData("SL-0004", TriggerType.OnDeploy)]
        [InlineData("SL-0006", TriggerType.OnAttack)]
        [InlineData("SL-0007", TriggerType.OnDestroy)]
        [InlineData("SL-0016", TriggerType.OnEndPhase)]
        [InlineData("SL-0021", TriggerType.Ignition)]
        [InlineData("SL-0024", TriggerType.OnAttackDeclared)]
        public void Cards_AreRegistered(string cardId, TriggerType trigger)
        {
            _registry.Has(cardId, trigger).Should().BeTrue(
                $"card #{cardId} should have a {trigger} handler");
            _registry.Get(cardId, trigger).Should().NotBeNull();
        }
    }

    [Trait("対象", "調律部 陣営カードの登録")]
    public class TunersFactionRegistration : Base
    {
        [Theory(DisplayName = "調律部 陣営のカードが指定トリガーのハンドラを登録済みである")]
        [InlineData("TN-0002", TriggerType.OnAttack)]
        [InlineData("TN-0013", TriggerType.OnIncident)]
        [InlineData("TN-0014", TriggerType.OnDestroy)]
        [InlineData("TN-0017", TriggerType.Ignition)]
        [InlineData("TN-0004", TriggerType.OnFieldChange)]
        public void Cards_AreRegistered(string cardId, TriggerType trigger)
        {
            _registry.Has(cardId, trigger).Should().BeTrue(
                $"card #{cardId} should have a {trigger} handler");
            _registry.Get(cardId, trigger).Should().NotBeNull();
        }
    }

    [Trait("対象", "ニュートラルカードの登録")]
    public class NeutralCardRegistration : Base
    {
        [Theory(DisplayName = "ニュートラルカードが指定トリガーのハンドラを登録済みである")]
        [InlineData("NT-0007", TriggerType.Ignition)]
        [InlineData("NT-0002", TriggerType.OnHit)]
        [InlineData("NT-0005", TriggerType.OnDeploy)]
        [InlineData("NT-0025", TriggerType.OnFieldChange)]
        public void Cards_AreRegistered(string cardId, TriggerType trigger)
        {
            _registry.Has(cardId, trigger).Should().BeTrue(
                $"card #{cardId} should have a {trigger} handler");
            _registry.Get(cardId, trigger).Should().NotBeNull();
        }
    }

    [Trait("対象", "インシデントカードの登録")]
    public class IncidentCardRegistration : Base
    {
        [Theory(DisplayName = "インシデントカードが指定トリガーのハンドラを登録済みである")]
        [InlineData("NT-0013", TriggerType.Ignition)]
        [InlineData("NT-0022", TriggerType.OnDeploy)]
        public void Cards_AreRegistered(string cardId, TriggerType trigger)
        {
            _registry.Has(cardId, trigger).Should().BeTrue(
                $"card #{cardId} should have a {trigger} handler");
            _registry.Get(cardId, trigger).Should().NotBeNull();
        }
    }

    [Trait("対象", "リアクティブカードの登録")]
    public class ReactiveCardRegistration : Base
    {
        [Theory(DisplayName = "リアクティブカードが指定トリガーのハンドラを登録済みである")]
        [InlineData("NT-0023", TriggerType.OnDestroy)]
        [InlineData("NT-0024", TriggerType.OnAttackDeclared)]
        public void Cards_AreRegistered(string cardId, TriggerType trigger)
        {
            _registry.Has(cardId, trigger).Should().BeTrue(
                $"card #{cardId} should have a {trigger} handler");
            _registry.Get(cardId, trigger).Should().NotBeNull();
        }
    }

    [Trait("対象", "未登録カードの照会")]
    public class UnregisteredCardLookup : Base
    {
        [Fact(DisplayName = "未登録カードを照会すると Get は null、Has は false を返す")]
        public void ReturnsNull()
        {
            _registry.Get("TEST-9999", TriggerType.Ignition).Should().BeNull();
            _registry.Has("TEST-9999", TriggerType.Ignition).Should().BeFalse();
        }
    }

    [Trait("対象", "分岐選択肢の公開")]
    public class ChoiceBranchOptions : Base
    {
        [Theory(DisplayName = "分岐を持つカードが期待どおりの分岐選択肢キーを返す")]
        [InlineData("SH-0006", TriggerType.OnDeploy, new[] { "use", "skip" })]
        [InlineData("SH-0010", TriggerType.OnDeploy, new[] { "memcached", "redis" })]
        [InlineData("SL-0012", TriggerType.OnDeploy, new[] { "memcached", "redis" })]
        [InlineData("SL-0004", TriggerType.OnDeploy, new[] { "autopilot", "standard" })]
        public void Cards_HaveExpectedBranches(string cardId, TriggerType trigger, string[] expectedKeys)
        {
            var options = _registry.GetChoiceOptions(cardId, trigger);
            options.Should().NotBeNull();
            options.Should().BeEquivalentTo(expectedKeys);
        }
    }

    [Trait("対象", "バジェット要件の抽出")]
    public class BudgetRequirementExtraction : Base
    {
        [Fact(DisplayName = "SH-0009 の起動効果からバジェット下限 400 が抽出される")]
        public void Card10_RequiresBudget400()
        {
            var req = _registry.GetBudgetRequirement("SH-0009", TriggerType.Ignition);
            req.Should().NotBeNull();
            req!.MinBudget.Should().Be(400);
        }

        [Fact(DisplayName = "NT-0026 の起動効果からバジェット上限 1000 が抽出される")]
        public void Card120_RequiresMaxBudget1000()
        {
            var req = _registry.GetBudgetRequirement("NT-0026", TriggerType.Ignition);
            req.Should().NotBeNull();
            req!.MaxBudget.Should().Be(1000);
        }

        [Fact(DisplayName = "NT-0007 の起動効果にはバジェット要件がなく null が返る")]
        public void Card98_HasNoBudgetRequirement()
        {
            var req = _registry.GetBudgetRequirement("NT-0007", TriggerType.Ignition);
            req.Should().BeNull();
        }
    }

    [Trait("対象", "効果情報の公開")]
    public class EffectInfoClassification : Base
    {
        [Theory(DisplayName = "起動効果の効果情報が期待どおりの効果カテゴリを持つ")]
        [InlineData("NT-0013", EffectCategory.SingleDamage)]
        [InlineData("NT-0010", EffectCategory.BudgetGain)]
        public void EffectInfo_HasExpectedCategory(string cardId, EffectCategory expected)
        {
            var info = _registry.GetEffectInfo(cardId, TriggerType.Ignition);

            info.Should().NotBeNull();
            info!.HasCategory(expected).Should().BeTrue();
        }
    }

    [Trait("対象", "バジェット効果の実行")]
    public class BudgetEffectBehavior : Base
    {
        [Fact(DisplayName = "NT-0010 の起動効果を実行するとバジェットが 400 増える")]
        public void NT0010_Ignite_GainsBudget400()
        {
            var state = TestFactory.MakeGameState(p1Budget: 1000);

            ExecuteEffect(state, "NT-0010", TriggerType.Ignition, playerNum: 1);

            state.Player1Budget.Should().Be(1400, "NT-0010 grants +400 budget");
        }

        [Fact(DisplayName = "NT-0026 はバジェットが 1000 を超えると発動条件を満たさず、バジェットが変わらない")]
        public void NT0026_Ignite_FailsIfBudgetOver1000()
        {
            var state = TestFactory.MakeGameState(p1Budget: 2000);

            var result = ExecuteEffect(state, "NT-0026", TriggerType.Ignition, playerNum: 1);

            result.HasGuardFailed.Should().BeTrue("NT-0026 requires budget <= 1000");
            state.Player1Budget.Should().Be(2000, "budget should not change when guard fails");
        }

        [Fact(DisplayName = "SH-0019 はフィールドの SHE カードが 3 体未満だと発動条件を満たさず、バジェットが変わらない")]
        public void SH0019_Ignite_FailsIfFewerThan3SHE()
        {
            var state = TestFactory.MakeGameState(p1Budget: 1000);
            // Only 2 SHE resources on field — guard requires 3+
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "r1");
            state.Player1Field.Frontend[1] = TestFactory.MakeResource(cardId: "SH-0002", instanceId: "r2");

            var result = ExecuteEffect(state, "SH-0019", TriggerType.Ignition, playerNum: 1);

            result.HasGuardFailed.Should().BeTrue("SH-0019 requires 3+ SHE cards on field");
            state.Player1Budget.Should().Be(1000, "budget should not change when guard fails");
        }
    }

    [Trait("対象", "選択効果の実行")]
    public class ChoiceEffectBehavior : Base
    {
        [Fact(DisplayName = "SH-0010 のデプロイ時効果で memcached を選択するとバジェットが 400 増える")]
        public void SH0010_Deploy_MemcachedChoice_GainsBudget400()
        {
            var state = TestFactory.MakeGameState(p1Budget: 1000);
            var source = TestFactory.MakeResource(cardId: "SH-0010", instanceId: "cache_1");
            state.Player1Field.Backend[0] = source;

            var handler = _registry.Get("SH-0010", TriggerType.OnDeploy)
                ?? throw new InvalidOperationException("SH-0010 OnDeploy handler not registered");
            var ctx = new EffectContext
            {
                State = state,
                Game = _game,
                PlayerNum = 1,
                Source = source,
                CardCache = _cardCache,
                ChoiceData = new Dictionary<string, object> { ["option"] = "memcached" },
                Effects = _registry,
            };
            handler(ctx);

            state.Player1Budget.Should().Be(1400, "memcached choice grants +400 budget");
        }
    }

    [Trait("対象", "トリガー別カード ID の照会")]
    public class CardIdsForTriggerLookup : Base
    {
        [Fact(DisplayName = "OnAttackDeclared トリガーのカード一覧に登録済みカードが含まれる")]
        public void OnAttackDeclared_ContainsExpectedCards()
        {
            var cards = _registry.CardIdsForTrigger(TriggerType.OnAttackDeclared);
            cards.Should().Contain(new string[] { "SL-0024", "NT-0024", "NT-0027", "NT-0028" });
        }

        [Fact(DisplayName = "OnIncident トリガーのカード一覧に登録済みカードが含まれる")]
        public void OnIncident_ContainsExpectedCards()
        {
            var cards = _registry.CardIdsForTrigger(TriggerType.OnIncident);
            cards.Should().Contain(new string[] { "SH-0014", "SH-0017", "TK-0014", "TK-0017", "TK-0023", "TN-0013" });
        }

        [Fact(DisplayName = "OnDestroy トリガーのカード一覧にリアクティブカードが含まれる")]
        public void OnDestroy_ContainsExpectedReactiveCards()
        {
            var cards = _registry.CardIdsForTrigger(TriggerType.OnDestroy);
            cards.Should().Contain(new string[] { "TK-0024", "TN-0018", "NT-0023" });
        }

        [Fact(DisplayName = "OnDamaged トリガーのカード一覧に SH-0021 が含まれる")]
        public void OnDamaged_ContainsExpectedCards()
        {
            var cards = _registry.CardIdsForTrigger(TriggerType.OnDamaged);
            cards.Should().Contain("SH-0021");
        }

        [Fact(DisplayName = "OnAttack トリガーのカード一覧に登録済みカードが含まれる")]
        public void OnAttack_ContainsExpectedCards()
        {
            var onAttackCards = _registry.CardIdsForTrigger(TriggerType.OnAttack);
            onAttackCards.Should().Contain(new string[] { "SL-0006", "SL-0007", "SL-0011", "SL-0018", "TN-0002" });
        }

        [Fact(DisplayName = "OnEndPhase トリガーのカード一覧に SL-0016 が含まれる")]
        public void OnEndPhase_ContainsCard61()
        {
            var endPhaseCards = _registry.CardIdsForTrigger(TriggerType.OnEndPhase);
            endPhaseCards.Should().Contain("SL-0016");
        }
    }

    [Trait("対象", "TK-0025 のリアクティブのぞき見とインシデント軽減")]
    public class Tk0025PeekAndIncidentReduction : Base
    {
        [Fact(DisplayName = "TK-0025 のデプロイ時効果は相手の裏向きリアクティブを裏向きのままのぞき見し、自分のリソースに incident_reduction を付与する")]
        public void Deploy_PeeksHiddenReactive_AndAppliesIncidentReduction()
        {
            var state = TestFactory.MakeGameState(p1Budget: 3000);

            // Player 1's resource to receive the buff
            var resource = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "r1");
            state.Player1Field.Frontend[0] = resource;

            // Opponent's hidden reactive
            state.Player2Field.Support[0] = new DeployedSupport
            {
                InstanceID = "opp_react",
                CardID = "TK-0024",
                FaceUp = false,
            };

            var handler = _registry.Get("TK-0025", TriggerType.OnDeploy)
                ?? throw new InvalidOperationException("TK-0025 OnDeploy handler not registered");
            var ctx = new EffectContext
            {
                State = state,
                Game = _game,
                PlayerNum = 1,
                CardCache = _cardCache,
                Effects = new EffectRegistry(),
            };
            handler(ctx);

            // peek_reactive: card stays face-down, player 1 added to PeekedBy
            var oppSupport = state.Player2Field.Support[0]!;
            oppSupport.FaceUp.Should().BeFalse();
            oppSupport.PeekedBy.Should().Contain(1);

            // apply_buff: incident_reduction while_on_field
            resource.TemporaryEffects.Should().ContainSingle(e =>
                e.EffectType == "incident_reduction"
                && e.Value == 300
                && e.Duration == "while_on_field");
        }

        [Fact(DisplayName = "incident_reduction を持つリソースはインシデントの 500 ダメージが 300 軽減され 200 になる")]
        public void IncidentReduction_ReducesDamageBy300()
        {
            var state = TestFactory.MakeGameState(p1Budget: 3000);
            var resource = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "r1");
            state.Player1Field.Frontend[0] = resource;

            // Simulate TK-0025's while_on_field buff already applied
            resource.TemporaryEffects.Add(new TemporaryEffect
            {
                EffectType = "incident_reduction",
                Value = 300,
                Duration = "while_on_field",
                SourceID = "tk0025_inst",
            });

            // Fire an incident that deals 500 damage
            var selector = new FixedSelector([resource]);
            var op = new IncidentDamageOp(selector, new StaticAmount(500));
            var opCtx = new OpContext(new EffectContext
            {
                State = state,
                Game = _game,
                PlayerNum = 1,
                CardCache = _cardCache,
                Effects = new EffectRegistry(),
            });

            op.Execute(opCtx);

            resource.Damage.Should().Be(200, "500 - 300 reduction = 200");
        }
    }
}
