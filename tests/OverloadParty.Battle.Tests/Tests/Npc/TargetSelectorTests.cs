using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Models;
using OverloadParty.Battle.Npc;

namespace OverloadParty.Battle.Tests.Tests.Npc;

public class TargetSelectorTests
{
    private readonly TestCardCache _cc = new();

    public TargetSelectorTests()
    {
        _cc.Add(TestFactory.ComputeCard(cardNo: 1, tp: 600, av: 1400, mc: 150));
        _cc.Add(TestFactory.DataCard(cardNo: 100, cardType: CardTypes.Database, yield: 400, av: 800, mc: 100));
        _cc.Add(TestFactory.PlatformCard(cardNo: 200, name: "TestPlatform"));
    }

    // ─── WeakestInZone ──────────────────────────────────────────

    [Fact]
    public void WeakestInZone_ReturnsFaceUpResourceWithLowestEffectiveAV()
    {
        var field = TestFactory.MakeField();
        field.Frontend[0] = TestFactory.MakeResource(instanceId: "strong", maxAV: 2000, damage: 0);
        field.Frontend[1] = TestFactory.MakeResource(instanceId: "weak", maxAV: 600, damage: 0);

        var result = TargetSelector.WeakestInZone(field, GameConstants.ZoneFrontend);

        result.Should().Be("weak");
    }

    [Fact]
    public void WeakestInZone_ConsidersDamageInEffectiveAV()
    {
        var field = TestFactory.MakeField();
        field.Frontend[0] = TestFactory.MakeResource(instanceId: "high_av_damaged", maxAV: 2000, damage: 1800);
        field.Frontend[1] = TestFactory.MakeResource(instanceId: "low_av_healthy", maxAV: 500, damage: 0);

        var result = TargetSelector.WeakestInZone(field, GameConstants.ZoneFrontend);

        // EffectiveAV: high_av_damaged = 200, low_av_healthy = 500
        result.Should().Be("high_av_damaged");
    }

    [Fact]
    public void WeakestInZone_NullZone_SearchesBothZones()
    {
        var field = TestFactory.MakeField();
        field.Frontend[0] = TestFactory.MakeResource(instanceId: "fe_res", maxAV: 1000);
        field.Backend[0] = TestFactory.MakeResource(instanceId: "be_res", maxAV: 300);

        var result = TargetSelector.WeakestInZone(field, null);

        result.Should().Be("be_res");
    }

    [Fact]
    public void WeakestInZone_IgnoresFaceDownResources()
    {
        var field = TestFactory.MakeField();
        field.Frontend[0] = TestFactory.MakeResource(instanceId: "face_down", maxAV: 100, faceUp: false);
        field.Frontend[1] = TestFactory.MakeResource(instanceId: "face_up", maxAV: 800);

        var result = TargetSelector.WeakestInZone(field, GameConstants.ZoneFrontend);

        result.Should().Be("face_up");
    }

    [Fact]
    public void WeakestInZone_EmptyField_ReturnsNull()
    {
        var field = TestFactory.MakeField();

        var result = TargetSelector.WeakestInZone(field, GameConstants.ZoneFrontend);

        result.Should().BeNull();
    }

    // ─── StrongestInZone ────────────────────────────────────────

    [Fact]
    public void StrongestInZone_ReturnsResourceWithHighestValue()
    {
        var field = TestFactory.MakeField();
        field.Frontend[0] = TestFactory.MakeResource(cardId: 1, instanceId: "low_tp", currentTP: 300);
        field.Frontend[1] = TestFactory.MakeResource(cardId: 1, instanceId: "high_tp", currentTP: 900);

        var result = TargetSelector.StrongestInZone(field, GameConstants.ZoneFrontend, _cc);

        result.Should().Be("high_tp");
    }

