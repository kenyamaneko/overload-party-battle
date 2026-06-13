namespace OverloadParty.Battle.Models;

/// <summary>
/// DeckSnapshotCard はデッキスナップショット内の 1 枚のカード情報を保持します
/// </summary>
public class DeckSnapshotCard
{
    public string CardId { get; set; } = "";
    public long ArtNo { get; set; }
}

/// <summary>
/// DeckSnapshot はデッキ全体のスナップショットを保持します
/// </summary>
public class DeckSnapshot
{
    public string DeckID { get; set; } = "";

    /// <summary>デッキが宣言した陣営。プレイヤーが使用できるプロダクト (施策) を規定します。</summary>
    public string Faction { get; set; } = "";

    public List<DeckSnapshotCard> Cards { get; set; } = [];
}
