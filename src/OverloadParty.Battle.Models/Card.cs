namespace OverloadParty.Battle.Models;

/// <summary>
/// CardDefinition はカードマスターの定義情報を保持します
/// </summary>
public class CardDefinition
{
    public string CardId { get; set; } = "";
    public string CardName { get; set; } = "";
    public string ResourceLabel { get; set; } = "";
    public string Faction { get; set; } = "";
    public string CardType { get; set; } = "";

    // Compute/Data カテゴリのサブタイプ (VM/Container/Database 等)。それ以外は null。
    public string? Subtype { get; set; }

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

    // カードタイプカテゴリに応じてどちらか一方が設定される
    public ComputeStats? ComputeStats { get; set; }
    public DataStats? DataStats { get; set; }

    public List<EffectDef>? Effects { get; set; }

    /// <summary>
    /// IsComputeType はカードタイプが Compute カテゴリに属するかを返します
    /// </summary>
    public bool IsComputeType => CardType == CardTypes.Compute;

    /// <summary>
    /// IsDataType はカードタイプが Data カテゴリに属するかを返します
    /// </summary>
    public bool IsDataType => CardType == CardTypes.Data;

    /// <summary>
    /// IsSupportType はカードタイプが Support カテゴリに属するかを返します
    /// </summary>
    public bool IsSupportType => CardType is CardTypes.Platform or CardTypes.Attachment
                                          or CardTypes.Strategy or CardTypes.Reactive or CardTypes.Incident;

    /// <summary>
    /// BaseThroughput はベーススループットを返します（非 Compute カードの場合は 0）
    /// </summary>
    public long BaseThroughput => ComputeStats?.Throughput ?? 0;

    /// <summary>
    /// BaseYield はベースイールドを返します（非 Data カードの場合は 0）
    /// </summary>
    public long BaseYield => DataStats?.Yield ?? 0;

    /// <summary>
    /// BaseAvailability は Compute または Data のベース可用性を返します
    /// </summary>
    public long BaseAvailability => ComputeStats?.Availability ?? DataStats?.Availability ?? 0;

    /// <summary>
    /// MaintenanceCost は Compute または Data の維持費を返します
    /// </summary>
    public long MaintenanceCost => ComputeStats?.MaintenanceCost ?? DataStats?.MaintenanceCost ?? 0;

    /// <summary>
    /// SLAPenalty は Compute または Data の SLA ペナルティを返します
    /// </summary>
    public long SLAPenalty => ComputeStats?.SLAPenalty ?? DataStats?.SLAPenalty ?? 0;
}

/// <summary>
/// ComputeStats は Compute カテゴリカードのステータスを保持します
/// </summary>
public class ComputeStats
{
    public long Throughput { get; set; }
    public long? ThroughputMax { get; set; }
    public long Availability { get; set; }
    public long MaintenanceCost { get; set; }
    public long SLAPenalty { get; set; }
}

/// <summary>
/// DataStats は Data カテゴリカードのステータスを保持します
/// </summary>
public class DataStats
{
    public long Yield { get; set; }
    public long? YieldMax { get; set; }
    public long Availability { get; set; }
    public long MaintenanceCost { get; set; }
    public long SLAPenalty { get; set; }
}
