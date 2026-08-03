namespace OverloadParty.Battle.Models;

/// <summary>
/// プレイヤーに紐付いてゲーム中持ち越される状態。場のカード個体では表せないものを収める。
/// </summary>
public class PlayerStatus
{
    /// <summary>
    /// 1 ゲーム 1 回の効果を使い終えたカードの ID。カード個体ではなくカード名単位で数えるため、
    /// 使用済みのカードが場を離れても同名の別コピーは使用できない。
    /// </summary>
    public List<string> UsedOncePerGameCardIds { get; set; } = [];
}
