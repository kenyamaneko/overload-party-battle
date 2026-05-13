namespace OverloadParty.Battle.Models;

/// <summary>
/// 対戦当時の player 表示情報スナップショット (match 成立時点の値、試合中不変)。
/// battle は player_id を知らず、外部から渡される name / level を信頼して保持する。
/// </summary>
public class PlayerSummarySnapshot
{
    public long PlayerNum { get; set; }
    public string Name { get; set; } = "";
    public long Level { get; set; }
}
