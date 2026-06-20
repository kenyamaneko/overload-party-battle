using System.Text.Json.Serialization;

namespace OverloadParty.Battle.Models;

/// <summary>
/// Phase はゲームのフェーズを表現します
/// </summary>
public enum Phase
{
    Draw,
    Main,
    Battle,
    End
}

/// <summary>
/// Zone はフィールドのゾーンを表現します
/// </summary>
public enum Zone
{
    Frontend,
    Backend,
    Support
}

[JsonConverter(typeof(JsonStringEnumConverter<Rank>))]
public enum Rank
{
    [JsonPropertyName("small")] Small,
    [JsonPropertyName("medium")] Medium,
    [JsonPropertyName("large")] Large
}

[JsonConverter(typeof(JsonStringEnumConverter<InstanceFamily>))]
public enum InstanceFamily
{
    [JsonPropertyName("M")] M, // バランス型
    [JsonPropertyName("C")] C, // コンピュート最適化型
    [JsonPropertyName("R")] R  // 信頼性重視型
}

/// <summary>
/// CardTypeCategory はカードタイプの大分類を表現します
/// </summary>
public enum CardTypeCategory
{
    Compute,
    DataResource,
    Support
}

/// <summary>
/// GameStatus はゲームの進行状態を表現します
/// </summary>
public enum GameStatus
{
    Playing,
    Finished
}

/// <summary>
/// WinReason は勝利理由を表現します
/// </summary>
public enum WinReason
{
    BudgetZero,
    SystemDown,
    RepositoryOut,
    TurnTimeout,
    Disconnect,
    TurnLimit,
    Draw,
    LaunchFailure,
    Surrender
}

/// <summary>
/// ActionType はプレイヤーアクションの種類を表現します
/// </summary>
public enum ActionType
{
    PlayCard,
    Attack,
    ScaleUp,
    Monetize,
    DiscardHand,
    UseEffect,
    UseInitiative,
    EndPhase,
    Forfeit,
    SelectSlot,
    ResolvePendingChoice
}

/// <summary>
/// TriggerType は効果の発動条件を表現します
/// </summary>
public enum TriggerType
{
    OnDeploy,
    Ignition,
    Passive,
    OnAttack,
    OnHit,
    OnDestroy,
    OnAttackDeclared,
    OnIncident,
    OnDamaged,
    OnEndPhase,
    OnFieldChange,
    OnScaleUp
}
