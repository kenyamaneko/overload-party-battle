using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Models;
using OverloadParty.Battle.Npc;
using GD = OverloadParty.GameState;

namespace OverloadParty.Battle.Tests.Npc;

public class TargetSelectorTests
{
    private readonly TestCardCache _cc = new();

    public TargetSelectorTests()
    {
        _cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", tp: 600, av: 1400, mc: 150));
        _cc.Add(TestFactory.DataCard(cardId: "TST-0002", subtype: "Database", yield: 400, av: 800, mc: 100));
        _cc.Add(TestFactory.PlatformCard(cardId: "TEST-0200", name: "TestPlatform"));
    }

    // ─── WeakestInZone ──────────────────────────────────────────

    [Fact]
    public void WeakestInZone_ReturnsFaceUpResourceWithLowestEffectiveAV()
    {
        var field = TestFactory.MakeWireField();
        field.Frontend[0] = TestFactory.MakeWireResource(instanceId: "strong", maxAV: 2000, damage: 0);
        field.Frontend[1] = TestFactory.MakeWireResource(instanceId: "weak", maxAV: 600, damage: 0);

        var result = TargetSelector.WeakestInZone(field, Zones.Frontend);

        result.Should().Be("weak");
    }

    [Fact]
    public void WeakestInZone_ConsidersDamageInEffectiveAV()
    {
        var field = TestFactory.MakeWireField();
        field.Frontend[0] = TestFactory.MakeWireResource(instanceId: "high_av_damaged", maxAV: 2000, damage: 1800);
        field.Frontend[1] = TestFactory.MakeWireResource(instanceId: "low_av_healthy", maxAV: 500, damage: 0);

        var result = TargetSelector.WeakestInZone(field, Zones.Frontend);

        result.Should().Be("high_av_damaged");
    }

    [Fact]
    public void WeakestInZone_NullZone_SearchesBothZones()
    {
        var field = TestFactory.MakeWireField();
        field.Frontend[0] = TestFactory.MakeWireResource(instanceId: "fe_res", maxAV: 1000);
        field.Backend[0] = TestFactory.MakeWireResource(instanceId: "be_res", maxAV: 300);

        var result = TargetSelector.WeakestInZone(field, null);

        result.Should().Be("be_res");
    }

    [Fact]
    public void WeakestInZone_IgnoresFaceDownResources()
    {
        var field = TestFactory.MakeWireField();
        field.Frontend[0] = TestFactory.MakeWireResource(instanceId: "face_down", maxAV: 100, faceUp: false);
        field.Frontend[1] = TestFactory.MakeWireResource(instanceId: "face_up", maxAV: 800);

        var result = TargetSelector.WeakestInZone(field, Zones.Frontend);

        result.Should().Be("face_up");
    }

    [Fact]
    public void WeakestInZone_EmptyField_ReturnsNull()
    {
        var field = TestFactory.MakeWireField();

        var result = TargetSelector.WeakestInZone(field, Zones.Frontend);

        result.Should().BeNull();
    }

    // ─── StrongestInZone ────────────────────────────────────────

    [Fact]
    public void StrongestInZone_ReturnsResourceWithHighestValue()
    {
        var field = TestFactory.MakeWireField();
        field.Frontend[0] = TestFactory.MakeWireResource(cardId: "TST-0001", instanceId: "low_tp", currentTP: 300);
        field.Frontend[1] = TestFactory.MakeWireResource(cardId: "TST-0001", instanceId: "high_tp", currentTP: 900);

        var result = TargetSelector.StrongestInZone(field, Zones.Frontend, _cc);

        result.Should().Be("high_tp");
    }

    [Fact]
    public void StrongestInZone_UsesYieldForDataCards()
    {
        var field = TestFactory.MakeWireField();
        field.Backend[0] = TestFactory.MakeWireResource(
            cardId: "TST-0002", instanceId: "data_res", currentTP: null, currentYield: 500, maxYield: 500);
        field.Backend[1] = TestFactory.MakeWireResource(
            cardId: "TST-0001", instanceId: "compute_res", currentTP: 200, currentYield: null);

        var result = TargetSelector.StrongestInZone(field, Zones.Backend, _cc);

        result.Should().Be("data_res");
    }

