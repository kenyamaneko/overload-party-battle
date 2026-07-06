using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Models;
using OverloadParty.Battle.Npc;
using GD = OverloadParty.GameState;

namespace OverloadParty.Battle.Tests.Npc;

public class TargetSelectorTests
{
    /// <summary>Shared setup for TargetSelector tests (seeded card cache).</summary>
    public abstract class Base
    {
        protected readonly TestCardCache _cc = new();

        protected Base()
        {
            _cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", tp: 600, av: 1400, mc: 150));
            _cc.Add(TestFactory.DataCard(cardId: "TST-0002", subtype: "Database", yield: 400, av: 800, mc: 100));
            _cc.Add(TestFactory.PlatformCard(cardId: "TEST-0200", name: "TestPlatform"));
        }
    }

    [Trait("対象", "最弱リソースの選択")]
    public class WeakestInZone : Base
    {
        [Fact(DisplayName = "実効可用性が最も低い表向きリソースを返す")]
        public void WeakestInZone_ReturnsFaceUpResourceWithLowestEffectiveAV()
        {
            var field = TestFactory.MakeWireField();
            field.Frontend[0] = TestFactory.MakeWireResource(instanceId: "strong", maxAV: 2000, damage: 0);
            field.Frontend[1] = TestFactory.MakeWireResource(instanceId: "weak", maxAV: 600, damage: 0);

            var result = TargetSelector.WeakestInZone(field, Zones.Frontend);

            result.Should().Be("weak");
        }

        [Fact(DisplayName = "実効可用性はダメージを差し引いて評価する")]
        public void WeakestInZone_ConsidersDamageInEffectiveAV()
        {
            var field = TestFactory.MakeWireField();
            field.Frontend[0] = TestFactory.MakeWireResource(instanceId: "high_av_damaged", maxAV: 2000, damage: 1800);
            field.Frontend[1] = TestFactory.MakeWireResource(instanceId: "low_av_healthy", maxAV: 500, damage: 0);

            var result = TargetSelector.WeakestInZone(field, Zones.Frontend);

            result.Should().Be("high_av_damaged");
        }

        [Fact(DisplayName = "ゾーン指定が null のとき、フロントエンドとバックエンドの両方から探す")]
        public void WeakestInZone_NullZone_SearchesBothZones()
        {
            var field = TestFactory.MakeWireField();
            field.Frontend[0] = TestFactory.MakeWireResource(instanceId: "fe_res", maxAV: 1000);
            field.Backend[0] = TestFactory.MakeWireResource(instanceId: "be_res", maxAV: 300);

            var result = TargetSelector.WeakestInZone(field, null);

            result.Should().Be("be_res");
        }

        [Fact(DisplayName = "裏向きリソースを対象から除く")]
        public void WeakestInZone_IgnoresFaceDownResources()
        {
            var field = TestFactory.MakeWireField();
            field.Frontend[0] = TestFactory.MakeWireResource(instanceId: "face_down", maxAV: 100, faceUp: false);
            field.Frontend[1] = TestFactory.MakeWireResource(instanceId: "face_up", maxAV: 800);

            var result = TargetSelector.WeakestInZone(field, Zones.Frontend);

            result.Should().Be("face_up");
        }

        [Fact(DisplayName = "フィールドが空のとき、null を返す")]
        public void WeakestInZone_EmptyField_ReturnsNull()
        {
            var field = TestFactory.MakeWireField();

            var result = TargetSelector.WeakestInZone(field, Zones.Frontend);

            result.Should().BeNull();
        }
    }

    [Trait("対象", "最強リソースの選択")]
    public class StrongestInZone : Base
    {
        [Fact(DisplayName = "比較値が最も高いリソースを返す")]
        public void StrongestInZone_ReturnsResourceWithHighestValue()
        {
            var field = TestFactory.MakeWireField();
            field.Frontend[0] = TestFactory.MakeWireResource(cardId: "TST-0001", instanceId: "low_tp", currentTP: 300);
            field.Frontend[1] = TestFactory.MakeWireResource(cardId: "TST-0001", instanceId: "high_tp", currentTP: 900);

            var result = TargetSelector.StrongestInZone(field, Zones.Frontend, _cc);

            result.Should().Be("high_tp");
        }

        [Fact(DisplayName = "Data系リソースはイールドを比較値に使う")]
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

        [Fact(DisplayName = "フィールドが空のとき、null を返す")]
        public void StrongestInZone_EmptyField_ReturnsNull()
        {
            var field = TestFactory.MakeWireField();

            var result = TargetSelector.StrongestInZone(field, Zones.Frontend, _cc);

            result.Should().BeNull();
        }
    }

