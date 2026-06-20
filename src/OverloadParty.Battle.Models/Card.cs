namespace OverloadParty.Battle.Models;

/// <summary>
/// CardDefinition はカードマスターの定義情報を保持します
/// </summary>
public class CardDefinition : IEffectSource
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
    public DataResourceStats? DataResourceStats { get; set; }

    public List<EffectDef>? Effects { get; set; }

    /// <summary>EffectRegistry への登録キーとしてカード ID を返します。</summary>
    public string EffectSourceId => CardId;

    /// <summary>登録対象の効果定義群を返します（未定義なら空）。</summary>
    public IReadOnlyList<EffectDef> EffectDefs => Effects ?? [];

    /// <summary>
    /// IsComputeType はカードタイプが Compute カテゴリに属するかを返します
    /// </summary>
    public bool IsComputeType => CardType == CardTypes.Compute;

    /// <summary>
    /// IsDataResource はカードタイプが Data カテゴリに属するかを返します
    /// </summary>
    public bool IsDataResource => CardType == CardTypes.DataResource;

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
    public long BaseYield => DataResourceStats?.Yield ?? 0;

    /// <summary>
    /// BaseAvailability は Compute または Data のベース可用性を返します
    /// </summary>
    public long BaseAvailability => ComputeStats?.Availability ?? DataResourceStats?.Availability ?? 0;

    /// <summary>
    /// MaintenanceCost は Compute または Data の維持コストを返します
    /// </summary>
    public long MaintenanceCost => ComputeStats?.MaintenanceCost ?? DataResourceStats?.MaintenanceCost ?? 0;

    /// <summary>
    /// SLAPenalty は Compute または Data の SLA ペナルティを返します
    /// </summary>
    public long SLAPenalty => ComputeStats?.SLAPenalty ?? DataResourceStats?.SLAPenalty ?? 0;
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
/// DataResourceStats は Data カテゴリカードのステータスを保持します
/// </summary>
public class DataResourceStats
{
    public long Yield { get; set; }
    public long? YieldMax { get; set; }
    public long Availability { get; set; }
    public long MaintenanceCost { get; set; }
    public long SLAPenalty { get; set; }
}
