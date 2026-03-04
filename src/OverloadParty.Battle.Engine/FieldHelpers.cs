using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine;

/// <summary>
/// Utility methods for searching and manipulating fields.
/// </summary>
public static class FieldHelpers
{
    /// <summary>
    /// Find a resource on the field by InstanceID. Returns (resource, zone) or null.
    /// </summary>
    public static (ResourceInstance Resource, Zone Zone)? FindResourceByID(Field field, string instanceID)
    {
        var fr = field.Frontend.FirstOrDefault(r => r.InstanceID == instanceID);
        if (fr is not null) return (fr, Zone.Frontend);

        var br = field.Backend.FirstOrDefault(r => r.InstanceID == instanceID);
        if (br is not null) return (br, Zone.Backend);

        return null;
    }

    /// <summary>
    /// Find a support instance by InstanceID.
    /// </summary>
    public static SupportInstance? FindSupportByID(Field field, string instanceID)
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
    public static bool HasTemporaryEffect(ResourceInstance resource, string effectType)
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
    /// Enumerate all face-up resources on the field (frontend + backend).
    /// </summary>
    public static IEnumerable<ResourceInstance> AllFaceUpResources(Field field)
    {
        return field.Frontend.Concat(field.Backend).Where(r => r.FaceUp);
    }

    /// <summary>
    /// Enumerate all resources on the field (including face-down).
    /// </summary>
    public static IEnumerable<ResourceInstance> AllResources(Field field)
    {
        return field.Frontend.Concat(field.Backend);
    }

    /// <summary>
    /// Enumerate all support instances.
    /// </summary>
    public static IEnumerable<SupportInstance> AllSupports(Field field)
    {
        return field.Support;
    }

    /// <summary>
    /// Check card type eligibility for frontend zone.
    /// </summary>
    public static bool IsFrontendEligible(string cardType)
    {
        return cardType is "Compute" or "Container" or "Orchestrator" or "Serverless" or "AI/ML" or "ObjectStorage";
    }

    /// <summary>
    /// Check card type eligibility for backend zone.
    /// </summary>
    public static bool IsBackendEligible(string cardType)
    {
        return cardType is "Compute" or "Container" or "Orchestrator" or "Serverless" or "AI/ML"
            or "Database" or "ObjectStorage" or "CacheDB" or "Datawarehouse";
    }

    /// <summary>
    /// Check if a card type belongs to the support category.
    /// </summary>
    public static bool IsSupportType(string cardType)
    {
        return cardType is "Platform" or "Reactive" or "Strategy" or "Incident" or "Attachment";
    }

    /// <summary>
    /// Check if a card type is immediate (resolves on play, then removed).
    /// </summary>
    public static bool IsImmediateType(string cardType)
    {
        return cardType is "Strategy" or "Incident";
    }

    /// <summary>
    /// Check if card is a compute type.
    /// </summary>
    public static bool IsComputeType(string cardType)
    {
        return cardType is "Compute" or "Container" or "Orchestrator" or "Serverless" or "AI/ML";
    }

    /// <summary>
    /// Check if card is a data type.
    /// </summary>
    public static bool IsDataType(string cardType)
    {
        return cardType is "Database" or "ObjectStorage" or "CacheDB" or "Datawarehouse";
    }

    /// <summary>
    /// Add a card to the player's trash.
    /// </summary>
    public static void AddToTrash(GameState state, long playerNum, long cardID, string instanceID)
    {
        var trash = state.GetTrash(playerNum);
        trash.Add(new HandCard { InstanceID = instanceID, CardID = cardID });
    }

    /// <summary>
    /// Create a ResourceInstance from a card definition.
    /// </summary>
    public static ResourceInstance CreateResourceInstance(CardDefinition card, string instanceID, long deployTurn)
    {
        var resource = new ResourceInstance
        {
            InstanceID = instanceID,
            CardID = card.CardNo,
            Rank = Rank.Small,
            FaceUp = card.DeployTurns <= 0,
            DeployingTurnsLeft = card.DeployTurns,
            DeployedOnTurn = deployTurn,
        };

        // Set initial stats based on card type category
        if (card.IsComputeType && card.ComputeStats is { } cs)
        {
            resource.MaxAV = cs.Availability;
            resource.CurrentAV = cs.Availability;
            resource.MaxTP = cs.Throughput;
            resource.CurrentTP = cs.Throughput;
        }
        else if (card.IsDataType && card.DataStats is { } ds)
        {
            resource.MaxAV = ds.Availability;
            resource.CurrentAV = ds.Availability;
            resource.MaxYield = ds.Yield;
            resource.CurrentYield = ds.Yield;
        }

        return resource;
    }

    /// <summary>
    /// Clear migration links when a migration source is destroyed.
    /// </summary>
    public static void ClearMigrationOnSourceDestroyed(Field field, ResourceInstance destroyed)
    {
        if (destroyed.MigrationTarget is null) return;

        // Find the target and clear its MigratingFrom
        var targetResult = FindResourceByID(field, destroyed.MigrationTarget);
        if (targetResult is { } t)
        {
            t.Resource.MigratingFrom = null;
            t.Resource.MigratingOnTurn = 0;
        }
    }
}
