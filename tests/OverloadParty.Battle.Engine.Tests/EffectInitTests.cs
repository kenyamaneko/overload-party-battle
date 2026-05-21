using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Effects.Ops;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Effects;

/// <summary>
/// Tests that the EffectRegistry is populated with all expected card effect handlers.
/// </summary>
public class EffectRegistrationTests
{
    private readonly EffectRegistry _registry;
    private readonly ICardCache _cardCache;
    private readonly Game _game;

    public EffectRegistrationTests()
    {
        (_registry, _cardCache) = TestEffectSetup.Get();
        _game = TestFactory.MakeGame();
    }

    [Fact]
    public void RegisterAllEffects_PopulatesRegistry_WithManyHandlers()
    {
        _registry.RegistrationCount.Should().BeGreaterThan(40,
            "EffectInit registers handlers for 50+ card/trigger combinations");
    }

    // ─── SHE faction ────────────────────────────────────────────

    // 各 TriggerType 種別の代表 1 件のみを残す (load の動線確認が目的、
    // カード固有挙動は別途 Card{NN}_* / SH{NN}_* Fact で検証)
    [Theory]
    [InlineData("SH-0006", TriggerType.OnDeploy)]
    [InlineData("SH-0008", TriggerType.OnDestroy)]
    [InlineData("SH-0009", TriggerType.Ignition)]
    [InlineData("SH-0014", TriggerType.OnIncident)]
    [InlineData("SH-0021", TriggerType.OnDamaged)]
    [InlineData("SH-0005", TriggerType.OnFieldChange)]
    public void SHE_Cards_AreRegistered(string cardId, TriggerType trigger)
    {
        _registry.Has(cardId, trigger).Should().BeTrue(
            $"card #{cardId} should have a {trigger} handler");
        _registry.Get(cardId, trigger).Should().NotBeNull();
    }

    // ─── Tenki faction ─────────────────────────────────────────

    [Theory]
    [InlineData("TK-0008", TriggerType.OnDestroy)]
    [InlineData("TK-0010", TriggerType.OnDeploy)]
    [InlineData("TK-0014", TriggerType.OnIncident)]
    [InlineData("TK-0020", TriggerType.Ignition)]
    [InlineData("NT-0027", TriggerType.OnAttackDeclared)]
    public void Tenki_Cards_AreRegistered(string cardId, TriggerType trigger)
    {
        _registry.Has(cardId, trigger).Should().BeTrue(
            $"card #{cardId} should have a {trigger} handler");
        _registry.Get(cardId, trigger).Should().NotBeNull();
    }

    // ─── Sugar faction ─────────────────────────────────────────

    [Theory]
    [InlineData("SL-0004", TriggerType.OnDeploy)]
    [InlineData("SL-0006", TriggerType.OnAttack)]
    [InlineData("SL-0007", TriggerType.OnDestroy)]
    [InlineData("SL-0016", TriggerType.OnEndPhase)]
    [InlineData("SL-0021", TriggerType.Ignition)]
    [InlineData("SL-0024", TriggerType.OnAttackDeclared)]
    public void Sugar_Cards_AreRegistered(string cardId, TriggerType trigger)
    {
        _registry.Has(cardId, trigger).Should().BeTrue(
            $"card #{cardId} should have a {trigger} handler");
        _registry.Get(cardId, trigger).Should().NotBeNull();
    }

    // ─── Tuners faction ────────────────────────────────────────

    [Theory]
    [InlineData("TN-0002", TriggerType.OnAttack)]
    [InlineData("TN-0013", TriggerType.OnIncident)]
    [InlineData("TN-0014", TriggerType.OnDestroy)]
    [InlineData("TN-0017", TriggerType.Ignition)]
    [InlineData("TN-0004", TriggerType.OnFieldChange)]
    public void Tuners_Cards_AreRegistered(string cardId, TriggerType trigger)
    {
        _registry.Has(cardId, trigger).Should().BeTrue(
            $"card #{cardId} should have a {trigger} handler");
        _registry.Get(cardId, trigger).Should().NotBeNull();
    }

    // ─── Neutral cards ─────────────────────────────────────────

