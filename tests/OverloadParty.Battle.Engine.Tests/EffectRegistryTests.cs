using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Effects.Ops;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Effects;

[Trait("対象", "効果レジストリ")]
public class EffectRegistryTests
{
    [Fact(DisplayName = "登録した効果ハンドラを取得でき呼び出せる")]
    public void Register_AndGet_ReturnsHandler()
    {
        var registry = new EffectRegistry();
        bool called = false;
        registry.Register("TST-0001", TriggerType.OnDeploy, _ => { called = true; return new EffectResult(); });

        var handler = registry.Get("TST-0001", TriggerType.OnDeploy);
        handler.Should().NotBeNull();

        handler!(null!); // just to verify it's callable
        called.Should().BeTrue();
    }

    [Fact(DisplayName = "登録されていない効果を取得すると null が返る")]
    public void Get_Unregistered_ReturnsNull()
    {
        var registry = new EffectRegistry();
        registry.Get("TEST-0999", TriggerType.OnDeploy).Should().BeNull();
    }

    [Fact(DisplayName = "登録済みの効果があると判定される")]
    public void Has_Registered_ReturnsTrue()
    {
        var registry = new EffectRegistry();
        registry.Register("TST-0001", TriggerType.Ignition, _ => new EffectResult());

        registry.Has("TST-0001", TriggerType.Ignition).Should().BeTrue();
    }

    [Fact(DisplayName = "登録されていない効果はないと判定される")]
    public void Has_Unregistered_ReturnsFalse()
    {
        var registry = new EffectRegistry();
        registry.Has("TST-0001", TriggerType.Ignition).Should().BeFalse();
    }

    [Fact(DisplayName = "合成登録した効果に op が保持される")]
    public void RegisterComposed_StoresOps_ForClassification()
    {
        var registry = new EffectRegistry();
        var ops = new IEffectOp[] { new GainBudgetOp(PlayerRef.Myself, new StaticAmount(500)) };
        registry.RegisterComposed("TST-0002", TriggerType.OnDeploy, ops);

        var reg = registry.GetRegistration("TST-0002", TriggerType.OnDeploy);
        reg.Should().NotBeNull();
        reg!.Ops.Should().NotBeNull();
        reg.Ops.Should().ContainSingle();
    }

    [Fact(DisplayName = "合成した効果からバジェット獲得とドローの分類が得られる")]
    public void GetEffectInfo_ReturnsClassification()
    {
        var registry = new EffectRegistry();
        registry.RegisterComposed("TST-0002", TriggerType.OnDeploy,
            new GainBudgetOp(PlayerRef.Myself, new StaticAmount(500)),
            new DrawCardsOp(1));

        var info = registry.GetEffectInfo("TST-0002", TriggerType.OnDeploy);
        info.Should().NotBeNull();
        info!.HasCategory(EffectCategory.BudgetGain).Should().BeTrue();
        info.HasCategory(EffectCategory.Draw).Should().BeTrue();
    }

    [Fact(DisplayName = "op を持たない効果の分類は null になる")]
    public void GetEffectInfo_NoOps_ReturnsNull()
    {
        var registry = new EffectRegistry();
        registry.Register("TST-0002", TriggerType.OnDeploy, _ => new EffectResult());

        registry.GetEffectInfo("TST-0002", TriggerType.OnDeploy).Should().BeNull();
    }

    [Fact(DisplayName = "分岐効果から選択肢として use と redis が返る")]
    public void GetChoiceOptions_BranchOnChoice_ReturnsBranchKeys()
    {
        var registry = new EffectRegistry();
        var branches = new Dictionary<string, List<IEffectOp>>
        {
            ["use"] = [new GainBudgetOp(PlayerRef.Myself, new StaticAmount(100))],
            ["redis"] = [new DrawCardsOp(1)],
        };
        registry.RegisterComposed("TST-0003", TriggerType.Ignition, new BranchOnChoiceOp(branches));

        var options = registry.GetChoiceOptions("TST-0003", TriggerType.Ignition);
        options.Should().NotBeNull();
        options.Should().HaveCount(2);
        options.Should().Contain("use");
        options.Should().Contain("redis");
    }

    [Fact(DisplayName = "分岐を持たない効果の選択肢は null になる")]
    public void GetChoiceOptions_NoBranch_ReturnsNull()
    {
        var registry = new EffectRegistry();
        registry.RegisterComposed("TST-0002", TriggerType.OnDeploy,
            new GainBudgetOp(PlayerRef.Myself, new StaticAmount(500)));

        registry.GetChoiceOptions("TST-0002", TriggerType.OnDeploy).Should().BeNull();
    }

    [Fact(DisplayName = "OnDeploy トリガーに登録された TST-0001 と TST-0004 が返る")]
    public void CardIdsForTrigger_ReturnsMatchingCards()
    {
        var registry = new EffectRegistry();
        registry.Register("TST-0001", TriggerType.OnDeploy, _ => new EffectResult());
        registry.Register("TST-0004", TriggerType.OnDeploy, _ => new EffectResult());
        registry.Register("TST-0004", TriggerType.Ignition, _ => new EffectResult());

        var nos = registry.CardIdsForTrigger(TriggerType.OnDeploy);
        nos.Should().HaveCount(2);
        nos.Should().Contain("TST-0001");
        nos.Should().Contain("TST-0004");
    }

    [Fact(DisplayName = "効果を 2 件登録すると登録数が 2 になる")]
    public void RegistrationCount_TracksHandlers()
    {
        var registry = new EffectRegistry();
        registry.RegistrationCount.Should().Be(0);

        registry.Register("TST-0001", TriggerType.OnDeploy, _ => new EffectResult());
        registry.Register("TST-0001", TriggerType.Ignition, _ => new EffectResult());

        registry.RegistrationCount.Should().Be(2);
    }

    [Fact(DisplayName = "同じキーで再登録すると後の効果で上書きされる")]
    public void Register_SameKey_Overwrites()
    {
        var registry = new EffectRegistry();
        registry.Register("TST-0001", TriggerType.OnDeploy, _ => new EffectResult { ShouldCancelAction = false });
        registry.Register("TST-0001", TriggerType.OnDeploy, _ => new EffectResult { ShouldCancelAction = true });

        var handler = registry.Get("TST-0001", TriggerType.OnDeploy)!;
        var result = handler(null!);
        result.ShouldCancelAction.Should().BeTrue();
    }
}