    [Fact]
    public void StrongestInZone_EmptyField_ReturnsNull()
    {
        var field = TestFactory.MakeWireField();

        var result = TargetSelector.StrongestInZone(field, Zones.Frontend, _cc);

        result.Should().BeNull();
    }

    // ─── MostDamagedOwn ────────────────────────────────────────

    [Fact]
    public void MostDamagedOwn_ReturnsMostDamagedFaceUpResource()
    {
        var field = TestFactory.MakeWireField();
        field.Frontend[0] = TestFactory.MakeWireResource(instanceId: "slightly_dmg", damage: 100);
        field.Frontend[1] = TestFactory.MakeWireResource(instanceId: "heavily_dmg", damage: 800);
        field.Backend[0] = TestFactory.MakeWireResource(instanceId: "medium_dmg", damage: 400);

        var result = TargetSelector.MostDamagedOwn(field);

        result.Should().Be("heavily_dmg");
    }

    [Fact]
    public void MostDamagedOwn_NoDamagedResources_ReturnsNull()
    {
        var field = TestFactory.MakeWireField();
        field.Frontend[0] = TestFactory.MakeWireResource(instanceId: "healthy", damage: 0);

        var result = TargetSelector.MostDamagedOwn(field);

        result.Should().BeNull();
    }

    [Fact]
    public void MostDamagedOwn_EmptyField_ReturnsNull()
    {
        var field = TestFactory.MakeWireField();

        var result = TargetSelector.MostDamagedOwn(field);

        result.Should().BeNull();
    }

    // ─── CountAllResources ─────────────────────────────────────

    [Fact]
    public void CountAllResources_CountsFaceUpOnly()
    {
        var field = TestFactory.MakeWireField();
        field.Frontend[0] = TestFactory.MakeWireResource(instanceId: "fe1", faceUp: true);
        field.Frontend[1] = TestFactory.MakeWireResource(instanceId: "fe2", faceUp: false);
        field.Backend[0] = TestFactory.MakeWireResource(instanceId: "be1", faceUp: true);

        var count = TargetSelector.CountAllResources(field);

        count.Should().Be(2);
    }

    [Fact]
    public void CountAllResources_EmptyField_ReturnsZero()
    {
        var field = TestFactory.MakeWireField();

        TargetSelector.CountAllResources(field).Should().Be(0);
    }

    // ─── CountResourcesInZone ──────────────────────────────────

    [Fact]
    public void CountResourcesInZone_FrontendOnly()
    {
        var field = TestFactory.MakeWireField();
        field.Frontend[0] = TestFactory.MakeWireResource(instanceId: "fe1");
        field.Frontend[1] = TestFactory.MakeWireResource(instanceId: "fe2");
        field.Backend[0] = TestFactory.MakeWireResource(instanceId: "be1");

        var count = TargetSelector.CountResourcesInZone(field, Zones.Frontend);

        count.Should().Be(2);
    }

    [Fact]
    public void CountResourcesInZone_NullZone_CountsBothZones()
    {
        var field = TestFactory.MakeWireField();
        field.Frontend[0] = TestFactory.MakeWireResource(instanceId: "fe1");
        field.Backend[0] = TestFactory.MakeWireResource(instanceId: "be1");

        var count = TargetSelector.CountResourcesInZone(field, null);

        count.Should().Be(2);
    }

    // ─── HasDamagedResource ────────────────────────────────────

    [Fact]
    public void HasDamagedResource_WithDamaged_ReturnsTrue()
    {
        var field = TestFactory.MakeWireField();
        field.Frontend[0] = TestFactory.MakeWireResource(instanceId: "dmg", damage: 100);

        TargetSelector.HasDamagedResource(field).Should().BeTrue();
    }

    [Fact]
    public void HasDamagedResource_NoDamaged_ReturnsFalse()
    {
        var field = TestFactory.MakeWireField();
        field.Frontend[0] = TestFactory.MakeWireResource(instanceId: "healthy", damage: 0);

        TargetSelector.HasDamagedResource(field).Should().BeFalse();
    }

