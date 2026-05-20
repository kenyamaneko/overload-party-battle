using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Engine;

/// <summary>
/// Tests for FieldHelpers. Validates field search, zone eligibility,
/// resource management per RULEBOOK.md §3 field layout.
/// </summary>
public class FieldHelpersTests
{
    // ─── FindResourceByID ─────────────────────────────────────

    [Fact]
    public void FindResourceByID_Frontend_Found()
    {
        var field = TestFactory.MakeField();
        var res = TestFactory.MakeResource(instanceId: "inst_1");
        field.Frontend[1] = res;

        FieldHelpers.FindResourceByID(field, "inst_1").Should().BeSameAs(res);
    }

    [Fact]
    public void FindResourceByID_Backend_Found()
    {
        var field = TestFactory.MakeField();
        var res = TestFactory.MakeResource(instanceId: "inst_2");
        field.Backend[2] = res;

        FieldHelpers.FindResourceByID(field, "inst_2").Should().BeSameAs(res);
    }

    [Fact]
    public void FindResourceByID_NotFound_ReturnsNull()
    {
        var field = TestFactory.MakeField();
        FieldHelpers.FindResourceByID(field, "nonexistent").Should().BeNull();
    }

    // ─── FindResourceZone ────────────────────────────────────

    [Fact]
    public void FindResourceZone_Frontend()
    {
        var field = TestFactory.MakeField();
        field.Frontend[1] = TestFactory.MakeResource(instanceId: "inst_1");

        FieldHelpers.FindResourceZone(field, "inst_1").Should().Be(Zone.Frontend);
    }

    [Fact]
    public void FindResourceZone_Backend()
    {
        var field = TestFactory.MakeField();
        field.Backend[2] = TestFactory.MakeResource(instanceId: "inst_2");

        FieldHelpers.FindResourceZone(field, "inst_2").Should().Be(Zone.Backend);
    }

    [Fact]
    public void FindResourceZone_NotFound_ReturnsNull()
    {
        var field = TestFactory.MakeField();
        FieldHelpers.FindResourceZone(field, "nonexistent").Should().BeNull();
    }

    // ─── FindSupportByID ──────────────────────────────────────

    [Fact]
    public void FindSupportByID_Found()
    {
        var field = TestFactory.MakeField();
        field.Support[0] = new DeployedSupport { InstanceID = "sup_1", CardID = "TEST-0200" };

        var result = FieldHelpers.FindSupportByID(field, "sup_1");
        result.Should().NotBeNull();
        result!.InstanceID.Should().Be("sup_1");
    }

    [Fact]
    public void FindSupportByID_NotFound_ReturnsNull()
    {
        var field = TestFactory.MakeField();
        FieldHelpers.FindSupportByID(field, "nonexistent").Should().BeNull();
    }

    // ─── HasFrontendResources ─────────────────────────────────

    /// <summary>
    /// 裏向きカードは「いないものとみなす」
    /// Face-down cards don't count as frontend resources.
    /// </summary>
    [Fact]
    public void HasFrontendResources_OnlyFaceDown_ReturnsFalse()
    {
        var field = TestFactory.MakeField();
        field.Frontend[0] = TestFactory.MakeResource(faceUp: false, deployLeft: 2);

        FieldHelpers.HasFrontendResources(field).Should().BeFalse();
    }

    [Fact]
    public void HasFrontendResources_OneFaceUp_ReturnsTrue()
    {
        var field = TestFactory.MakeField();
        field.Frontend[0] = TestFactory.MakeResource(faceUp: false);
        field.Frontend[1] = TestFactory.MakeResource(instanceId: "inst_2", faceUp: true);

        FieldHelpers.HasFrontendResources(field).Should().BeTrue();
    }

    [Fact]
    public void HasFrontendResources_Empty_ReturnsFalse()
    {
        var field = TestFactory.MakeField();
        FieldHelpers.HasFrontendResources(field).Should().BeFalse();
    }

    // ─── HasAnyActiveResources ────────────────────────────────

    [Fact]
    public void HasAnyActiveResources_BackendOnly_ReturnsTrue()
    {
        var field = TestFactory.MakeField();
        field.Backend[0] = TestFactory.MakeResource(faceUp: true);

        FieldHelpers.HasAnyActiveResources(field).Should().BeTrue();
    }

    [Fact]
    public void HasAnyActiveResources_AllFaceDown_ReturnsFalse()
    {
        var field = TestFactory.MakeField();
        field.Frontend[0] = TestFactory.MakeResource(faceUp: false);
        field.Backend[0] = TestFactory.MakeResource(instanceId: "inst_2", faceUp: false);

        FieldHelpers.HasAnyActiveResources(field).Should().BeFalse();
    }

    // ─── RemoveResourceFromField ──────────────────────────────

    [Fact]
    public void RemoveResourceFromField_RemovesFrontend()
    {
        var field = TestFactory.MakeField();
        field.Frontend[1] = TestFactory.MakeResource(instanceId: "inst_1");

        FieldHelpers.RemoveResourceFromField(field, "inst_1").Should().BeTrue();
        field.Frontend[1].Should().BeNull();
    }

    [Fact]
    public void RemoveResourceFromField_RemovesBackend()
    {
        var field = TestFactory.MakeField();
        field.Backend[2] = TestFactory.MakeResource(instanceId: "inst_2");

        FieldHelpers.RemoveResourceFromField(field, "inst_2").Should().BeTrue();
        field.Backend[2].Should().BeNull();
    }

    [Fact]
    public void RemoveResourceFromField_NotFound_ReturnsFalse()
    {
        var field = TestFactory.MakeField();
        FieldHelpers.RemoveResourceFromField(field, "nonexistent").Should().BeFalse();
    }

    // ─── AllFaceUpResources ───────────────────────────────────

