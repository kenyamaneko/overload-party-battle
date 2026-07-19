using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Engine;

public class FieldHelpersTests
{
    [Trait("対象", "リソースの ID 検索")]
    public class FindResourceByID
    {
        [Fact(DisplayName = "フロントエンドのリソースを ID 検索で見つけられる")]
        public void Frontend_Found()
        {
            var field = TestFactory.MakeField();
            var res = TestFactory.MakeResource(instanceId: "inst_1");
            field.Frontend[1] = res;

            FieldHelpers.FindResourceByID(field, "inst_1").Should().BeSameAs(res);
        }

        [Fact(DisplayName = "バックエンドのリソースを ID 検索で見つけられる")]
        public void Backend_Found()
        {
            var field = TestFactory.MakeField();
            var res = TestFactory.MakeResource(instanceId: "inst_2");
            field.Backend[2] = res;

            FieldHelpers.FindResourceByID(field, "inst_2").Should().BeSameAs(res);
        }

        [Fact(DisplayName = "存在しない ID で検索すると null が返る")]
        public void NotFound_ReturnsNull()
        {
            var field = TestFactory.MakeField();
            FieldHelpers.FindResourceByID(field, "nonexistent").Should().BeNull();
        }
    }

    [Trait("対象", "リソースのゾーン判定")]
    public class FindResourceZone
    {
        [Fact(DisplayName = "フロントエンドにあるリソースのゾーンとしてフロントエンドが返る")]
        public void Frontend()
        {
            var field = TestFactory.MakeField();
            field.Frontend[1] = TestFactory.MakeResource(instanceId: "inst_1");

            FieldHelpers.FindResourceZone(field, "inst_1").Should().Be(Zone.Frontend);
        }

        [Fact(DisplayName = "バックエンドにあるリソースのゾーンとしてバックエンドが返る")]
        public void Backend()
        {
            var field = TestFactory.MakeField();
            field.Backend[2] = TestFactory.MakeResource(instanceId: "inst_2");

            FieldHelpers.FindResourceZone(field, "inst_2").Should().Be(Zone.Backend);
        }

        [Fact(DisplayName = "存在しない ID のゾーンを問い合わせると null が返る")]
        public void NotFound_ReturnsNull()
        {
            var field = TestFactory.MakeField();
            FieldHelpers.FindResourceZone(field, "nonexistent").Should().BeNull();
        }
    }

    [Trait("対象", "サポートカードの ID 検索")]
    public class FindSupportByID
    {
        [Fact(DisplayName = "サポートカードを ID 検索で見つけられる")]
        public void Found()
        {
            var field = TestFactory.MakeField();
            field.Support[0] = new DeployedSupport { InstanceID = "sup_1", CardID = "TEST-0200" };

            var result = FieldHelpers.FindSupportByID(field, "sup_1");
            result.Should().NotBeNull();
            result!.InstanceID.Should().Be("sup_1");
        }

        [Fact(DisplayName = "存在しない ID でサポートを検索すると null が返る")]
        public void NotFound_ReturnsNull()
        {
            var field = TestFactory.MakeField();
            FieldHelpers.FindSupportByID(field, "nonexistent").Should().BeNull();
        }
    }

    [Trait("対象", "フロントエンドのリソース有無判定")]
    public class HasFrontendResources
    {
        [Fact(DisplayName = "フロントエンドに裏向きリソースしかないときは在席なしとみなし false を返す")]
        public void OnlyFaceDown_ReturnsFalse()
        {
            var field = TestFactory.MakeField();
            field.Frontend[0] = TestFactory.MakeResource(faceUp: false, deployLeft: 2);

            FieldHelpers.HasFrontendResources(field).Should().BeFalse();
        }

        [Fact(DisplayName = "フロントエンドに表向きリソースが 1 体あれば true を返す")]
        public void OneFaceUp_ReturnsTrue()
        {
            var field = TestFactory.MakeField();
            field.Frontend[0] = TestFactory.MakeResource(faceUp: false);
            field.Frontend[1] = TestFactory.MakeResource(instanceId: "inst_2", faceUp: true);

            FieldHelpers.HasFrontendResources(field).Should().BeTrue();
        }

