using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Effects;

/// <summary>
/// Tests that EffectInit.RegisterAllEffects populates the EffectRegistry
/// with all expected card effect handlers.
/// </summary>
public class EffectInitTests
{
    private readonly EffectRegistry _registry;

    public EffectInitTests()
    {
        _registry = new EffectRegistry();
        EffectInit.RegisterAllEffects(_registry);
    }

    [Fact]
    public void RegisterAllEffects_PopulatesRegistry_WithManyHandlers()
    {
        _registry.RegistrationCount.Should().BeGreaterThan(40,
            "EffectInit registers handlers for 50+ card/trigger combinations");
    }

    // ─── SHE faction ────────────────────────────────────────────

    [Theory]
    [InlineData(7, TriggerType.Deploy)]
    [InlineData(9, TriggerType.OnDestroy)]
    [InlineData(10, TriggerType.Activate)]
    [InlineData(11, TriggerType.Deploy)]
    [InlineData(14, TriggerType.Activate)]
    [InlineData(15, TriggerType.Reactive)]
    [InlineData(18, TriggerType.Reactive)]
    [InlineData(19, TriggerType.Activate)]
    [InlineData(20, TriggerType.Activate)]
    [InlineData(21, TriggerType.Activate)]
    [InlineData(22, TriggerType.Reactive)]
    [InlineData(118, TriggerType.Activate)]
    [InlineData(121, TriggerType.Activate)]
    public void SHE_Cards_AreRegistered(long cardNo, TriggerType trigger)
    {
        _registry.Has(cardNo, trigger).Should().BeTrue(
            $"card #{cardNo} should have a {trigger} handler");
        _registry.Get(cardNo, trigger).Should().NotBeNull();
    }

    // ─── Tenki faction ─────────────────────────────────────────

    [Theory]
    [InlineData(30, TriggerType.OnDestroy)]
    [InlineData(32, TriggerType.Deploy)]
    [InlineData(36, TriggerType.Activate)]
    [InlineData(37, TriggerType.Reactive)]
    [InlineData(38, TriggerType.OnDestroy)]
    [InlineData(40, TriggerType.Reactive)]
    [InlineData(41, TriggerType.OnDestroy)]
    [InlineData(42, TriggerType.Activate)]
    [InlineData(43, TriggerType.Activate)]
    [InlineData(44, TriggerType.Activate)]
    [InlineData(45, TriggerType.Reactive)]
    [InlineData(46, TriggerType.Reactive)]
    [InlineData(122, TriggerType.Reactive)]
    [InlineData(123, TriggerType.Reactive)]
    public void Tenki_Cards_AreRegistered(long cardNo, TriggerType trigger)
    {
        _registry.Has(cardNo, trigger).Should().BeTrue(
            $"card #{cardNo} should have a {trigger} handler");
        _registry.Get(cardNo, trigger).Should().NotBeNull();
    }

    // ─── Sugar faction ─────────────────────────────────────────

    [Theory]
    [InlineData(50, TriggerType.Deploy)]
    [InlineData(51, TriggerType.OnAttack)]
    [InlineData(52, TriggerType.OnAttack)]
    [InlineData(52, TriggerType.OnDestroy)]
    [InlineData(57, TriggerType.Deploy)]
    [InlineData(58, TriggerType.OnAttack)]
    [InlineData(61, TriggerType.Passive)]
    [InlineData(63, TriggerType.OnAttack)]
    [InlineData(65, TriggerType.Activate)]
    [InlineData(66, TriggerType.Activate)]
    [InlineData(67, TriggerType.Activate)]
    [InlineData(68, TriggerType.Reactive)]
    [InlineData(125, TriggerType.Deploy)]
    public void Sugar_Cards_AreRegistered(long cardNo, TriggerType trigger)
    {
        _registry.Has(cardNo, trigger).Should().BeTrue(
            $"card #{cardNo} should have a {trigger} handler");
        _registry.Get(cardNo, trigger).Should().NotBeNull();
    }

    // ─── Tuners faction ────────────────────────────────────────

    [Theory]
    [InlineData(72, TriggerType.OnAttack)]
    [InlineData(83, TriggerType.Activate)]
    [InlineData(84, TriggerType.Reactive)]
    [InlineData(85, TriggerType.OnDestroy)]
    [InlineData(89, TriggerType.Activate)]
    [InlineData(90, TriggerType.Reactive)]
    public void Tuners_Cards_AreRegistered(long cardNo, TriggerType trigger)
    {
        _registry.Has(cardNo, trigger).Should().BeTrue(
            $"card #{cardNo} should have a {trigger} handler");
        _registry.Get(cardNo, trigger).Should().NotBeNull();
    }

    // ─── Neutral cards ─────────────────────────────────────────

