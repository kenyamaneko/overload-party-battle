using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Npc;

public class NpcDeckDefinition
{
    public string Name { get; init; } = "";
    public string Faction { get; init; } = "";
    public long[] Cards { get; init; } = [];
}

public static class NpcDecks
{
    public static readonly NpcDeckDefinition SDDeck = new()
    {
        Name = "SD Standard",
        Faction = GameConstants.FactionSD,
        Cards = [
            1, 1, 1, 3, 6, 6, 7, 7, 7, 8,
            8, 8, 9, 9, 13, 13, 15, 17, 20, 20,
            20, 98, 99, 100, 101, 101, 115, 118, 119, 121,
        ],
    };

    public static readonly NpcDeckDefinition TenkiDeck = new()
    {
        Name = "Tenki Standard",
        Faction = GameConstants.FactionTenki,
        Cards = [
            23, 23, 26, 26, 26, 27, 27, 29, 29, 29,
            31, 32, 32, 32, 35, 35, 37, 38, 38, 38,
            41, 46, 46, 94, 98, 99, 100, 101, 101, 117,
        ],
    };

    public static readonly NpcDeckDefinition SugarDeck = new()
    {
        Name = "Sugar Standard",
        Faction = GameConstants.FactionSugar,
        Cards = [
            47, 47, 47, 48, 48, 48, 49, 49, 50, 50,
            51, 51, 52, 55, 56, 58, 58, 60, 60, 61,
            62, 62, 66, 98, 99, 100, 104, 104, 104, 106,
        ],
    };

    public static readonly NpcDeckDefinition TunersDeck = new()
    {
        Name = "Tuners Standard",
        Faction = GameConstants.FactionTuners,
        Cards = [
            70, 70, 70, 72, 74, 74, 74, 76, 76, 77,
            77, 77, 78, 79, 79, 81, 82, 84, 85, 86,
            86, 86, 89, 89, 90, 90, 100, 101, 101, 101,
        ],
    };

    public static readonly NpcDeckDefinition DevDeck = new()
    {
        Name = "Dev Test Deck",
        Faction = "Mixed",
        Cards = [
            1, 1, 1, 2, 2, 2, 3, 3, 3, 4, 4, 5,
            6, 6, 6, 7, 7, 8, 8, 8, 1, 1, 1, 2,
            2, 2, 3, 3, 3, 3,
        ],
    };

    public static readonly Dictionary<string, NpcDeckDefinition> Decks = new()
    {
        [GameConstants.FactionSD] = SDDeck,
        [GameConstants.FactionTenki] = TenkiDeck,
        [GameConstants.FactionSugar] = SugarDeck,
        [GameConstants.FactionTuners] = TunersDeck,
    };

    public static NpcDeckDefinition? GetDeck(string faction)
        => Decks.GetValueOrDefault(faction);
}