    [Fact]
    public void AllFaceUpResources_SkipsFaceDown()
    {
        var field = TestFactory.MakeField();
        field.Frontend[0] = TestFactory.MakeResource(instanceId: "fu_1", faceUp: true);
        field.Frontend[1] = TestFactory.MakeResource(instanceId: "fd_1", faceUp: false);
        field.Backend[0] = TestFactory.MakeResource(instanceId: "fu_2", faceUp: true);
        field.Backend[1] = TestFactory.MakeResource(instanceId: "fd_2", faceUp: false);

        var result = FieldHelpers.AllFaceUpResources(field).ToList();
        result.Should().HaveCount(2);
        result.Should().Contain(r => r.InstanceID == "fu_1");
        result.Should().Contain(r => r.InstanceID == "fu_2");
    }

    // ─── Zone eligibility ─────────────────────────────────────

    /// <summary>
    /// Frontend: Compute 全般 + Data の ObjectStorage subtype のみ
    /// </summary>
    [Theory]
    [InlineData("Compute", null, true)]
    [InlineData("Data", "ObjectStorage", true)]
    [InlineData("Data", "Database", false)]
    [InlineData("Data", "CacheDB", false)]
    [InlineData("Platform", null, false)]
    public void IsFrontendEligible_CorrectTypes(string cardType, string? subtype, bool expected)
    {
        FieldHelpers.IsFrontendEligible(cardType, subtype).Should().Be(expected);
    }

    /// <summary>
    /// Backend: All resource types (compute + data).
    /// </summary>
    [Theory]
    [InlineData("Compute", true)]
    [InlineData("Data", true)]
    [InlineData("Platform", false)]
    [InlineData("Strategy", false)]
    public void IsBackendEligible_CorrectTypes(string cardType, bool expected)
    {
        FieldHelpers.IsBackendEligible(cardType).Should().Be(expected);
    }

    // ─── Card type classification ─────────────────────────────

    [Theory]
    [InlineData("Compute", true)]
    [InlineData("Data", false)]
    [InlineData("Platform", false)]
    [InlineData("Container", false)] // 旧個別 subtype は category ではないため false
    public void IsComputeType_Correct(string cardType, bool expected)
    {
        FieldHelpers.IsComputeType(cardType).Should().Be(expected);
    }

    [Theory]
    [InlineData("Data", true)]
    [InlineData("Compute", false)]
    [InlineData("Database", false)] // 旧個別 subtype は category ではないため false
    public void IsDataType_Correct(string cardType, bool expected)
    {
        FieldHelpers.IsDataType(cardType).Should().Be(expected);
    }

    [Theory]
    [InlineData("Strategy", true)]
    [InlineData("Incident", true)]
    [InlineData("Compute", false)]
    [InlineData("Platform", false)]
    public void IsImmediateType_Correct(string cardType, bool expected)
    {
        FieldHelpers.IsImmediateType(cardType).Should().Be(expected);
    }

    // ─── CreateDeployedResource ───────────────────────────────

    /// <summary>
    /// Serverless (deploy_turns=0) → immediate face-up.
    /// </summary>
    [Fact]
    public void CreateDeployedResource_ZeroDeployTurns_FaceUp()
    {
        var card = TestFactory.ServerlessCard();
        var res = ResourceHelpers.CreateDeployedResource(card, "inst_1", 1);

        res.FaceUp.Should().BeTrue();
        res.DeployingTurnsLeft.Should().Be(0);
        res.Rank.Should().BeNull();
    }

    /// <summary>
    /// Compute (deploy_turns=1) → face-down with 1 turn left.
    /// </summary>
    [Fact]
    public void CreateDeployedResource_OneDeployTurn_FaceDown()
    {
        var card = TestFactory.ComputeCard(deployTurns: 1);
        var res = ResourceHelpers.CreateDeployedResource(card, "inst_1", 3);

        res.FaceUp.Should().BeFalse();
        res.DeployingTurnsLeft.Should().Be(1);
        res.DeployedOnTurn.Should().Be(3);
    }

    [Fact]
    public void CreateDeployedResource_ComputeCard_SetsTpStats()
    {
        var card = TestFactory.ComputeCard(tp: 700, av: 1400);
        var res = ResourceHelpers.CreateDeployedResource(card, "inst_1", 1);

        res.MaxTP.Should().Be(700);
        res.CurrentTP.Should().Be(700);
        res.MaxAV.Should().Be(1400);
        res.CurrentAV.Should().Be(1400);
    }

    [Fact]
    public void CreateDeployedResource_DataCard_SetsYieldStats()
    {
        var card = TestFactory.DataCard(yield: 500, av: 800);
        var res = ResourceHelpers.CreateDeployedResource(card, "inst_1", 1);

        res.MaxYield.Should().Be(500);
        res.CurrentYield.Should().Be(500);
        res.MaxAV.Should().Be(800);
        res.CurrentAV.Should().Be(800);
    }

    // ─── AddToTrash ───────────────────────────────────────────

    [Fact]
    public void AddToTrash_AddsToCorrectPlayerTrash()
    {
        var state = TestFactory.MakeGameState();

        CardMoveHelpers.AddToTrash(state, 1, "TST-4020", "inst_42");
        state.Player1Trash.Should().ContainSingle()
            .Which.CardID.Should().Be("TST-4020");
        state.Player2Trash.Should().BeEmpty();
    }

    // ─── 3 slots per zone ─────────────────────────────────────

    [Fact]
    public void Field_HasThreeSlotsPerZone()
    {
        var field = TestFactory.MakeField();
        field.Frontend.Capacity.Should().Be(3);
        field.Backend.Capacity.Should().Be(3);
        field.Support.Capacity.Should().Be(3);
    }
}
