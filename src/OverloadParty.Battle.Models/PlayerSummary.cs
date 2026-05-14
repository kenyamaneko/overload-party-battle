namespace OverloadParty.Battle.Models;

/// <summary>
/// 対戦当時の player の name / level snapshot (battle 開始時点の値、試合中不変)。
/// battle は player_id を知らず、外部から渡される値を信頼して保持する。
/// NPC のように level を持たない player では Level は null。
/// </summary>
public class PlayerSummarySnapshot
{
    public long PlayerNum { get; set; }
    public string Name { get; set; } = "";
    public long? Level { get; set; }
}
