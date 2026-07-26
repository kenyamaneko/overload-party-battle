using OverloadParty.Battle.Data;

namespace OverloadParty.Battle.Tests.Data;

[Trait("対象", "カードキャッシュ")]
public class CardCacheTests
{
    [Fact(DisplayName = "カード一覧を読み込むと、ID でカード定義を引ける")]
    public void LoadFromList_TwoCards_ResolvesByCardId()
    {
        var cache = new CardCache();
        var cardA = TestFactory.ComputeCard(cardId: "TST-0001");
        var cardB = TestFactory.ComputeCard(cardId: "TST-0002");

        cache.LoadFromList([cardA, cardB]);

        cache.Get("TST-0001").Should().BeSameAs(cardA);
        cache.MustGet("TST-0002").Should().BeSameAs(cardB);
        cache.Count.Should().Be(2);
    }

    [Fact(DisplayName = "未登録のカード ID を必須取得すると、InvalidOperationException になる")]
    public void MustGet_UnregisteredCardId_Throws()
    {
        var cache = new CardCache();
        cache.LoadFromList([TestFactory.ComputeCard(cardId: "TST-0001")]);

        var act = () => cache.MustGet("TST-9999");

        act.Should().Throw<InvalidOperationException>().WithMessage("*TST-9999*");
    }

    [Fact(DisplayName = "未登録のカード ID の取得は、null を返す")]
    public void Get_UnregisteredCardId_ReturnsNull()
    {
        var cache = new CardCache();
        cache.LoadFromList([TestFactory.ComputeCard(cardId: "TST-0001")]);

        cache.Get("TST-9999").Should().BeNull();
    }

    [Fact(DisplayName = "再読み込みすると、新しい一覧に置き換わる")]
    public void LoadFromList_CalledAgain_ReplacesPreviousEntries()
    {
        var cache = new CardCache();
        cache.LoadFromList([TestFactory.ComputeCard(cardId: "TST-0001")]);

        cache.LoadFromList([TestFactory.ComputeCard(cardId: "TST-0002")]);

        cache.Get("TST-0001").Should().BeNull();
        cache.Get("TST-0002").Should().NotBeNull();
        cache.Count.Should().Be(1);
    }
}
