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

    /// <summary>セットしたルーチン施策の ID。</summary>
    public string RoutineId { get; set; } = "";

    /// <summary>セットしたスペシャル施策の ID。</summary>
    public string SpecialId { get; set; } = "";

    public List<DeckSnapshotCard> Cards { get; set; } = [];
}
