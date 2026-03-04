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

        var result = FieldHelpers.FindResourceByID(field, "inst_1");
        Assert.NotNull(result);
        Assert.Equal(Zone.Frontend, result.Value.Zone);
        Assert.Same(res, result.Value.Resource);
    }

    [Fact]
    public void FindResourceByID_Backend_Found()
    {
        var field = TestFactory.MakeField();
        var res = TestFactory.MakeResource(instanceId: "inst_2");
        field.Backend[2] = res;

        var result = FieldHelpers.FindResourceByID(field, "inst_2");
        Assert.NotNull(result);
        Assert.Equal(Zone.Backend, result.Value.Zone);
    }

    [Fact]
    public void FindResourceByID_NotFound_ReturnsNull()
    {
        var field = TestFactory.MakeField();
        Assert.Null(FieldHelpers.FindResourceByID(field, "nonexistent"));
    }

    // ─── FindSupportByID ──────────────────────────────────────

    [Fact]
    public void FindSupportByID_Found()
    {
        var field = TestFactory.MakeField();
        field.Support[0] = new SupportInstance { InstanceID = "sup_1", CardID = 200 };

        var result = FieldHelpers.FindSupportByID(field, "sup_1");
        Assert.NotNull(result);
        Assert.Equal("sup_1", result.InstanceID);
    }

    [Fact]
    public void FindSupportByID_NotFound_ReturnsNull()
    {
        var field = TestFactory.MakeField();
        Assert.Null(FieldHelpers.FindSupportByID(field, "nonexistent"));
    }

    // ─── HasFrontendResources ─────────────────────────────────

    /// <summary>
    /// Rulebook §3: 裏向きカードは「いないものとみなす」
    /// Face-down cards don't count as frontend resources.
    /// </summary>
    [Fact]
    public void HasFrontendResources_OnlyFaceDown_ReturnsFalse()
    {
        var field = TestFactory.MakeField();
        field.Frontend[0] = TestFactory.MakeResource(faceUp: false, deployLeft: 2);

        Assert.False(FieldHelpers.HasFrontendResources(field));
    }

    [Fact]
    public void HasFrontendResources_OneFaceUp_ReturnsTrue()
    {
        var field = TestFactory.MakeField();
        field.Frontend[0] = TestFactory.MakeResource(faceUp: false);
        field.Frontend[1] = TestFactory.MakeResource(instanceId: "inst_2", faceUp: true);

        Assert.True(FieldHelpers.HasFrontendResources(field));
    }

    [Fact]
    public void HasFrontendResources_Empty_ReturnsFalse()
    {
        var field = TestFactory.MakeField();
        Assert.False(FieldHelpers.HasFrontendResources(field));
    }

    // ─── HasAnyActiveResources ────────────────────────────────

    [Fact]
    public void HasAnyActiveResources_BackendOnly_ReturnsTrue()
    {
        var field = TestFactory.MakeField();
        field.Backend[0] = TestFactory.MakeResource(faceUp: true);

        Assert.True(FieldHelpers.HasAnyActiveResources(field));
    }

    [Fact]
    public void HasAnyActiveResources_AllFaceDown_ReturnsFalse()
    {
        var field = TestFactory.MakeField();
        field.Frontend[0] = TestFactory.MakeResource(faceUp: false);
        field.Backend[0] = TestFactory.MakeResource(instanceId: "inst_2", faceUp: false);

        Assert.False(FieldHelpers.HasAnyActiveResources(field));
    }

    // ─── RemoveResourceFromField ──────────────────────────────

    [Fact]
    public void RemoveResourceFromField_RemovesFrontend()
    {
        var field = TestFactory.MakeField();
        field.Frontend[1] = TestFactory.MakeResource(instanceId: "inst_1");

        Assert.True(FieldHelpers.RemoveResourceFromField(field, "inst_1"));
        Assert.Null(field.Frontend[1]);
    }

    [Fact]
    public void RemoveResourceFromField_RemovesBackend()
    {
        var field = TestFactory.MakeField();
        field.Backend[2] = TestFactory.MakeResource(instanceId: "inst_2");

        Assert.True(FieldHelpers.RemoveResourceFromField(field, "inst_2"));
        Assert.Null(field.Backend[2]);
    }

    [Fact]
    public void RemoveResourceFromField_NotFound_ReturnsFalse()
    {
        var field = TestFactory.MakeField();
        Assert.False(FieldHelpers.RemoveResourceFromField(field, "nonexistent"));
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
        Assert.Equal(2, result.Count);
        Assert.Contains(result, r => r.InstanceID == "fu_1");
        Assert.Contains(result, r => r.InstanceID == "fu_2");
    }

    // ─── Zone eligibility (Rulebook §3) ──────────────────────

    /// <summary>
    /// Frontend: Compute, Container, Orchestrator, Serverless, AI/ML, ObjectStorage
    /// </summary>
    [Theory]
    [InlineData("Compute", true)]
    [InlineData("Container", true)]
    [InlineData("Orchestrator", true)]
    [InlineData("Serverless", true)]
    [InlineData("AI/ML", true)]
    [InlineData("ObjectStorage", true)]
    [InlineData("Database", false)]
    [InlineData("CacheDB", false)]
    [InlineData("Platform", false)]
    public void IsFrontendEligible_CorrectTypes(string cardType, bool expected)
    {
        Assert.Equal(expected, FieldHelpers.IsFrontendEligible(cardType));
    }

    /// <summary>
    /// Backend: All resource types (compute + data).
    /// </summary>
    [Theory]
    [InlineData("Compute", true)]
    [InlineData("Database", true)]
    [InlineData("ObjectStorage", true)]
    [InlineData("CacheDB", true)]
    [InlineData("Datawarehouse", true)]
    [InlineData("Platform", false)]
    [InlineData("Strategy", false)]
    public void IsBackendEligible_CorrectTypes(string cardType, bool expected)
    {
        Assert.Equal(expected, FieldHelpers.IsBackendEligible(cardType));
    }

    // ─── Card type classification ─────────────────────────────

    [Theory]
    [InlineData("Compute", true)]
    [InlineData("Container", true)]
    [InlineData("Orchestrator", true)]
    [InlineData("Serverless", true)]
    [InlineData("AI/ML", true)]
    [InlineData("Database", false)]
    public void IsComputeType_Correct(string cardType, bool expected)
    {
        Assert.Equal(expected, FieldHelpers.IsComputeType(cardType));
    }

    [Theory]
    [InlineData("Database", true)]
    [InlineData("ObjectStorage", true)]
    [InlineData("CacheDB", true)]
    [InlineData("Datawarehouse", true)]
    [InlineData("Compute", false)]
    public void IsDataType_Correct(string cardType, bool expected)
    {
        Assert.Equal(expected, FieldHelpers.IsDataType(cardType));
    }

    [Theory]
    [InlineData("Strategy", true)]
    [InlineData("Incident", true)]
    [InlineData("Compute", false)]
    [InlineData("Platform", false)]
    public void IsImmediateType_Correct(string cardType, bool expected)
    {
        Assert.Equal(expected, FieldHelpers.IsImmediateType(cardType));
    }

    // ─── CreateResourceInstance ───────────────────────────────

    /// <summary>
    /// Serverless (deploy_turns=0) → immediate face-up.
    /// </summary>
    [Fact]
    public void CreateResourceInstance_ZeroDeployTurns_FaceUp()
    {
        var card = TestFactory.ServerlessCard();
        var res = FieldHelpers.CreateResourceInstance(card, "inst_1", 1);

        Assert.True(res.FaceUp);
        Assert.Equal(0, res.DeployingTurnsLeft);
        Assert.Equal(Rank.Small, res.Rank);
    }

    /// <summary>
    /// Compute (deploy_turns=1) → face-down with 1 turn left.
    /// </summary>
    [Fact]
    public void CreateResourceInstance_OneDeployTurn_FaceDown()
    {
        var card = TestFactory.ComputeCard(deployTurns: 1);
        var res = FieldHelpers.CreateResourceInstance(card, "inst_1", 3);

        Assert.False(res.FaceUp);
        Assert.Equal(1, res.DeployingTurnsLeft);
        Assert.Equal(3, res.DeployedOnTurn);
    }

    [Fact]
    public void CreateResourceInstance_ComputeCard_SetsTpStats()
    {
        var card = TestFactory.ComputeCard(tp: 700, av: 1400);
        var res = FieldHelpers.CreateResourceInstance(card, "inst_1", 1);

        Assert.Equal(700, res.MaxTP);
        Assert.Equal(700, res.CurrentTP);
        Assert.Equal(1400, res.MaxAV);
        Assert.Equal(1400, res.CurrentAV);
    }

    [Fact]
    public void CreateResourceInstance_DataCard_SetsYieldStats()
    {
        var card = TestFactory.DataCard(yield: 500, av: 800);
        var res = FieldHelpers.CreateResourceInstance(card, "inst_1", 1);

        Assert.Equal(500, res.MaxYield);
        Assert.Equal(500, res.CurrentYield);
        Assert.Equal(800, res.MaxAV);
        Assert.Equal(800, res.CurrentAV);
    }

    // ─── AddToTrash ───────────────────────────────────────────

    [Fact]
    public void AddToTrash_AddsToCorrectPlayerTrash()
    {
        var state = TestFactory.MakeGameState();

        FieldHelpers.AddToTrash(state, 1, 42, "inst_42");
        Assert.Single(state.Player1Trash);
        Assert.Equal(42, state.Player1Trash[0].CardID);
        Assert.Empty(state.Player2Trash);
    }

    // ─── 3 slots per zone (Rulebook §3) ──────────────────────

    [Fact]
    public void Field_HasThreeSlotsPerZone()
    {
        var field = TestFactory.MakeField();
        Assert.Equal(3, field.Frontend.Length);
        Assert.Equal(3, field.Backend.Length);
        Assert.Equal(3, field.Support.Length);
    }
}
