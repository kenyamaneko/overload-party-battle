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
        Assert.Equal(0, StatCalculator.EffectiveElasticBonus(0, 500));
    }

    [Fact]
    public void EffectiveElasticBonus_ZeroScale_ReturnsRaw()
    {
        Assert.Equal(100, StatCalculator.EffectiveElasticBonus(100, 0));
    }

    [Fact]
    public void EffectiveElasticBonus_NegativeRaw_ReturnsNegative()
    {
        Assert.Equal(-100, StatCalculator.EffectiveElasticBonus(-100, 500));
    }

    /// <summary>
    /// Rulebook example: Container (free_tier=500), 1 trigger → raw=100
    /// effective = 500 × ln(1 + 100/500) ≈ 500 × ln(1.2) ≈ 500 × 0.1823 ≈ 91
    /// </summary>
    [Fact]
    public void EffectiveElasticBonus_RulebookExample_1Trigger()
    {
        long result = StatCalculator.EffectiveElasticBonus(100, 500);
        Assert.Equal(91, result);
    }

    /// <summary>
    /// Rulebook: 5 triggers → raw=500, effective = 500 × ln(2) ≈ 346
    /// </summary>
    [Fact]
    public void EffectiveElasticBonus_RulebookExample_5Triggers()
    {
        long result = StatCalculator.EffectiveElasticBonus(500, 500);
        Assert.Equal(346, result);
    }

    /// <summary>
    /// Rulebook: 10 triggers → raw=1000, effective = 500 × ln(3) ≈ 549
    /// </summary>
    [Fact]
    public void EffectiveElasticBonus_RulebookExample_10Triggers()
    {
        long result = StatCalculator.EffectiveElasticBonus(1000, 500);
        Assert.Equal(549, result);
    }

    /// <summary>
    /// Rulebook: 20 triggers → raw=2000, effective = 500 × ln(5) ≈ 804
    /// </summary>
    [Fact]
    public void EffectiveElasticBonus_RulebookExample_20Triggers()
    {
        long result = StatCalculator.EffectiveElasticBonus(2000, 500);
        Assert.Equal(804, result);
    }

    // ─── CalculateEffectiveTP ─────────────────────────────────

    [Fact]
    public void CalculateEffectiveTP_BasicCompute_SmallRank()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardNo: 1, tp: 600, av: 1400));

        var field = TestFactory.MakeField();
        var resource = TestFactory.MakeResource(cardId: 1, maxTP: 600, currentTP: 600);

        long tp = StatCalculator.CalculateEffectiveTP(resource, field, cc);
        Assert.Equal(600, tp);
    }

    /// <summary>
    /// Rulebook: えくぼ (TP 700) at medium rank → 700 × 2 = 1400
    /// </summary>
    [Fact]
    public void CalculateEffectiveTP_MediumRank_DoublesBase()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardNo: 1, tp: 700, av: 1400));

        var field = TestFactory.MakeField();
        var resource = TestFactory.MakeResource(cardId: 1, rank: Rank.Medium);

        long tp = StatCalculator.CalculateEffectiveTP(resource, field, cc);
        Assert.Equal(1400, tp);
    }

    /// <summary>
    /// Rulebook: large rank → base × 3
    /// </summary>
    [Fact]
    public void CalculateEffectiveTP_LargeRank_TriplesBase()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardNo: 1, tp: 700));

        var field = TestFactory.MakeField();
        var resource = TestFactory.MakeResource(cardId: 1, rank: Rank.Large);

        long tp = StatCalculator.CalculateEffectiveTP(resource, field, cc);
        Assert.Equal(2100, tp);
    }

    /// <summary>
    /// Instance Family C: TP × 1.5
    /// TP 600 at small with C family → 600 × 1 × 1.5 = 900
    /// </summary>
    [Fact]
    public void CalculateEffectiveTP_FamilyC_MultipliesTP()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardNo: 1, tp: 600));

        var field = TestFactory.MakeField();
        var resource = TestFactory.MakeResource(cardId: 1, family: InstanceFamily.C);

        long tp = StatCalculator.CalculateEffectiveTP(resource, field, cc);
        Assert.Equal(900, tp);
    }

    /// <summary>
    /// Instance Family R: TP × 0.75
    /// TP 600 at small with R family → 600 × 0.75 = 450
    /// </summary>
    [Fact]
    public void CalculateEffectiveTP_FamilyR_ReducesTP()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardNo: 1, tp: 600));

        var field = TestFactory.MakeField();
        var resource = TestFactory.MakeResource(cardId: 1, family: InstanceFamily.R);

        long tp = StatCalculator.CalculateEffectiveTP(resource, field, cc);
        Assert.Equal(450, tp);
    }

    /// <summary>
    /// Family C + medium rank: TP 600 × 2 × 1.5 = 1800
    /// </summary>
    [Fact]
    public void CalculateEffectiveTP_FamilyC_MediumRank()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardNo: 1, tp: 600));

        var field = TestFactory.MakeField();
        var resource = TestFactory.MakeResource(cardId: 1, rank: Rank.Medium, family: InstanceFamily.C);

        long tp = StatCalculator.CalculateEffectiveTP(resource, field, cc);
        Assert.Equal(1800, tp);
    }

    /// <summary>
    /// Elastic container: base 500, raw bonus 100, free_tier 500
    /// effective bonus = 91, total TP = 500 + 91 = 591
    /// </summary>
    [Fact]
    public void CalculateEffectiveTP_ElasticContainer_WithBonus()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ElasticContainerCard(cardNo: 2));

        var field = TestFactory.MakeField();
        var resource = TestFactory.MakeResource(cardId: 2, elasticBonus: 100, maxTP: 500, currentTP: 500);

        long tp = StatCalculator.CalculateEffectiveTP(resource, field, cc);
        // 500 + 91 = 591
        Assert.Equal(591, tp);
    }

    /// <summary>
    /// Elastic container: 5 triggers → effective bonus 346, total = 846
    /// </summary>
    [Fact]
    public void CalculateEffectiveTP_ElasticContainer_5Triggers()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ElasticContainerCard(cardNo: 2));

        var field = TestFactory.MakeField();
        var resource = TestFactory.MakeResource(cardId: 2, elasticBonus: 500, maxTP: 500, currentTP: 500);

        long tp = StatCalculator.CalculateEffectiveTP(resource, field, cc);
        Assert.Equal(846, tp);
    }

    /// <summary>
    /// Non-compute card → TP = 0
    /// </summary>
    [Fact]
    public void CalculateEffectiveTP_DataCard_ReturnsZero()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.DataCard(cardNo: 100));

        var field = TestFactory.MakeField();
        var resource = TestFactory.MakeResource(cardId: 100);

        long tp = StatCalculator.CalculateEffectiveTP(resource, field, cc);
        Assert.Equal(0, tp);
    }

    /// <summary>
    /// Temporary buff_tp effect should be added to TP.
    /// </summary>
    [Fact]
    public void CalculateEffectiveTP_WithTempBuff()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardNo: 1, tp: 600));

        var field = TestFactory.MakeField();
        var resource = TestFactory.MakeResource(cardId: 1);
        resource.TemporaryEffects.Add(new TemporaryEffect { EffectType = "buff_tp", Value = 200 });

        long tp = StatCalculator.CalculateEffectiveTP(resource, field, cc);
        Assert.Equal(800, tp);
    }

    /// <summary>
    /// Temporary debuff_tp should reduce TP (but not below 0).
    /// </summary>
    [Fact]
    public void CalculateEffectiveTP_WithTempDebuff_FlooredAtZero()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardNo: 1, tp: 600));

        var field = TestFactory.MakeField();
        var resource = TestFactory.MakeResource(cardId: 1);
        resource.TemporaryEffects.Add(new TemporaryEffect { EffectType = "debuff_tp", Value = 700 });

        long tp = StatCalculator.CalculateEffectiveTP(resource, field, cc);
        Assert.Equal(0, tp);
    }

    // ─── CalculateEffectiveYield ──────────────────────────────

    [Fact]
    public void CalculateEffectiveYield_BasicDB()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.DataCard(cardNo: 100, yield: 400));

        var field = TestFactory.MakeField();
        var resource = TestFactory.MakeResource(cardId: 100, maxYield: 400, currentYield: 400, maxTP: null, currentTP: null);

        long yield = StatCalculator.CalculateEffectiveYield(resource, field, cc);
        Assert.Equal(400, yield);
    }

    [Fact]
    public void CalculateEffectiveYield_ComputeCard_ReturnsZero()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardNo: 1));

        var field = TestFactory.MakeField();
        var resource = TestFactory.MakeResource(cardId: 1);

        long yield = StatCalculator.CalculateEffectiveYield(resource, field, cc);
        Assert.Equal(0, yield);
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
        cc.Add(TestFactory.ComputeCard(cardNo: 1, av: 1400));

        var resource = TestFactory.MakeResource(cardId: 1, rank: rank, family: family);

        long av = StatCalculator.CalculateMaxAV(resource, cc);
        Assert.Equal(expected, av);
    }

    /// <summary>
    /// Family R: AV × 1.5 → 1400 × 1.5 = 2100
    /// </summary>
    [Fact]
    public void CalculateMaxAV_FamilyR_IncreasesAV()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardNo: 1, av: 1400));

        var resource = TestFactory.MakeResource(cardId: 1, family: InstanceFamily.R);

        long av = StatCalculator.CalculateMaxAV(resource, cc);
        Assert.Equal(2100, av);
    }

    /// <summary>
    /// Family C: AV × 0.75 → 1400 × 0.75 = 1050
    /// </summary>
    [Fact]
    public void CalculateMaxAV_FamilyC_DecreasesAV()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardNo: 1, av: 1400));

        var resource = TestFactory.MakeResource(cardId: 1, family: InstanceFamily.C);

        long av = StatCalculator.CalculateMaxAV(resource, cc);
        Assert.Equal(1050, av);
    }

    /// <summary>
    /// Rulebook truncation: 1350 × 0.75 = 1012.5 → 1012
    /// </summary>
    [Fact]
    public void CalculateMaxAV_Truncation()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardNo: 1, av: 1350));

        var resource = TestFactory.MakeResource(cardId: 1, family: InstanceFamily.C);

        long av = StatCalculator.CalculateMaxAV(resource, cc);
        Assert.Equal(1012, av);
    }

    // ─── ApplyElasticBonus ────────────────────────────────────

    [Fact]
    public void ApplyElasticBonus_IncrementsRawBonus()
    {
        var cc = new TestCardCache();
        var card = TestFactory.ElasticContainerCard(cardNo: 2);
        cc.Add(card);

        var resource = TestFactory.MakeResource(cardId: 2, elasticBonus: 0);
        StatCalculator.ApplyElasticBonus(resource, card);

        Assert.Equal(100, resource.ElasticBonus);
    }

    [Fact]
    public void ApplyElasticBonus_NonElastic_NoChange()
    {
        var card = TestFactory.ComputeCard(cardNo: 1, elastic: false);
        var resource = TestFactory.MakeResource(cardId: 1, elasticBonus: 0);
        StatCalculator.ApplyElasticBonus(resource, card);

        Assert.Equal(0, resource.ElasticBonus);
    }

    // ─── Truncate ─────────────────────────────────────────────

    [Theory]
    [InlineData(1012.5, 1012)]
    [InlineData(675.0, 675)]
    [InlineData(0.9, 0)]
    [InlineData(-1.5, -1)]
    public void Truncate_TruncatesTowardsZero(double input, long expected)
    {
        Assert.Equal(expected, StatCalculator.Truncate(input));
    }

    // ─── Platform bonus integration ───────────────────────────

    [Fact]
    public void CalculateEffectiveTP_WithPlatformBonus()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardNo: 1, tp: 600));
        cc.Add(TestFactory.PlatformCard(cardNo: 200, platformEffects:
        [
            new PlatformEffect
            {
                Type = PlatformEffectTypes.TPBonus,
                Params = new PlatformEffectConfig { Bonus = 150 }
            }
        ]));

        var field = TestFactory.MakeField();
        field.Support[0] = new SupportInstance { InstanceID = "sup_1", CardID = 200, FaceDown = false, DeployingTurnsLeft = 0 };

        var resource = TestFactory.MakeResource(cardId: 1);
        field.Frontend[0] = resource;

        long tp = StatCalculator.CalculateEffectiveTP(resource, field, cc);
        Assert.Equal(750, tp); // 600 + 150
    }

    // ─── Attachment bonus integration ─────────────────────────

    [Fact]
    public void CalculateEffectiveTP_WithAttachmentBonus()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardNo: 1, tp: 600));
        cc.Add(TestFactory.AttachmentCard(cardNo: 300, attachmentEffects:
        [
            new AttachmentEffect
            {
                Type = AttachmentEffectTypes.StatBonus,
                Params = new AttachmentEffectConfig { StatType = "tp", Bonus = 200 }
            }
        ]));

        var field = TestFactory.MakeField();
        var resource = TestFactory.MakeResource(cardId: 1);
        resource.Attachments.Add(new AttachmentRef { InstanceID = "att_1", CardID = 300 });
        field.Frontend[0] = resource;

        long tp = StatCalculator.CalculateEffectiveTP(resource, field, cc);
        Assert.Equal(800, tp); // 600 + 200
    }
}
