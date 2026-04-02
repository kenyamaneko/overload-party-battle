using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Npc;

public class NpcDeckDefinition
{
    public string Name { get; init; } = "";
    public string Faction { get; init; } = "";
    public DeckSnapshotCard[] Cards { get; init; } = [];
}

public static class NpcDecks
{
    /// <summary>
    /// Shorthand: create DeckSnapshotCard array from card IDs (ArtNo = 0).
    /// </summary>
    private static DeckSnapshotCard[] C(params string[] cardIds)
        => cardIds.Select(n => new DeckSnapshotCard { CardId = n }).ToArray();

    public static readonly NpcDeckDefinition SHEDeck = new()
    {
        Name = "SHE Standard",
        Faction = Factions.SHE,
        Cards = C(
            "SH-0001", "SH-0001", "SH-0001", "SH-0002", "SH-0005", "SH-0005", "SH-0006", "SH-0006", "SH-0006", "SH-0007",
            "SH-0007", "SH-0007", "SH-0008", "SH-0008", "SH-0012", "SH-0012", "SH-0014", "SH-0016", "SH-0019", "SH-0019",
            "SH-0019", "NT-0007", "NT-0008", "NT-0009", "NT-0010", "NT-0010", "NT-0023", "SH-0022", "NT-0025", "SH-0023"),
    };

    public static readonly NpcDeckDefinition TenkiDeck = new()
    {
        Name = "Tenki Standard",
        Faction = Factions.Tenki,
        Cards = C(
            "TK-0001", "TK-0001", "TK-0004", "TK-0004", "TK-0004", "TK-0005", "TK-0005", "TK-0007", "TK-0007", "TK-0007",
            "TK-0009", "TK-0010", "TK-0010", "TK-0010", "TK-0013", "TK-0013", "TK-0014", "TK-0015", "TK-0015", "TK-0015",
            "TK-0018", "TK-0024", "TK-0024", "NT-0004", "NT-0007", "NT-0008", "NT-0009", "NT-0010", "NT-0010", "NT-0024"),
    };

    public static readonly NpcDeckDefinition SugarDeck = new()
    {
        Name = "Sugar Standard",
        Faction = Factions.Sugar,
        Cards = C(
            "SL-0001", "SL-0001", "SL-0001", "SL-0002", "SL-0002", "SL-0002", "SL-0003", "SL-0003", "SL-0004", "SL-0004",
            "SL-0006", "SL-0006", "SL-0007", "SL-0009", "SL-0013", "SL-0011", "SL-0011", "SL-0015", "SL-0015", "SL-0016",
            "SL-0017", "SL-0017", "SL-0022", "NT-0007", "NT-0008", "NT-0009", "NT-0013", "NT-0013", "NT-0013", "NT-0015"),
    };

    public static readonly NpcDeckDefinition TunersDeck = new()
    {
        Name = "Tuners Standard",
        Faction = Factions.Tuners,
        Cards = C(
            "TN-0001", "TN-0001", "TN-0001", "TN-0002", "TN-0004", "TN-0004", "TN-0004", "TN-0006", "TN-0006", "TN-0007",
            "TN-0007", "TN-0007", "TN-0008", "TN-0009", "TN-0009", "TN-0010", "TN-0011", "TN-0013", "TN-0014", "TN-0015",
            "TN-0015", "TN-0015", "TN-0017", "TN-0017", "TN-0018", "TN-0018", "NT-0009", "NT-0010", "NT-0010", "NT-0010"),
    };

    public static readonly NpcDeckDefinition DevDeck = new()
    {
        Name = "Dev Test Deck",
        Faction = "Mixed",
        Cards = C(
            "SH-0001", "SH-0001", "SH-0001", "SH-0002", "SH-0002", "SH-0002", "SH-0003", "SH-0003", "SH-0003", "SH-0004", "SH-0004", "SH-0005",
            "SH-0005", "SH-0005", "SH-0005", "SH-0006", "SH-0006", "SH-0007", "SH-0007", "SH-0007", "SH-0001", "SH-0001", "SH-0001", "SH-0002",
            "SH-0002", "SH-0002", "SH-0003", "SH-0003", "SH-0003", "SH-0003"),
    };

    public static readonly Dictionary<string, NpcDeckDefinition> Decks = new()
    {
        [Factions.SHE] = SHEDeck,
        [Factions.Tenki] = TenkiDeck,
        [Factions.Sugar] = SugarDeck,
        [Factions.Tuners] = TunersDeck,
    };

    public static NpcDeckDefinition? GetDeck(string faction)
        => Decks.GetValueOrDefault(faction);
}
