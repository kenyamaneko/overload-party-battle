using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Engine;

public class ResourceHelpersTests
{
    [Trait("対象", "リソースの生成")]
    public class CreateDeployedResource
    {
        [Fact(DisplayName = "Compute カードからリソースを生成するとスループットと可用性が設定される")]
        public void ComputeCard_SetsTPAndAV()
        {
            var card = TestFactory.ComputeCard(cardId: "TST-0001", tp: 600, av: 1400);

            var resource = ResourceHelpers.CreateDeployedResource(card, "inst_1", deployTurn: 3);

            resource.InstanceID.Should().Be("inst_1");
            resource.CardID.Should().Be("TST-0001");
            resource.MaxAV.Should().Be(1400);
            resource.CurrentAV.Should().Be(1400);
            resource.MaxTP.Should().Be(600);
            resource.CurrentTP.Should().Be(600);
            resource.DeployedOnTurn.Should().Be(3);
        }

        [Fact(DisplayName = "Data カードからリソースを生成するとイールドと可用性が設定されスループットを持たない")]
        public void DataCard_SetsYieldAndAV()
        {
            var card = TestFactory.DataCard(cardId: "TST-0002", yield: 400, av: 800);

            var resource = ResourceHelpers.CreateDeployedResource(card, "inst_2", deployTurn: 1);

            resource.MaxAV.Should().Be(800);
            resource.CurrentAV.Should().Be(800);
            resource.MaxYield.Should().Be(400);
            resource.CurrentYield.Should().Be(400);
            resource.MaxTP.Should().BeNull();
        }

        [Theory(DisplayName = "Resizable が true のときランクが small になり false のときランクを持たない")]
        [InlineData(true)]
        [InlineData(false)]
        public void ResizableFlag_SetsRank(bool resizable)
        {
            var card = TestFactory.ComputeCard(cardId: "TST-0001", resizable: resizable);

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

        [Theory(DisplayName = "デプロイターンに応じて表向き状態と残りデプロイターンが決まる")]
        [InlineData(2, false, 2)]
        [InlineData(0, true, 0)]
        public void DeployTurns_SetsFaceUpAndTurnsLeft(
            int deployTurns, bool expectedFaceUp, int expectedTurnsLeft)
        {
            var card = TestFactory.ComputeCard(cardId: "TST-0001", deployTurns: deployTurns);

            var resource = ResourceHelpers.CreateDeployedResource(card, "inst_1", deployTurn: 1);

            resource.FaceUp.Should().Be(expectedFaceUp);
            resource.DeployingTurnsLeft.Should().Be(expectedTurnsLeft);
        }

        [Fact(DisplayName = "アート番号を指定して生成するとリソースにアート番号が設定される")]
        public void SetsArtNo()
        {
            var card = TestFactory.ComputeCard(cardId: "TST-0001");

            var resource = ResourceHelpers.CreateDeployedResource(card, "inst_1", deployTurn: 1, artNo: 42);

            resource.ArtNo.Should().Be(42);
        }
    }

    [Trait("対象", "リソースのフィールド配置")]
    public class PlaceResourceOnField
    {
        [Fact(DisplayName = "Compute リソースはまずフロントエンドに配置される")]
        public void ComputeType_PlacedInFrontendFirst()
        {
            var field = TestFactory.MakeField();
            var resource = TestFactory.MakeResource(instanceId: "inst_1");

            ResourceHelpers.PlaceResourceOnField(field, resource, TestFactory.ComputeCard());

            field.Frontend.Any(r => r.InstanceID == "inst_1").Should().BeTrue();
        }

        [Fact(DisplayName = "フロントエンドが満杯なら Compute リソースはバックエンドに配置される")]
        public void ComputeType_FrontendFull_PlacedInBackend()
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

        [Fact(DisplayName = "フロントエンドもバックエンドも満杯なら Compute リソースの配置は拒否される")]
        public void ComputeType_AllFull_Throws()
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

        [Theory(DisplayName = "Data 系リソースはバックエンドに配置される")]
        [InlineData("Database")]
        [InlineData("ObjectStorage")]
        public void BackendType_PlacedInBackend(string subtype)
        {
            var field = TestFactory.MakeField();
            var resource = TestFactory.MakeResource(instanceId: "be_1");

            ResourceHelpers.PlaceResourceOnField(field, resource, TestFactory.DataCard(subtype: subtype));

            field.Backend.Any(r => r.InstanceID == "be_1").Should().BeTrue();
        }

