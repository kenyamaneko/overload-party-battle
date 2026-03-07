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

public enum Rank
{
    Small,
    Medium,
    Large
}

public enum InstanceFamily
{
    M, // Balanced
    C, // Compute-optimized
    R  // Reliability
}

public enum Faction
{
    SD,
    Tenki,
    Sugar,
    Tuners,
    Neutral
}

public enum CardTypeCategory
{
    Compute,
    Data,
    Support
}

public enum CardType
{
    // Compute types
    Compute,
    Container,
    Orchestrator,
    Serverless,
    AiMl,

    // Data types
    Database,
    ObjectStorage,
    CacheDB,

    // Support types
    Platform,
    Attachment,
    Strategy,
    Reactive,
    Incident
}

public enum Restriction
{
    Unlimited,
    Limited,
    SemiLimited
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

public enum EffectDuration
{
    ThisTurn,
    UntilNextTurnEnd
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
