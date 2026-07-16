using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Models;

public class CardDefinitionTests
{
    [Trait("対象", "Compute系リソース判定")]
    public class IsComputeType
    {
        [Fact(DisplayName = "カードタイプが Compute系リソースのとき、true を返す")]
        public void ComputeCategory_ReturnsTrue()
        {
            var card = new CardDefinition { CardType = "Compute", Subtype = "VM" };
            card.IsComputeType.Should().BeTrue();
        }

        [Theory(DisplayName = "Compute系リソース以外のカードタイプのとき、false を返す")]
        [InlineData("DataResource")]
        [InlineData("Platform")]
        [InlineData("Attachment")]
        public void NonComputeCategory_ReturnsFalse(string cardType)
        {
            var card = new CardDefinition { CardType = cardType };
            card.IsComputeType.Should().BeFalse();
        }
    }

    [Trait("対象", "Data系リソース判定")]
    public class IsDataResource
    {
        [Fact(DisplayName = "カードタイプが Data系リソースのとき、true を返す")]
        public void DataCategory_ReturnsTrue()
        {
            var card = new CardDefinition { CardType = "DataResource", Subtype = "Database" };
            card.IsDataResource.Should().BeTrue();
        }

        [Theory(DisplayName = "Data系リソース以外のカードタイプのとき、false を返す")]
        [InlineData("Compute")]
        [InlineData("Platform")]
        [InlineData("Strategy")]
        public void NonDataCategory_ReturnsFalse(string cardType)
        {
            var card = new CardDefinition { CardType = cardType };
            card.IsDataResource.Should().BeFalse();
        }
    }

    [Trait("対象", "サポートカード判定")]
    public class IsSupportType
    {
        [Theory(DisplayName = "サポートカードのカードタイプのとき、true を返す")]
        [InlineData("Platform")]
        [InlineData("Attachment")]
        [InlineData("Strategy")]
        [InlineData("Reactive")]
        [InlineData("Incident")]
        public void SupportCategory_ReturnsTrue(string cardType)
        {
            var card = new CardDefinition { CardType = cardType };
            card.IsSupportType.Should().BeTrue();
        }

        [Theory(DisplayName = "サポートカード以外のカードタイプのとき、false を返す")]
        [InlineData("Compute")]
        [InlineData("DataResource")]
        public void NonSupportCategory_ReturnsFalse(string cardType)
        {
            var card = new CardDefinition { CardType = cardType };
            card.IsSupportType.Should().BeFalse();
        }
    }

    [Trait("対象", "基礎スループット")]
    public class BaseThroughput
    {
        [Fact(DisplayName = "Compute系リソースのとき、スループットを返す")]
        public void WithComputeStats_ReturnsTP()
        {
            var card = TestFactory.ComputeCard(tp: 800);
            card.BaseThroughput.Should().Be(800);
        }

        [Fact(DisplayName = "Compute系リソースでないとき、0 を返す")]
        public void WithoutComputeStats_ReturnsZero()
        {
            var card = TestFactory.DataCard();
            card.BaseThroughput.Should().Be(0);
        }
    }

    [Trait("対象", "基礎イールド")]
    public class BaseYield
    {
        [Fact(DisplayName = "Data系リソースのとき、イールドを返す")]
        public void WithDataStats_ReturnsYield()
        {
            var card = TestFactory.DataCard(yield: 500);
            card.BaseYield.Should().Be(500);
        }

        [Fact(DisplayName = "Data系リソースでないとき、0 を返す")]
        public void WithoutDataStats_ReturnsZero()
        {
            var card = TestFactory.ComputeCard();
            card.BaseYield.Should().Be(0);
        }
    }

    [Trait("対象", "基礎可用性")]
    public class BaseAvailability
    {
        [Fact(DisplayName = "Compute系リソースのとき、可用性を返す")]
        public void ComputeCard_ReturnsComputeAV()
        {
            var card = TestFactory.ComputeCard(av: 1400);
            card.BaseAvailability.Should().Be(1400);
        }

        [Fact(DisplayName = "Data系リソースのとき、可用性を返す")]
        public void DataCard_ReturnsDataAV()
        {
            var card = TestFactory.DataCard(av: 800);
            card.BaseAvailability.Should().Be(800);
        }

        [Fact(DisplayName = "リソースでないとき、0 を返す")]
        public void NoStats_ReturnsZero()
        {
            var card = new CardDefinition { CardType = "Platform" };
            card.BaseAvailability.Should().Be(0);
        }
    }

    [Trait("対象", "維持コスト")]
    public class MaintenanceCost
    {
        [Fact(DisplayName = "Compute系リソースのとき、維持コストを返す")]
        public void ComputeCard_ReturnsComputeMC()
        {
            var card = TestFactory.ComputeCard(mc: 150);
            card.MaintenanceCost.Should().Be(150);
        }

        [Fact(DisplayName = "Data系リソースのとき、維持コストを返す")]
        public void DataCard_ReturnsDataMC()
        {
            var card = TestFactory.DataCard(mc: 100);
            card.MaintenanceCost.Should().Be(100);
        }

        [Fact(DisplayName = "リソースでないとき、0 を返す")]
        public void NoStats_ReturnsZero()
        {
            var card = new CardDefinition { CardType = "Strategy" };
            card.MaintenanceCost.Should().Be(0);
        }
    }

    [Trait("対象", "SLA ペナルティ")]
    public class SLAPenalty
    {
        [Fact(DisplayName = "Compute系リソースのとき、SLA ペナルティを返す")]
        public void ComputeCard_ReturnsComputePenalty()
        {
            var card = TestFactory.ComputeCard(slaPenalty: 400);
            card.SLAPenalty.Should().Be(400);
        }

        [Fact(DisplayName = "Data系リソースのとき、SLA ペナルティを返す")]
        public void DataCard_ReturnsDataPenalty()
        {
            var card = TestFactory.DataCard(slaPenalty: 300);
            card.SLAPenalty.Should().Be(300);
        }

        [Fact(DisplayName = "リソースでないとき、0 を返す")]
        public void NoStats_ReturnsZero()
        {
            var card = new CardDefinition { CardType = "Incident" };
            card.SLAPenalty.Should().Be(0);
        }
    }

    [Trait("対象", "カードタイプ判定の排他性")]
    public class CategoryExclusivity
    {
        [Fact(DisplayName = "Compute系リソース・Data系リソース・サポートカードの判定は互いに排他になる")]
        public void CategoryFlags_AreMutuallyExclusive()
        {
            var compute = TestFactory.ComputeCard();
            compute.IsComputeType.Should().BeTrue();
            compute.IsDataResource.Should().BeFalse();
            compute.IsSupportType.Should().BeFalse();

            var data = TestFactory.DataCard();
            data.IsComputeType.Should().BeFalse();
            data.IsDataResource.Should().BeTrue();
            data.IsSupportType.Should().BeFalse();

            var support = TestFactory.PlatformCard();
            support.IsComputeType.Should().BeFalse();
            support.IsDataResource.Should().BeFalse();
            support.IsSupportType.Should().BeTrue();
        }
    }
}
