using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Npc;
using GD = OverloadParty.GameState;

namespace OverloadParty.Battle.Tests.Npc;

public class ActionFilterTests
{
    [Trait("対象", "ゾーン文字列の解析")]
    public class ParseZoneStr
    {
        [Theory(DisplayName = "有効なゾーン文字列を解析すると、ゾーン名とスロット位置を返す")]
        [InlineData("frontend_0", "frontend", 0)]
        [InlineData("frontend_2", "frontend", 2)]
        [InlineData("backend_0", "backend", 0)]
        [InlineData("backend_2", "backend", 2)]
        [InlineData("support_1", "support", 1)]
        public void ValidInput_ReturnsCorrectSlotPosition(string input, string expectedZone, int expectedIndex)
        {
            var result = ActionFilter.ParseZoneStr(input);

            result.Should().NotBeNull();
            result!.Zone.Should().Be(expectedZone);
            result.Index.Should().Be(expectedIndex);
        }

        [Theory(DisplayName = "スロット番号を含まないゾーン文字列を解析すると、null を返す")]
        [InlineData("")]
        [InlineData("frontend")]
        [InlineData("abc")]
        [InlineData("no_number_here_x")]
        public void InvalidInput_ReturnsNull(string input)
        {
            ActionFilter.ParseZoneStr(input).Should().BeNull();
        }

        [Fact(DisplayName = "アンダースコアが複数あるとき、末尾の数字をスロット位置に使う")]
        public void MultipleUnderscores_UsesLastSegment()
        {
            var result = ActionFilter.ParseZoneStr("some_zone_3");

            result.Should().NotBeNull();
            result!.Zone.Should().Be("some_zone");
            result.Index.Should().Be(3);
        }
    }

    [Trait("対象", "デプロイゾーンの選択")]
    public class PickBestZone
    {
        [Fact(DisplayName = "有効ゾーンが null のとき、null を返す")]
        public void NullValidZones_ReturnsNull()
        {
            var card = TestFactory.ComputeCard();
            ActionFilter.PickBestZone(null, card, []).Should().BeNull();
        }

        [Fact(DisplayName = "Compute系リソースはフロントエンドを優先する")]
        public void ComputeCard_PrefersFrontend()
        {
            var card = TestFactory.ComputeCard();
            var zones = new List<string> { "backend_0", "frontend_1" };

            var result = ActionFilter.PickBestZone(zones, card, []);

            result.Should().Be("frontend_1");
        }

        [Fact(DisplayName = "Compute系リソースはフロントエンドが無いとき、バックエンドに置く")]
        public void ComputeCard_FallsBackToBackend()
        {
            var card = TestFactory.ComputeCard();
            var zones = new List<string> { "backend_0" };

            var result = ActionFilter.PickBestZone(zones, card, []);

            result.Should().Be("backend_0");
        }

        [Fact(DisplayName = "ObjectStorage はバックエンドを優先する")]
        public void ObjectStorageCard_PrefersBackend()
        {
            var card = TestFactory.DataCard(subtype: "ObjectStorage");
            var zones = new List<string> { "frontend_0", "backend_1" };

            var result = ActionFilter.PickBestZone(zones, card, []);

            result.Should().Be("backend_1");
        }

        [Fact(DisplayName = "Compute系リソースに置けるゾーンが無いとき、例外を投げる")]
        public void ComputeCard_NoMatchingZone_Throws()
        {
            var card = TestFactory.ComputeCard();
            var zones = new List<string> { "support_0" };

            var act = () => ActionFilter.PickBestZone(zones, card, []);

            act.Should().Throw<InvalidOperationException>();
        }

        [Fact(DisplayName = "ObjectStorage に置けるゾーンが無いとき、例外を投げる")]
        public void ObjectStorageCard_NoMatchingZone_Throws()
        {
            var card = TestFactory.DataCard(subtype: "ObjectStorage");
            var zones = new List<string> { "support_0" };

            var act = () => ActionFilter.PickBestZone(zones, card, []);

            act.Should().Throw<InvalidOperationException>();
        }

        [Fact(DisplayName = "使用済みのゾーンを避けて選ぶ")]
        public void SkipsUsedZones()
        {
            var card = TestFactory.ComputeCard();
            var zones = new List<string> { "frontend_0", "frontend_1" };
            var used = new HashSet<string> { "frontend_0" };

            var result = ActionFilter.PickBestZone(zones, card, used);

            result.Should().Be("frontend_1");
        }

        [Fact(DisplayName = "候補ゾーンが全て使用済みのとき、null を返す")]
        public void AllZonesUsed_ReturnsNull()
        {
            var card = TestFactory.ComputeCard();
            var zones = new List<string> { "frontend_0" };
            var used = new HashSet<string> { "frontend_0" };

            ActionFilter.PickBestZone(zones, card, used).Should().BeNull();
        }
    }

    [Trait("対象", "サポートゾーンの選択")]
    public class PickSupportZone
    {
        [Fact(DisplayName = "有効ゾーンが null のとき、null を返す")]
        public void NullValidZones_ReturnsNull()
        {
            ActionFilter.PickSupportZone(null, []).Should().BeNull();
        }

        [Fact(DisplayName = "未使用のサポートゾーンを返す")]
        public void ReturnsSupportZoneNotUsed()
        {
            var zones = new List<string> { "support_0", "support_1" };
            var used = new HashSet<string> { "support_0" };

            ActionFilter.PickSupportZone(zones, used).Should().Be("support_1");
        }

        [Fact(DisplayName = "サポート以外のゾーンしか無いとき、null を返す")]
        public void SkipsNonSupportZones()
        {
            var zones = new List<string> { "frontend_0", "backend_1" };

            ActionFilter.PickSupportZone(zones, []).Should().BeNull();
        }
    }

    [Trait("対象", "インスタンスの CardID 解決")]
    public class ResolveCardIdForInstance
    {
        [Fact(DisplayName = "フロントエンドのリソースのインスタンスから CardID を解決する")]
        public void FindsResourceInFrontend()
        {
            var field = TestFactory.MakeWireField();
            field.Frontend[0] = TestFactory.MakeWireResource(cardId: "TST-0001", instanceId: "inst_42");

            ActionFilter.ResolveCardIdForInstance("inst_42", field).Should().Be("TST-0001");
        }

        [Fact(DisplayName = "デプロイ済みサポートのインスタンスから CardID を解決する")]
        public void FindsDeployedSupport()
        {
            var field = TestFactory.MakeWireField();
            field.Support[0] = TestFactory.MakeWireSupport(instanceId: "sup_1", cardId: "TST-0002");

            ActionFilter.ResolveCardIdForInstance("sup_1", field).Should().Be("TST-0002");
        }

        [Fact(DisplayName = "インスタンスが見つからないとき、例外を投げる")]
        public void NotFound_Throws()
        {
            var field = TestFactory.MakeWireField();
            var act = () => ActionFilter.ResolveCardIdForInstance("missing", field);
            act.Should().Throw<InvalidOperationException>();
        }
    }
}