    [Fact]
    public void StrongestInZone_UsesYieldForDataCards()
    {
        var field = TestFactory.MakeField();
        field.Backend[0] = TestFactory.MakeResource(
            cardId: 100, instanceId: "data_res", currentTP: null, currentYield: 500, maxYield: 500);
        field.Backend[1] = TestFactory.MakeResource(
            cardId: 1, instanceId: "compute_res", currentTP: 200, currentYield: null);

        var result = TargetSelector.StrongestInZone(field, GameConstants.ZoneBackend, _cc);

        result.Should().Be("data_res");
    }

    [Fact]
    public void StrongestInZone_EmptyField_ReturnsNull()
    {
        var field = TestFactory.MakeField();

        var result = TargetSelector.StrongestInZone(field, GameConstants.ZoneFrontend, _cc);

        result.Should().BeNull();
    }

    // ─── MostDamagedOwn ────────────────────────────────────────

    [Fact]
    public void MostDamagedOwn_ReturnsMostDamagedFaceUpResource()
    {
        var field = TestFactory.MakeField();
        field.Frontend[0] = TestFactory.MakeResource(instanceId: "slightly_dmg", damage: 100);
        field.Frontend[1] = TestFactory.MakeResource(instanceId: "heavily_dmg", damage: 800);
        field.Backend[0] = TestFactory.MakeResource(instanceId: "medium_dmg", damage: 400);

        var result = TargetSelector.MostDamagedOwn(field);

        result.Should().Be("heavily_dmg");
    }

    [Fact]
    public void MostDamagedOwn_NoDamagedResources_ReturnsNull()
    {
        var field = TestFactory.MakeField();
        field.Frontend[0] = TestFactory.MakeResource(instanceId: "healthy", damage: 0);

        var result = TargetSelector.MostDamagedOwn(field);

        result.Should().BeNull();
    }

    [Fact]
    public void MostDamagedOwn_EmptyField_ReturnsNull()
    {
        var field = TestFactory.MakeField();

        var result = TargetSelector.MostDamagedOwn(field);

        result.Should().BeNull();
    }

    // ─── CountAllResources ─────────────────────────────────────

    [Fact]
    public void CountAllResources_CountsFaceUpOnly()
    {
        var field = TestFactory.MakeField();
        field.Frontend[0] = TestFactory.MakeResource(instanceId: "fe1", faceUp: true);
        field.Frontend[1] = TestFactory.MakeResource(instanceId: "fe2", faceUp: false);
        field.Backend[0] = TestFactory.MakeResource(instanceId: "be1", faceUp: true);

        var count = TargetSelector.CountAllResources(field);

        count.Should().Be(2);
    }

    [Fact]
    public void CountAllResources_EmptyField_ReturnsZero()
    {
        var field = TestFactory.MakeField();

        TargetSelector.CountAllResources(field).Should().Be(0);
    }

    // ─── CountResourcesInZone ──────────────────────────────────

    [Fact]
    public void CountResourcesInZone_FrontendOnly()
    {
        var field = TestFactory.MakeField();
        field.Frontend[0] = TestFactory.MakeResource(instanceId: "fe1");
        field.Frontend[1] = TestFactory.MakeResource(instanceId: "fe2");
        field.Backend[0] = TestFactory.MakeResource(instanceId: "be1");

        var count = TargetSelector.CountResourcesInZone(field, GameConstants.ZoneFrontend);

        count.Should().Be(2);
    }

    [Fact]
    public void CountResourcesInZone_NullZone_CountsBothZones()
    {
        var field = TestFactory.MakeField();
        field.Frontend[0] = TestFactory.MakeResource(instanceId: "fe1");
        field.Backend[0] = TestFactory.MakeResource(instanceId: "be1");

        var count = TargetSelector.CountResourcesInZone(field, null);

        count.Should().Be(2);
    }

    // ─── HasDamagedResource ────────────────────────────────────

    [Fact]
    public void HasDamagedResource_WithDamaged_ReturnsTrue()
    {
        var field = TestFactory.MakeField();
        field.Frontend[0] = TestFactory.MakeResource(instanceId: "dmg", damage: 100);

        TargetSelector.HasDamagedResource(field).Should().BeTrue();
    }

