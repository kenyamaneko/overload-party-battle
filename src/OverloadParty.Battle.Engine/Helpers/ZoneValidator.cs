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
    /// <param name="field">対象フィールド。</param>
    /// <param name="zone">対象ゾーン。</param>
    /// <param name="index">スロットインデックス。</param>
    /// <returns>容量範囲内なら true。</returns>
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
    /// <param name="card">判定対象のカード定義。</param>
    /// <param name="zone">配置候補のゾーン。</param>
    /// <returns>そのゾーンに配置可能なら true。</returns>
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
    /// <param name="field">対象フィールド。</param>
    /// <param name="zone">対象ゾーン。</param>
    /// <param name="index">スロットインデックス。</param>
    /// <returns>配置可能と扱える状態なら true。</returns>
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
