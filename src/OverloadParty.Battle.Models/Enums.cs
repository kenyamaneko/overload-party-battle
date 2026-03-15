using System.Text.Json.Serialization;

namespace OverloadParty.Battle.Models;

public enum Phase
{
    Draw,
    Main,
    Battle,
    End
}

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
    [JsonPropertyName("M")] M, // Balanced
    [JsonPropertyName("C")] C, // Compute-optimized
    [JsonPropertyName("R")] R  // Reliability
}

public enum CardTypeCategory
{
    Compute,
    Data,
    Support
}

public enum GameStatus
{
    Playing,
    Finished
}

public enum WinReason
{
    BudgetZero,
    SystemDown,
    RepositoryOut,
    Timeout,
    TurnLimit,
    Draw,
    LaunchFailure
}

public enum ActionType
{
    PlayCard,
    Attack,
    ScaleUp,
    Monetize,
    DiscardHand,
    ActivateEffect,
    SetReactive,
    Migrate,
    EndPhase,
    Forfeit
}

public enum EventType
{
    PlayCard,
    AttachCard,
    Attack,
    ScaleUp,
    Monetize,
    DiscardHand,
    ActivateEffect,
    ReactiveRevealed,
    Migrate,
    MigrationComplete,
    PhaseChange,
    PhaseEnd,
    TurnEnd,
    GameOver
}

public enum TriggerType
{
    Deploy,
    Activate,
    Passive,
    OnAttack,
    OnHit,
    OnDestroy,
    Reactive,
    OnEnemyDeploy
}