    [Fact]
    public void HasDamagedResource_NoDamaged_ReturnsFalse()
    {
        var field = TestFactory.MakeField();
        field.Frontend[0] = TestFactory.MakeResource(instanceId: "healthy", damage: 0);

        TargetSelector.HasDamagedResource(field).Should().BeFalse();
    }

    [Fact]
    public void HasDamagedResource_EmptyField_ReturnsFalse()
    {
        var field = TestFactory.MakeField();

        TargetSelector.HasDamagedResource(field).Should().BeFalse();
    }

    // ─── HasFaceDownSupport ────────────────────────────────────

    [Fact]
    public void HasFaceDownSupport_WithFaceDown_ReturnsTrue()
    {
        var field = TestFactory.MakeField();
        field.Support[0] = new SupportInstance { InstanceID = "sup1", FaceUp = false };

        TargetSelector.HasFaceDownSupport(field).Should().BeTrue();
    }

    [Fact]
    public void HasFaceDownSupport_AllFaceUp_ReturnsFalse()
    {
        var field = TestFactory.MakeField();
        field.Support[0] = new SupportInstance { InstanceID = "sup1", FaceUp = true };

        TargetSelector.HasFaceDownSupport(field).Should().BeFalse();
    }

    [Fact]
    public void HasFaceDownSupport_EmptySupport_ReturnsFalse()
    {
        var field = TestFactory.MakeField();

        TargetSelector.HasFaceDownSupport(field).Should().BeFalse();
    }

    // ─── HasPlatform ───────────────────────────────────────────

    [Fact]
    public void HasPlatform_WithPlatformCard_ReturnsTrue()
    {
        var field = TestFactory.MakeField();
        field.Support[0] = new SupportInstance { InstanceID = "plat1", CardID = 200, FaceUp = true };

        TargetSelector.HasPlatform(field, _cc).Should().BeTrue();
    }

    [Fact]
    public void HasPlatform_NoPlatform_ReturnsFalse()
    {
        var field = TestFactory.MakeField();
        field.Support[0] = new SupportInstance { InstanceID = "sup1", CardID = 999, FaceUp = true };

        TargetSelector.HasPlatform(field, _cc).Should().BeFalse();
    }

    // ─── FirstPlatformId ───────────────────────────────────────

    [Fact]
    public void FirstPlatformId_ReturnsPlatformInstanceID()
    {
        var field = TestFactory.MakeField();
        field.Support[0] = new SupportInstance { InstanceID = "plat_1", CardID = 200, FaceUp = true };

        var result = TargetSelector.FirstPlatformId(field, _cc);

        result.Should().Be("plat_1");
    }

    [Fact]
    public void FirstPlatformId_NoPlatform_ReturnsNull()
    {
        var field = TestFactory.MakeField();

        TargetSelector.FirstPlatformId(field, _cc).Should().BeNull();
    }

    // ─── ResourceValue ─────────────────────────────────────────

    [Fact]
    public void ResourceValue_CurrentTP_ReturnsTP()
    {
        var res = TestFactory.MakeResource(cardId: 1, currentTP: 700);

        var value = TargetSelector.ResourceValue(res, _cc);

        value.Should().Be(700);
    }

    [Fact]
    public void ResourceValue_CurrentYield_ReturnsYield()
    {
        var res = TestFactory.MakeResource(cardId: 100, currentTP: null, currentYield: 500, maxYield: 500);

        var value = TargetSelector.ResourceValue(res, _cc);

        value.Should().Be(500);
    }

    [Fact]
    public void ResourceValue_NoCurrentStats_FallsBackToCardDefinition()
    {
        var res = TestFactory.MakeResource(cardId: 1, currentTP: null, currentYield: null);

        var value = TargetSelector.ResourceValue(res, _cc);

        // Card 1 is Compute with BaseThroughput = 600
        value.Should().Be(600);
    }

    [Fact]
    public void ResourceValue_UnknownCard_ReturnsZero()
    {
        var res = TestFactory.MakeResource(cardId: 999, currentTP: null, currentYield: null);

        var value = TargetSelector.ResourceValue(res, _cc);

        value.Should().Be(0);
    }
}
