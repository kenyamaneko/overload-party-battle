using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Engine;

/// <summary>
/// Tests for StatCalculator based on RULEBOOK.md rules:
/// - Rank multipliers: small=×1, medium=×2, large=×3
/// - Instance family: M=(1,1), C=(1.5,0.75), R=(0.75,1.5)
/// - Elastic: effectiveBonus = free_tier × ln(1 + rawBonus / free_tier)
/// - MC Elastic: max(0, intrinsicStat - free_tier) × cost_per_request / 100
/// - Truncation: floor (1 未満切り捨て)
/// </summary>
public class StatCalculatorTests
{
    // ─── ElasticBonus ─────────────────────────────────────────

    [Fact]
    public void EffectiveElasticBonus_ZeroRaw_ReturnsZero()
    {
        StatCalculator.EffectiveElasticBonus(0, 500).Should().Be(0);
    }

    [Fact]
    public void EffectiveElasticBonus_ZeroScale_ReturnsRaw()
    {
        StatCalculator.EffectiveElasticBonus(100, 0).Should().Be(100);
    }

    [Fact]
    public void EffectiveElasticBonus_NegativeRaw_ReturnsNegative()
    {
        StatCalculator.EffectiveElasticBonus(-100, 500).Should().Be(-100);
    }

    /// <summary>
    /// Rulebook example: Container (free_tier=500), 1 trigger → raw=100
    /// effective = 500 × ln(1 + 100/500) ≈ 500 × ln(1.2) ≈ 500 × 0.1823 ≈ 91
    /// </summary>
    [Fact]
    public void EffectiveElasticBonus_RulebookExample_1Trigger()
    {
        StatCalculator.EffectiveElasticBonus(100, 500).Should().Be(91);
    }

    /// <summary>
    /// Rulebook: 5 triggers → raw=500, effective = 500 × ln(2) ≈ 346
    /// </summary>
    [Fact]
    public void EffectiveElasticBonus_RulebookExample_5Triggers()
    {
        StatCalculator.EffectiveElasticBonus(500, 500).Should().Be(346);
    }

    /// <summary>
    /// Rulebook: 10 triggers → raw=1000, effective = 500 × ln(3) ≈ 549
    /// </summary>
    [Fact]
    public void EffectiveElasticBonus_RulebookExample_10Triggers()
    {
        StatCalculator.EffectiveElasticBonus(1000, 500).Should().Be(549);
    }

    /// <summary>
    /// Rulebook: 20 triggers → raw=2000, effective = 500 × ln(5) ≈ 804
    /// </summary>
    [Fact]
    public void EffectiveElasticBonus_RulebookExample_20Triggers()
    {
        StatCalculator.EffectiveElasticBonus(2000, 500).Should().Be(804);
    }

    // ─── CalculateEffectiveTP ─────────────────────────────────

    [Fact]
    public void CalculateEffectiveTP_BasicCompute_SmallRank()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", tp: 600, av: 1400));

        var field = TestFactory.MakeField();
        var resource = TestFactory.MakeResource(cardId: "TST-0001", maxTP: 600, currentTP: 600);

