using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Helpers;

/// <summary>
/// フィールド上のリソース・サポートの検索・問い合わせを扱うヘルパー。
/// </summary>
public static class FieldHelpers
{
    /// <summary>
    /// Find a resource on the field by InstanceID.
    /// </summary>
    public static DeployedResource? FindResourceByID(Field field, string instanceID)
    {
        return field.Frontend.FirstOrDefault(r => r.InstanceID == instanceID)
            ?? field.Backend.FirstOrDefault(r => r.InstanceID == instanceID);
    }

    /// <summary>
    /// Find the zone (Frontend/Backend) of a resource by InstanceID.
    /// </summary>
    public static Zone? FindResourceZone(Field field, string instanceID)
    {
        if (field.Frontend.Any(r => r.InstanceID == instanceID)) { return Zone.Frontend; }
        if (field.Backend.Any(r => r.InstanceID == instanceID)) { return Zone.Backend; }
        return null;
    }

    /// <summary>
    /// Find a support instance by InstanceID.
    /// </summary>
    public static DeployedSupport? FindSupportByID(Field field, string instanceID)
    {
        return field.Support.FirstOrDefault(s => s.InstanceID == instanceID);
    }

    /// <summary>
    /// Check if a field has any face-up resources in the frontend zone.
    /// </summary>
    public static bool HasFrontendResources(Field field)
    {
        return field.Frontend.Any(r => r.FaceUp);
    }

    /// <summary>
    /// Check if a field has any face-up resources in frontend or backend.
    /// </summary>
    public static bool HasAnyActiveResources(Field field)
    {
        return field.Frontend.Concat(field.Backend).Any(r => r.FaceUp);
    }

    /// <summary>
    /// Check if a resource has a specific temporary effect.
    /// </summary>
    public static bool HasTemporaryEffect(DeployedResource resource, string effectType)
    {
        return resource.TemporaryEffects.Any(e => e.EffectType == effectType);
    }

    /// <summary>
    /// Remove a resource from the field by InstanceID. Returns true if found and removed.
    /// </summary>
    public static bool RemoveResourceFromField(Field field, string instanceID)
    {
        return field.Frontend.Remove(r => r.InstanceID == instanceID)
            || field.Backend.Remove(r => r.InstanceID == instanceID);
    }

    /// <summary>
    /// Remove a support card from the field by InstanceID. Returns true if found and removed.
    /// </summary>
    public static bool RemoveSupportFromField(Field field, string instanceID)
    {
        return field.Support.Remove(s => s.InstanceID == instanceID);
    }

    /// <summary>
    /// フィールド上の表向きリソースをすべて返す (frontend + backend)。
    /// </summary>
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
    /// <remarks>
    /// 呼び出し元でフィールド状態が変更される可能性があるため、
    /// 遅延実行せず即時評価して結果を確定させる。
    /// </remarks>
    public static List<DeployedSupport> AllSupports(Field field)
    {
        return field.Support.ToList();
    }

    /// <summary>
    /// Check card type eligibility for frontend zone.
    /// </summary>
    public static bool IsFrontendEligible(string cardType)
    {
        return cardType is CardTypes.Compute or CardTypes.Container or CardTypes.Orchestrator or CardTypes.Serverless or CardTypes.AiMl or CardTypes.ObjectStorage;
    }

    /// <summary>
    /// Check card type eligibility for backend zone.
    /// </summary>
    public static bool IsBackendEligible(string cardType)
    {
        return cardType is CardTypes.Compute or CardTypes.Container or CardTypes.Orchestrator or CardTypes.Serverless or CardTypes.AiMl
            or CardTypes.Database or CardTypes.ObjectStorage or CardTypes.CacheDB;
    }

    /// <summary>
    /// Check if a card type belongs to the support category.
    /// </summary>
    public static bool IsSupportType(string cardType)
    {
        return cardType is CardTypes.Platform or CardTypes.Reactive or CardTypes.Strategy or CardTypes.Incident or CardTypes.Attachment;
    }

    /// <summary>
    /// Check if a card type is immediate (resolves on play, then removed).
    /// </summary>
    public static bool IsImmediateType(string cardType)
    {
        return cardType is CardTypes.Strategy or CardTypes.Incident;
    }

    /// <summary>
    /// Check if card is a compute type.
    /// </summary>
    public static bool IsComputeType(string cardType)
    {
        return cardType is CardTypes.Compute or CardTypes.Container or CardTypes.Orchestrator or CardTypes.Serverless or CardTypes.AiMl;
    }

    /// <summary>
    /// Check if card is a data type.
    /// </summary>
    public static bool IsDataType(string cardType)
    {
        return cardType is CardTypes.Database or CardTypes.ObjectStorage or CardTypes.CacheDB;
    }

    /// <summary>
    /// サポートカードを破壊してトラッシュに移動する。
    /// </summary>
    public static bool DestroySupport(GameState state, long ownerNum, Field field, string instanceID)
    {
        var support = field.Support.FirstOrDefault(s => s.InstanceID == instanceID);
        if (support is null)
        {
            return false;
        }

        RemoveWhileOnFieldBuffs(field, support.InstanceID);
        CardMoveHelpers.AddToTrash(state, ownerNum, support.CardID, support.InstanceID, support.ArtNo);
        field.Support.Remove(s => s.InstanceID == instanceID);
        return true;
    }

    /// <summary>
    /// Checks whether a resource is protected by a target_shield attachment
    /// (e.g. Load Balancer) and has other face-up frontends on the same field.
    /// </summary>
    public static bool IsTargetShielded(DeployedResource resource, Field field, ICardCache cc)
    {
        bool hasShieldAttachment = field.Support
            .Where(a => a.TargetInstanceID == resource.InstanceID)
            .Any(a => cc.Get(a.CardID)?.Effects?.Any(e => e.Custom == "target_shield") == true);
        if (!hasShieldAttachment) { return false; }

        return field.Frontend.Any(r => r.InstanceID != resource.InstanceID && r.FaceUp);
    }

    /// <summary>
    /// Removes all <c>while_on_field</c> buffs whose SourceID matches the given instance.
    /// Called when a card leaves the field (destroyed, etc.) to clean up its persistent buffs.
    /// </summary>
    public static void RemoveWhileOnFieldBuffs(Field field, string sourceInstanceID)
    {
        foreach (var resource in AllResources(field))
        {
            resource.TemporaryEffects.RemoveAll(e =>
                e.Duration == "while_on_field" && e.SourceID == sourceInstanceID);
        }
    }

    /// <summary>
    /// Clear migration links when a migration source is destroyed.
    /// </summary>
    public static void ClearMigrationOnSourceDestroyed(Field field, DeployedResource destroyed)
    {
        if (destroyed.MigrationTarget is null) { return; }

        var target = FindResourceByID(field, destroyed.MigrationTarget);
        if (target is not null)
        {
            target.MigratingFrom = null;
            target.MigratingOnTurn = 0;
        }
    }
}
