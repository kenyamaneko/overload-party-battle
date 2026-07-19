using OverloadParty.Battle.Models;
using OverloadParty.Battle.Engine.Effects;

namespace OverloadParty.Battle.Engine.Helpers;

/// <summary>
/// フィールド上のリソース・サポートの検索・問い合わせを扱うヘルパー。
/// </summary>
public static class FieldHelpers
{
    /// <summary>
    /// Find a resource on the field by InstanceID.
    /// </summary>
    /// <param name="field">対象フィールド。</param>
    /// <param name="instanceID">探すリソースのインスタンス ID。</param>
    /// <returns>該当リソース。見つからなければ null。</returns>
    public static DeployedResource? FindResourceByID(Field field, string instanceID)
    {
        return field.Frontend.FirstOrDefault(r => r.InstanceID == instanceID)
            ?? field.Backend.FirstOrDefault(r => r.InstanceID == instanceID);
    }

    /// <summary>
    /// Find the zone (Frontend/Backend) of a resource by InstanceID.
    /// </summary>
    /// <param name="field">対象フィールド。</param>
    /// <param name="instanceID">探すリソースのインスタンス ID。</param>
    /// <returns>所属ゾーン。見つからなければ null。</returns>
    public static Zone? FindResourceZone(Field field, string instanceID)
    {
        if (field.Frontend.Any(r => r.InstanceID == instanceID)) { return Zone.Frontend; }
        if (field.Backend.Any(r => r.InstanceID == instanceID)) { return Zone.Backend; }
        return null;
    }

    /// <summary>
    /// Find a support instance by InstanceID.
    /// </summary>
    /// <param name="field">対象フィールド。</param>
    /// <param name="instanceID">探すサポートのインスタンス ID。</param>
    /// <returns>該当サポート。見つからなければ null。</returns>
    public static DeployedSupport? FindSupportByID(Field field, string instanceID)
    {
        return field.Support.FirstOrDefault(s => s.InstanceID == instanceID);
    }

    /// <summary>
    /// Check if a field has any face-up resources in the frontend zone.
    /// </summary>
    /// <param name="field">対象フィールド。</param>
    /// <returns>フロントエンドに表向きリソースがあれば true。</returns>
    public static bool HasFrontendResources(Field field)
    {
        return field.Frontend.Any(r => r.FaceUp);
    }

    /// <summary>
    /// Check if a field has any face-up resources in frontend or backend.
    /// </summary>
    /// <param name="field">対象フィールド。</param>
    /// <returns>表向きリソースが 1 体以上あれば true。</returns>
    public static bool HasAnyActiveResources(Field field)
    {
        return field.Frontend.Concat(field.Backend).Any(r => r.FaceUp);
    }

    /// <summary>
    /// Check if a resource has a specific temporary effect.
    /// </summary>
    /// <param name="resource">対象リソース。</param>
    /// <param name="effectType">判定する一時効果の種別。</param>
    /// <returns>該当する一時効果を持っていれば true。</returns>
    public static bool HasTemporaryEffect(DeployedResource resource, string effectType)
    {
        return resource.TemporaryEffects.Any(e => e.EffectType == effectType);
    }

    /// <summary>
    /// Remove a resource from the field by InstanceID. Returns true if found and removed.
    /// </summary>
    /// <param name="field">対象フィールド。</param>
    /// <param name="instanceID">除去するリソースのインスタンス ID。</param>
    /// <returns>除去に成功すれば true。</returns>
    public static bool RemoveResourceFromField(Field field, string instanceID)
    {
        return field.Frontend.Remove(r => r.InstanceID == instanceID)
            || field.Backend.Remove(r => r.InstanceID == instanceID);
    }

    /// <summary>
    /// Remove a support card from the field by InstanceID. Returns true if found and removed.
    /// </summary>
    /// <param name="field">対象フィールド。</param>
    /// <param name="instanceID">除去するサポートのインスタンス ID。</param>
    /// <returns>除去に成功すれば true。</returns>
    public static bool RemoveSupportFromField(Field field, string instanceID)
    {
        return field.Support.Remove(s => s.InstanceID == instanceID);
    }

