namespace OverloadParty.Battle.Models;

public class CardDefinition
{
    public string CardId { get; set; } = "";
    public string CardName { get; set; } = "";
    public string ResourceLabel { get; set; } = "";
    public string Faction { get; set; } = "";
    public string CardType { get; set; } = "";
    public long DeployTurns { get; set; }
    public bool Resizable { get; set; }
    public bool Elastic { get; set; }
    public long ElasticIncrement { get; set; }
    public long FreeTier { get; set; }
    public long CostPerRequest { get; set; }
    public string? EffectText { get; set; }
    public string Restriction { get; set; } = "unlimited";
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Typed stats (one of these is populated based on card type category)
    public ComputeStats? ComputeStats { get; set; }
    public DataStats? DataStats { get; set; }

    // Typed effect definitions
    public List<PassiveEffect> PassiveEffects { get; set; } = [];
    public List<PlatformEffect> PlatformEffects { get; set; } = [];
    public List<AttachmentEffect> AttachmentEffects { get; set; } = [];

    /// <summary>
    /// Returns whether this card type falls under the Compute category.
    /// </summary>
    public bool IsComputeType => CardType is CardTypes.Compute or CardTypes.Container or CardTypes.Orchestrator or CardTypes.Serverless or CardTypes.AiMl;

    /// <summary>
    /// Returns whether this card type falls under the Data category.
    /// </summary>
    public bool IsDataType => CardType is CardTypes.Database or CardTypes.ObjectStorage or CardTypes.CacheDB;

    /// <summary>
    /// Returns whether this card type falls under the Support category.
    /// </summary>
    public bool IsSupportType => CardType is CardTypes.Platform or CardTypes.Attachment or CardTypes.Strategy or CardTypes.Reactive or CardTypes.Incident;

    /// <summary>
    /// Base throughput from stats. Returns 0 for non-compute cards.
    /// </summary>
    public long BaseThroughput => ComputeStats?.Throughput ?? 0;

    /// <summary>
    /// Base yield from stats. Returns 0 for non-data cards.
    /// </summary>
    public long BaseYield => DataStats?.Yield ?? 0;

    /// <summary>
    /// Base availability from either compute or data stats.
    /// </summary>
    public long BaseAvailability => ComputeStats?.Availability ?? DataStats?.Availability ?? 0;

    /// <summary>
    /// Maintenance cost from either compute or data stats.
    /// </summary>
    public long MaintenanceCost => ComputeStats?.MaintenanceCost ?? DataStats?.MaintenanceCost ?? 0;

    /// <summary>
    /// SLA penalty from either compute or data stats.
    /// </summary>
    public long SLAPenalty => ComputeStats?.SLAPenalty ?? DataStats?.SLAPenalty ?? 0;
}

public class ComputeStats
{
    public long Throughput { get; set; }
    public long? ThroughputMax { get; set; }
    public long Availability { get; set; }
    public long MaintenanceCost { get; set; }
    public long SLAPenalty { get; set; }
}

public class DataStats
{
    public long Yield { get; set; }
    public long? YieldMax { get; set; }
    public long Availability { get; set; }
    public long MaintenanceCost { get; set; }
    public long SLAPenalty { get; set; }
}
