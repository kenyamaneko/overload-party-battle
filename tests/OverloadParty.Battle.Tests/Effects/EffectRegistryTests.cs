using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Effects.Ops;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Effects;

/// <summary>
/// Tests for EffectRegistry — maps (cardNo, triggerType) → handler.
/// </summary>
public class EffectRegistryTests
{
    [Fact]
    public void Register_AndGet_ReturnsHandler()
    {
        var registry = new EffectRegistry();
        bool called = false;
        registry.Register(1, TriggerType.Deploy, _ => { called = true; return new EffectResult(); });

        var handler = registry.Get(1, TriggerType.Deploy);
        handler.Should().NotBeNull();

        handler!(null!); // just to verify it's callable
        called.Should().BeTrue();
    }

    [Fact]
    public void Get_Unregistered_ReturnsNull()
    {
        var registry = new EffectRegistry();
        registry.Get(999, TriggerType.Deploy).Should().BeNull();
    }

    [Fact]
    public void Has_Registered_ReturnsTrue()
    {
        var registry = new EffectRegistry();
        registry.Register(1, TriggerType.Activate, _ => new EffectResult());

        registry.Has(1, TriggerType.Activate).Should().BeTrue();
    }

    [Fact]
    public void Has_Unregistered_ReturnsFalse()
    {
        var registry = new EffectRegistry();
        registry.Has(1, TriggerType.Activate).Should().BeFalse();
    }

    [Fact]
    public void RegisterComposed_StoresOps_ForClassification()
    {
        var registry = new EffectRegistry();
        var ops = new IEffectOp[] { new GainBudgetOp(PlayerRef.Self, new StaticAmount(500)) };
        registry.RegisterComposed(42, TriggerType.Deploy, ops);

        var reg = registry.GetRegistration(42, TriggerType.Deploy);
        reg.Should().NotBeNull();
        reg!.Ops.Should().NotBeNull();
        reg.Ops.Should().ContainSingle();
    }

    [Fact]
    public void GetEffectInfo_ReturnsClassification()
    {
        var registry = new EffectRegistry();
        registry.RegisterComposed(42, TriggerType.Deploy,
            new GainBudgetOp(PlayerRef.Self, new StaticAmount(500)),
            new DrawCardsOp(1));

        var info = registry.GetEffectInfo(42, TriggerType.Deploy);
        info.Should().NotBeNull();
        info!.HasCategory(EffectCategory.BudgetGain).Should().BeTrue();
        info.HasCategory(EffectCategory.Draw).Should().BeTrue();
    }

    [Fact]
    public void GetEffectInfo_NoOps_ReturnsNull()
    {
        var registry = new EffectRegistry();
        registry.Register(42, TriggerType.Deploy, _ => new EffectResult());

        registry.GetEffectInfo(42, TriggerType.Deploy).Should().BeNull();
    }

    [Fact]
    public void GetChoiceOptions_BranchOnChoice_ReturnsBranchKeys()
    {
        var registry = new EffectRegistry();
        var branches = new Dictionary<string, List<IEffectOp>>
        {
            ["use"] = [new GainBudgetOp(PlayerRef.Self, new StaticAmount(100))],
            ["redis"] = [new DrawCardsOp(1)],
        };
        registry.RegisterComposed(7, TriggerType.Activate, new BranchOnChoiceOp(branches));

        var options = registry.GetChoiceOptions(7, TriggerType.Activate);
        options.Should().NotBeNull();
        options.Should().HaveCount(2);
        options.Should().Contain("use");
        options.Should().Contain("redis");
    }

    [Fact]
    public void GetChoiceOptions_NoBranch_ReturnsNull()
    {
        var registry = new EffectRegistry();
        registry.RegisterComposed(42, TriggerType.Deploy,
            new GainBudgetOp(PlayerRef.Self, new StaticAmount(500)));

        registry.GetChoiceOptions(42, TriggerType.Deploy).Should().BeNull();
    }

    [Fact]
    public void CardNosForTrigger_ReturnsMatchingCards()
    {
        var registry = new EffectRegistry();
        registry.Register(1, TriggerType.Deploy, _ => new EffectResult());
        registry.Register(2, TriggerType.Deploy, _ => new EffectResult());
        registry.Register(3, TriggerType.Activate, _ => new EffectResult());

        var nos = registry.CardNosForTrigger(TriggerType.Deploy);
        nos.Should().HaveCount(2);
        nos.Should().Contain(1);
        nos.Should().Contain(2);
    }

    [Fact]
    public void AddPassive_TrackedSeparately()
    {
        var registry = new EffectRegistry();
        registry.AddPassive(new PassiveDef { CardNo = 10, PassiveType = "tp_bonus", Value = 200 });
        registry.AddPassive(new PassiveDef { CardNo = 11, PassiveType = "yield_bonus", Value = 100 });

        registry.PassiveCount.Should().Be(2);
        registry.GetPassives().Should().HaveCount(2);
    }

    [Fact]
    public void RegistrationCount_TracksHandlers()
    {
        var registry = new EffectRegistry();
        registry.RegistrationCount.Should().Be(0);

        registry.Register(1, TriggerType.Deploy, _ => new EffectResult());
        registry.Register(1, TriggerType.Activate, _ => new EffectResult());

        registry.RegistrationCount.Should().Be(2);
    }

    /// <summary>
    /// Same key overwrites previous registration.
    /// </summary>
    [Fact]
    public void Register_SameKey_Overwrites()
    {
        var registry = new EffectRegistry();
        registry.Register(1, TriggerType.Deploy, _ => new EffectResult { CancelAction = false });
        registry.Register(1, TriggerType.Deploy, _ => new EffectResult { CancelAction = true });

        var handler = registry.Get(1, TriggerType.Deploy)!;
        var result = handler(null!);
        result.CancelAction.Should().BeTrue();
    }
}