        [Fact(DisplayName = "フロントエンドが空なら false を返す")]
        public void Empty_ReturnsFalse()
        {
            var field = TestFactory.MakeField();
            FieldHelpers.HasFrontendResources(field).Should().BeFalse();
        }
    }

    [Trait("対象", "稼働リソースの有無判定")]
    public class HasAnyActiveResources
    {
        [Fact(DisplayName = "バックエンドだけに表向きリソースがあっても true を返す")]
        public void BackendOnly_ReturnsTrue()
        {
            var field = TestFactory.MakeField();
            field.Backend[0] = TestFactory.MakeResource(faceUp: true);

            FieldHelpers.HasAnyActiveResources(field).Should().BeTrue();
        }

        [Fact(DisplayName = "フロントエンドもバックエンドも裏向きだけなら false を返す")]
        public void AllFaceDown_ReturnsFalse()
        {
            var field = TestFactory.MakeField();
            field.Frontend[0] = TestFactory.MakeResource(faceUp: false);
            field.Backend[0] = TestFactory.MakeResource(instanceId: "inst_2", faceUp: false);

            FieldHelpers.HasAnyActiveResources(field).Should().BeFalse();
        }
    }

    [Trait("対象", "フィールドからのリソース除去")]
    public class RemoveResourceFromField
    {
        [Fact(DisplayName = "フロントエンドのリソースを除去すると true が返りスロットが空になる")]
        public void RemovesFrontend()
        {
            var field = TestFactory.MakeField();
            field.Frontend[1] = TestFactory.MakeResource(instanceId: "inst_1");

            FieldHelpers.RemoveResourceFromField(field, "inst_1").Should().BeTrue();
            field.Frontend[1].Should().BeNull();
        }

        [Fact(DisplayName = "バックエンドのリソースを除去すると true が返りスロットが空になる")]
        public void RemovesBackend()
        {
            var field = TestFactory.MakeField();
            field.Backend[2] = TestFactory.MakeResource(instanceId: "inst_2");

            FieldHelpers.RemoveResourceFromField(field, "inst_2").Should().BeTrue();
            field.Backend[2].Should().BeNull();
        }

        [Fact(DisplayName = "存在しない ID の除去は false を返す")]
        public void NotFound_ReturnsFalse()
        {
            var field = TestFactory.MakeField();
            FieldHelpers.RemoveResourceFromField(field, "nonexistent").Should().BeFalse();
        }
    }

    [Trait("対象", "表向きリソースの列挙")]
    public class AllFaceUpResources
    {
        [Fact(DisplayName = "表向きリソースだけを列挙し裏向きは除外する")]
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

    [Trait("対象", "フロントエンド配置可否の判定")]
    public class IsFrontendEligible
    {
        [Theory(DisplayName = "フロントエンドには Compute系リソース全般と Data系リソースのオブジェクトストレージサブタイプだけが配置可能と判定される")]
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

    [Trait("対象", "バックエンド配置可否の判定")]
    public class IsBackendEligible
    {
        [Theory(DisplayName = "バックエンドには Compute系リソースと Data系リソースが配置可能でプラットフォームやストラテジーは不可になる")]
        [InlineData("Compute", true)]
        [InlineData("DataResource", true)]
        [InlineData("Platform", false)]
        [InlineData("Strategy", false)]
        public void CorrectTypes(string cardType, bool expected)
        {
            FieldHelpers.IsBackendEligible(cardType).Should().Be(expected);
        }
    }

    [Trait("対象", "Compute系タイプ判定")]
    public class IsComputeType
    {
        [Theory(DisplayName = "Compute系タイプ判定は card_type が Compute系リソースのときだけ true になる")]
        [InlineData("Compute", true)]
        [InlineData("DataResource", false)]
        [InlineData("Platform", false)]
        [InlineData("Container", false)] // 旧個別 subtype は category ではないため false
        public void Correct(string cardType, bool expected)
        {
            FieldHelpers.IsComputeType(cardType).Should().Be(expected);
        }
    }