    /// <summary>
    /// フィールド上の表向きリソースをすべて返す (frontend + backend)。
    /// </summary>
    /// <param name="field">対象フィールド。</param>
    /// <returns>表向きリソースの即時評価リスト。</returns>
    /// <remarks>
    /// 呼び出し元でフィールド状態が変更される可能性があるため、
    /// 遅延実行せず即時評価して結果を確定させる。
    /// </remarks>
    public static List<DeployedResource> AllFaceUpResources(Field field)
    {
        return field.Frontend.Concat(field.Backend).Where(r => r.FaceUp).ToList();
    }

    /// <summary>
    /// フィールド上の全リソースを返す (face-down 含む)。
    /// </summary>
    /// <param name="field">対象フィールド。</param>
    /// <returns>全リソースの即時評価リスト。</returns>
    /// <remarks>
    /// 呼び出し元でフィールド状態が変更される可能性があるため、
    /// 遅延実行せず即時評価して結果を確定させる。
    /// </remarks>
    public static List<DeployedResource> AllResources(Field field)
    {
        return field.Frontend.Concat(field.Backend).ToList();
    }

    /// <summary>
    /// サポートインスタンスをすべて返す。
    /// </summary>
    /// <param name="field">対象フィールド。</param>
    /// <returns>サポートインスタンスの即時評価リスト。</returns>
    /// <remarks>
    /// 呼び出し元でフィールド状態が変更される可能性があるため、
    /// 遅延実行せず即時評価して結果を確定させる。
    /// </remarks>
    public static List<DeployedSupport> AllSupports(Field field)
    {
        return field.Support.ToList();
    }

    /// <summary>
    /// イベントトリガーの候補になり得るサポートインスタンスを返す (デプロイターンが残っているものを除く)。
    /// </summary>
    /// <param name="field">対象フィールド。</param>
    /// <returns>デプロイターンが残っていないサポートインスタンスの即時評価リスト。</returns>
    /// <remarks>
    /// 呼び出し元でフィールド状態が変更される可能性があるため、
    /// 遅延実行せず即時評価して結果を確定させる。
    /// </remarks>
    public static List<DeployedSupport> AllTriggerableSupports(Field field)
    {
        return field.Support.Where(s => s.DeployingTurnsLeft <= 0).ToList();
    }

    /// <summary>
    /// Check card eligibility for frontend zone (Compute 全般 + Data の ObjectStorage subtype のみ)。
    /// </summary>
    /// <param name="cardType">判定対象のカードタイプ。</param>
    /// <param name="subtype">判定対象のサブタイプ。</param>
    /// <returns>フロントエンドに配置可能なら true。</returns>
    public static bool IsFrontendEligible(string cardType, string? subtype)
    {
        return cardType == CardTypes.Compute
            || (cardType == CardTypes.DataResource && subtype == "ObjectStorage");
    }

    /// <summary>
    /// Check card eligibility for backend zone (Compute と Data 全般)。
    /// </summary>
    /// <param name="cardType">判定対象のカードタイプ。</param>
    /// <returns>バックエンドに配置可能なら true。</returns>
    public static bool IsBackendEligible(string cardType)
    {
        return cardType is CardTypes.Compute or CardTypes.DataResource;
    }

    /// <summary>
    /// Check if a card type belongs to the support category.
    /// </summary>
    /// <param name="cardType">判定対象のカードタイプ。</param>
    /// <returns>サポートカードに属するなら true。</returns>
    public static bool IsSupportType(string cardType)
    {
        return cardType is CardTypes.Platform or CardTypes.Reactive or CardTypes.Strategy
                        or CardTypes.Incident or CardTypes.Attachment;
    }

    /// <summary>
    /// Check if a card type is immediate (resolves on play, then removed).
    /// </summary>
    /// <param name="cardType">判定対象のカードタイプ。</param>
    /// <returns>即時解決型 (ストラテジー / インシデント) なら true。</returns>
    public static bool IsImmediateType(string cardType)
    {
        return cardType is CardTypes.Strategy or CardTypes.Incident;
    }

