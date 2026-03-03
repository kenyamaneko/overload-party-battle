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
        Assert.NotNull(handler);

        handler(null!); // just to verify it's callable
        Assert.True(called);
    }

    [Fact]
    public void Get_Unregistered_ReturnsNull()
    {
        var registry = new EffectRegistry();
        Assert.Null(registry.Get(999, TriggerType.Deploy));
    }

    [Fact]
    public void Has_Registered_ReturnsTrue()
    {
        var registry = new EffectRegistry();
        registry.Register(1, TriggerType.Activate, _ => new EffectResult());

        Assert.True(registry.Has(1, TriggerType.Activate));
    }

    [Fact]
    public void Has_Unregistered_ReturnsFalse()
    {
        var registry = new EffectRegistry();
        Assert.False(registry.Has(1, TriggerType.Activate));
    }

    [Fact]
    public void RegisterComposed_StoresOps_ForClassification()
    {
        var registry = new EffectRegistry();
        var ops = new IEffectOp[] { new GainBudgetOp(PlayerRef.Self, new StaticAmount(500)) };
        registry.RegisterComposed(42, TriggerType.Deploy, ops);

        var reg = registry.GetRegistration(42, TriggerType.Deploy);
        Assert.NotNull(reg);
        Assert.NotNull(reg.Ops);
        Assert.Single(reg.Ops);
    }

    [Fact]
    public void GetEffectInfo_ReturnsClassification()
    {
        var registry = new EffectRegistry();
        registry.RegisterComposed(42, TriggerType.Deploy,
            new GainBudgetOp(PlayerRef.Self, new StaticAmount(500)),
            new DrawCardsOp(1));

        var info = registry.GetEffectInfo(42, TriggerType.Deploy);
        Assert.NotNull(info);
        Assert.True(info.HasCategory(EffectCategory.BudgetGain));
        Assert.True(info.HasCategory(EffectCategory.Draw));
    }

    [Fact]
    public void GetEffectInfo_NoOps_ReturnsNull()
    {
        var registry = new EffectRegistry();
        registry.Register(42, TriggerType.Deploy, _ => new EffectResult());

        var info = registry.GetEffectInfo(42, TriggerType.Deploy);
        Assert.Null(info);
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
        Assert.NotNull(options);
        Assert.Equal(2, options.Count);
        Assert.Contains("use", options);
        Assert.Contains("redis", options);
    }

    [Fact]
    public void GetChoiceOptions_NoBranch_ReturnsNull()
    {
        var registry = new EffectRegistry();
        registry.RegisterComposed(42, TriggerType.Deploy,
            new GainBudgetOp(PlayerRef.Self, new StaticAmount(500)));

        var options = registry.GetChoiceOptions(42, TriggerType.Deploy);
        Assert.Null(options);
    }

    [Fact]
    public void CardNosForTrigger_ReturnsMatchingCards()
    {
        var registry = new EffectRegistry();
        registry.Register(1, TriggerType.Deploy, _ => new EffectResult());
        registry.Register(2, TriggerType.Deploy, _ => new EffectResult());
        registry.Register(3, TriggerType.Activate, _ => new EffectResult());

        var nos = registry.CardNosForTrigger(TriggerType.Deploy);
        Assert.Equal(2, nos.Count);
        Assert.Contains(1, nos);
        Assert.Contains(2, nos);
    }

    [Fact]
    public void AddPassive_TrackedSeparately()
    {
        var registry = new EffectRegistry();
        registry.AddPassive(new PassiveDef { CardNo = 10, PassiveType = "tp_bonus", Value = 200 });
        registry.AddPassive(new PassiveDef { CardNo = 11, PassiveType = "yield_bonus", Value = 100 });

        Assert.Equal(2, registry.PassiveCount);
        var passives = registry.GetPassives();
        Assert.Equal(2, passives.Count);
    }

    [Fact]
    public void RegistrationCount_TracksHandlers()
    {
        var registry = new EffectRegistry();
        Assert.Equal(0, registry.RegistrationCount);

        registry.Register(1, TriggerType.Deploy, _ => new EffectResult());
        registry.Register(1, TriggerType.Activate, _ => new EffectResult());

        Assert.Equal(2, registry.RegistrationCount);
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
        Assert.True(result.CancelAction);
    }
}