    [Trait("対象", "Data系リソースタイプ判定")]
    public class IsDataResource
    {
        [Theory(DisplayName = "Data系リソースタイプ判定は card_type が Data系リソースのときだけ true になる")]
        [InlineData("DataResource", true)]
        [InlineData("Compute", false)]
        [InlineData("Database", false)] // 旧個別 subtype は category ではないため false
        public void Correct(string cardType, bool expected)
        {
            FieldHelpers.IsDataResource(cardType).Should().Be(expected);
        }
    }

    [Trait("対象", "即時使用タイプ判定")]
    public class IsImmediateType
    {
        [Theory(DisplayName = "ストラテジーとインシデントは即時使用タイプと判定され、それ以外は false になる")]
        [InlineData("Strategy", true)]
        [InlineData("Incident", true)]
        [InlineData("Compute", false)]
        [InlineData("Platform", false)]
        public void Correct(string cardType, bool expected)
        {
            FieldHelpers.IsImmediateType(cardType).Should().Be(expected);
        }
    }

    [Trait("対象", "リソースのデプロイ生成")]
    public class CreateDeployedResource
    {
        [Fact(DisplayName = "デプロイターン 0 のサーバーレスは即座に表向きで生成される")]
        public void ZeroDeployTurns_FaceUp()
        {
            var card = TestFactory.ServerlessCard();
            var res = ResourceHelpers.CreateDeployedResource(card, "inst_1", 1);

            res.FaceUp.Should().BeTrue();
            res.DeployingTurnsLeft.Should().Be(0);
            res.Rank.Should().BeNull();
        }

        [Fact(DisplayName = "デプロイターン 1 のリソースは裏向きでデプロイターン残 1 として生成され、デプロイしたターンを記録する")]
        public void OneDeployTurn_FaceDown()
        {
            var card = TestFactory.ComputeCard(deployTurns: 1);
            var res = ResourceHelpers.CreateDeployedResource(card, "inst_1", 3);

            res.FaceUp.Should().BeFalse();
            res.DeployingTurnsLeft.Should().Be(1);
            res.DeployedOnTurn.Should().Be(3);
        }

        [Fact(DisplayName = "Compute系カードから生成したリソースにスループット 700 と可用性 1400 が設定される")]
        public void ComputeCard_SetsTpStats()
        {
            var card = TestFactory.ComputeCard(tp: 700, av: 1400);
            var res = ResourceHelpers.CreateDeployedResource(card, "inst_1", 1);

            res.MaxTP.Should().Be(700);
            res.MaxAV.Should().Be(1400);
        }

        [Fact(DisplayName = "Data系カードから生成したリソースにイールド 500 と可用性 800 が設定される")]
        public void DataCard_SetsYieldStats()
        {
            var card = TestFactory.DataCard(yield: 500, av: 800);
            var res = ResourceHelpers.CreateDeployedResource(card, "inst_1", 1);

            res.MaxYield.Should().Be(500);
            res.MaxAV.Should().Be(800);
        }
    }

    [Trait("対象", "トラッシュへの追加")]
    public class AddToTrash
    {
        [Fact(DisplayName = "指定したプレイヤーのトラッシュにだけカードが追加され、相手のトラッシュは空のまま")]
        public void AddsToCorrectPlayerTrash()
        {
            var state = TestFactory.MakeGameState();

            CardMoveHelpers.AddToTrash(state, 1, "TST-0001", "inst_42");
            state.Player1Trash.Should().ContainSingle()
                .Which.CardID.Should().Be("TST-0001");
            state.Player2Trash.Should().BeEmpty();
        }
    }

    [Trait("対象", "フィールドのスロット数")]
    public class FieldSlots
    {
        [Fact(DisplayName = "フロントエンド・バックエンド・サポートゾーンがそれぞれ 3 スロットを持つ")]
        public void HasThreeSlotsPerZone()
        {
            var field = TestFactory.MakeField();
            field.Frontend.Capacity.Should().Be(3);
            field.Backend.Capacity.Should().Be(3);
            field.Support.Capacity.Should().Be(3);
        }
    }
}
