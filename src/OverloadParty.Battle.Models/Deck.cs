namespace OverloadParty.Battle.Models;

public class OwnedCard
{
    public string PlayerID { get; set; } = "";
    public long CardNo { get; set; }
    public long ArtNo { get; set; }
    public int Count { get; set; }
}

public class Deck
{
    public string PlayerID { get; set; } = "";
    public long DeckID { get; set; }
    public string DeckName { get; set; } = "";
    public bool IsValid { get; set; }
    public long? PlaymatNo { get; set; }
    public long? SleeveNo { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<long>? CardNos { get; set; }
}

public class DeckCard
{
    public string PlayerID { get; set; } = "";
    public long DeckID { get; set; }
    public long CardNo { get; set; }
    public long ArtNo { get; set; }
    public int Count { get; set; }
}

public class DeckSnapshotCard
{
    public long CardNo { get; set; }
    public long ArtNo { get; set; }
}

public class DeckSnapshot
{
    public string DeckID { get; set; } = "";
    public List<DeckSnapshotCard> Cards { get; set; } = [];
}