        [Fact(DisplayName = "バックエンドが満杯なら Database の配置は拒否される")]
        public void Database_BackendFull_Throws()
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

        [Fact(DisplayName = "バックエンドが満杯ならオブジェクトストレージはフロントエンドに配置される")]
        public void ObjectStorage_BackendFull_FallsToFrontend()
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

        [Fact(DisplayName = "未知のカードタイプの配置は拒否される")]
        public void UnknownCardType_Throws()
        {
            var field = TestFactory.MakeField();
            var resource = TestFactory.MakeResource(instanceId: "inst_1");

            var unknownCard = new CardDefinition { CardType = "UnknownType" };
            var act = () => ResourceHelpers.PlaceResourceOnField(field, resource, unknownCard);
            act.Should().Throw<GameRuleException>();
        }
    }

    [Trait("対象", "手札からのデプロイ")]
    public class DeployFromHand
    {
        [Fact(DisplayName = "手札からデプロイすると手札から取り除かれてフィールドに置かれる")]
        public void RemovesFromHandAndPlacesOnField()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0003", deployTurns: 0));

            var state = TestFactory.MakeGameState();
            state.Player1Hand.Add(new UndeployedCard { InstanceID = "h_1", CardID = "TST-0003", ArtNo = 5 });

            var field = state.Player1Field;
            ResourceHelpers.DeployFromHand(state, 1, field, cardId: "TST-0003", cc);

            state.Player1Hand.Should().BeEmpty();
            FieldHelpers.AllResources(field).Any(r => r.CardID == "TST-0003").Should().BeTrue();
        }

        [Fact(DisplayName = "手札に無いカードをデプロイしようとすると拒否される")]
        public void CardNotInHand_Throws()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0003"));

            var state = TestFactory.MakeGameState();
            var field = state.Player1Field;

