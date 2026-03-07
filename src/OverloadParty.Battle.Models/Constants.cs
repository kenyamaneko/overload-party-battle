namespace OverloadParty.Battle.Models;

// Generated constants are in GameConstants_gen.cs
public static partial class GameConstants
{
    // Game limits (not in shared constants.json)
    public const int MaxTurns = 30;
    public const int MaxChainLevel = 3;
    public const int LaunchFailureTurn = 3;

    // Stat types (not in shared constants.json)
    public const string StatTP = "tp";
    public const string StatYield = "yield";
    public const string StatAV = "av";

    // Rank multipliers
    public static long RankMultiplier(Rank? rank) => rank switch
    {
        Rank.Small => 1,
        Rank.Medium => 2,
        Rank.Large => 3,
        _ => 1
    };

    // Instance family multipliers (TP, AV)
    public static (double TpMult, double AvMult) FamilyMultiplier(InstanceFamily family) => family switch
    {
        InstanceFamily.M => (1.0, 1.0),
        InstanceFamily.C => (1.3, 0.7),
        InstanceFamily.R => (0.7, 1.3),
        _ => (1.0, 1.0)
    };
}