    [Fact]
    public void HasDamagedResource_EmptyField_ReturnsFalse()
    {
        var field = TestFactory.MakeWireField();

        TargetSelector.HasDamagedResource(field).Should().BeFalse();
    }

    // ─── HasFaceDownSupport (相手フィールド) ──────────────────

    [Fact]
    public void HasFaceDownSupport_WithFaceDown_ReturnsTrue()
    {
        var field = TestFactory.MakeWireOpponentField();
        field.Support[0] = TestFactory.MakeHiddenSupport("sup1", faceDown: true);

        TargetSelector.HasFaceDownSupport(field).Should().BeTrue();
    }

    [Fact]
    public void HasFaceDownSupport_AllFaceUp_ReturnsFalse()
    {
        var field = TestFactory.MakeWireOpponentField();
        field.Support[0] = TestFactory.MakeHiddenSupport("sup1", cardId: "TEST-0200", faceDown: false);

        TargetSelector.HasFaceDownSupport(field).Should().BeFalse();
    }

    [Fact]
    public void HasFaceDownSupport_EmptySupport_ReturnsFalse()
    {
        var field = TestFactory.MakeWireOpponentField();

        TargetSelector.HasFaceDownSupport(field).Should().BeFalse();
    }

    // ─── HasPlatform (相手フィールド) ─────────────────────────

    [Fact]
    public void HasPlatform_WithPlatformCard_ReturnsTrue()
    {
        var field = TestFactory.MakeWireOpponentField();
        field.Support[0] = TestFactory.MakeHiddenSupport("plat1", cardId: "TEST-0200", faceDown: false);

        TargetSelector.HasPlatform(field, _cc).Should().BeTrue();
    }

    [Fact]
    public void HasPlatform_NoPlatform_ReturnsFalse()
    {
        var field = TestFactory.MakeWireOpponentField();
        field.Support[0] = TestFactory.MakeHiddenSupport("sup1", cardId: "TST-0001", faceDown: false);

        TargetSelector.HasPlatform(field, _cc).Should().BeFalse();
    }

    [Fact]
    public void HasPlatform_FaceDownUnpeeked_NotVisible_ReturnsFalse()
    {
        // 情報秘匿: 裏向き未覗き見のサポートは CardID が null。Platform 判定対象外。
        var field = TestFactory.MakeWireOpponentField();
        field.Support[0] = TestFactory.MakeHiddenSupport("sup1", cardId: null, faceDown: true);

        TargetSelector.HasPlatform(field, _cc).Should().BeFalse();
    }

    // ─── FirstPlatformId (相手フィールド) ─────────────────────

    [Fact]
    public void FirstPlatformId_ReturnsPlatformInstanceID()
    {
        var field = TestFactory.MakeWireOpponentField();
        field.Support[0] = TestFactory.MakeHiddenSupport("plat_1", cardId: "TEST-0200", faceDown: false);

        var result = TargetSelector.FirstPlatformId(field, _cc);

        result.Should().Be("plat_1");
    }

    [Fact]
    public void FirstPlatformId_NoPlatform_ReturnsNull()
    {
        var field = TestFactory.MakeWireOpponentField();

        TargetSelector.FirstPlatformId(field, _cc).Should().BeNull();
    }

    // ─── ResourceValue ─────────────────────────────────────────

    [Fact]
    public void ResourceValue_CurrentTP_ReturnsTP()
    {
        var res = TestFactory.MakeWireResource(cardId: "TST-0001", currentTP: 700);

        var value = TargetSelector.ResourceValue(res, _cc);

        value.Should().Be(700);
    }

    [Fact]
    public void ResourceValue_CurrentYield_ReturnsYield()
    {
        var res = TestFactory.MakeWireResource(cardId: "TST-0002", currentTP: null, currentYield: 500, maxYield: 500);

        var value = TargetSelector.ResourceValue(res, _cc);

        value.Should().Be(500);
    }

    [Fact]
    public void ResourceValue_NoCurrentStats_FallsBackToCardDefinition()
    {
        var res = TestFactory.MakeWireResource(cardId: "TST-0001", currentTP: null, currentYield: null);

        var value = TargetSelector.ResourceValue(res, _cc);

        // Card 1 is Compute with BaseThroughput = 600
        value.Should().Be(600);
    }
}