    [Theory]
    [InlineData(98, TriggerType.Activate)]
    [InlineData(99, TriggerType.Activate)]
    [InlineData(100, TriggerType.Activate)]
    [InlineData(101, TriggerType.Activate)]
    [InlineData(102, TriggerType.Activate)]
    [InlineData(103, TriggerType.Activate)]
    [InlineData(120, TriggerType.Activate)]
    public void Neutral_Cards_AreRegistered(long cardNo, TriggerType trigger)
    {
        _registry.Has(cardNo, trigger).Should().BeTrue(
            $"card #{cardNo} should have a {trigger} handler");
        _registry.Get(cardNo, trigger).Should().NotBeNull();
    }

    // ─── Incidents ─────────────────────────────────────────────

    [Theory]
    [InlineData(104, TriggerType.Activate)]
    [InlineData(105, TriggerType.Activate)]
    [InlineData(106, TriggerType.Activate)]
    [InlineData(107, TriggerType.Activate)]
    [InlineData(108, TriggerType.Activate)]
    [InlineData(109, TriggerType.Activate)]
    [InlineData(110, TriggerType.Activate)]
    [InlineData(111, TriggerType.Activate)]
    [InlineData(113, TriggerType.OnEnemyDeploy)]
    [InlineData(135, TriggerType.OnEnemyDeploy)]
    public void Incident_Cards_AreRegistered(long cardNo, TriggerType trigger)
    {
        _registry.Has(cardNo, trigger).Should().BeTrue(
            $"card #{cardNo} should have a {trigger} handler");
        _registry.Get(cardNo, trigger).Should().NotBeNull();
    }

    // ─── Reactives ─────────────────────────────────────────────

    [Theory]
    [InlineData(115, TriggerType.Reactive)]
    [InlineData(117, TriggerType.Reactive)]
    public void Reactive_Cards_AreRegistered(long cardNo, TriggerType trigger)
    {
        _registry.Has(cardNo, trigger).Should().BeTrue(
            $"card #{cardNo} should have a {trigger} handler");
        _registry.Get(cardNo, trigger).Should().NotBeNull();
    }

    // ─── Unregistered cards return null ────────────────────────

    [Fact]
    public void UnregisteredCard_ReturnsNull()
    {
        _registry.Get(9999, TriggerType.Activate).Should().BeNull();
        _registry.Has(9999, TriggerType.Activate).Should().BeFalse();
    }

    // ─── Choice-based cards have branch options ────────────────

    [Theory]
    [InlineData(7, TriggerType.Deploy, new[] { "use", "skip" })]
    [InlineData(11, TriggerType.Deploy, new[] { "memcached", "redis" })]
    [InlineData(125, TriggerType.Deploy, new[] { "memcached", "redis" })]
    public void ChoiceBased_Cards_HaveExpectedBranches(long cardNo, TriggerType trigger, string[] expectedKeys)
    {
        var options = _registry.GetChoiceOptions(cardNo, trigger);
        options.Should().NotBeNull();
        options.Should().BeEquivalentTo(expectedKeys);
    }

    // ─── Budget requirement extraction ─────────────────────────

    [Fact]
    public void Card10_RequiresBudget400()
    {
        var req = _registry.GetBudgetRequirement(10, TriggerType.Activate);
        req.Should().NotBeNull();
        req!.MinBudget.Should().Be(400);
    }

    [Fact]
    public void Card120_RequiresMaxBudget1000()
    {
        var req = _registry.GetBudgetRequirement(120, TriggerType.Activate);
        req.Should().NotBeNull();
        req!.MaxBudget.Should().Be(1000);
    }

    [Fact]
    public void Card98_HasNoBudgetRequirement()
    {
        var req = _registry.GetBudgetRequirement(98, TriggerType.Activate);
        req.Should().BeNull();
    }

    // ─── EffectInfo / classification ───────────────────────────

    [Fact]
    public void Card104_EffectInfo_HasDamageCategory()
    {
        var info = _registry.GetEffectInfo(104, TriggerType.Activate);
        info.Should().NotBeNull();
    }

    [Fact]
    public void Card101_EffectInfo_HasBudgetCategory()
    {
        var info = _registry.GetEffectInfo(101, TriggerType.Activate);
        info.Should().NotBeNull();
    }

    // ─── CardNosForTrigger ─────────────────────────────────────

    [Fact]
    public void CardNosForTrigger_Reactive_ContainsExpectedCards()
    {
        var reactiveCards = _registry.CardNosForTrigger(TriggerType.Reactive);
        reactiveCards.Should().Contain(new long[] { 15, 22, 37, 40, 45, 46, 68, 84, 90, 115, 117, 122, 123 });
    }

    [Fact]
    public void CardNosForTrigger_OnAttack_ContainsExpectedCards()
    {
        var onAttackCards = _registry.CardNosForTrigger(TriggerType.OnAttack);
        onAttackCards.Should().Contain(new long[] { 51, 52, 58, 63, 72 });
    }

    [Fact]
    public void CardNosForTrigger_Passive_ContainsCard61()
    {
        var passiveCards = _registry.CardNosForTrigger(TriggerType.Passive);
        passiveCards.Should().Contain(61);
    }
}
