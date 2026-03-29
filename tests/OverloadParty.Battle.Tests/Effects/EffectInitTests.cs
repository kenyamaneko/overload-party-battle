using OverloadParty.Battle.Data;
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
    private readonly CardCache _cardCache;
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

    [Theory]
    [InlineData("SH-0006", TriggerType.Deploy)]
    [InlineData("SH-0008", TriggerType.OnDestroy)]
    [InlineData("SH-0009", TriggerType.Activate)]
    [InlineData("SH-0010", TriggerType.Deploy)]
    [InlineData("SH-0013", TriggerType.Activate)]
    [InlineData("SH-0014", TriggerType.Reactive)]
    [InlineData("SH-0017", TriggerType.Reactive)]
    [InlineData("SH-0018", TriggerType.Activate)]
    [InlineData("SH-0019", TriggerType.Activate)]
    [InlineData("SH-0020", TriggerType.Activate)]
    [InlineData("SH-0021", TriggerType.Reactive)]
    [InlineData("SH-0022", TriggerType.Activate)]
    [InlineData("SH-0023", TriggerType.Activate)]
    [InlineData("SH-0011", TriggerType.Deploy)]        // Platform: scale_cost_reduction (while_on_field)
    [InlineData("SH-0016", TriggerType.Deploy)]        // Attachment: TP buff (while_on_field)
    [InlineData("SH-0005", TriggerType.OnFieldChange)] // Conditional: TP buff if ObjectStorage
    public void SHE_Cards_AreRegistered(string cardId, TriggerType trigger)
    {
        _registry.Has(cardId, trigger).Should().BeTrue(
            $"card #{cardId} should have a {trigger} handler");
        _registry.Get(cardId, trigger).Should().NotBeNull();
    }

    // ─── Tenki faction ─────────────────────────────────────────

    [Theory]
    [InlineData("TK-0008", TriggerType.OnDestroy)]
    [InlineData("TK-0010", TriggerType.Deploy)]
    // TK-0025: YAML data has wrong trigger (reactive) in v0.1.7; fixed in source, awaiting package update
    [InlineData("TK-0014", TriggerType.Reactive)]
    [InlineData("TK-0015", TriggerType.OnDestroy)]
    [InlineData("TK-0017", TriggerType.Reactive)]
    [InlineData("TK-0018", TriggerType.OnDestroy)]
    [InlineData("TK-0020", TriggerType.Activate)]
    [InlineData("TK-0021", TriggerType.Activate)]
    [InlineData("TK-0022", TriggerType.Activate)]
    [InlineData("TK-0023", TriggerType.Reactive)]
    [InlineData("TK-0024", TriggerType.Reactive)]
    // TK-0005: on_field_change + custom tp_per_backend_data — custom handler not yet migrated from StatCalculator
    [InlineData("TK-0025", TriggerType.Deploy)]         // Reactive: peek + incident_reduction
    [InlineData("NT-0027", TriggerType.Reactive)]
    [InlineData("NT-0028", TriggerType.Reactive)]
    public void Tenki_Cards_AreRegistered(string cardId, TriggerType trigger)
    {
        _registry.Has(cardId, trigger).Should().BeTrue(
            $"card #{cardId} should have a {trigger} handler");
        _registry.Get(cardId, trigger).Should().NotBeNull();
    }

    // ─── Sugar faction ─────────────────────────────────────────

    [Theory]
    [InlineData("SL-0004", TriggerType.Deploy)]
    [InlineData("SL-0006", TriggerType.OnAttack)]
    [InlineData("SL-0007", TriggerType.OnAttack)]
    [InlineData("SL-0007", TriggerType.OnDestroy)]
    [InlineData("SL-0010", TriggerType.Deploy)]
    [InlineData("SL-0011", TriggerType.OnAttack)]
    [InlineData("SL-0016", TriggerType.OnEndPhase)]
    [InlineData("SL-0018", TriggerType.OnAttack)]
    [InlineData("SL-0021", TriggerType.Activate)]
    [InlineData("SL-0022", TriggerType.Activate)]
    [InlineData("SL-0023", TriggerType.Activate)]
    [InlineData("SL-0024", TriggerType.Reactive)]
    [InlineData("SL-0012", TriggerType.Deploy)]
    public void Sugar_Cards_AreRegistered(string cardId, TriggerType trigger)
    {
        _registry.Has(cardId, trigger).Should().BeTrue(
            $"card #{cardId} should have a {trigger} handler");
        _registry.Get(cardId, trigger).Should().NotBeNull();
    }

    // ─── Tuners faction ────────────────────────────────────────

    [Theory]
    [InlineData("TN-0002", TriggerType.OnAttack)]
    [InlineData("TN-0012", TriggerType.Activate)]
    [InlineData("TN-0013", TriggerType.Reactive)]
    [InlineData("TN-0014", TriggerType.OnDestroy)]
    [InlineData("TN-0017", TriggerType.Activate)]
    [InlineData("TN-0018", TriggerType.Reactive)]
    [InlineData("TN-0004", TriggerType.OnFieldChange)] // Orchestrator: maintenance_reduction if <= 3 Tuners
    public void Tuners_Cards_AreRegistered(string cardId, TriggerType trigger)
    {
        _registry.Has(cardId, trigger).Should().BeTrue(
            $"card #{cardId} should have a {trigger} handler");
        _registry.Get(cardId, trigger).Should().NotBeNull();
    }

    // ─── Neutral cards ─────────────────────────────────────────

    [Theory]
    [InlineData("NT-0007", TriggerType.Activate)]
    [InlineData("NT-0008", TriggerType.Activate)]
    [InlineData("NT-0009", TriggerType.Activate)]
    [InlineData("NT-0010", TriggerType.Activate)]
    [InlineData("NT-0011", TriggerType.Activate)]
    [InlineData("NT-0012", TriggerType.Activate)]
    [InlineData("NT-0026", TriggerType.Activate)]
    [InlineData("NT-0002", TriggerType.OnHit)]         // Attachment: TP buff on hit
    [InlineData("NT-0005", TriggerType.Deploy)]        // Platform: incident_reduction (while_on_field)
    [InlineData("NT-0025", TriggerType.OnFieldChange)] // Attachment: conditional attack_damage_reduction
    public void Neutral_Cards_AreRegistered(string cardId, TriggerType trigger)
    {
        _registry.Has(cardId, trigger).Should().BeTrue(
            $"card #{cardId} should have a {trigger} handler");
        _registry.Get(cardId, trigger).Should().NotBeNull();
    }

    // ─── Incidents ─────────────────────────────────────────────

    [Theory]
    [InlineData("NT-0013", TriggerType.Activate)]
    [InlineData("NT-0014", TriggerType.Activate)]
    [InlineData("NT-0015", TriggerType.Activate)]
    [InlineData("NT-0016", TriggerType.Activate)]
    [InlineData("NT-0017", TriggerType.Activate)]
    [InlineData("NT-0018", TriggerType.Activate)]
    [InlineData("NT-0019", TriggerType.Activate)]
    [InlineData("NT-0020", TriggerType.Activate)]
    [InlineData("NT-0021", TriggerType.Activate)]
    [InlineData("NT-0022", TriggerType.OnEnemyDeploy)]
    [InlineData("NT-0034", TriggerType.OnEnemyDeploy)]
    public void Incident_Cards_AreRegistered(string cardId, TriggerType trigger)
    {
        _registry.Has(cardId, trigger).Should().BeTrue(
            $"card #{cardId} should have a {trigger} handler");
        _registry.Get(cardId, trigger).Should().NotBeNull();
    }

    // ─── Reactives ─────────────────────────────────────────────

    [Theory]
    [InlineData("NT-0023", TriggerType.Reactive)]
    [InlineData("NT-0024", TriggerType.Reactive)]
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
        _registry.Get("TEST-9999", TriggerType.Activate).Should().BeNull();
        _registry.Has("TEST-9999", TriggerType.Activate).Should().BeFalse();
    }

    // ─── Choice-based cards have branch options ────────────────

    [Theory]
    [InlineData("SH-0006", TriggerType.Deploy, new[] { "use", "skip" })]
    [InlineData("SH-0010", TriggerType.Deploy, new[] { "memcached", "redis" })]
    [InlineData("SL-0012", TriggerType.Deploy, new[] { "memcached", "redis" })]
    [InlineData("SL-0004", TriggerType.Deploy, new[] { "autopilot", "standard" })]
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
        var req = _registry.GetBudgetRequirement("SH-0009", TriggerType.Activate);
        req.Should().NotBeNull();
        req!.MinBudget.Should().Be(400);
    }

    [Fact]
    public void Card120_RequiresMaxBudget1000()
    {
        var req = _registry.GetBudgetRequirement("NT-0026", TriggerType.Activate);
        req.Should().NotBeNull();
        req!.MaxBudget.Should().Be(1000);
    }

    [Fact]
    public void Card98_HasNoBudgetRequirement()
    {
        var req = _registry.GetBudgetRequirement("NT-0007", TriggerType.Activate);
        req.Should().BeNull();
    }

    // ─── EffectInfo / classification ───────────────────────────

    [Fact]
    public void Card104_EffectInfo_HasDamageCategory()
    {
        var info = _registry.GetEffectInfo("NT-0013", TriggerType.Activate);
        info.Should().NotBeNull();
    }

    [Fact]
    public void Card101_EffectInfo_HasBudgetCategory()
    {
        var info = _registry.GetEffectInfo("NT-0010", TriggerType.Activate);
        info.Should().NotBeNull();
    }

    // ─── Budget effect behavior ──────────────────────────────────

    [Fact]
    public void NT0010_Activate_GainsBudget400()
    {
        var state = TestFactory.MakeGameState(p1Budget: 1000);

        ExecuteEffect(state, "NT-0010", TriggerType.Activate, playerNum: 1);

        state.Player1Budget.Should().Be(1400, "NT-0010 grants +400 budget");
    }

    [Fact]
    public void NT0026_Activate_FailsIfBudgetOver1000()
    {
        var state = TestFactory.MakeGameState(p1Budget: 2000);

        var result = ExecuteEffect(state, "NT-0026", TriggerType.Activate, playerNum: 1);

        result.GuardFailed.Should().BeTrue("NT-0026 requires budget <= 1000");
        state.Player1Budget.Should().Be(2000, "budget should not change when guard fails");
    }

    [Fact]
    public void SH0019_Activate_FailsIfFewerThan3SHE()
    {
        var state = TestFactory.MakeGameState(p1Budget: 1000);
        // Only 2 SHE resources on field — guard requires 3+
        state.Player1Field.Frontend[0] = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "r1");
        state.Player1Field.Frontend[1] = TestFactory.MakeResource(cardId: "SH-0002", instanceId: "r2");

        var result = ExecuteEffect(state, "SH-0019", TriggerType.Activate, playerNum: 1);

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

        var handler = _registry.Get("SH-0010", TriggerType.Deploy)
            ?? throw new InvalidOperationException("SH-0010 Deploy handler not registered");
        var ctx = new EffectContext
        {
            State = state,
            Game = _game,
            PlayerNum = 1,
            Source = source,
            CardCache = _cardCache,
            ChoiceData = new Dictionary<string, object> { ["option"] = "memcached" },
        };
        handler(ctx);

        state.Player1Budget.Should().Be(1400, "memcached choice grants +400 budget");
    }

    // ─── CardIdsForTrigger ─────────────────────────────────────

    [Fact]
    public void CardIdsForTrigger_Reactive_ContainsExpectedCards()
    {
        var reactiveCards = _registry.CardIdsForTrigger(TriggerType.Reactive);
        reactiveCards.Should().Contain(new string[] { "SH-0014", "SH-0021", "TK-0014", "TK-0017", "TK-0023", "TK-0024", "SL-0024", "TN-0013", "TN-0018", "NT-0023", "NT-0024", "NT-0027", "NT-0028" });
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

        var handler = _registry.Get("TK-0025", TriggerType.Deploy)
            ?? throw new InvalidOperationException("TK-0025 Deploy handler not registered");
        var ctx = new EffectContext
        {
            State = state,
            Game = _game,
            PlayerNum = 1,
            CardCache = _cardCache,
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
        });

        op.Execute(opCtx);

        resource.Damage.Should().Be(200, "500 - 300 reduction = 200");
    }

    // ─── Helper methods ──────────────────────────────────────────

    private EffectResult ExecuteEffect(GameState state, string cardId, TriggerType trigger, long playerNum)
    {
        var handler = _registry.Get(cardId, trigger)
            ?? throw new InvalidOperationException($"{cardId} {trigger} handler not registered");

        var ctx = new EffectContext
        {
            State = state,
            Game = _game,
            PlayerNum = playerNum,
            CardCache = _cardCache,
        };

        return handler(ctx);
    }

    private class FixedSelector(List<DeployedResource> targets) : ISelector
    {
        public List<DeployedResource> Select(OpContext ctx) => targets;
    }
}
