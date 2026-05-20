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
        registry.Register("TST-2001", TriggerType.OnDeploy, _ => { called = true; return new EffectResult(); });

        var handler = registry.Get("TST-2001", TriggerType.OnDeploy);
        handler.Should().NotBeNull();

        handler!(null!); // just to verify it's callable
        called.Should().BeTrue();
    }

    [Fact]
    public void Get_Unregistered_ReturnsNull()
    {
        var registry = new EffectRegistry();
        registry.Get("TEST-0999", TriggerType.OnDeploy).Should().BeNull();
    }

    [Fact]
    public void Has_Registered_ReturnsTrue()
    {
        var registry = new EffectRegistry();
        registry.Register("TST-2001", TriggerType.Ignition, _ => new EffectResult());

        registry.Has("TST-2001", TriggerType.Ignition).Should().BeTrue();
    }

    [Fact]
    public void Has_Unregistered_ReturnsFalse()
    {
        var registry = new EffectRegistry();
        registry.Has("TST-2001", TriggerType.Ignition).Should().BeFalse();
    }

    [Fact]
    public void RegisterComposed_StoresOps_ForClassification()
    {
        var registry = new EffectRegistry();
        var ops = new IEffectOp[] { new GainBudgetOp(PlayerRef.Myself, new StaticAmount(500)) };
        registry.RegisterComposed("TST-4020", TriggerType.OnDeploy, ops);

        var reg = registry.GetRegistration("TST-4020", TriggerType.OnDeploy);
        reg.Should().NotBeNull();
        reg!.Ops.Should().NotBeNull();
        reg.Ops.Should().ContainSingle();
    }

    [Fact]
    public void GetEffectInfo_ReturnsClassification()
    {
        var registry = new EffectRegistry();
        registry.RegisterComposed("TST-4020", TriggerType.OnDeploy,
            new GainBudgetOp(PlayerRef.Myself, new StaticAmount(500)),
            new DrawCardsOp(1));

        var info = registry.GetEffectInfo("TST-4020", TriggerType.OnDeploy);
        info.Should().NotBeNull();
        info!.HasCategory(EffectCategory.BudgetGain).Should().BeTrue();
        info.HasCategory(EffectCategory.Draw).Should().BeTrue();
    }

    [Fact]
    public void GetEffectInfo_NoOps_ReturnsNull()
    {
        var registry = new EffectRegistry();
        registry.Register("TST-4020", TriggerType.OnDeploy, _ => new EffectResult());

        registry.GetEffectInfo("TST-4020", TriggerType.OnDeploy).Should().BeNull();
    }

    [Fact]
    public void GetChoiceOptions_BranchOnChoice_ReturnsBranchKeys()
    {
        var registry = new EffectRegistry();
        var branches = new Dictionary<string, List<IEffectOp>>
        {
            ["use"] = [new GainBudgetOp(PlayerRef.Myself, new StaticAmount(100))],
            ["redis"] = [new DrawCardsOp(1)],
        };
        registry.RegisterComposed("TST-2006", TriggerType.Ignition, new BranchOnChoiceOp(branches));

        var options = registry.GetChoiceOptions("TST-2006", TriggerType.Ignition);
        options.Should().NotBeNull();
        options.Should().HaveCount(2);
        options.Should().Contain("use");
        options.Should().Contain("redis");
    }

    [Fact]
    public void GetChoiceOptions_NoBranch_ReturnsNull()
    {
        var registry = new EffectRegistry();
        registry.RegisterComposed("TST-4020", TriggerType.OnDeploy,
            new GainBudgetOp(PlayerRef.Myself, new StaticAmount(500)));

        registry.GetChoiceOptions("TST-4020", TriggerType.OnDeploy).Should().BeNull();
    }

    [Fact]
    public void CardIdsForTrigger_ReturnsMatchingCards()
    {
        var registry = new EffectRegistry();
        registry.Register("TST-2001", TriggerType.OnDeploy, _ => new EffectResult());
        registry.Register("TST-2002", TriggerType.OnDeploy, _ => new EffectResult());
        registry.Register("TST-2002", TriggerType.Ignition, _ => new EffectResult());

        var nos = registry.CardIdsForTrigger(TriggerType.OnDeploy);
        nos.Should().HaveCount(2);
        nos.Should().Contain("TST-2001");
        nos.Should().Contain("TST-2002");
    }

    [Fact]
    public void RegistrationCount_TracksHandlers()
    {
        var registry = new EffectRegistry();
        registry.RegistrationCount.Should().Be(0);

        registry.Register("TST-2001", TriggerType.OnDeploy, _ => new EffectResult());
        registry.Register("TST-2001", TriggerType.Ignition, _ => new EffectResult());

        registry.RegistrationCount.Should().Be(2);
    }

    /// <summary>
    /// Same key overwrites previous registration.
    /// </summary>
    [Fact]
    public void Register_SameKey_Overwrites()
    {
        var registry = new EffectRegistry();
        registry.Register("TST-2001", TriggerType.OnDeploy, _ => new EffectResult { CancelAction = false });
        registry.Register("TST-2001", TriggerType.OnDeploy, _ => new EffectResult { CancelAction = true });

        var handler = registry.Get("TST-2001", TriggerType.OnDeploy)!;
        var result = handler(null!);
        result.CancelAction.Should().BeTrue();
    }
}