    /// <summary>
    /// Check if card is a compute type.
    /// </summary>
    /// <param name="cardType">判定対象のカードタイプ。</param>
    /// <returns>Compute系リソースなら true。</returns>
    public static bool IsComputeType(string cardType)
    {
        return cardType == CardTypes.Compute;
    }

    /// <summary>
    /// Check if card is a data type.
    /// </summary>
    /// <param name="cardType">判定対象のカードタイプ。</param>
    /// <returns>DB系リソースなら true。</returns>
    public static bool IsDataResource(string cardType)
    {
        return cardType == CardTypes.DataResource;
    }

    /// <summary>
    /// サポートカードを破壊してトラッシュに移動し、盤面変化に伴うパッシブ効果を再計算する。
    /// </summary>
    /// <param name="state">現在のゲーム状態。</param>
    /// <param name="game">対象ゲームのメタデータ。</param>
    /// <param name="ownerNum">サポートカードの所有プレイヤー番号。</param>
    /// <param name="field">対象フィールド。</param>
    /// <param name="instanceID">破壊するサポートのインスタンス ID。</param>
    /// <param name="cc">カード定義キャッシュ。</param>
    /// <param name="effects">効果ハンドラのレジストリ。</param>
    /// <returns>破壊に成功すれば true。</returns>
    public static bool DestroySupport(
        BattleGameState state, Game game, long ownerNum, Field field, string instanceID,
        ICardCache cc, IEffectRegistry effects)
    {
        var support = field.Support.FirstOrDefault(s => s.InstanceID == instanceID);
        if (support is null)
        {
            return false;
        }

        CardMoveHelpers.AddToTrash(state, ownerNum, support.CardID, support.InstanceID, support.ArtNo);
        field.Support.Remove(s => s.InstanceID == instanceID);

        PassiveRecalculator.Recalculate(state, game, cc, effects);
        return true;
    }

    /// <summary>
    /// Checks whether a resource is protected by a target_shield attachment
    /// (e.g. Load Balancer) and has other face-up frontends on the same field.
    /// </summary>
    /// <param name="resource">判定対象のリソース。</param>
    /// <param name="field">リソースが置かれているフィールド。</param>
    /// <param name="cc">カード定義キャッシュ。</param>
    /// <returns>シールドで保護されていれば true。</returns>
    public static bool IsTargetShielded(DeployedResource resource, Field field, ICardCache cc)
    {
        bool hasShieldAttachment = field.Support
            .Where(a => a.TargetInstanceID == resource.InstanceID)
            .Any(a => cc.Get(a.CardID)?.Effects?.Any(e => e.Custom == CustomEffects.TargetShield) == true);
        if (!hasShieldAttachment) { return false; }

        return field.Frontend.Any(r => r.InstanceID != resource.InstanceID && r.FaceUp);
    }

    /// <summary>
    /// Applies reduction from temporary effects with the given type, supporting both flat and percent modes.
    /// Flat values are subtracted first, then percent values reduce the remainder.
    /// </summary>
    /// <param name="effects">適用対象の一時効果リスト。</param>
    /// <param name="effectType">軽減を引き起こす効果種別。</param>
    /// <param name="baseValue">軽減前の基準値。</param>
    /// <returns>軽減を適用した結果値。</returns>
    public static long ApplyReduction(List<TemporaryEffect> effects, string effectType, long baseValue)
    {
        long flatSum = effects
            .Where(e => e.EffectType == effectType && e.Mode is not BuffModes.Percent)
            .Sum(e => e.Value);
        long afterFlat = Math.Max(0, baseValue - flatSum);

        long percentSum = effects
            .Where(e => e.EffectType == effectType && e.Mode is BuffModes.Percent)
            .Sum(e => e.Value);
        long clamped = Math.Clamp(percentSum, 0, 100);

        return afterFlat * (100 - clamped) / 100;
    }
}
