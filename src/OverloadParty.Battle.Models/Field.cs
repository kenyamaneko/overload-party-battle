namespace OverloadParty.Battle.Models;

/// <summary>
/// フィールド は 1 プレイヤーの全ゾーン配置を表現します（各ゾーン 3 スロット）
/// </summary>
public class Field
{
    public Zone<DeployedResource> Frontend { get; set; } = new();
    public Zone<DeployedResource> Backend { get; set; } = new();
    public Zone<DeployedSupport> Support { get; set; } = new();
}

/// <summary>
/// DeployedResource はフロントエンドまたはバックエンドゾーンに配置されたカードを表現します
/// </summary>
public class DeployedResource
{
    public string InstanceID { get; set; } = "";
    public string CardID { get; set; } = "";
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
    public List<TemporaryEffect> TemporaryEffects { get; set; } = [];
    public long MonetizedAmount { get; set; }
    public bool HasAttacked { get; set; }
    public bool EffectUsedThisTurn { get; set; }
    public bool EffectUsedThisGame { get; set; }
    public long DeployedOnTurn { get; set; }
    public long DeployOrder { get; set; }
    public long ElasticBonus { get; set; }
    public long LastAttackTurn { get; set; }

    /// <summary>
    /// 実効 AV = MaxAV - Damage（負になりうる）
    /// </summary>
    public long EffectiveAV => MaxAV - Damage;
}

/// <summary>
/// TemporaryEffect はリソースに付与される期限付き修飾効果を表現します
/// </summary>
public class TemporaryEffect
{
    public string EffectType { get; set; } = "";
    public long Value { get; set; }
    public string Duration { get; set; } = "";
    public string SourceID { get; set; } = "";
    public string Mode { get; set; } = "";
}

/// <summary>
/// DeployedSupport is a Platform, Reactive, or Attachment card in the support zone.
/// Attachment cards use TargetInstanceID to reference the resource they are equipped to.
/// </summary>
public class DeployedSupport
{
    public string InstanceID { get; set; } = "";
    public string CardID { get; set; } = "";
    public long ArtNo { get; set; }
    public bool FaceUp { get; set; }
    public long DeployingTurnsLeft { get; set; }
    public long DeployOrder { get; set; }
    public bool EffectUsedThisTurn { get; set; }
    public bool EffectUsedThisGame { get; set; }

    /// <summary>
    /// For Attachment cards: the instance ID of the resource this attachment targets.
    /// Null for Platform and Reactive cards.
    /// </summary>
    public string? TargetInstanceID { get; set; }

    /// <summary>
    /// Player numbers that have peeked at this face-down card.
    /// Used to show the card info to specific players without flipping it face-up.
    /// </summary>
    public List<long> PeekedBy { get; set; } = [];
}

/// <summary>
/// UndeployedCard は未配置のカード（手札・リポジトリ・トラッシュ）を表現します
/// </summary>
public class UndeployedCard
{
    public string InstanceID { get; set; } = "";
    public string CardID { get; set; } = "";
    public long ArtNo { get; set; }
}

/// <summary>
/// 効果でデプロイするカードと、その配置先スロットの選択待ちを表す。
/// カードは選択が解決されるまで元の領域に残るため、不発に終わっても戻す処理は要らない。
/// </summary>
public class AwaitingSlotSelect
{
    public long PlayerNum { get; set; }

    /// <summary>配置するカードが置かれている領域 (<see cref="SlotSelectSources"/> の値)。</summary>
    public string SourceZone { get; set; } = "";

    /// <summary>配置するカードのインスタンス ID。領域内のカードを一意に指す。</summary>
    public string CardInstanceID { get; set; } = "";

    /// <summary>配置するリソースに上書きする可用性。0 ならカード定義の値を使う。</summary>
    public long OverrideAV { get; set; }
}

/// <summary>
/// スロット選択待ちのカードが置かれている領域 (<see cref="AwaitingSlotSelect.SourceZone"/> の値) の定数。
/// </summary>
public static class SlotSelectSources
{
    /// <summary>手札。</summary>
    public const string Hand = "hand";

    /// <summary>デッキ。</summary>
    public const string Repository = "repository";
}
