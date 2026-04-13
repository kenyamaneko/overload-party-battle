using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Helpers;

/// <summary>
/// カードタイプとゾーンの配置可否を判定する単一の権威。
/// PlayCardProcessor (action validation) と AvailableActions (action enumeration) の
/// 双方から参照され、両者のルール解釈がずれないようにする。
/// </summary>
public static class ZoneValidator
{
    /// <summary>
    /// スロットインデックスがゾーンの容量範囲内か判定する。
    /// Support は動的容量 (Support.Capacity)、Frontend/Backend は BattleConstants.SlotsPerZone に従う。
    /// </summary>
    public static bool IsValidSlotIndex(Field field, string zone, int index)
    {
        return zone switch
        {
            Zones.Frontend or Zones.Backend => index >= 0 && index < BattleConstants.SlotsPerZone,
            Zones.Support => index >= 0 && index < field.Support.Capacity,
            _ => false,
        };
    }

    /// <summary>
    /// カードが指定ゾーンに配置可能か判定する。
    /// スロット占有や attachment の target 有無といったインスタンス固有条件は含まない。
    /// </summary>
    public static bool IsZoneEligible(CardDefinition card, string zone)
    {
        return zone switch
        {
            Zones.Frontend => FieldHelpers.IsFrontendEligible(card.CardType, card.Subtype),
            Zones.Backend => FieldHelpers.IsBackendEligible(card.CardType),
            Zones.Support => FieldHelpers.IsSupportType(card.CardType),
            _ => false,
        };
    }

    /// <summary>
    /// Frontend/Backend の指定スロットが空いているか判定する。
    /// Support は張り替え可 (RULEBOOK §3) のため対象外 — 占有していても配置可。
    /// </summary>
    public static bool IsSlotEmpty(Field field, string zone, int index)
    {
        return zone switch
        {
            Zones.Frontend => field.Frontend[index] is null,
            Zones.Backend => field.Backend[index] is null,
            Zones.Support => true,
            _ => false,
        };
    }
}