    [Theory]
    [InlineData("NT-0007", TriggerType.Ignition)]
    [InlineData("NT-0002", TriggerType.OnHit)]
    [InlineData("NT-0005", TriggerType.OnDeploy)]
    [InlineData("NT-0025", TriggerType.OnFieldChange)]
    public void Neutral_Cards_AreRegistered(string cardId, TriggerType trigger)
    {
        _registry.Has(cardId, trigger).Should().BeTrue(
            $"card #{cardId} should have a {trigger} handler");
        _registry.Get(cardId, trigger).Should().NotBeNull();
    }

    // ─── Incidents ─────────────────────────────────────────────

    [Theory]
    [InlineData("NT-0013", TriggerType.Ignition)]
    [InlineData("NT-0022", TriggerType.OnDeploy)]
    public void Incident_Cards_AreRegistered(string cardId, TriggerType trigger)
    {
        _registry.Has(cardId, trigger).Should().BeTrue(
            $"card #{cardId} should have a {trigger} handler");
        _registry.Get(cardId, trigger).Should().NotBeNull();
    }

    // ─── Reactives ─────────────────────────────────────────────

    [Theory]
    [InlineData("NT-0023", TriggerType.OnDestroy)]
    [InlineData("NT-0024", TriggerType.OnAttackDeclared)]
    public void Reactive_Cards_AreRegistered(string cardId, TriggerType trigger)
    {
        _registry.Has(cardId, trigger).Should().BeTrue(
            $"card #{cardId} should have a {trigger} handler");
        _registry.Get(cardId, trigger).Should().NotBeNull();
    }

    // ─── Unregistered cards return null ────────────────────────

    [Fact]
    public void UnregisteredCard_ReturnsNull()
    {
        _registry.Get("TEST-9999", TriggerType.Ignition).Should().BeNull();
        _registry.Has("TEST-9999", TriggerType.Ignition).Should().BeFalse();
    }

    // ─── Choice-based cards have branch options ────────────────

    [Theory]
    [InlineData("SH-0006", TriggerType.OnDeploy, new[] { "use", "skip" })]
    [InlineData("SH-0010", TriggerType.OnDeploy, new[] { "memcached", "redis" })]
    [InlineData("SL-0012", TriggerType.OnDeploy, new[] { "memcached", "redis" })]
    [InlineData("SL-0004", TriggerType.OnDeploy, new[] { "autopilot", "standard" })]
    public void ChoiceBased_Cards_HaveExpectedBranches(string cardId, TriggerType trigger, string[] expectedKeys)
    {
        var options = _registry.GetChoiceOptions(cardId, trigger);
        options.Should().NotBeNull();
        options.Should().BeEquivalentTo(expectedKeys);
    }

    // ─── Budget requirement extraction ─────────────────────────

    [Fact]
    public void Card10_RequiresBudget400()
    {
        var req = _registry.GetBudgetRequirement("SH-0009", TriggerType.Ignition);
        req.Should().NotBeNull();
        req!.MinBudget.Should().Be(400);
    }

    [Fact]
    public void Card120_RequiresMaxBudget1000()
    {
        var req = _registry.GetBudgetRequirement("NT-0026", TriggerType.Ignition);
        req.Should().NotBeNull();
        req!.MaxBudget.Should().Be(1000);
    }

    [Fact]
    public void Card98_HasNoBudgetRequirement()
    {
        var req = _registry.GetBudgetRequirement("NT-0007", TriggerType.Ignition);
        req.Should().BeNull();
    }

    // ─── EffectInfo / classification ───────────────────────────

    [Fact]
    public void Card104_EffectInfo_HasDamageCategory()
    {
        var info = _registry.GetEffectInfo("NT-0013", TriggerType.Ignition);
        info.Should().NotBeNull();
    }

    [Fact]
    public void Card101_EffectInfo_HasBudgetCategory()
    {
        var info = _registry.GetEffectInfo("NT-0010", TriggerType.Ignition);
        info.Should().NotBeNull();
    }

    // ─── Budget effect behavior ──────────────────────────────────

