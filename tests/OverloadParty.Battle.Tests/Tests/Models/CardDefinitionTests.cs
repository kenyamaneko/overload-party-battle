using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Models;

/// <summary>
/// Tests for CardDefinition computed properties: type category checks,
/// base stat accessors, and null-safe fallback behavior.
/// </summary>
public class CardDefinitionTests
{
    // ─── IsComputeType ─────────────────────────────────────────

    [Theory]
    [InlineData("Compute")]
    [InlineData("Container")]
    [InlineData("Orchestrator")]
    [InlineData("Serverless")]
    [InlineData("AI/ML")]
    public void IsComputeType_ComputeCards_ReturnsTrue(string cardType)
    {
        var card = new CardDefinition { CardType = cardType };
        card.IsComputeType.Should().BeTrue();
    }

    [Theory]
    [InlineData("Database")]
    [InlineData("Platform")]
    [InlineData("Attachment")]
    public void IsComputeType_NonComputeCards_ReturnsFalse(string cardType)
    {
        var card = new CardDefinition { CardType = cardType };
        card.IsComputeType.Should().BeFalse();
    }

    // ─── IsDataType ────────────────────────────────────────────

    [Theory]
    [InlineData("Database")]
    [InlineData("ObjectStorage")]
    [InlineData("CacheDB")]
    public void IsDataType_DataCards_ReturnsTrue(string cardType)
    {
        var card = new CardDefinition { CardType = cardType };
        card.IsDataType.Should().BeTrue();
    }

    [Theory]
    [InlineData("Compute")]
    [InlineData("Platform")]
    [InlineData("Strategy")]
    public void IsDataType_NonDataCards_ReturnsFalse(string cardType)
    {
        var card = new CardDefinition { CardType = cardType };
        card.IsDataType.Should().BeFalse();
    }

    // ─── IsSupportType ─────────────────────────────────────────

    [Theory]
    [InlineData("Platform")]
    [InlineData("Attachment")]
    [InlineData("Strategy")]
    [InlineData("Reactive")]
    [InlineData("Incident")]
    public void IsSupportType_SupportCards_ReturnsTrue(string cardType)
    {
        var card = new CardDefinition { CardType = cardType };
        card.IsSupportType.Should().BeTrue();
    }

    [Theory]
    [InlineData("Compute")]
    [InlineData("Database")]
    [InlineData("Container")]
    public void IsSupportType_NonSupportCards_ReturnsFalse(string cardType)
    {
        var card = new CardDefinition { CardType = cardType };
        card.IsSupportType.Should().BeFalse();
    }

    // ─── BaseThroughput ────────────────────────────────────────

    [Fact]
    public void BaseThroughput_WithComputeStats_ReturnsTP()
    {
        var card = TestFactory.ComputeCard(tp: 800);
        card.BaseThroughput.Should().Be(800);
    }

    [Fact]
    public void BaseThroughput_WithoutComputeStats_ReturnsZero()
    {
        var card = TestFactory.DataCard();
        card.BaseThroughput.Should().Be(0);
    }

    // ─── BaseYield ─────────────────────────────────────────────

    [Fact]
    public void BaseYield_WithDataStats_ReturnsYield()
    {
        var card = TestFactory.DataCard(yield: 500);
        card.BaseYield.Should().Be(500);
    }

    [Fact]
    public void BaseYield_WithoutDataStats_ReturnsZero()
    {
        var card = TestFactory.ComputeCard();
        card.BaseYield.Should().Be(0);
    }

    // ─── BaseAvailability ──────────────────────────────────────

    [Fact]
    public void BaseAvailability_ComputeCard_ReturnsComputeAV()
    {
        var card = TestFactory.ComputeCard(av: 1400);
        card.BaseAvailability.Should().Be(1400);
    }

    [Fact]
    public void BaseAvailability_DataCard_ReturnsDataAV()
    {
        var card = TestFactory.DataCard(av: 800);
        card.BaseAvailability.Should().Be(800);
    }

    [Fact]
    public void BaseAvailability_NoStats_ReturnsZero()
    {
        var card = new CardDefinition { CardType = "Platform" };
        card.BaseAvailability.Should().Be(0);
    }

    // ─── MaintenanceCost ───────────────────────────────────────

    [Fact]
    public void MaintenanceCost_ComputeCard_ReturnsComputeMC()
    {
        var card = TestFactory.ComputeCard(mc: 150);
        card.MaintenanceCost.Should().Be(150);
    }

    [Fact]
    public void MaintenanceCost_DataCard_ReturnsDataMC()
    {
        var card = TestFactory.DataCard(mc: 100);
        card.MaintenanceCost.Should().Be(100);
    }

    [Fact]
    public void MaintenanceCost_NoStats_ReturnsZero()
    {
        var card = new CardDefinition { CardType = "Strategy" };
        card.MaintenanceCost.Should().Be(0);
    }

    // ─── SLAPenalty ────────────────────────────────────────────

    [Fact]
    public void SLAPenalty_ComputeCard_ReturnsComputePenalty()
    {
        var card = TestFactory.ComputeCard(slaPenalty: 400);
        card.SLAPenalty.Should().Be(400);
    }

    [Fact]
    public void SLAPenalty_DataCard_ReturnsDataPenalty()
    {
        var card = TestFactory.DataCard(slaPenalty: 300);
        card.SLAPenalty.Should().Be(300);
    }

    [Fact]
    public void SLAPenalty_NoStats_ReturnsZero()
    {
        var card = new CardDefinition { CardType = "Incident" };
        card.SLAPenalty.Should().Be(0);
    }

    // ─── Category exclusivity ──────────────────────────────────

    [Fact]
    public void CategoryFlags_AreMutuallyExclusive()
    {
        var compute = TestFactory.ComputeCard();
        compute.IsComputeType.Should().BeTrue();
        compute.IsDataType.Should().BeFalse();
        compute.IsSupportType.Should().BeFalse();

        var data = TestFactory.DataCard();
        data.IsComputeType.Should().BeFalse();
        data.IsDataType.Should().BeTrue();
        data.IsSupportType.Should().BeFalse();

        var support = TestFactory.PlatformCard();
        support.IsComputeType.Should().BeFalse();
        support.IsDataType.Should().BeFalse();
        support.IsSupportType.Should().BeTrue();
    }
}