            var act = () => ResourceHelpers.DeployFromHand(state, 1, field, cardId: "TEST-0999", cc);
            act.Should().Throw<GameRuleException>();
        }
    }

    [Trait("対象", "デッキからのデプロイ")]
    public class DeployFromRepo
    {
        [Fact(DisplayName = "デッキからデプロイするとデッキから取り除かれてフィールドに置かれる")]
        public void RemovesFromRepoAndPlaces()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0004", deployTurns: 0));

            var state = TestFactory.MakeGameState();
            var repoCard = new UndeployedCard { InstanceID = "r_1", CardID = "TST-0004", ArtNo = 3 };
            state.Player1Repository.Add(repoCard);

            var field = state.Player1Field;
            ResourceHelpers.DeployFromRepo(state, 1, field, repoCard, overrideAV: 0, cc);

            state.Player1Repository.Should().BeEmpty();
            FieldHelpers.AllResources(field).Any(r => r.CardID == "TST-0004").Should().BeTrue();
        }

        [Fact(DisplayName = "可用性の上書き値を指定してデッキからデプロイすると最大可用性がその値になる")]
        public void WithOverrideAV_SetsMaxAV()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0004", av: 1400, deployTurns: 0));

            var state = TestFactory.MakeGameState();
            var repoCard = new UndeployedCard { InstanceID = "r_1", CardID = "TST-0004" };
            state.Player1Repository.Add(repoCard);

            var field = state.Player1Field;
            ResourceHelpers.DeployFromRepo(state, 1, field, repoCard, overrideAV: 999, cc);

            var deployed = FieldHelpers.AllResources(field).First(r => r.CardID == "TST-0004");
            deployed.MaxAV.Should().Be(999);
            deployed.Damage.Should().Be(0);
        }
    }

    [Trait("対象", "リソースの破壊")]
    public class DestroyResource
    {
        [Fact(DisplayName = "リソースを破壊するとオーナーのバジェットから SLA ペナルティが減算される")]
        public void AppliesSLAPenalty()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0003", slaPenalty: 400));

            var state = TestFactory.MakeGameState(p1Budget: 5000);
            var field = state.Player1Field;
            var resource = TestFactory.MakeResource(cardId: "TST-0003", instanceId: "inst_1");
            field.Frontend[0] = resource;

            ResourceHelpers.DestroyResource(state, 1, field, resource, cc);

            state.Player1Budget.Should().Be(4600);
        }

        [Fact(DisplayName = "リソースを破壊するとトラッシュへ移る")]
        public void MovesToTrash()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0003"));

            var state = TestFactory.MakeGameState();
            var field = state.Player1Field;
            var resource = TestFactory.MakeResource(cardId: "TST-0003", instanceId: "inst_1");
            field.Frontend[0] = resource;

            ResourceHelpers.DestroyResource(state, 1, field, resource, cc);

            state.Player1Trash.Should().Contain(c => c.CardID == "TST-0003");
        }

        [Fact(DisplayName = "リソースを破壊するとフィールドから取り除かれる")]
        public void RemovesFromField()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0003"));

            var state = TestFactory.MakeGameState();
            var field = state.Player1Field;
            var resource = TestFactory.MakeResource(cardId: "TST-0003", instanceId: "inst_1");
            field.Frontend[0] = resource;

            ResourceHelpers.DestroyResource(state, 1, field, resource, cc);

            FieldHelpers.FindResourceByID(field, "inst_1").Should().BeNull();
        }

        [Fact(DisplayName = "リソースを破壊するとアタッチされたカードも一緒にトラッシュへ移る")]
        public void AttachmentsAlsoTrashed()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0003"));

            var state = TestFactory.MakeGameState();
            var field = state.Player1Field;
            var resource = TestFactory.MakeResource(cardId: "TST-0003", instanceId: "inst_1");
            field.Frontend[0] = resource;
            field.Support.TryPlace(new DeployedSupport { InstanceID = "att_1", CardID = "TEST-0300", ArtNo = 0, TargetInstanceID = "inst_1" });
            field.Support.TryPlace(new DeployedSupport { InstanceID = "att_2", CardID = "TEST-0301", ArtNo = 0, TargetInstanceID = "inst_1" });

            ResourceHelpers.DestroyResource(state, 1, field, resource, cc);

            state.Player1Trash.Select(c => c.CardID).Should().Contain(new[] { "TST-0003", "TEST-0300", "TEST-0301" });
        }
    }

    [Trait("対象", "リソースのランク変更")]
    public class ChangeRank
    {
        [Fact(DisplayName = "ランクを medium に上げると最大可用性が 2800 になる")]
        public void UpdatesRankAndMaxAV()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", av: 1400, tp: 600));

            var resource = TestFactory.MakeResource(cardId: "TST-0001", rank: Rank.Small);

            ResourceHelpers.ChangeRank(resource, Rank.Medium, TestFactory.MakeField(), cc);

            resource.Rank.Should().Be(Rank.Medium);
            resource.MaxAV.Should().Be(2800); // 1400 * 2
        }

        [Fact(DisplayName = "非 Elastic の Compute リソースをランク large にするとスループットが 1800 に再計算される")]
        public void NonElasticCompute_RecalculatesTP()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", tp: 600, elastic: false));

            var resource = TestFactory.MakeResource(cardId: "TST-0001", rank: Rank.Small);

            ResourceHelpers.ChangeRank(resource, Rank.Large, TestFactory.MakeField(), cc);

            resource.MaxTP.Should().Be(1800); // 600 * 3
            resource.CurrentTP.Should().Be(1800);
        }

        [Fact(DisplayName = "非 Elastic の Data リソースをランク medium にするとイールドが 800 に再計算される")]
        public void NonElasticData_RecalculatesYield()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.DataCard(cardId: "TST-0002", yield: 400));

            var resource = TestFactory.MakeResource(cardId: "TST-0002", rank: Rank.Small, maxTP: null, currentTP: null, maxYield: 400, currentYield: 400);

            ResourceHelpers.ChangeRank(resource, Rank.Medium, TestFactory.MakeField(), cc);

            resource.MaxYield.Should().Be(800); // 400 * 2
            resource.CurrentYield.Should().Be(800);
        }

        [Fact(DisplayName = "Elastic のリソースはランクを上げてもスループットが再計算されない")]
        public void ElasticCard_DoesNotRecalculateTP()
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
}