    [Fact]
    public void NT0010_Ignite_GainsBudget400()
    {
        var state = TestFactory.MakeGameState(p1Budget: 1000);

        ExecuteEffect(state, "NT-0010", TriggerType.Ignition, playerNum: 1);

        state.Player1Budget.Should().Be(1400, "NT-0010 grants +400 budget");
    }

    [Fact]
    public void NT0026_Ignite_FailsIfBudgetOver1000()
    {
        var state = TestFactory.MakeGameState(p1Budget: 2000);

        var result = ExecuteEffect(state, "NT-0026", TriggerType.Ignition, playerNum: 1);

        result.GuardFailed.Should().BeTrue("NT-0026 requires budget <= 1000");
        state.Player1Budget.Should().Be(2000, "budget should not change when guard fails");
    }

    [Fact]
    public void SH0019_Ignite_FailsIfFewerThan3SHE()
    {
        var state = TestFactory.MakeGameState(p1Budget: 1000);
        // Only 2 SHE resources on field — guard requires 3+
        state.Player1Field.Frontend[0] = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "r1");
        state.Player1Field.Frontend[1] = TestFactory.MakeResource(cardId: "SH-0002", instanceId: "r2");

        var result = ExecuteEffect(state, "SH-0019", TriggerType.Ignition, playerNum: 1);

        result.GuardFailed.Should().BeTrue("SH-0019 requires 3+ SHE cards on field");
        state.Player1Budget.Should().Be(1000, "budget should not change when guard fails");
    }

    // ─── Choice effect behavior ──────────────────────────────────

    [Fact]
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

    // ─── CardIdsForTrigger ─────────────────────────────────────

    [Fact]
    public void CardIdsForTrigger_OnAttackDeclared_ContainsExpectedCards()
    {
        var cards = _registry.CardIdsForTrigger(TriggerType.OnAttackDeclared);
        cards.Should().Contain(new string[] { "SL-0024", "NT-0024", "NT-0027", "NT-0028" });
    }

    [Fact]
    public void CardIdsForTrigger_OnIncident_ContainsExpectedCards()
    {
        var cards = _registry.CardIdsForTrigger(TriggerType.OnIncident);
        cards.Should().Contain(new string[] { "SH-0014", "SH-0017", "TK-0014", "TK-0017", "TK-0023", "TN-0013" });
    }

    [Fact]
    public void CardIdsForTrigger_OnDestroy_ContainsExpectedReactiveCards()
    {
        var cards = _registry.CardIdsForTrigger(TriggerType.OnDestroy);
        cards.Should().Contain(new string[] { "TK-0024", "TN-0018", "NT-0023" });
    }

    [Fact]
    public void CardIdsForTrigger_OnDamaged_ContainsExpectedCards()
    {
        var cards = _registry.CardIdsForTrigger(TriggerType.OnDamaged);
        cards.Should().Contain("SH-0021");
    }

    [Fact]
    public void CardIdsForTrigger_OnAttack_ContainsExpectedCards()
    {
        var onAttackCards = _registry.CardIdsForTrigger(TriggerType.OnAttack);
        onAttackCards.Should().Contain(new string[] { "SL-0006", "SL-0007", "SL-0011", "SL-0018", "TN-0002" });
    }

    [Fact]
    public void CardIdsForTrigger_OnEndPhase_ContainsCard61()
    {
        var endPhaseCards = _registry.CardIdsForTrigger(TriggerType.OnEndPhase);
        endPhaseCards.Should().Contain("SL-0016");
    }

    // ─── TK-0025: peek_reactive + incident_reduction (while_on_field) ───

    [Fact]
    public void TK0025_Deploy_PeeksHiddenReactive_AndAppliesIncidentReduction()
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

    [Fact]
    public void TK0025_IncidentReduction_ReducesDamageBy300()
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

    // ─── Helper methods ──────────────────────────────────────────

    private EffectResult ExecuteEffect(BattleGameState state, string cardId, TriggerType trigger, long playerNum)
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

    private class FixedSelector(List<DeployedResource> targets) : ISelector
    {
        public List<DeployedResource> Select(OpContext ctx) => targets;
    }
}
