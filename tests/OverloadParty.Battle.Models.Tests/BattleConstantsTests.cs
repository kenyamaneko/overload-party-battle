using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Models;

public class BattleConstantsTests
{
    [Trait("対象", "ランク倍率")]
    public class GetRankMultiplier
    {
        [Theory(DisplayName = "ランクに応じた倍率を返す")]
        [InlineData(Rank.Small, 1)]
        [InlineData(Rank.Medium, 2)]
        [InlineData(Rank.Large, 3)]
        public void ScalesByRank(Rank rank, long expected)
        {
            BattleConstants.GetRankMultiplier(rank).Should().Be(expected);
        }

        [Fact(DisplayName = "ランクが null (リサイズ不可) のとき、基準倍率 1 を返す")]
        public void NoRank_NonResizable_ReturnsBaseMultiplier()
        {
            BattleConstants.GetRankMultiplier(null).Should().Be(1);
        }

        [Fact(DisplayName = "未定義のランクのとき、ArgumentOutOfRangeException を投げる")]
        public void UnknownRank_Throws()
        {
            var act = () => BattleConstants.GetRankMultiplier((Rank)99);
            act.Should().Throw<ArgumentOutOfRangeException>();
        }
    }

    [Trait("対象", "インスタンスファミリー倍率")]
    public class GetFamilyMultiplier
    {
        [Theory(DisplayName = "インスタンスファミリーごとにスループット・可用性の倍率を返す")]
        [InlineData(InstanceFamily.M, 1.0, 1.0)]
        [InlineData(InstanceFamily.C, 1.3, 0.7)]
        [InlineData(InstanceFamily.R, 0.7, 1.3)]
        public void ReturnsRulebookMultipliers(InstanceFamily family, double tpMult, double avMult)
        {
            BattleConstants.GetFamilyMultiplier(family).Should().Be((tpMult, avMult));
        }

        [Fact(DisplayName = "未定義のインスタンスファミリーのとき、ArgumentOutOfRangeException を投げる")]
        public void UnknownFamily_Throws()
        {
            var act = () => BattleConstants.GetFamilyMultiplier((InstanceFamily)99);
            act.Should().Throw<ArgumentOutOfRangeException>();
        }
    }
}
