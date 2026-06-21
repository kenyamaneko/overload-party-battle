using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Models;

/// <summary>
/// Tests for CardDefinition computed properties: type category checks,
/// base stat accessors, and null-safe fallback behavior.
/// </summary>
public class CardDefinitionTests
{
    /// <summary>Tests for CardDefinition.IsComputeType.</summary>
    public class IsComputeType
    {
        [Fact]
        public void ComputeCategory_ReturnsTrue()
        {
            var card = new CardDefinition { CardType = "Compute", Subtype = "VM" };
            card.IsComputeType.Should().BeTrue();
        }

        [Theory]
        [InlineData("DataResource")]
        [InlineData("Platform")]
        [InlineData("Attachment")]
        public void NonComputeCategory_ReturnsFalse(string cardType)
        {
            var card = new CardDefinition { CardType = cardType };
            card.IsComputeType.Should().BeFalse();
        }
    }

    /// <summary>Tests for CardDefinition.IsDataResource.</summary>
    public class IsDataResource
    {
        [Fact]
        public void DataCategory_ReturnsTrue()
        {
            var card = new CardDefinition { CardType = "DataResource", Subtype = "Database" };
            card.IsDataResource.Should().BeTrue();
        }

        [Theory]
        [InlineData("Compute")]
        [InlineData("Platform")]
        [InlineData("Strategy")]
        public void NonDataCategory_ReturnsFalse(string cardType)
        {
            var card = new CardDefinition { CardType = cardType };
            card.IsDataResource.Should().BeFalse();
        }
    }

    /// <summary>Tests for CardDefinition.IsSupportType.</summary>
    public class IsSupportType
    {
        [Theory]
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

        [Theory]
        [InlineData("Compute")]
        [InlineData("DataResource")]
        public void NonSupportCategory_ReturnsFalse(string cardType)
        {
            var card = new CardDefinition { CardType = cardType };
            card.IsSupportType.Should().BeFalse();
        }
    }

    /// <summary>Tests for CardDefinition.BaseThroughput.</summary>
    public class BaseThroughput
    {
        [Fact]
        public void WithComputeStats_ReturnsTP()
        {
            var card = TestFactory.ComputeCard(tp: 800);
            card.BaseThroughput.Should().Be(800);
        }

        [Fact]
        public void WithoutComputeStats_ReturnsZero()
        {
            var card = TestFactory.DataCard();
            card.BaseThroughput.Should().Be(0);
        }
    }

    /// <summary>Tests for CardDefinition.BaseYield.</summary>
    public class BaseYield
    {
        [Fact]
        public void WithDataStats_ReturnsYield()
        {
            var card = TestFactory.DataCard(yield: 500);
            card.BaseYield.Should().Be(500);
        }

        [Fact]
        public void WithoutDataStats_ReturnsZero()
        {
            var card = TestFactory.ComputeCard();
            card.BaseYield.Should().Be(0);
        }
    }

    /// <summary>Tests for CardDefinition.BaseAvailability.</summary>
    public class BaseAvailability
    {
        [Fact]
        public void ComputeCard_ReturnsComputeAV()
        {
            var card = TestFactory.ComputeCard(av: 1400);
            card.BaseAvailability.Should().Be(1400);
        }

        [Fact]
        public void DataCard_ReturnsDataAV()
        {
            var card = TestFactory.DataCard(av: 800);
            card.BaseAvailability.Should().Be(800);
        }

        [Fact]
        public void NoStats_ReturnsZero()
        {
            var card = new CardDefinition { CardType = "Platform" };
            card.BaseAvailability.Should().Be(0);
        }
    }

    /// <summary>Tests for CardDefinition.MaintenanceCost.</summary>
    public class MaintenanceCost
    {
        [Fact]
        public void ComputeCard_ReturnsComputeMC()
        {
            var card = TestFactory.ComputeCard(mc: 150);
            card.MaintenanceCost.Should().Be(150);
        }

        [Fact]
        public void DataCard_ReturnsDataMC()
        {
            var card = TestFactory.DataCard(mc: 100);
            card.MaintenanceCost.Should().Be(100);
        }

        [Fact]
        public void NoStats_ReturnsZero()
        {
            var card = new CardDefinition { CardType = "Strategy" };
            card.MaintenanceCost.Should().Be(0);
        }
    }

    /// <summary>Tests for CardDefinition.SLAPenalty.</summary>
    public class SLAPenalty
    {
        [Fact]
        public void ComputeCard_ReturnsComputePenalty()
        {
            var card = TestFactory.ComputeCard(slaPenalty: 400);
            card.SLAPenalty.Should().Be(400);
        }

        [Fact]
        public void DataCard_ReturnsDataPenalty()
        {
            var card = TestFactory.DataCard(slaPenalty: 300);
            card.SLAPenalty.Should().Be(300);
        }

        [Fact]
        public void NoStats_ReturnsZero()
        {
            var card = new CardDefinition { CardType = "Incident" };
            card.SLAPenalty.Should().Be(0);
        }
    }

    /// <summary>Tests that CardDefinition category flags are mutually exclusive.</summary>
    public class CategoryExclusivity
    {
        [Fact]
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
