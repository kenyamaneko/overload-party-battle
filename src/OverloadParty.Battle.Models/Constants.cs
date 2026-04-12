namespace OverloadParty.Battle.Models;

/// <summary>
/// BattleConstants はバトル固有の定数を定義します
/// </summary>
public static class BattleConstants
{
    public const int InitialBudget = 5000;
    public const int InitialInsightPool = 0;
    public const int InitialHandSize = 5;
    public const int HandLimit = 6;
    public const int InitialTimeBank = 480;
    public const int SlotsPerZone = 3;

    public const int MaxTurns = 30;
    public const int MaxChainLevel = 3;
    public const int LaunchFailureTurn = 3;

    /// <summary>
    /// RankMultiplier はランクに応じた倍率を返します
    /// </summary>
    public static long RankMultiplier(Rank? rank) => rank switch
    {
        Rank.Small => 1,
        Rank.Medium => 2,
        Rank.Large => 3,
        _ => 1
    };

    /// <summary>
    /// FamilyMultiplier はインスタンスファミリーに応じた TP/AV 倍率を返します
    /// </summary>
    public static (double TpMult, double AvMult) FamilyMultiplier(InstanceFamily family) => family switch
    {
        InstanceFamily.M => (1.0, 1.0),
        InstanceFamily.C => (1.3, 0.7),
        InstanceFamily.R => (0.7, 1.3),
        _ => (1.0, 1.0)
    };
}
