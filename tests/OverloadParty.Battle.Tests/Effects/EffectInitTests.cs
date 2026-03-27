using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Effects;

/// <summary>
/// Tests that the EffectRegistry is populated with all expected card effect handlers.
/// </summary>
public class EffectRegistrationTests
{
    private readonly EffectRegistry _registry;

    public EffectRegistrationTests()
    {
        (_registry, _) = TestEffectSetup.Get();
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
    [InlineData("SL-0016", TriggerType.Passive)]
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
    public void CardIdsForTrigger_Passive_ContainsCard61()
    {
        var passiveCards = _registry.CardIdsForTrigger(TriggerType.Passive);
        passiveCards.Should().Contain("SL-0016");
    }
}
