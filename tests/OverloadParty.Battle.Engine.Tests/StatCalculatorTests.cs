using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Engine;

/// <summary>
/// Tests for StatCalculator based on RULEBOOK.md rules:
/// - Rank multipliers: small=×1, medium=×2, large=×3
/// - Instance family: M=(1,1), C=(1.3,0.7), R=(0.7,1.3)
/// - Elastic: effectiveBonus = free_tier × ln(1 + rawBonus / free_tier)
/// - MC Elastic: max(0, intrinsicStat - free_tier) × cost_per_request / 100
/// - Truncation: floor (1 未満切り捨て)
/// </summary>
public class StatCalculatorTests
{
    [Trait("対象", "実効エラスティックボーナスの計算")]
    public class CalculateEffectiveElasticBonus
    {
        [Theory(DisplayName = "生の ElasticBonus と scale から、実効エラスティックボーナスを計算する")]
        [InlineData(0, 500, 0)]        // zero raw → 0
        [InlineData(100, 0, 100)]      // zero scale → raw passthrough
        [InlineData(-100, 500, -100)]  // negative raw passthrough
        [InlineData(100, 500, 91)]     // 1 trigger: 500 × ln(1.2) ≈ 91
        [InlineData(500, 500, 346)]    // 5 triggers: 500 × ln(2) ≈ 346
        [InlineData(1000, 500, 549)]   // 10 triggers: 500 × ln(3) ≈ 549
        [InlineData(2000, 500, 804)]   // 20 triggers: 500 × ln(5) ≈ 804
        public void ComputesElasticBonus(long rawBonus, long scale, long expected)
        {
            StatCalculator.CalculateEffectiveElasticBonus(rawBonus, scale).Should().Be(expected);
        }
    }

    [Trait("対象", "MC 用固有ステータスの計算")]
    public class CalculateIntrinsicStat
    {
        [Theory(DisplayName = "ランク・インスタンスファミリー・逓減後エラスティックボーナスを反映した固有ステータスを計算する")]
        [InlineData(Rank.Small, InstanceFamily.M, 0, 600)]      // base のみ
        [InlineData(Rank.Medium, InstanceFamily.M, 0, 1200)]    // medium ×2
        [InlineData(Rank.Large, InstanceFamily.M, 0, 1800)]     // large ×3
        [InlineData(Rank.Medium, InstanceFamily.C, 0, 1560)]    // C ×1.3 = trunc(600×2×1.3)
        [InlineData(Rank.Medium, InstanceFamily.R, 0, 840)]     // R ×0.7 = trunc(600×2×0.7)
        [InlineData(Rank.Medium, InstanceFamily.C, 300, 1803)]  // + 逓減後 243
        public void IncludesRankFamilyAndDiminishedElasticBonus(
            Rank rank, InstanceFamily family, long elasticBonus, long expected)
        {
            var card = TestFactory.OrchestratorCard(cardId: "TST-0004");
            var resource = TestFactory.MakeResource(
                cardId: "TST-0004", rank: rank, family: family, elasticBonus: elasticBonus);

            StatCalculator.CalculateIntrinsicStat(resource, card).Should().Be(expected);
        }
    }

    [Trait("対象", "実効スループットの計算")]
    public class CalculateEffectiveTP
    {
        [Fact(DisplayName = "small ランクのコンピュート系リソースの実効スループットは基礎値 600 になる")]
        public void BasicCompute_SmallRank()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", tp: 600, av: 1400));

            var field = TestFactory.MakeField();
            var resource = TestFactory.MakeResource(cardId: "TST-0001", maxTP: 600, currentTP: 600);

            StatCalculator.CalculateEffectiveTP(resource, field, cc).Should().Be(600);
        }

        [Fact(DisplayName = "medium ランクでは実効スループットが基礎値の 2 倍 (700→1400) になる")]
        public void MediumRank_DoublesBase()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", tp: 700, av: 1400));

            var field = TestFactory.MakeField();
            var resource = TestFactory.MakeResource(cardId: "TST-0001", rank: Rank.Medium);

