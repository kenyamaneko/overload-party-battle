using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Engine;

/// <summary>
/// Tests for FieldHelpers. Validates field search, zone eligibility,
/// resource management per RULEBOOK.md §3 field layout.
/// </summary>
public class FieldHelpersTests
{
    /// <summary>Tests for FieldHelpers.FindResourceByID.</summary>
    public class FindResourceByID
    {
        [Fact]
        public void Frontend_Found()
        {
            var field = TestFactory.MakeField();
            var res = TestFactory.MakeResource(instanceId: "inst_1");
            field.Frontend[1] = res;

            FieldHelpers.FindResourceByID(field, "inst_1").Should().BeSameAs(res);
        }

        [Fact]
        public void Backend_Found()
        {
            var field = TestFactory.MakeField();
            var res = TestFactory.MakeResource(instanceId: "inst_2");
            field.Backend[2] = res;

            FieldHelpers.FindResourceByID(field, "inst_2").Should().BeSameAs(res);
        }

        [Fact]
        public void NotFound_ReturnsNull()
        {
            var field = TestFactory.MakeField();
            FieldHelpers.FindResourceByID(field, "nonexistent").Should().BeNull();
        }
    }

    /// <summary>Tests for FieldHelpers.FindResourceZone.</summary>
    public class FindResourceZone
    {
        [Fact]
        public void Frontend()
        {
            var field = TestFactory.MakeField();
            field.Frontend[1] = TestFactory.MakeResource(instanceId: "inst_1");

            FieldHelpers.FindResourceZone(field, "inst_1").Should().Be(Zone.Frontend);
        }

        [Fact]
        public void Backend()
        {
            var field = TestFactory.MakeField();
            field.Backend[2] = TestFactory.MakeResource(instanceId: "inst_2");

            FieldHelpers.FindResourceZone(field, "inst_2").Should().Be(Zone.Backend);
        }

        [Fact]
        public void NotFound_ReturnsNull()
        {
            var field = TestFactory.MakeField();
            FieldHelpers.FindResourceZone(field, "nonexistent").Should().BeNull();
        }
    }

    /// <summary>Tests for FieldHelpers.FindSupportByID.</summary>
    public class FindSupportByID
    {
        [Fact]
        public void Found()
        {
            var field = TestFactory.MakeField();
            field.Support[0] = new DeployedSupport { InstanceID = "sup_1", CardID = "TEST-0200" };

            var result = FieldHelpers.FindSupportByID(field, "sup_1");
            result.Should().NotBeNull();
            result!.InstanceID.Should().Be("sup_1");
        }

        [Fact]
        public void NotFound_ReturnsNull()
        {
            var field = TestFactory.MakeField();
            FieldHelpers.FindSupportByID(field, "nonexistent").Should().BeNull();
        }
    }

    /// <summary>Tests for FieldHelpers.HasFrontendResources.</summary>
    public class HasFrontendResources
    {
        /// <summary>
        /// 裏向きカードは「いないものとみなす」
        /// Face-down cards don't count as frontend resources.
        /// </summary>
        [Fact]
        public void OnlyFaceDown_ReturnsFalse()
        {
            var field = TestFactory.MakeField();
            field.Frontend[0] = TestFactory.MakeResource(faceUp: false, deployLeft: 2);

            FieldHelpers.HasFrontendResources(field).Should().BeFalse();
        }

        [Fact]
        public void OneFaceUp_ReturnsTrue()
        {
            var field = TestFactory.MakeField();
            field.Frontend[0] = TestFactory.MakeResource(faceUp: false);
            field.Frontend[1] = TestFactory.MakeResource(instanceId: "inst_2", faceUp: true);

            FieldHelpers.HasFrontendResources(field).Should().BeTrue();
        }

        [Fact]
        public void Empty_ReturnsFalse()
        {
            var field = TestFactory.MakeField();
            FieldHelpers.HasFrontendResources(field).Should().BeFalse();
        }
    }

    /// <summary>Tests for FieldHelpers.HasAnyActiveResources.</summary>
    public class HasAnyActiveResources
    {
        [Fact]
        public void BackendOnly_ReturnsTrue()
        {
            var field = TestFactory.MakeField();
            field.Backend[0] = TestFactory.MakeResource(faceUp: true);

            FieldHelpers.HasAnyActiveResources(field).Should().BeTrue();
        }

        [Fact]
        public void AllFaceDown_ReturnsFalse()
        {
            var field = TestFactory.MakeField();
            field.Frontend[0] = TestFactory.MakeResource(faceUp: false);
            field.Backend[0] = TestFactory.MakeResource(instanceId: "inst_2", faceUp: false);

            FieldHelpers.HasAnyActiveResources(field).Should().BeFalse();
        }
    }

    /// <summary>Tests for FieldHelpers.RemoveResourceFromField.</summary>
    public class RemoveResourceFromField
    {
        [Fact]
        public void RemovesFrontend()
        {
            var field = TestFactory.MakeField();
            field.Frontend[1] = TestFactory.MakeResource(instanceId: "inst_1");

            FieldHelpers.RemoveResourceFromField(field, "inst_1").Should().BeTrue();
            field.Frontend[1].Should().BeNull();
        }

        [Fact]
        public void RemovesBackend()
        {
            var field = TestFactory.MakeField();
            field.Backend[2] = TestFactory.MakeResource(instanceId: "inst_2");

            FieldHelpers.RemoveResourceFromField(field, "inst_2").Should().BeTrue();
            field.Backend[2].Should().BeNull();
        }

