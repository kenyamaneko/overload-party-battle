namespace OverloadParty.Battle.Models;

public class DeckSnapshotCard
{
    public string CardId { get; set; } = "";
    public long ArtNo { get; set; }
}

public class DeckSnapshot
{
    public string DeckID { get; set; } = "";
    public List<DeckSnapshotCard> Cards { get; set; } = [];
}