        StatCalculator.CalculateEffectiveTP(resource, field, cc).Should().Be(600);
    }

    /// <summary>
    /// Rulebook: えくぼ (TP 700) at medium rank → 700 × 2 = 1400
    /// </summary>
    [Fact]
    public void CalculateEffectiveTP_MediumRank_DoublesBase()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", tp: 700, av: 1400));

        var field = TestFactory.MakeField();
        var resource = TestFactory.MakeResource(cardId: "TST-0001", rank: Rank.Medium);

        StatCalculator.CalculateEffectiveTP(resource, field, cc).Should().Be(1400);
    }

    /// <summary>
    /// Rulebook: large rank → base × 3
    /// </summary>
    [Fact]
    public void CalculateEffectiveTP_LargeRank_TriplesBase()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", tp: 700));

        var field = TestFactory.MakeField();
        var resource = TestFactory.MakeResource(cardId: "TST-0001", rank: Rank.Large);

        StatCalculator.CalculateEffectiveTP(resource, field, cc).Should().Be(2100);
    }

    /// <summary>
    /// Instance Family C: TP × 1.3
    /// TP 600 at small with C family → 600 × 1 × 1.3 = 780
    /// </summary>
    [Fact]
    public void CalculateEffectiveTP_FamilyC_MultipliesTP()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", tp: 600));

        var field = TestFactory.MakeField();
        var resource = TestFactory.MakeResource(cardId: "TST-0001", family: InstanceFamily.C);

        StatCalculator.CalculateEffectiveTP(resource, field, cc).Should().Be(780);
    }

    /// <summary>
    /// Instance Family R: TP × 0.7
    /// TP 600 at small with R family → 600 × 0.7 = 420
    /// </summary>
    [Fact]
    public void CalculateEffectiveTP_FamilyR_ReducesTP()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", tp: 600));

        var field = TestFactory.MakeField();
        var resource = TestFactory.MakeResource(cardId: "TST-0001", family: InstanceFamily.R);

        StatCalculator.CalculateEffectiveTP(resource, field, cc).Should().Be(420);
    }

    /// <summary>
    /// Family C + medium rank: TP 600 × 2 × 1.3 = 1560
    /// </summary>
    [Fact]
    public void CalculateEffectiveTP_FamilyC_MediumRank()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", tp: 600));

        var field = TestFactory.MakeField();
        var resource = TestFactory.MakeResource(cardId: "TST-0001", rank: Rank.Medium, family: InstanceFamily.C);

        StatCalculator.CalculateEffectiveTP(resource, field, cc).Should().Be(1560);
    }

    /// <summary>
    /// Elastic container: base 500, raw bonus 100, free_tier 500
    /// effective bonus = 91, total TP = 500 + 91 = 591
    /// </summary>
    [Fact]
    public void CalculateEffectiveTP_ElasticContainer_WithBonus()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ElasticContainerCard(cardId: "TEST-0002"));

        var field = TestFactory.MakeField();
        var resource = TestFactory.MakeResource(cardId: "TEST-0002", elasticBonus: 100, maxTP: 500, currentTP: 500);

        StatCalculator.CalculateEffectiveTP(resource, field, cc).Should().Be(591); // 500 + 91
    }

    /// <summary>
    /// Elastic container: 5 triggers → effective bonus 346, total = 846
    /// </summary>
    [Fact]
    public void CalculateEffectiveTP_ElasticContainer_5Triggers()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ElasticContainerCard(cardId: "TEST-0002"));

        var field = TestFactory.MakeField();
        var resource = TestFactory.MakeResource(cardId: "TEST-0002", elasticBonus: 500, maxTP: 500, currentTP: 500);

        StatCalculator.CalculateEffectiveTP(resource, field, cc).Should().Be(846);
    }

    /// <summary>
    /// Non-compute card → TP = 0
    /// </summary>
    [Fact]
    public void CalculateEffectiveTP_DataCard_ReturnsZero()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.DataCard(cardId: "TST-0002"));

        var field = TestFactory.MakeField();
        var resource = TestFactory.MakeResource(cardId: "TST-0002");

        StatCalculator.CalculateEffectiveTP(resource, field, cc).Should().Be(0);
    }

    /// <summary>
    /// Temporary buff_tp effect should be added to TP.
    /// </summary>
    [Fact]
    public void CalculateEffectiveTP_WithTempBuff()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", tp: 600));

        var field = TestFactory.MakeField();
        var resource = TestFactory.MakeResource(cardId: "TST-0001");
        resource.TemporaryEffects.Add(new TemporaryEffect { EffectType = "buff_tp", Value = 200 });

        StatCalculator.CalculateEffectiveTP(resource, field, cc).Should().Be(800);
    }

    /// <summary>
    /// Temporary debuff_tp should reduce TP (but not below 0).
    /// </summary>
    [Fact]
    public void CalculateEffectiveTP_WithTempDebuff_FlooredAtZero()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", tp: 600));

        var field = TestFactory.MakeField();
        var resource = TestFactory.MakeResource(cardId: "TST-0001");
        resource.TemporaryEffects.Add(new TemporaryEffect { EffectType = "debuff_tp", Value = 700 });

        StatCalculator.CalculateEffectiveTP(resource, field, cc).Should().Be(0);
    }

    // ─── CalculateEffectiveInsight ──────────────────────────────

    [Fact]
    public void CalculateEffectiveInsight_BasicDB()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.DataCard(cardId: "TST-0002", yield: 400));

        var field = TestFactory.MakeField();
        var resource = TestFactory.MakeResource(cardId: "TST-0002", maxYield: 400, currentYield: 400, maxTP: null, currentTP: null);

        StatCalculator.CalculateEffectiveInsight(resource, field, cc).Should().Be(400);
    }

    [Fact]
    public void CalculateEffectiveInsight_ComputeCard_ReturnsZero()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));

        var field = TestFactory.MakeField();
        var resource = TestFactory.MakeResource(cardId: "TST-0001");

        StatCalculator.CalculateEffectiveInsight(resource, field, cc).Should().Be(0);
    }

    // ─── CalculateMaxAV ───────────────────────────────────────

    /// <summary>
    /// Rulebook: AV at small = base AV. medium = base × 2.
    /// えくぼ (AV 1400): small=1400, medium=2800, large=4200
    /// </summary>
    [Theory]
    [InlineData(Rank.Small, null, 1400)]
    [InlineData(Rank.Medium, null, 2800)]
    [InlineData(Rank.Large, null, 4200)]
    public void CalculateMaxAV_RankMultiplier(Rank rank, InstanceFamily? family, long expected)
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", av: 1400));

        var resource = TestFactory.MakeResource(cardId: "TST-0001", rank: rank, family: family);

        StatCalculator.CalculateMaxAV(resource, TestFactory.MakeField(), cc).Should().Be(expected);
    }

    /// <summary>
    /// Family R: AV × 1.3 → 1400 × 1.3 = 1820
    /// </summary>
    [Fact]
    public void CalculateMaxAV_FamilyR_IncreasesAV()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", av: 1400));

        var resource = TestFactory.MakeResource(cardId: "TST-0001", family: InstanceFamily.R);

        StatCalculator.CalculateMaxAV(resource, TestFactory.MakeField(), cc).Should().Be(1820);
    }

    /// <summary>
    /// Family C: AV × 0.7 → 1400 × 0.7 = 979 (truncated)
    /// </summary>
    [Fact]
    public void CalculateMaxAV_FamilyC_DecreasesAV()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", av: 1400));

        var resource = TestFactory.MakeResource(cardId: "TST-0001", family: InstanceFamily.C);

        StatCalculator.CalculateMaxAV(resource, TestFactory.MakeField(), cc).Should().Be(979);
    }

    /// <summary>
    /// Rulebook truncation: 1350 × 0.7 = 944 (floating-point truncation)
    /// </summary>
    [Fact]
    public void CalculateMaxAV_Truncation()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", av: 1350));

        var resource = TestFactory.MakeResource(cardId: "TST-0001", family: InstanceFamily.C);

        StatCalculator.CalculateMaxAV(resource, TestFactory.MakeField(), cc).Should().Be(944);
    }

    // ─── ApplyElasticBonus ────────────────────────────────────

    [Fact]
    public void ApplyElasticBonus_IncrementsRawBonus()
    {
        var cc = new TestCardCache();
        var card = TestFactory.ElasticContainerCard(cardId: "TEST-0002");
        cc.Add(card);

        var resource = TestFactory.MakeResource(cardId: "TST-0003", elasticBonus: 0);
        StatCalculator.ApplyElasticBonus(resource, card);

        resource.ElasticBonus.Should().Be(100);
    }

    [Fact]
    public void ApplyElasticBonus_NonElastic_NoChange()
    {
        var card = TestFactory.ComputeCard(cardId: "TST-0001", elastic: false);
        var resource = TestFactory.MakeResource(cardId: "TST-0001", elasticBonus: 0);
        StatCalculator.ApplyElasticBonus(resource, card);

        resource.ElasticBonus.Should().Be(0);
    }

    // ─── Truncate ─────────────────────────────────────────────

    [Theory]
    [InlineData(1012.5, 1012)]
    [InlineData(675.0, 675)]
    [InlineData(0.9, 0)]
    [InlineData(-1.5, -1)]
    public void Truncate_TruncatesTowardsZero(double input, long expected)
    {
        StatCalculator.Truncate(input).Should().Be(expected);
    }

    // ─── RecalculateMaxTP ──────────────────────────────────────

    [Fact]
    public void RecalculateMaxTP_SmallRank_ReturnsBase()
    {
        var card = TestFactory.ComputeCard(cardId: "TST-0001", tp: 600);
        var resource = TestFactory.MakeResource(cardId: "TST-0001", rank: Rank.Small);

        StatCalculator.RecalculateMaxTP(resource, card).Should().Be(600);
    }

    [Fact]
    public void RecalculateMaxTP_MediumRank_DoublesBase()
    {
        var card = TestFactory.ComputeCard(cardId: "TST-0001", tp: 600);
        var resource = TestFactory.MakeResource(cardId: "TST-0001", rank: Rank.Medium);

        StatCalculator.RecalculateMaxTP(resource, card).Should().Be(1200);
    }

    [Fact]
    public void RecalculateMaxTP_LargeRank_TriplesBase()
    {
        var card = TestFactory.ComputeCard(cardId: "TST-0001", tp: 600);
        var resource = TestFactory.MakeResource(cardId: "TST-0001", rank: Rank.Large);

        StatCalculator.RecalculateMaxTP(resource, card).Should().Be(1800);
    }

    [Fact]
    public void RecalculateMaxTP_WithFamilyC_AppliesMultiplier()
    {
        var card = TestFactory.ComputeCard(cardId: "TST-0001", tp: 600);
        var resource = TestFactory.MakeResource(cardId: "TST-0001", rank: Rank.Small, family: InstanceFamily.C);

        // 600 * 1 * 1.3 = 780
        StatCalculator.RecalculateMaxTP(resource, card).Should().Be(780);
    }

    [Fact]
    public void RecalculateMaxTP_WithFamilyR_AppliesMultiplier()
    {
        var card = TestFactory.ComputeCard(cardId: "TST-0001", tp: 600);
        var resource = TestFactory.MakeResource(cardId: "TST-0001", rank: Rank.Small, family: InstanceFamily.R);

        // 600 * 1 * 0.7 = 420
        StatCalculator.RecalculateMaxTP(resource, card).Should().Be(420);
    }

    [Fact]
    public void RecalculateMaxTP_DataCard_ReturnsZero()
    {
        var card = TestFactory.DataCard(cardId: "TST-0002", yield: 400);
        var resource = TestFactory.MakeResource(cardId: "TST-0002");

        StatCalculator.RecalculateMaxTP(resource, card).Should().Be(0);
    }

    [Fact]
    public void RecalculateMaxTP_MediumRank_FamilyC()
    {
        var card = TestFactory.ComputeCard(cardId: "TST-0001", tp: 600);
        var resource = TestFactory.MakeResource(cardId: "TST-0001", rank: Rank.Medium, family: InstanceFamily.C);

        // 600 * 2 * 1.3 = 1560
        StatCalculator.RecalculateMaxTP(resource, card).Should().Be(1560);
    }

    // ─── RecalculateMaxYield ────────────────────────────────────

    [Fact]
    public void RecalculateMaxYield_SmallRank_ReturnsBase()
    {
        var card = TestFactory.DataCard(cardId: "TST-0002", yield: 400);
        var resource = TestFactory.MakeResource(cardId: "TST-0002", rank: Rank.Small);

        StatCalculator.RecalculateMaxYield(resource, card).Should().Be(400);
    }

    [Fact]
    public void RecalculateMaxYield_MediumRank_DoublesBase()
    {
        var card = TestFactory.DataCard(cardId: "TST-0002", yield: 400);
        var resource = TestFactory.MakeResource(cardId: "TST-0002", rank: Rank.Medium);

        StatCalculator.RecalculateMaxYield(resource, card).Should().Be(800);
    }

    [Fact]
    public void RecalculateMaxYield_LargeRank_TriplesBase()
    {
        var card = TestFactory.DataCard(cardId: "TST-0002", yield: 400);
        var resource = TestFactory.MakeResource(cardId: "TST-0002", rank: Rank.Large);

        StatCalculator.RecalculateMaxYield(resource, card).Should().Be(1200);
    }

    [Fact]
    public void RecalculateMaxYield_WithFamilyR_AppliesAVMultiplier()
    {
        var card = TestFactory.DataCard(cardId: "TST-0002", yield: 400);
        var resource = TestFactory.MakeResource(cardId: "TST-0002", rank: Rank.Small, family: InstanceFamily.R);

        // 400 * 1 * 1.3 = 520
        StatCalculator.RecalculateMaxYield(resource, card).Should().Be(520);
    }

    [Fact]
    public void RecalculateMaxYield_WithFamilyC_AppliesAVMultiplier()
    {
        var card = TestFactory.DataCard(cardId: "TST-0002", yield: 400);
        var resource = TestFactory.MakeResource(cardId: "TST-0002", rank: Rank.Small, family: InstanceFamily.C);

        // 400 * 1 * 0.7 = 280
        StatCalculator.RecalculateMaxYield(resource, card).Should().Be(280);
    }

    [Fact]
    public void RecalculateMaxYield_ComputeCard_ReturnsZero()
    {
        var card = TestFactory.ComputeCard(cardId: "TST-0001", tp: 600);
        var resource = TestFactory.MakeResource(cardId: "TST-0001");

        StatCalculator.RecalculateMaxYield(resource, card).Should().Be(0);
    }

    // ─── Elastic insight with elastic bonus ─────────────────────

    [Fact]
    public void CalculateEffectiveInsight_ElasticData_WithBonus()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.DataCard(
            cardId: "TST-0004", yield: 300, elastic: true, elasticIncrement: 50, freeTier: 300));

        var field = TestFactory.MakeField();
        var resource = TestFactory.MakeResource(
            cardId: "TST-0004", instanceId: "db_1", faceUp: true, elasticBonus: 100,
            maxAV: 800, currentAV: 800, maxYield: 300, currentYield: 300, maxTP: null, currentTP: null);
        field.Backend[0] = resource;

        // base=300, elastic bonus = 300 * ln(1 + 100/300) = 300 * ln(1.333) ≈ 300 * 0.2876 ≈ 86
        // total = 300 + 86 = 386
        StatCalculator.CalculateEffectiveInsight(resource, field, cc).Should().Be(386);
    }

    // ─── Instance family on Insight ─────────────────────────────

    [Fact]
    public void CalculateEffectiveInsight_FamilyR_IncreasesYield()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.DataCard(cardId: "TST-0002", yield: 400));

        var field = TestFactory.MakeField();
        var resource = TestFactory.MakeResource(
            cardId: "TST-0002", instanceId: "db_1", family: InstanceFamily.R,
            maxAV: 800, currentAV: 800, maxYield: 400, currentYield: 400, maxTP: null, currentTP: null);
        field.Backend[0] = resource;

        // 400 * 1 * 1.3 = 520
        StatCalculator.CalculateEffectiveInsight(resource, field, cc).Should().Be(520);
    }

    [Fact]
    public void CalculateEffectiveInsight_FamilyC_DecreasesYield()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.DataCard(cardId: "TST-0002", yield: 400));

        var field = TestFactory.MakeField();
        var resource = TestFactory.MakeResource(
            cardId: "TST-0002", instanceId: "db_1", family: InstanceFamily.C,
            maxAV: 800, currentAV: 800, maxYield: 400, currentYield: 400, maxTP: null, currentTP: null);
        field.Backend[0] = resource;

        // 400 * 1 * 0.7 = 280
        StatCalculator.CalculateEffectiveInsight(resource, field, cc).Should().Be(280);
    }

    // ─── Insight with temp buff and debuff ──────────────────────

    [Fact]
    public void CalculateEffectiveInsight_WithTempBuff()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.DataCard(cardId: "TST-0002", yield: 400));

        var field = TestFactory.MakeField();
        var resource = TestFactory.MakeResource(
            cardId: "TST-0002", instanceId: "db_1",
            maxAV: 800, currentAV: 800, maxYield: 400, currentYield: 400, maxTP: null, currentTP: null);
        resource.TemporaryEffects.Add(new TemporaryEffect { EffectType = EffectTypes.BuffYield, Value = 100 });
        field.Backend[0] = resource;

        StatCalculator.CalculateEffectiveInsight(resource, field, cc).Should().Be(500);
    }

    [Fact]
    public void CalculateEffectiveInsight_WithTempDebuff_FlooredAtZero()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.DataCard(cardId: "TST-0002", yield: 400));

        var field = TestFactory.MakeField();
        var resource = TestFactory.MakeResource(
            cardId: "TST-0002", instanceId: "db_1",
            maxAV: 800, currentAV: 800, maxYield: 400, currentYield: 400, maxTP: null, currentTP: null);
        resource.TemporaryEffects.Add(new TemporaryEffect { EffectType = EffectTypes.DebuffYield, Value = 500 });
        field.Backend[0] = resource;

        StatCalculator.CalculateEffectiveInsight(resource, field, cc).Should().Be(0);
    }

    // ─── Multiple temp buffs and debuffs stack ──────────────────

    [Fact]
    public void CalculateEffectiveTP_MultipleTempEffects_Stack()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", tp: 600));

        var field = TestFactory.MakeField();
        var resource = TestFactory.MakeResource(cardId: "TST-0001");
        resource.TemporaryEffects.Add(new TemporaryEffect { EffectType = EffectTypes.BuffTP, Value = 100 });
        resource.TemporaryEffects.Add(new TemporaryEffect { EffectType = EffectTypes.BuffTP, Value = 50 });
        resource.TemporaryEffects.Add(new TemporaryEffect { EffectType = EffectTypes.DebuffTP, Value = 30 });

        // 600 + 100 + 50 - 30 = 720
        StatCalculator.CalculateEffectiveTP(resource, field, cc).Should().Be(720);
    }

    // ─── ApplyElasticBonus: zero increment → no change ──────────

    [Fact]
    public void ApplyElasticBonus_ZeroIncrement_NoChange()
    {
        var card = TestFactory.ComputeCard(cardId: "TST-0001", elastic: true, elasticIncrement: 0);
        var resource = TestFactory.MakeResource(cardId: "TST-0001", elasticBonus: 50);
        StatCalculator.ApplyElasticBonus(resource, card);

        resource.ElasticBonus.Should().Be(50);
    }
}
