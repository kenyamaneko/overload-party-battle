using OverloadParty.Battle.Data;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Data;

public class MasterDataLoaderTests
{
    private const string EmptyList = "[]";
    private const string NullLiteral = "null";

    private static (List<CardDefinition> Cards, List<Initiative> Initiatives) LoadDistributedMasterData() =>
        MasterDataLoader.FromJson(
            File.ReadAllText(MasterData.CardsPath()),
            File.ReadAllText(MasterData.InitiativesPath()));

    [Trait("対象", "card が配布するマスターデータの読み込み")]
    public class 配布されたマスターデータ
    {
        [Fact(DisplayName = "card が配布するカードマスターデータを読み込むと、全ての Compute カードにコンピュートのステータスが設定される")]
        public void FillsComputeStats()
        {
            var (cards, _) = LoadDistributedMasterData();

            var computeCards = cards.Where(c => c.CardType == CardTypes.Compute).ToList();
            computeCards.Should().NotBeEmpty();
            computeCards.Should().OnlyContain(c => c.ComputeStats != null);
        }

        [Fact(DisplayName = "card が配布するカードマスターデータを読み込むと、全ての DataResource カードにデータリソースのステータスが設定される")]
        public void FillsDataResourceStats()
        {
            var (cards, _) = LoadDistributedMasterData();

            var dataResourceCards = cards.Where(c => c.CardType == CardTypes.DataResource).ToList();
            dataResourceCards.Should().NotBeEmpty();
            dataResourceCards.Should().OnlyContain(c => c.DataResourceStats != null);
        }

        [Fact(DisplayName = "card が配布する施策マスターデータを読み込むと、全ての施策に効果が設定される")]
        public void FillsInitiativeEffect()
        {
            var (_, initiatives) = LoadDistributedMasterData();

            initiatives.Should().NotBeEmpty();
            initiatives.Should().OnlyContain(i => i.Effect.Ops != null);
        }
    }

    [Trait("対象", "読み込めないマスターデータの検出")]
    public class 読み込めないマスターデータ
    {
        [Fact(DisplayName = "カード定義が 1 件も無いとき、カード定義が空だと分かるエラーで読み込みに失敗する")]
        public void EmptyCardsFails()
        {
            var act = () => MasterDataLoader.FromJson(EmptyList, EmptyList);

            act.Should().Throw<InvalidOperationException>()
                .WithMessage("cards master data has no entries");
        }

        [Fact(DisplayName = "カード定義はあるが施策定義が 1 件も無いとき、施策定義が空だと分かるエラーで読み込みに失敗する")]
        public void EmptyInitiativesFails()
        {
            var act = () => MasterDataLoader.FromJson(File.ReadAllText(MasterData.CardsPath()), EmptyList);

            act.Should().Throw<InvalidOperationException>()
                .WithMessage("initiatives master data has no entries");
        }

        [Fact(DisplayName = "カード定義が null のとき、読み込みに失敗する")]
        public void NullCardsFails()
        {
            var act = () => MasterDataLoader.FromJson(NullLiteral, NullLiteral);

            act.Should().Throw<InvalidOperationException>()
                .WithMessage("failed to deserialize cards master data");
        }
    }
}