        [Fact]
        public void NotFound_ReturnsFalse()
        {
            var field = TestFactory.MakeField();
            FieldHelpers.RemoveResourceFromField(field, "nonexistent").Should().BeFalse();
        }
    }

    /// <summary>Tests for FieldHelpers.AllFaceUpResources.</summary>
    public class AllFaceUpResources
    {
        [Fact]
        public void SkipsFaceDown()
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
    }

    /// <summary>Tests for FieldHelpers.IsFrontendEligible.</summary>
    public class IsFrontendEligible
    {
        /// <summary>
        /// Frontend: Compute 全般 + Data の ObjectStorage subtype のみ
        /// </summary>
        [Theory]
        [InlineData("Compute", null, true)]
        [InlineData("DataResource", "ObjectStorage", true)]
        [InlineData("DataResource", "Database", false)]
        [InlineData("DataResource", "CacheDB", false)]
        [InlineData("Platform", null, false)]
        public void CorrectTypes(string cardType, string? subtype, bool expected)
        {
            FieldHelpers.IsFrontendEligible(cardType, subtype).Should().Be(expected);
        }
    }

    /// <summary>Tests for FieldHelpers.IsBackendEligible.</summary>
    public class IsBackendEligible
    {
        /// <summary>
        /// Backend: All resource types (compute + data).
        /// </summary>
        [Theory]
        [InlineData("Compute", true)]
        [InlineData("DataResource", true)]
        [InlineData("Platform", false)]
        [InlineData("Strategy", false)]
        public void CorrectTypes(string cardType, bool expected)
        {
            FieldHelpers.IsBackendEligible(cardType).Should().Be(expected);
        }
    }

    /// <summary>Tests for FieldHelpers.IsComputeType.</summary>
    public class IsComputeType
    {
        [Theory]
        [InlineData("Compute", true)]
        [InlineData("DataResource", false)]
        [InlineData("Platform", false)]
        [InlineData("Container", false)] // 旧個別 subtype は category ではないため false
        public void Correct(string cardType, bool expected)
        {
            FieldHelpers.IsComputeType(cardType).Should().Be(expected);
        }
    }

    /// <summary>Tests for FieldHelpers.IsDataResource.</summary>
    public class IsDataResource
    {
        [Theory]
        [InlineData("DataResource", true)]
        [InlineData("Compute", false)]
        [InlineData("Database", false)] // 旧個別 subtype は category ではないため false
        public void Correct(string cardType, bool expected)
        {
            FieldHelpers.IsDataResource(cardType).Should().Be(expected);
        }
    }

    /// <summary>Tests for FieldHelpers.IsImmediateType.</summary>
    public class IsImmediateType
    {
        [Theory]
        [InlineData("Strategy", true)]
        [InlineData("Incident", true)]
        [InlineData("Compute", false)]
        [InlineData("Platform", false)]
        public void Correct(string cardType, bool expected)
        {
            FieldHelpers.IsImmediateType(cardType).Should().Be(expected);
        }
    }

    /// <summary>Tests for ResourceHelpers.CreateDeployedResource.</summary>
    public class CreateDeployedResource
    {
        /// <summary>
        /// Serverless (deploy_turns=0) → immediate face-up.
        /// </summary>
        [Fact]
        public void ZeroDeployTurns_FaceUp()
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
        public void OneDeployTurn_FaceDown()
        {
            var card = TestFactory.ComputeCard(deployTurns: 1);
            var res = ResourceHelpers.CreateDeployedResource(card, "inst_1", 3);

            res.FaceUp.Should().BeFalse();
            res.DeployingTurnsLeft.Should().Be(1);
            res.DeployedOnTurn.Should().Be(3);
        }

        [Fact]
        public void ComputeCard_SetsTpStats()
        {
            var card = TestFactory.ComputeCard(tp: 700, av: 1400);
            var res = ResourceHelpers.CreateDeployedResource(card, "inst_1", 1);

            res.MaxTP.Should().Be(700);
            res.CurrentTP.Should().Be(700);
            res.MaxAV.Should().Be(1400);
            res.CurrentAV.Should().Be(1400);
        }

        [Fact]
        public void DataCard_SetsYieldStats()
        {
            var card = TestFactory.DataCard(yield: 500, av: 800);
            var res = ResourceHelpers.CreateDeployedResource(card, "inst_1", 1);

            res.MaxYield.Should().Be(500);
            res.CurrentYield.Should().Be(500);
            res.MaxAV.Should().Be(800);
            res.CurrentAV.Should().Be(800);
        }
    }

    /// <summary>Tests for CardMoveHelpers.AddToTrash.</summary>
    public class AddToTrash
    {
        [Fact]
        public void AddsToCorrectPlayerTrash()
        {
            var state = TestFactory.MakeGameState();

            CardMoveHelpers.AddToTrash(state, 1, "TST-0001", "inst_42");
            state.Player1Trash.Should().ContainSingle()
                .Which.CardID.Should().Be("TST-0001");
            state.Player2Trash.Should().BeEmpty();
        }
    }

    /// <summary>Tests that each field zone exposes three slots.</summary>
    public class FieldSlots
    {
        [Fact]
        public void HasThreeSlotsPerZone()
        {
            var field = TestFactory.MakeField();
            field.Frontend.Capacity.Should().Be(3);
            field.Backend.Capacity.Should().Be(3);
            field.Support.Capacity.Should().Be(3);
        }
    }
}