            StatCalculator.CalculateEffectiveTP(resource, field, cc).Should().Be(1400);
        }

        [Fact(DisplayName = "large ランクでは実効スループットが基礎値の 3 倍 (700→2100) になる")]
        public void LargeRank_TriplesBase()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", tp: 700));

            var field = TestFactory.MakeField();
            var resource = TestFactory.MakeResource(cardId: "TST-0001", rank: Rank.Large);

            StatCalculator.CalculateEffectiveTP(resource, field, cc).Should().Be(2100);
        }

        [Fact(DisplayName = "C系インスタンスファミリーでは実効スループットが ×1.3 (600→780) になる")]
        public void FamilyC_MultipliesTP()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", tp: 600));

            var field = TestFactory.MakeField();
            var resource = TestFactory.MakeResource(cardId: "TST-0001", family: InstanceFamily.C);

            StatCalculator.CalculateEffectiveTP(resource, field, cc).Should().Be(780);
        }

        [Fact(DisplayName = "R系インスタンスファミリーでは実効スループットが ×0.7 (600→420) になる")]
        public void FamilyR_ReducesTP()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", tp: 600));

            var field = TestFactory.MakeField();
            var resource = TestFactory.MakeResource(cardId: "TST-0001", family: InstanceFamily.R);

            StatCalculator.CalculateEffectiveTP(resource, field, cc).Should().Be(420);
        }

        [Fact(DisplayName = "medium ランクと C系ファミリーを併せると実効スループットが ×2×1.3 (600→1560) になる")]
        public void FamilyC_MediumRank()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", tp: 600));

            var field = TestFactory.MakeField();
            var resource = TestFactory.MakeResource(cardId: "TST-0001", rank: Rank.Medium, family: InstanceFamily.C);

            StatCalculator.CalculateEffectiveTP(resource, field, cc).Should().Be(1560);
        }

        [Fact(DisplayName = "エラスティックなリソースでは基礎スループットに実効エラスティックボーナス 91 が加わり 591 になる")]
        public void ElasticContainer_WithBonus()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ElasticContainerCard(cardId: "TEST-0002"));

            var field = TestFactory.MakeField();
            var resource = TestFactory.MakeResource(cardId: "TEST-0002", elasticBonus: 100, maxTP: 500, currentTP: 500);

            StatCalculator.CalculateEffectiveTP(resource, field, cc).Should().Be(591); // 500 + 91
        }

        [Fact(DisplayName = "エラスティックボーナスの生値 500 では逓減後 346 が加わり実効スループットが 846 になる")]
        public void ElasticContainer_5Triggers()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ElasticContainerCard(cardId: "TEST-0002"));

            var field = TestFactory.MakeField();
            var resource = TestFactory.MakeResource(cardId: "TEST-0002", elasticBonus: 500, maxTP: 500, currentTP: 500);

            StatCalculator.CalculateEffectiveTP(resource, field, cc).Should().Be(846);
        }

        [Fact(DisplayName = "Data系リソースの実効スループットは 0 になる")]
        public void DataCard_ReturnsZero()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.DataCard(cardId: "TST-0002"));

            var field = TestFactory.MakeField();
            var resource = TestFactory.MakeResource(cardId: "TST-0002");

            StatCalculator.CalculateEffectiveTP(resource, field, cc).Should().Be(0);
        }

        [Fact(DisplayName = "buff_tp の一時効果でスループットが 200 増えて 800 になる")]
        public void WithTempBuff()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", tp: 600));

            var field = TestFactory.MakeField();
            var resource = TestFactory.MakeResource(cardId: "TST-0001");
            resource.TemporaryEffects.Add(new TemporaryEffect { EffectType = "buff_tp", Value = 200 });

            StatCalculator.CalculateEffectiveTP(resource, field, cc).Should().Be(800);
        }

        [Fact(DisplayName = "debuff_tp の一時効果でスループットが減っても 0 未満にはならず 0 になる")]
        public void WithTempDebuff_FlooredAtZero()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", tp: 600));

            var field = TestFactory.MakeField();
            var resource = TestFactory.MakeResource(cardId: "TST-0001");
            resource.TemporaryEffects.Add(new TemporaryEffect { EffectType = "debuff_tp", Value = 700 });

            StatCalculator.CalculateEffectiveTP(resource, field, cc).Should().Be(0);
        }

        [Fact(DisplayName = "複数の一時効果 (buff/debuff) が累積してスループットが 720 になる")]
        public void MultipleTempEffects_Stack()
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
    }

    [Trait("対象", "実効インサイト生成量の計算")]
    public class CalculateEffectiveInsight
    {
        [Fact(DisplayName = "Data系リソースの実効インサイト生成量は基礎イールド 400 になる")]
        public void BasicDB()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.DataCard(cardId: "TST-0002", yield: 400));

            var field = TestFactory.MakeField();
            var resource = TestFactory.MakeResource(cardId: "TST-0002", maxYield: 400, currentYield: 400, maxTP: null, currentTP: null);

            StatCalculator.CalculateEffectiveInsight(resource, field, cc).Should().Be(400);
        }

        [Fact(DisplayName = "コンピュート系リソースの実効インサイト生成量は 0 になる")]
        public void ComputeCard_ReturnsZero()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));

            var field = TestFactory.MakeField();
            var resource = TestFactory.MakeResource(cardId: "TST-0001");

            StatCalculator.CalculateEffectiveInsight(resource, field, cc).Should().Be(0);
        }

        [Theory(DisplayName = "インスタンスファミリーごとの係数 (C×1.3 / R×0.7) を適用したインサイト生成量を計算する")]
        [InlineData(InstanceFamily.M, 400)] // 400 * 1.0
        [InlineData(InstanceFamily.C, 520)] // 400 * 1.3
        [InlineData(InstanceFamily.R, 280)] // 400 * 0.7
        public void FamilyAppliesThroughputSideMultiplier(InstanceFamily family, long expected)
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.DataCard(cardId: "TST-0002", yield: 400));

            var field = TestFactory.MakeField();
            var resource = TestFactory.MakeResource(
                cardId: "TST-0002", maxYield: 400, currentYield: 400, maxTP: null, currentTP: null, family: family);

            StatCalculator.CalculateEffectiveInsight(resource, field, cc).Should().Be(expected);
        }

        [Fact(DisplayName = "エラスティックな Data系リソースでは基礎イールドに逓減後エラスティックボーナスが加わり 386 になる")]
        public void ElasticData_WithBonus()
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

        [Fact(DisplayName = "buff_yield の一時効果でインサイト生成量が 100 増えて 500 になる")]
        public void WithTempBuff()
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

        [Fact(DisplayName = "debuff_yield の一時効果でインサイト生成量が減っても 0 未満にはならず 0 になる")]
        public void WithTempDebuff_FlooredAtZero()
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
    }

    [Trait("対象", "最大可用性の計算")]
    public class CalculateMaxAV
    {
        [Theory(DisplayName = "ランク倍率 (small×1 / medium×2 / large×3) を適用した最大可用性を計算する")]
        [InlineData(Rank.Small, null, 1400)]
        [InlineData(Rank.Medium, null, 2800)]
        [InlineData(Rank.Large, null, 4200)]
        public void RankMultiplier(Rank rank, InstanceFamily? family, long expected)
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", av: 1400));

            var resource = TestFactory.MakeResource(cardId: "TST-0001", rank: rank, family: family);

            StatCalculator.CalculateMaxAV(resource, TestFactory.MakeField(), cc).Should().Be(expected);
        }

        [Fact(DisplayName = "R系インスタンスファミリーでは最大可用性が ×1.3 (1400→1820) になる")]
        public void FamilyR_IncreasesAV()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", av: 1400));

            var resource = TestFactory.MakeResource(cardId: "TST-0001", family: InstanceFamily.R);

            StatCalculator.CalculateMaxAV(resource, TestFactory.MakeField(), cc).Should().Be(1820);
        }

        [Fact(DisplayName = "C系インスタンスファミリーでは最大可用性が ×0.7 され切り捨てで 979 になる")]
        public void FamilyC_DecreasesAV()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", av: 1400));

            var resource = TestFactory.MakeResource(cardId: "TST-0001", family: InstanceFamily.C);

            StatCalculator.CalculateMaxAV(resource, TestFactory.MakeField(), cc).Should().Be(979);
        }

        [Fact(DisplayName = "×0.7 の結果に端数が出るとき最大可用性は切り捨てで 944 (1350×0.7) になる")]
        public void Truncation()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", av: 1350));

            var resource = TestFactory.MakeResource(cardId: "TST-0001", family: InstanceFamily.C);

            StatCalculator.CalculateMaxAV(resource, TestFactory.MakeField(), cc).Should().Be(944);
        }
    }

    [Trait("対象", "エラスティックボーナスの加算")]
    public class ApplyElasticBonus
    {
        [Fact(DisplayName = "エラスティックなカードにボーナスを適用すると、生のエラスティックボーナスが増分 100 だけ増える")]
        public void IncrementsRawBonus()
        {
            var cc = new TestCardCache();
            var card = TestFactory.ElasticContainerCard(cardId: "TEST-0002");
            cc.Add(card);

            var resource = TestFactory.MakeResource(cardId: "TST-0003", elasticBonus: 0);
            StatCalculator.ApplyElasticBonus(resource, card);

            resource.ElasticBonus.Should().Be(100);
        }

        [Fact(DisplayName = "エラスティックでないカードにボーナスを適用しても、エラスティックボーナスは変わらない")]
        public void NonElastic_NoChange()
        {
            var card = TestFactory.ComputeCard(cardId: "TST-0001", elastic: false);
            var resource = TestFactory.MakeResource(cardId: "TST-0001", elasticBonus: 0);
            StatCalculator.ApplyElasticBonus(resource, card);

            resource.ElasticBonus.Should().Be(0);
        }

        [Fact(DisplayName = "増分が 0 のエラスティックカードでは、エラスティックボーナスが変わらない")]
        public void ZeroIncrement_NoChange()
        {
            var card = TestFactory.ComputeCard(cardId: "TST-0001", elastic: true, elasticIncrement: 0);
            var resource = TestFactory.MakeResource(cardId: "TST-0001", elasticBonus: 50);
            StatCalculator.ApplyElasticBonus(resource, card);

            resource.ElasticBonus.Should().Be(50);
        }
    }

    [Trait("対象", "0 方向への切り捨て")]
    public class Truncate
    {
        [Theory(DisplayName = "小数を 0 方向に切り捨てて整数にする (1 未満切り捨て・負値も 0 方向)")]
        [InlineData(1012.5, 1012)]
        [InlineData(675.0, 675)]
        [InlineData(0.9, 0)]
        [InlineData(-1.5, -1)]
        public void TruncatesTowardsZero(double input, long expected)
        {
            StatCalculator.Truncate(input).Should().Be(expected);
        }
    }

    [Trait("対象", "最大スループットの再計算")]
    public class RecalculateMaxTP
    {
        [Fact(DisplayName = "small ランクでは最大スループットが基礎値 600 になる")]
        public void SmallRank_ReturnsBase()
        {
            var card = TestFactory.ComputeCard(cardId: "TST-0001", tp: 600);
            var resource = TestFactory.MakeResource(cardId: "TST-0001", rank: Rank.Small);

            StatCalculator.RecalculateMaxTP(resource, card).Should().Be(600);
        }

        [Fact(DisplayName = "medium ランクでは最大スループットが基礎値の 2 倍 (600→1200) になる")]
        public void MediumRank_DoublesBase()
        {
            var card = TestFactory.ComputeCard(cardId: "TST-0001", tp: 600);
            var resource = TestFactory.MakeResource(cardId: "TST-0001", rank: Rank.Medium);

            StatCalculator.RecalculateMaxTP(resource, card).Should().Be(1200);
        }

        [Fact(DisplayName = "large ランクでは最大スループットが基礎値の 3 倍 (600→1800) になる")]
        public void LargeRank_TriplesBase()
        {
            var card = TestFactory.ComputeCard(cardId: "TST-0001", tp: 600);
            var resource = TestFactory.MakeResource(cardId: "TST-0001", rank: Rank.Large);

            StatCalculator.RecalculateMaxTP(resource, card).Should().Be(1800);
        }

        [Fact(DisplayName = "C系ファミリーでは最大スループットが ×1.3 (600→780) になる")]
        public void WithFamilyC_AppliesMultiplier()
        {
            var card = TestFactory.ComputeCard(cardId: "TST-0001", tp: 600);
            var resource = TestFactory.MakeResource(cardId: "TST-0001", rank: Rank.Small, family: InstanceFamily.C);

            // 600 * 1 * 1.3 = 780
            StatCalculator.RecalculateMaxTP(resource, card).Should().Be(780);
        }

        [Fact(DisplayName = "R系ファミリーでは最大スループットが ×0.7 (600→420) になる")]
        public void WithFamilyR_AppliesMultiplier()
        {
            var card = TestFactory.ComputeCard(cardId: "TST-0001", tp: 600);
            var resource = TestFactory.MakeResource(cardId: "TST-0001", rank: Rank.Small, family: InstanceFamily.R);

            // 600 * 1 * 0.7 = 420
            StatCalculator.RecalculateMaxTP(resource, card).Should().Be(420);
        }

        [Fact(DisplayName = "Data系リソースの最大スループットは 0 になる")]
        public void DataCard_ReturnsZero()
        {
            var card = TestFactory.DataCard(cardId: "TST-0002", yield: 400);
            var resource = TestFactory.MakeResource(cardId: "TST-0002");

            StatCalculator.RecalculateMaxTP(resource, card).Should().Be(0);
        }

        [Fact(DisplayName = "medium ランクと C系ファミリーを併せると最大スループットが ×2×1.3 (600→1560) になる")]
        public void MediumRank_FamilyC()
        {
            var card = TestFactory.ComputeCard(cardId: "TST-0001", tp: 600);
            var resource = TestFactory.MakeResource(cardId: "TST-0001", rank: Rank.Medium, family: InstanceFamily.C);

            // 600 * 2 * 1.3 = 1560
            StatCalculator.RecalculateMaxTP(resource, card).Should().Be(1560);
        }
    }

    [Trait("対象", "最大イールドの再計算")]
    public class RecalculateMaxYield
    {
        [Fact(DisplayName = "small ランクでは最大イールドが基礎値 400 になる")]
        public void SmallRank_ReturnsBase()
        {
            var card = TestFactory.DataCard(cardId: "TST-0002", yield: 400);
            var resource = TestFactory.MakeResource(cardId: "TST-0002", rank: Rank.Small);

            StatCalculator.RecalculateMaxYield(resource, card).Should().Be(400);
        }

        [Fact(DisplayName = "medium ランクでは最大イールドが基礎値の 2 倍 (400→800) になる")]
        public void MediumRank_DoublesBase()
        {
            var card = TestFactory.DataCard(cardId: "TST-0002", yield: 400);
            var resource = TestFactory.MakeResource(cardId: "TST-0002", rank: Rank.Medium);

            StatCalculator.RecalculateMaxYield(resource, card).Should().Be(800);
        }

        [Fact(DisplayName = "large ランクでは最大イールドが基礎値の 3 倍 (400→1200) になる")]
        public void LargeRank_TriplesBase()
        {
            var card = TestFactory.DataCard(cardId: "TST-0002", yield: 400);
            var resource = TestFactory.MakeResource(cardId: "TST-0002", rank: Rank.Large);

            StatCalculator.RecalculateMaxYield(resource, card).Should().Be(1200);
        }

        [Fact(DisplayName = "R系ファミリーでは最大イールドが ×0.7 (400→280) になる")]
        public void WithFamilyR_ReducesYield()
        {
            var card = TestFactory.DataCard(cardId: "TST-0002", yield: 400);
            var resource = TestFactory.MakeResource(cardId: "TST-0002", rank: Rank.Small, family: InstanceFamily.R);

            // 400 * 1 * 0.7 = 280
            StatCalculator.RecalculateMaxYield(resource, card).Should().Be(280);
        }

        [Fact(DisplayName = "C系ファミリーでは最大イールドが ×1.3 (400→520) になる")]
        public void WithFamilyC_IncreasesYield()
        {
            var card = TestFactory.DataCard(cardId: "TST-0002", yield: 400);
            var resource = TestFactory.MakeResource(cardId: "TST-0002", rank: Rank.Small, family: InstanceFamily.C);

            // 400 * 1 * 1.3 = 520
            StatCalculator.RecalculateMaxYield(resource, card).Should().Be(520);
        }

        [Fact(DisplayName = "コンピュート系リソースの最大イールドは 0 になる")]
        public void ComputeCard_ReturnsZero()
        {
            var card = TestFactory.ComputeCard(cardId: "TST-0001", tp: 600);
            var resource = TestFactory.MakeResource(cardId: "TST-0001");

            StatCalculator.RecalculateMaxYield(resource, card).Should().Be(0);
        }
    }
}
