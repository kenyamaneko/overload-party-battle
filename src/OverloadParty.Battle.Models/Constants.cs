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
    public const int LaunchFailureTurn = 3;

    /// <summary>
    /// ランクに応じたステータス倍率を返す。
    /// </summary>
    /// <param name="rank">対象のランク。非 Resizable カードは Rank を持たないため null。</param>
    /// <returns>ランクに応じた倍率</returns>
    public static long GetRankMultiplier(Rank? rank) => rank switch
    {
        null => 1, // 非 Resizable カードは Rank を持たない固定スペックなので基準値 ×1
        Rank.Small => 1,
        Rank.Medium => 2,
        Rank.Large => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(rank), rank, "unknown rank"),
    };

    /// <summary>
    /// インスタンスファミリーに応じたステータス倍率を返す。
    /// </summary>
    /// <param name="family">対象のインスタンスファミリー</param>
    /// <returns>スループット/Yield 倍率と可用性倍率の組</returns>
    public static (double TpMult, double AvMult) GetFamilyMultiplier(InstanceFamily family) => family switch
    {
        InstanceFamily.M => (1.0, 1.0),
        InstanceFamily.C => (1.3, 0.7),
        InstanceFamily.R => (0.7, 1.3),
        _ => throw new ArgumentOutOfRangeException(nameof(family), family, "unknown instance family"),
    };
}
