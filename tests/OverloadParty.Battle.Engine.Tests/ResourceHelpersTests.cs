using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Engine;

/// <summary>
/// Tests for ResourceHelpers: resource creation, field placement,
/// deploy from hand/repo, destruction, and rank changes.
/// </summary>
public class ResourceHelpersTests
{
    // ─── CreateDeployedResource ────────────────────────────────

    [Fact]
    public void CreateDeployedResource_ComputeCard_SetsTPAndAV()
    {
        var card = TestFactory.ComputeCard(cardId: "TST-2001", tp: 600, av: 1400);

        var resource = ResourceHelpers.CreateDeployedResource(card, "inst_1", deployTurn: 3);

        resource.InstanceID.Should().Be("inst_1");
        resource.CardID.Should().Be("TST-2001");
        resource.MaxAV.Should().Be(1400);
        resource.CurrentAV.Should().Be(1400);
        resource.MaxTP.Should().Be(600);
        resource.CurrentTP.Should().Be(600);
        resource.DeployedOnTurn.Should().Be(3);
    }

    [Fact]
    public void CreateDeployedResource_DataCard_SetsYieldAndAV()
    {
        var card = TestFactory.DataCard(cardId: "TST-1009", yield: 400, av: 800);

        var resource = ResourceHelpers.CreateDeployedResource(card, "inst_2", deployTurn: 1);

        resource.MaxAV.Should().Be(800);
        resource.CurrentAV.Should().Be(800);
        resource.MaxYield.Should().Be(400);
        resource.CurrentYield.Should().Be(400);
        resource.MaxTP.Should().BeNull();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CreateDeployedResource_ResizableFlag_SetsRank(bool resizable)
    {
        var card = TestFactory.ComputeCard(cardId: "TST-2001", resizable: resizable);

        var resource = ResourceHelpers.CreateDeployedResource(card, "inst_1", deployTurn: 1);

        if (resizable)
        {
            resource.Rank.Should().Be(Rank.Small);
        }
        else
        {
            resource.Rank.Should().BeNull();
        }
    }

    [Theory]
    [InlineData(2, false, 2)]
    [InlineData(0, true, 0)]
    public void CreateDeployedResource_DeployTurns_SetsFaceUpAndTurnsLeft(
        int deployTurns, bool expectedFaceUp, int expectedTurnsLeft)
    {
        var card = TestFactory.ComputeCard(cardId: "TST-2001", deployTurns: deployTurns);

        var resource = ResourceHelpers.CreateDeployedResource(card, "inst_1", deployTurn: 1);

        resource.FaceUp.Should().Be(expectedFaceUp);
        resource.DeployingTurnsLeft.Should().Be(expectedTurnsLeft);
    }

    [Fact]
    public void CreateDeployedResource_SetsArtNo()
    {
        var card = TestFactory.ComputeCard(cardId: "TST-2001");

        var resource = ResourceHelpers.CreateDeployedResource(card, "inst_1", deployTurn: 1, artNo: 42);

        resource.ArtNo.Should().Be(42);
    }

    // ─── PlaceResourceOnField ─────────────────────────────────

    [Fact]
    public void PlaceResourceOnField_ComputeType_PlacedInFrontendFirst()
    {
        var field = TestFactory.MakeField();
        var resource = TestFactory.MakeResource(instanceId: "inst_1");

        ResourceHelpers.PlaceResourceOnField(field, resource, TestFactory.ComputeCard());

        field.Frontend.Any(r => r.InstanceID == "inst_1").Should().BeTrue();
    }

    [Fact]
    public void PlaceResourceOnField_ComputeType_FrontendFull_PlacedInBackend()
    {
        var field = TestFactory.MakeField();
        // Fill frontend
        foreach (var i in Enumerable.Range(0, field.Frontend.Capacity))
        {
            field.Frontend[i] = TestFactory.MakeResource(instanceId: $"fill_{i}");
        }

        var resource = TestFactory.MakeResource(instanceId: "inst_new");
        ResourceHelpers.PlaceResourceOnField(field, resource, TestFactory.ComputeCard());

        field.Backend.Any(r => r.InstanceID == "inst_new").Should().BeTrue();
    }

    [Fact]
    public void PlaceResourceOnField_ComputeType_AllFull_Throws()
    {
        var field = TestFactory.MakeField();
        foreach (var i in Enumerable.Range(0, field.Frontend.Capacity))
        {
            field.Frontend[i] = TestFactory.MakeResource(instanceId: $"f_{i}");
        }
        foreach (var i in Enumerable.Range(0, field.Backend.Capacity))
        {
            field.Backend[i] = TestFactory.MakeResource(instanceId: $"b_{i}");
        }

        var resource = TestFactory.MakeResource(instanceId: "overflow");

        var act = () => ResourceHelpers.PlaceResourceOnField(field, resource, TestFactory.ComputeCard());
        act.Should().Throw<GameRuleException>();
    }

    [Theory]
    [InlineData("Database")]
    [InlineData("ObjectStorage")]
    public void PlaceResourceOnField_BackendType_PlacedInBackend(string subtype)
    {
        var field = TestFactory.MakeField();
        var resource = TestFactory.MakeResource(instanceId: "be_1");

        ResourceHelpers.PlaceResourceOnField(field, resource, TestFactory.DataCard(subtype: subtype));

        field.Backend.Any(r => r.InstanceID == "be_1").Should().BeTrue();
    }

    [Fact]
    public void PlaceResourceOnField_Database_BackendFull_Throws()
    {
        var field = TestFactory.MakeField();
        foreach (var i in Enumerable.Range(0, field.Backend.Capacity))
        {
            field.Backend[i] = TestFactory.MakeResource(instanceId: $"b_{i}");
        }

        var resource = TestFactory.MakeResource(instanceId: "db_overflow");

        var act = () => ResourceHelpers.PlaceResourceOnField(field, resource, TestFactory.DataCard(subtype: "Database"));
        act.Should().Throw<GameRuleException>();
    }

    [Fact]
    public void PlaceResourceOnField_ObjectStorage_BackendFull_FallsToFrontend()
    {
        var field = TestFactory.MakeField();
        foreach (var i in Enumerable.Range(0, field.Backend.Capacity))
        {
            field.Backend[i] = TestFactory.MakeResource(instanceId: $"b_{i}");
        }

        var resource = TestFactory.MakeResource(instanceId: "os_fallback");
        ResourceHelpers.PlaceResourceOnField(field, resource, TestFactory.DataCard(subtype: "ObjectStorage"));

        field.Frontend.Any(r => r.InstanceID == "os_fallback").Should().BeTrue();
    }

    [Fact]
    public void PlaceResourceOnField_UnknownCardType_Throws()
    {
        var field = TestFactory.MakeField();
        var resource = TestFactory.MakeResource(instanceId: "inst_1");

        var unknownCard = new CardDefinition { CardType = "UnknownType" };
        var act = () => ResourceHelpers.PlaceResourceOnField(field, resource, unknownCard);
        act.Should().Throw<GameRuleException>();
    }

    // ─── DeployFromHand ───────────────────────────────────────

    [Fact]
    public void DeployFromHand_RemovesFromHandAndPlacesOnField()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "TST-2009", deployTurns: 0));

        var state = TestFactory.MakeGameState();
        state.Player1Hand.Add(new UndeployedCard { InstanceID = "h_1", CardID = "TST-2009", ArtNo = 5 });

        var field = state.Player1Field;
        ResourceHelpers.DeployFromHand(state, 1, field, cardId: "TST-2009", cc);

        state.Player1Hand.Should().BeEmpty();
        FieldHelpers.AllResources(field).Any(r => r.CardID == "TST-2009").Should().BeTrue();
    }

    [Fact]
    public void DeployFromHand_CardNotInHand_Throws()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "TST-2009"));

        var state = TestFactory.MakeGameState();
        var field = state.Player1Field;

        var act = () => ResourceHelpers.DeployFromHand(state, 1, field, cardId: "TEST-0999", cc);
        act.Should().Throw<GameRuleException>();
    }

    // ─── DeployFromRepo ───────────────────────────────────────

    [Fact]
    public void DeployFromRepo_RemovesFromRepoAndPlaces()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "TST-2019", deployTurns: 0));

        var state = TestFactory.MakeGameState();
        var repoCard = new UndeployedCard { InstanceID = "r_1", CardID = "TST-2019", ArtNo = 3 };
        state.Player1Repository.Add(repoCard);

        var field = state.Player1Field;
        ResourceHelpers.DeployFromRepo(state, 1, field, repoCard, overrideAV: 0, cc);

        state.Player1Repository.Should().BeEmpty();
        FieldHelpers.AllResources(field).Any(r => r.CardID == "TST-2019").Should().BeTrue();
    }

    [Fact]
    public void DeployFromRepo_WithOverrideAV_SetsMaxAV()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "TST-2019", av: 1400, deployTurns: 0));

        var state = TestFactory.MakeGameState();
        var repoCard = new UndeployedCard { InstanceID = "r_1", CardID = "TST-2019" };
        state.Player1Repository.Add(repoCard);

        var field = state.Player1Field;
        ResourceHelpers.DeployFromRepo(state, 1, field, repoCard, overrideAV: 999, cc);

        var deployed = FieldHelpers.AllResources(field).First(r => r.CardID == "TST-2019");
        deployed.MaxAV.Should().Be(999);
        deployed.Damage.Should().Be(0);
    }

    // ─── DestroyResource ──────────────────────────────────────

    [Fact]
    public void DestroyResource_AppliesSLAPenalty()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "TST-2009", slaPenalty: 400));

        var state = TestFactory.MakeGameState(p1Budget: 5000);
        var field = state.Player1Field;
        var resource = TestFactory.MakeResource(cardId: "TST-2009", instanceId: "inst_1");
        field.Frontend[0] = resource;

        ResourceHelpers.DestroyResource(state, 1, field, resource, cc);

        state.Player1Budget.Should().Be(4600);
    }

    [Fact]
    public void DestroyResource_MovesToTrash()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "TST-2009"));

        var state = TestFactory.MakeGameState();
        var field = state.Player1Field;
        var resource = TestFactory.MakeResource(cardId: "TST-2009", instanceId: "inst_1");
        field.Frontend[0] = resource;

        ResourceHelpers.DestroyResource(state, 1, field, resource, cc);

        state.Player1Trash.Should().Contain(c => c.CardID == "TST-2009");
    }

    [Fact]
    public void DestroyResource_RemovesFromField()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "TST-2009"));

        var state = TestFactory.MakeGameState();
        var field = state.Player1Field;
        var resource = TestFactory.MakeResource(cardId: "TST-2009", instanceId: "inst_1");
        field.Frontend[0] = resource;

        ResourceHelpers.DestroyResource(state, 1, field, resource, cc);

        FieldHelpers.FindResourceByID(field, "inst_1").Should().BeNull();
    }

    [Fact]
    public void DestroyResource_AttachmentsAlsoTrashed()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "TST-2009"));

        var state = TestFactory.MakeGameState();
        var field = state.Player1Field;
        var resource = TestFactory.MakeResource(cardId: "TST-2009", instanceId: "inst_1");
        field.Frontend[0] = resource;
        field.Support.TryPlace(new DeployedSupport { InstanceID = "att_1", CardID = "TEST-0300", ArtNo = 0, TargetInstanceID = "inst_1" });
        field.Support.TryPlace(new DeployedSupport { InstanceID = "att_2", CardID = "TEST-0301", ArtNo = 0, TargetInstanceID = "inst_1" });

        ResourceHelpers.DestroyResource(state, 1, field, resource, cc);

        state.Player1Trash.Select(c => c.CardID).Should().Contain(new[] { "TST-2009", "TEST-0300", "TEST-0301" });
    }

    // ─── ChangeRank ───────────────────────────────────────────

    [Fact]
    public void ChangeRank_UpdatesRankAndMaxAV()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "TST-2001", av: 1400, tp: 600));

        var resource = TestFactory.MakeResource(cardId: "TST-2001", rank: Rank.Small);

        ResourceHelpers.ChangeRank(resource, Rank.Medium, TestFactory.MakeField(), cc);

        resource.Rank.Should().Be(Rank.Medium);
        resource.MaxAV.Should().Be(2800); // 1400 * 2
    }

    [Fact]
    public void ChangeRank_NonElasticCompute_RecalculatesTP()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "TST-2001", tp: 600, elastic: false));

        var resource = TestFactory.MakeResource(cardId: "TST-2001", rank: Rank.Small);

        ResourceHelpers.ChangeRank(resource, Rank.Large, TestFactory.MakeField(), cc);

        resource.MaxTP.Should().Be(1800); // 600 * 3
        resource.CurrentTP.Should().Be(1800);
    }

    [Fact]
    public void ChangeRank_NonElasticData_RecalculatesYield()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.DataCard(cardId: "TST-1009", yield: 400));

        var resource = TestFactory.MakeResource(cardId: "TST-1009", rank: Rank.Small, maxTP: null, currentTP: null, maxYield: 400, currentYield: 400);

        ResourceHelpers.ChangeRank(resource, Rank.Medium, TestFactory.MakeField(), cc);

        resource.MaxYield.Should().Be(800); // 400 * 2
        resource.CurrentYield.Should().Be(800);
    }

    [Fact]
    public void ChangeRank_ElasticCard_DoesNotRecalculateTP()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ElasticContainerCard(cardId: "TEST-0002"));

        var resource = TestFactory.MakeResource(cardId: "TEST-0002", rank: Rank.Small, maxTP: 500, currentTP: 500);

        ResourceHelpers.ChangeRank(resource, Rank.Medium, TestFactory.MakeField(), cc);

        // Elastic cards should NOT have MaxTP recalculated
        resource.MaxTP.Should().Be(500);
        resource.CurrentTP.Should().Be(500);
    }
}
