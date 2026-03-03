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
    M, // Balanced (1.0, 1.0)
    C, // Compute-optimized (1.5 TP, 0.75 AV)
    R  // Reliability (0.75 TP, 1.5 AV)
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
    Datawarehouse,

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
    Waiting,
    Playing,
    Finished
}

public enum WinReason
{
    BudgetZero,
    SystemDown,
    RepositoryOut,
    Timeout,
    Disconnect,
    TurnLimit,
    Draw,
    LaunchFailure
}

public enum ActionType
{
    PlayCard,
    Attack,
    ScaleUp,
    DistributeYield,
    DiscardHand,
    ActivateEffect,
    SetReactive,
    Migrate,
    EndPhase
}

public enum EventType
{
    PlayCard,
    AttachCard,
    Attack,
    ScaleUp,
    DistributeYield,
    DiscardHand,
    ActivateEffect,
    TrapRevealed,
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
