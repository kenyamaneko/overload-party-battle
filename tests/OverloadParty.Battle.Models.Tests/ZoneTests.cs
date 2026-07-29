using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Models;

public class ZoneTests
{
    [Trait("対象", "ゾーンのスロット操作")]
    public class SlotOperations
    {
        [Fact(DisplayName = "満杯のゾーンでは、空きスロットが見つからず配置に失敗し内容が変わらない")]
        public void FullZone_FindEmptySlotReturnsMinusOneAndPlaceFails()
        {
            var zone = new Zone<DeployedResource>(3);
            var a = TestFactory.MakeResource(instanceId: "inst_a");
            var b = TestFactory.MakeResource(instanceId: "inst_b");
            var c = TestFactory.MakeResource(instanceId: "inst_c");
            zone[0] = a;
            zone[1] = b;
            zone[2] = c;

            zone.FindEmptySlot().Should().Be(-1);
            zone.TryPlace(TestFactory.MakeResource(instanceId: "inst_d")).Should().BeFalse();
            zone.ToArray().Should().Equal(a, b, c);
        }

        [Fact(DisplayName = "途中に空きスロットがあるゾーンに配置すると、最初の空きスロットに入る")]
        public void GappedZone_PlacesIntoFirstEmptySlot()
        {
            var zone = new Zone<DeployedResource>(3);
            zone[0] = TestFactory.MakeResource(instanceId: "inst_a");
            zone[2] = TestFactory.MakeResource(instanceId: "inst_c");
            var placed = TestFactory.MakeResource(instanceId: "inst_b");

            zone.TryPlace(placed).Should().BeTrue();

            zone[1].Should().BeSameAs(placed);
        }

        [Fact(DisplayName = "条件に一致する要素が無い除去は、失敗し内容が変わらない")]
        public void Remove_NoMatch_ReturnsFalseAndKeepsContent()
        {
            var zone = new Zone<DeployedResource>(3);
            var a = TestFactory.MakeResource(instanceId: "inst_a");
            zone[0] = a;

            zone.Remove(r => r.InstanceID == "inst_nonexistent").Should().BeFalse();

            zone.ToArray().Should().Equal(a, null, null);
        }

        [Fact(DisplayName = "条件に一致する複数要素の一括除去は、除去件数を返し該当スロットが空く")]
        public void RemoveAll_MultipleMatches_ReturnsCountAndClearsSlots()
        {
            var zone = new Zone<DeployedResource>(3);
            var keep = TestFactory.MakeResource(instanceId: "inst_keep", cardId: "TST-0001");
            var removeA = TestFactory.MakeResource(instanceId: "inst_remove_a", cardId: "TST-0002");
            var removeB = TestFactory.MakeResource(instanceId: "inst_remove_b", cardId: "TST-0002");
            zone[0] = keep;
            zone[1] = removeA;
            zone[2] = removeB;

            var removedCount = zone.RemoveAll(r => r.CardID == "TST-0002");

            removedCount.Should().Be(2);
            zone.ToArray().Should().Equal(keep, null, null);
        }

        [Fact(DisplayName = "列挙は空きスロットを飛ばし、埋まった要素だけを返す")]
        public void Enumeration_SkipsEmptySlotsAndReturnsOnlyFilledItems()
        {
            var zone = new Zone<DeployedResource>(3);
            var a = TestFactory.MakeResource(instanceId: "inst_a");
            var b = TestFactory.MakeResource(instanceId: "inst_b");
            zone[0] = a;
            zone[2] = b;

            zone.Should().Equal(a, b);
        }
    }
}