    [Trait("対象", "最大ダメージリソースの選択")]
    public class MostDamagedOwn : Base
    {
        [Fact(DisplayName = "最もダメージの大きい表向きリソースを返す")]
        public void MostDamagedOwn_ReturnsMostDamagedFaceUpResource()
        {
            var field = TestFactory.MakeWireField();
            field.Frontend[0] = TestFactory.MakeWireResource(instanceId: "slightly_dmg", damage: 100);
            field.Frontend[1] = TestFactory.MakeWireResource(instanceId: "heavily_dmg", damage: 800);
            field.Backend[0] = TestFactory.MakeWireResource(instanceId: "medium_dmg", damage: 400);

            var result = TargetSelector.MostDamagedOwn(field);

            result.Should().Be("heavily_dmg");
        }

        [Fact(DisplayName = "ダメージを受けたリソースが無いとき、null を返す")]
        public void MostDamagedOwn_NoDamagedResources_ReturnsNull()
        {
            var field = TestFactory.MakeWireField();
            field.Frontend[0] = TestFactory.MakeWireResource(instanceId: "healthy", damage: 0);

            var result = TargetSelector.MostDamagedOwn(field);

            result.Should().BeNull();
        }

        [Fact(DisplayName = "フィールドが空のとき、null を返す")]
        public void MostDamagedOwn_EmptyField_ReturnsNull()
        {
            var field = TestFactory.MakeWireField();

            var result = TargetSelector.MostDamagedOwn(field);

            result.Should().BeNull();
        }
    }

    [Trait("対象", "全リソース数の集計")]
    public class CountAllResources : Base
    {
        [Fact(DisplayName = "表向きのリソースだけを数える")]
        public void CountAllResources_CountsFaceUpOnly()
        {
            var field = TestFactory.MakeWireField();
            field.Frontend[0] = TestFactory.MakeWireResource(instanceId: "fe1", faceUp: true);
            field.Frontend[1] = TestFactory.MakeWireResource(instanceId: "fe2", faceUp: false);
            field.Backend[0] = TestFactory.MakeWireResource(instanceId: "be1", faceUp: true);

            var count = TargetSelector.CountAllResources(field);

            count.Should().Be(2);
        }

        [Fact(DisplayName = "フィールドが空のとき、0 を返す")]
        public void CountAllResources_EmptyField_ReturnsZero()
        {
            var field = TestFactory.MakeWireField();

            TargetSelector.CountAllResources(field).Should().Be(0);
        }
    }

    [Trait("対象", "ゾーン内リソース数の集計")]
    public class CountResourcesInZone : Base
    {
        [Fact(DisplayName = "フロントエンドを指定したとき、そのゾーンのリソースだけを数える")]
        public void CountResourcesInZone_FrontendOnly()
        {
            var field = TestFactory.MakeWireField();
            field.Frontend[0] = TestFactory.MakeWireResource(instanceId: "fe1");
            field.Frontend[1] = TestFactory.MakeWireResource(instanceId: "fe2");
            field.Backend[0] = TestFactory.MakeWireResource(instanceId: "be1");

            var count = TargetSelector.CountResourcesInZone(field, Zones.Frontend);

            count.Should().Be(2);
        }

        [Fact(DisplayName = "ゾーン指定が null のとき、両ゾーンのリソースを数える")]
        public void CountResourcesInZone_NullZone_CountsBothZones()
        {
            var field = TestFactory.MakeWireField();
            field.Frontend[0] = TestFactory.MakeWireResource(instanceId: "fe1");
            field.Backend[0] = TestFactory.MakeWireResource(instanceId: "be1");

            var count = TargetSelector.CountResourcesInZone(field, null);

            count.Should().Be(2);
        }
    }

    [Trait("対象", "被ダメージリソースの有無")]
    public class HasDamagedResource : Base
    {
        [Fact(DisplayName = "ダメージを受けたリソースがあるとき、true を返す")]
        public void HasDamagedResource_WithDamaged_ReturnsTrue()
        {
            var field = TestFactory.MakeWireField();
            field.Frontend[0] = TestFactory.MakeWireResource(instanceId: "dmg", damage: 100);

            TargetSelector.HasDamagedResource(field).Should().BeTrue();
        }

        [Fact(DisplayName = "ダメージを受けたリソースが無いとき、false を返す")]
        public void HasDamagedResource_NoDamaged_ReturnsFalse()
        {
            var field = TestFactory.MakeWireField();
            field.Frontend[0] = TestFactory.MakeWireResource(instanceId: "healthy", damage: 0);

            TargetSelector.HasDamagedResource(field).Should().BeFalse();
        }

        [Fact(DisplayName = "フィールドが空のとき、false を返す")]
        public void HasDamagedResource_EmptyField_ReturnsFalse()
        {
            var field = TestFactory.MakeWireField();

            TargetSelector.HasDamagedResource(field).Should().BeFalse();
        }
    }

