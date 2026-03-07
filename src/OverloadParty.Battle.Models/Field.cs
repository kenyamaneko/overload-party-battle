namespace OverloadParty.Battle.Models;

/// <summary>
/// Field represents one player's complete field layout. Each zone has 3 slots.
/// </summary>
public class Field
{
    public Zone<ResourceInstance> Frontend { get; set; } = new();
    public Zone<ResourceInstance> Backend { get; set; } = new();
    public Zone<SupportInstance> Support { get; set; } = new();
}

/// <summary>
/// ResourceInstance is a card deployed on the frontend or backend zone.
/// </summary>
public class ResourceInstance
{
    public string InstanceID { get; set; } = "";
    public long CardID { get; set; }
    public long ArtNo { get; set; }
    public Rank? Rank { get; set; }
    public InstanceFamily? InstanceFamily { get; set; }
    public bool FaceUp { get; set; }
    public long DeployingTurnsLeft { get; set; }
    public long CurrentAV { get; set; }
    public long MaxAV { get; set; }
    public long? CurrentTP { get; set; }
    public long? MaxTP { get; set; }
    public long? CurrentYield { get; set; }
    public long? MaxYield { get; set; }
    public long Damage { get; set; }
    public List<AttachmentRef> Attachments { get; set; } = [];
    public List<TemporaryEffect> TemporaryEffects { get; set; } = [];
    public long MonetizedAmount { get; set; }
    public bool HasAttacked { get; set; }
    public bool EffectUsedThisTurn { get; set; }
    public bool ScaleChangedThisTurn { get; set; }
    public long DeployedOnTurn { get; set; }
    public long DeployOrder { get; set; }
    public string? MigratingFrom { get; set; }
    public string? MigrationTarget { get; set; }
    public long MigratingOnTurn { get; set; }
    public long ElasticBonus { get; set; }

    /// <summary>
    /// Effective AV = MaxAV - Damage. Can go below zero.
    /// </summary>
    public long EffectiveAV => MaxAV - Damage;
}

/// <summary>
/// AttachmentRef references an attachment card on a resource.
/// </summary>
public class AttachmentRef
{
    public string InstanceID { get; set; } = "";
    public long CardID { get; set; }
    public long ArtNo { get; set; }
}

/// <summary>
/// TemporaryEffect is a time-limited modifier on a resource.
/// </summary>
public class TemporaryEffect
{
    public string EffectType { get; set; } = "";
    public long Value { get; set; }
    public string Duration { get; set; } = "";
    public string SourceID { get; set; } = "";
}

/// <summary>
/// SupportInstance is a Platform or Reactive card in the support zone.
/// </summary>
public class SupportInstance
{
    public string InstanceID { get; set; } = "";
    public long CardID { get; set; }
    public long ArtNo { get; set; }
    public bool FaceUp { get; set; }
    public long DeployingTurnsLeft { get; set; }
    public long DeployOrder { get; set; }
    public bool EffectUsedThisTurn { get; set; }
}

/// <summary>
/// HandCard represents a card in a player's hand.
/// </summary>
public class HandCard
{
    public string InstanceID { get; set; } = "";
    public long CardID { get; set; }
    public long ArtNo { get; set; }
}

/// <summary>
/// ChainEntry is one entry in the chain stack.
/// </summary>
public class ChainEntry
{
    public long ChainLevel { get; set; }
    public string ActionType { get; set; } = "";
    public string SourcePlayerID { get; set; } = "";
    public string SourceInstanceID { get; set; } = "";
    public string TargetInstanceID { get; set; } = "";
    public long TargetChainLevel { get; set; }
    public Dictionary<string, object>? EffectData { get; set; }
    public bool Resolved { get; set; }
}
