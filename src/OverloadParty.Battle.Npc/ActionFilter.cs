using System.Linq;
using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Npc;

/// <summary>
/// ActionFilter は NPC アクションのフィルタリングと選択のヘルパー関数を提供します
/// </summary>
public static class ActionFilter
{
    /// <summary>
    /// 指定アクションタイプに一致するものだけを抽出します。
    /// </summary>
    /// <param name="actions">フィルタ対象のアクション列。</param>
    /// <param name="actionType">抽出するアクションタイプ。</param>
    /// <returns>該当アクションのみのリスト。</returns>
    public static List<AvailableAction> FilterByType(List<AvailableAction> actions, string actionType)
    {
        return actions.Where(a => a.Type == actionType).ToList();
    }

    /// <summary>
    /// Selects the best zone from validZones based on card type strategy.
    /// </summary>
    /// <param name="validZones">エンジンが許可する配置先ゾーン候補。</param>
    /// <param name="cardDef">配置するカード定義。</param>
    /// <param name="usedZones">既に使用済みのゾーン集合。</param>
    /// <returns>選択したゾーン文字列。該当なしなら null。</returns>
    public static string? PickBestZone(List<string>? validZones, CardDefinition cardDef, HashSet<string> usedZones)
    {
        if (validZones is null || validZones.Count == 0) { return null; }
        var available = FilterZones(validZones, usedZones);
        if (available.Count == 0) { return null; }

        if (cardDef.IsComputeType)
        {
            var frontend = FirstWithPrefix(available, "frontend_");
            if (frontend is not null) { return frontend; }
            var backend = FirstWithPrefix(available, "backend_");
            if (backend is not null) { return backend; }
            throw new InvalidOperationException(
                $"PickBestZone: Compute card '{cardDef.CardId}' received validZones with no frontend/backend: [{string.Join(", ", available)}]");
        }

        if (cardDef.IsDataType && cardDef.Subtype == "ObjectStorage")
        {
            var backend = FirstWithPrefix(available, "backend_");
            if (backend is not null) { return backend; }
            var frontend = FirstWithPrefix(available, "frontend_");
            if (frontend is not null) { return frontend; }
            throw new InvalidOperationException(
                $"PickBestZone: ObjectStorage card '{cardDef.CardId}' received validZones with no frontend/backend: [{string.Join(", ", available)}]");
        }

        // Data / Support / Incident / Strategy: AvailableActions emits exactly one zone kind; any available entry is valid.
        return available[0];
    }

    /// <summary>
    /// 未使用のサポートゾーン枠を 1 つ選択します。
    /// </summary>
    /// <param name="validZones">エンジンが許可する配置先ゾーン候補。</param>
    /// <param name="usedZones">既に使用済みのゾーン集合。</param>
    /// <returns>選択したサポートゾーン文字列。該当なしなら null。</returns>
    public static string? PickSupportZone(List<string>? validZones, HashSet<string> usedZones)
    {
        return validZones?.FirstOrDefault(z => z.StartsWith("support_") && !usedZones.Contains(z));
    }

    /// <summary>
    /// "frontend_0" 形式のゾーン文字列をゾーン名とインデックスに分解します。
    /// </summary>
    /// <param name="zone">パース対象のゾーン文字列。</param>
    /// <returns>解析結果。形式不正なら null。</returns>
    public static SlotPosition? ParseZoneStr(string zone)
    {
        int idx = zone.LastIndexOf('_');
        if (idx < 0)
        {
            return null;
        }
        if (!int.TryParse(zone[(idx + 1)..], out int index))
        {
            return null;
        }
        return new SlotPosition { Zone = zone[..idx], Index = index };
    }

    /// <summary>
    /// フィールド上の InstanceID からカード ID を解決します。
    /// </summary>
    /// <param name="instanceId">対象の InstanceID。</param>
    /// <param name="field">検索対象のフィールド。</param>
    /// <returns>該当カードの CardID。</returns>
    public static string ResolveCardIdForInstance(string instanceId, Field field)
    {
        var resource = FieldHelpers.AllResources(field).FirstOrDefault(r => r.InstanceID == instanceId);
        if (resource is not null)
        {
            return resource.CardID;
        }

        var support = field.Support.FirstOrDefault(s => s.InstanceID == instanceId);
        if (support is not null)
        {
            return support.CardID;
        }

        throw new InvalidOperationException($"Instance '{instanceId}' not found on field");
    }

    private static List<string> FilterZones(List<string> validZones, HashSet<string> usedZones)
    {
        return validZones.Where(z => !usedZones.Contains(z)).ToList();
    }

    private static string? FirstWithPrefix(List<string> zones, string prefix)
    {
        return zones.FirstOrDefault(z => z.StartsWith(prefix));
    }
}

public class SlotPosition
{
    public string Zone { get; init; } = "";
    public int Index { get; init; }
}
