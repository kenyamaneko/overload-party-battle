using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Effects.Ops;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Effects;

/// <summary>
/// Tests for EffectRegistry — maps (cardId, triggerType) → handler.
/// </summary>
public class EffectRegistryTests
{
    [Fact]
    public void Register_AndGet_ReturnsHandler()
    {
        var registry = new EffectRegistry();
        bool called = false;
        registry.Register("SH-0001", TriggerType.Deploy, _ => { called = true; return new EffectResult(); });

        var handler = registry.Get("SH-0001", TriggerType.Deploy);
        handler.Should().NotBeNull();

        handler!(null!); // just to verify it's callable
        called.Should().BeTrue();
    }

    [Fact]
    public void Get_Unregistered_ReturnsNull()
    {
        var registry = new EffectRegistry();
        registry.Get("TEST-0999", TriggerType.Deploy).Should().BeNull();
    }

    [Fact]
    public void Has_Registered_ReturnsTrue()
    {
        var registry = new EffectRegistry();
        registry.Register("SH-0001", TriggerType.Activate, _ => new EffectResult());

        registry.Has("SH-0001", TriggerType.Activate).Should().BeTrue();
    }

    [Fact]
    public void Has_Unregistered_ReturnsFalse()
    {
        var registry = new EffectRegistry();
        registry.Has("SH-0001", TriggerType.Activate).Should().BeFalse();
    }

    [Fact]
    public void RegisterComposed_StoresOps_ForClassification()
    {
        var registry = new EffectRegistry();
        var ops = new IEffectOp[] { new GainBudgetOp(PlayerRef.Self, new StaticAmount(500)) };
        registry.RegisterComposed("TK-0020", TriggerType.Deploy, ops);

        var reg = registry.GetRegistration("TK-0020", TriggerType.Deploy);
        reg.Should().NotBeNull();
        reg!.Ops.Should().NotBeNull();
        reg.Ops.Should().ContainSingle();
    }

    [Fact]
    public void GetEffectInfo_ReturnsClassification()
    {
        var registry = new EffectRegistry();
        registry.RegisterComposed("TK-0020", TriggerType.Deploy,
            new GainBudgetOp(PlayerRef.Self, new StaticAmount(500)),
            new DrawCardsOp(1));

        var info = registry.GetEffectInfo("TK-0020", TriggerType.Deploy);
        info.Should().NotBeNull();
        info!.HasCategory(EffectCategory.BudgetGain).Should().BeTrue();
        info.HasCategory(EffectCategory.Draw).Should().BeTrue();
    }

    [Fact]
    public void GetEffectInfo_NoOps_ReturnsNull()
    {
        var registry = new EffectRegistry();
        registry.Register("TK-0020", TriggerType.Deploy, _ => new EffectResult());

        registry.GetEffectInfo("TK-0020", TriggerType.Deploy).Should().BeNull();
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
        registry.RegisterComposed("SH-0006", TriggerType.Activate, new BranchOnChoiceOp(branches));

        var options = registry.GetChoiceOptions("SH-0006", TriggerType.Activate);
        options.Should().NotBeNull();
        options.Should().HaveCount(2);
        options.Should().Contain("use");
        options.Should().Contain("redis");
    }

    [Fact]
    public void GetChoiceOptions_NoBranch_ReturnsNull()
    {
        var registry = new EffectRegistry();
        registry.RegisterComposed("TK-0020", TriggerType.Deploy,
            new GainBudgetOp(PlayerRef.Self, new StaticAmount(500)));

        registry.GetChoiceOptions("TK-0020", TriggerType.Deploy).Should().BeNull();
    }

    [Fact]
    public void CardIdsForTrigger_ReturnsMatchingCards()
    {
        var registry = new EffectRegistry();
        registry.Register("SH-0001", TriggerType.Deploy, _ => new EffectResult());
        registry.Register("SH-0002", TriggerType.Deploy, _ => new EffectResult());
        registry.Register("SH-0002", TriggerType.Activate, _ => new EffectResult());

        var nos = registry.CardIdsForTrigger(TriggerType.Deploy);
        nos.Should().HaveCount(2);
        nos.Should().Contain("SH-0001");
        nos.Should().Contain("SH-0002");
    }

    [Fact]
    public void RegistrationCount_TracksHandlers()
    {
        var registry = new EffectRegistry();
        registry.RegistrationCount.Should().Be(0);

        registry.Register("SH-0001", TriggerType.Deploy, _ => new EffectResult());
        registry.Register("SH-0001", TriggerType.Activate, _ => new EffectResult());

        registry.RegistrationCount.Should().Be(2);
    }

    /// <summary>
    /// Same key overwrites previous registration.
    /// </summary>
    [Fact]
    public void Register_SameKey_Overwrites()
    {
        var registry = new EffectRegistry();
        registry.Register("SH-0001", TriggerType.Deploy, _ => new EffectResult { CancelAction = false });
        registry.Register("SH-0001", TriggerType.Deploy, _ => new EffectResult { CancelAction = true });

        var handler = registry.Get("SH-0001", TriggerType.Deploy)!;
        var result = handler(null!);
        result.CancelAction.Should().BeTrue();
    }
}
