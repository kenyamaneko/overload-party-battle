namespace OverloadParty.Battle.Models;

public static class GameConstants
{
    // Initial values
    public const long InitialBudget = 5000;
    public const long InitialInsightPool = 0;
    public const int InitialHandSize = 5;
    public const int HandLimit = 6;
    public const long InitialTimeBank = 480;
    public const int DeckSize = 30;
    public const int MaxAttachments = 2;
    public const long PerTurnBudget = 500;
    public const int SlotsPerZone = 3;

    // Level / XP
    public const int ExpWin = 40;
    public const int ExpLoss = 20;
    public const int ExpDraw = 30;

    // Game limits
    public const int MaxTurns = 30;
    public const int MaxChainLevel = 3;
    public const int LaunchFailureTurn = 3;

    // Factions
    public const string FactionSD = "SD";
    public const string FactionTenki = "Tenki";
    public const string FactionSugar = "Sugar";
    public const string FactionTuners = "Tuners";

    // Zones (wire format)
    public const string ZoneFrontend = "frontend";
    public const string ZoneBackend = "backend";

    // Rank multipliers
    public static long RankMultiplier(Rank rank) => rank switch
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
        InstanceFamily.C => (1.5, 0.75),
        InstanceFamily.R => (0.75, 1.5),
        _ => (1.0, 1.0)
    };
}