    [Trait("対象", "裏向きサポートの有無")]
    public class HasFaceDownSupport : Base
    {
        [Fact(DisplayName = "裏向きのサポートがあるとき、true を返す")]
        public void HasFaceDownSupport_WithFaceDown_ReturnsTrue()
        {
            var field = TestFactory.MakeWireOpponentField();
            field.Support[0] = TestFactory.MakeHiddenSupport("sup1", faceDown: true);

            TargetSelector.HasFaceDownSupport(field).Should().BeTrue();
        }

        [Fact(DisplayName = "全て表向きのとき、false を返す")]
        public void HasFaceDownSupport_AllFaceUp_ReturnsFalse()
        {
            var field = TestFactory.MakeWireOpponentField();
            field.Support[0] = TestFactory.MakeHiddenSupport("sup1", cardId: "TEST-0200", faceDown: false);

            TargetSelector.HasFaceDownSupport(field).Should().BeFalse();
        }

        [Fact(DisplayName = "サポートが無いとき、false を返す")]
        public void HasFaceDownSupport_EmptySupport_ReturnsFalse()
        {
            var field = TestFactory.MakeWireOpponentField();

            TargetSelector.HasFaceDownSupport(field).Should().BeFalse();
        }
    }

    [Trait("対象", "プラットフォームの有無")]
    public class HasPlatform : Base
    {
        [Fact(DisplayName = "見えているプラットフォームがあるとき、true を返す")]
        public void HasPlatform_WithPlatformCard_ReturnsTrue()
        {
            var field = TestFactory.MakeWireOpponentField();
            field.Support[0] = TestFactory.MakeHiddenSupport("plat1", cardId: "TEST-0200", faceDown: false);

            TargetSelector.HasPlatform(field, _cc).Should().BeTrue();
        }

        [Fact(DisplayName = "プラットフォームでないサポートだけのとき、false を返す")]
        public void HasPlatform_NoPlatform_ReturnsFalse()
        {
            var field = TestFactory.MakeWireOpponentField();
            field.Support[0] = TestFactory.MakeHiddenSupport("sup1", cardId: "TST-0001", faceDown: false);

            TargetSelector.HasPlatform(field, _cc).Should().BeFalse();
        }

        [Fact(DisplayName = "裏向きで未覗き見のサポートは対象外で、false を返す")]
        public void HasPlatform_FaceDownUnpeeked_NotVisible_ReturnsFalse()
        {
            // 情報秘匿: 裏向き未覗き見のサポートは CardID が null。Platform 判定対象外。
            var field = TestFactory.MakeWireOpponentField();
            field.Support[0] = TestFactory.MakeHiddenSupport("sup1", cardId: null, faceDown: true);

            TargetSelector.HasPlatform(field, _cc).Should().BeFalse();
        }
    }

    [Trait("対象", "先頭プラットフォームの特定")]
    public class FirstPlatformId : Base
    {
        [Fact(DisplayName = "プラットフォームがあるとき、そのインスタンス ID を返す")]
        public void FirstPlatformId_ReturnsPlatformInstanceID()
        {
            var field = TestFactory.MakeWireOpponentField();
            field.Support[0] = TestFactory.MakeHiddenSupport("plat_1", cardId: "TEST-0200", faceDown: false);

            var result = TargetSelector.FindFirstPlatformId(field, _cc);

            result.Should().Be("plat_1");
        }

        [Fact(DisplayName = "プラットフォームが無いとき、null を返す")]
        public void FirstPlatformId_NoPlatform_ReturnsNull()
        {
            var field = TestFactory.MakeWireOpponentField();

            TargetSelector.FindFirstPlatformId(field, _cc).Should().BeNull();
        }
    }

    [Trait("対象", "リソース比較値の算出")]
    public class ResourceValue : Base
    {
        [Fact(DisplayName = "現在スループットがあるとき、その値を比較値にする")]
        public void ResourceValue_CurrentTP_ReturnsTP()
        {
            var res = TestFactory.MakeWireResource(cardId: "TST-0001", currentTP: 700);

            var value = TargetSelector.CalculateResourceValue(res, _cc);

            value.Should().Be(700);
        }

        [Fact(DisplayName = "現在イールドがあるとき、その値を比較値にする")]
        public void ResourceValue_CurrentYield_ReturnsYield()
        {
            var res = TestFactory.MakeWireResource(cardId: "TST-0002", currentTP: null, currentYield: 500, maxYield: 500);

            var value = TargetSelector.CalculateResourceValue(res, _cc);

            value.Should().Be(500);
        }

        [Fact(DisplayName = "現在ステータスが無いとき、カード定義の基礎値を比較値にする")]
        public void ResourceValue_NoCurrentStats_FallsBackToCardDefinition()
        {
            var res = TestFactory.MakeWireResource(cardId: "TST-0001", currentTP: null, currentYield: null);

            var value = TargetSelector.CalculateResourceValue(res, _cc);

            // Card 1 is Compute with BaseThroughput = 600
            value.Should().Be(600);
        }
    }
}
