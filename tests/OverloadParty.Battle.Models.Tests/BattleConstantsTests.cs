using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Models;

/// <summary>
/// Tests for BattleConstants multiplier functions. Validates the rulebook
/// rank multipliers (small ×1 / medium ×2 / large ×3) and instance family
/// multipliers (M standard / C throughput-yield ×1.3 / R availability ×1.3).
/// </summary>
public class BattleConstantsTests
{
    // ─── RankMultiplier ─────────────────────────────────────────

    [Theory]
    [InlineData(Rank.Small, 1)]
    [InlineData(Rank.Medium, 2)]
    [InlineData(Rank.Large, 3)]
    public void RankMultiplier_ScalesByRank(Rank rank, long expected)
    {
        BattleConstants.GetRankMultiplier(rank).Should().Be(expected);
    }

    [Fact]
    public void RankMultiplier_NoRank_NonResizable_ReturnsBaseMultiplier()
    {
        BattleConstants.GetRankMultiplier(null).Should().Be(1);
    }

    [Fact]
    public void RankMultiplier_UnknownRank_Throws()
    {
        var act = () => BattleConstants.GetRankMultiplier((Rank)99);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ─── FamilyMultiplier ───────────────────────────────────────

    [Theory]
    [InlineData(InstanceFamily.M, 1.0, 1.0)]
    [InlineData(InstanceFamily.C, 1.3, 0.7)]
    [InlineData(InstanceFamily.R, 0.7, 1.3)]
    public void FamilyMultiplier_ReturnsRulebookMultipliers(InstanceFamily family, double tpMult, double avMult)
    {
        BattleConstants.GetFamilyMultiplier(family).Should().Be((tpMult, avMult));
    }

    [Fact]
    public void FamilyMultiplier_UnknownFamily_Throws()
    {
        var act = () => BattleConstants.GetFamilyMultiplier((InstanceFamily)99);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
