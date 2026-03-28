namespace OverloadParty.Battle.Models;

/// <summary>
/// Game maps to the Games table.
/// </summary>
public class Game
{
    public string GameID { get; set; } = "";
    public string Player1ID { get; set; } = "";
    public string Player2ID { get; set; } = "";
    public DeckSnapshot? Player1DeckSnapshot { get; set; }
    public DeckSnapshot? Player2DeckSnapshot { get; set; }
    public GameStatus Status { get; set; } = GameStatus.Playing;
    public string? WinnerID { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public string EngineVersion { get; set; } = "";
    public string CardDataVersion { get; set; } = "";

    public string GetPlayerID(long playerNum) => playerNum switch
    {
        1 => Player1ID,
        2 => Player2ID,
        _ => throw new ArgumentOutOfRangeException(nameof(playerNum)),
    };
}

/// <summary>
/// GameState holds the full mutable state of a game in progress.
/// The Data layer handles serialization/deserialization to/from the DB.
/// </summary>
public class GameState
{
    public string GameID { get; set; } = "";
    public long Version { get; set; }
    public long CurrentTurn { get; set; }
    public Phase CurrentPhase { get; set; } = Phase.Draw;
    public long ActivePlayer { get; set; }

    // Player 1 state
    public long Player1Budget { get; set; }
    public long Player1InsightPool { get; set; }
    public Field Player1Field { get; set; } = new();
    public List<UndeployedCard> Player1Hand { get; set; } = [];
    public List<UndeployedCard> Player1Repository { get; set; } = [];
    public List<UndeployedCard> Player1Trash { get; set; } = [];
    public long Player1TimeBank { get; set; }
    public bool Player1IncidentPlayedThisTurn { get; set; }
    public bool Player1HasHadActiveResource { get; set; }

    // Player 2 state
    public long Player2Budget { get; set; }
    public long Player2InsightPool { get; set; }
    public Field Player2Field { get; set; } = new();
    public List<UndeployedCard> Player2Hand { get; set; } = [];
    public List<UndeployedCard> Player2Repository { get; set; } = [];
    public List<UndeployedCard> Player2Trash { get; set; } = [];
    public long Player2TimeBank { get; set; }
    public bool Player2IncidentPlayedThisTurn { get; set; }
    public bool Player2HasHadActiveResource { get; set; }

    // Shared state
    public List<ChainEntry> ChainStack { get; set; } = [];
    public long? CurrentActionTimer { get; set; }
    public DateTime TurnStartedAt { get; set; }
    public long NextInstanceSeq { get; set; }
    public DateTime UpdatedAt { get; set; }

    // ─── Accessor helpers (by player number) ────────────────

    public long GetBudget(long playerNum) => playerNum switch
    {
        1 => Player1Budget,
        2 => Player2Budget,
        _ => throw new ArgumentOutOfRangeException(nameof(playerNum)),
    };
    public void SetBudget(long playerNum, long value)
    {
        switch (playerNum)
        {
            case 1: Player1Budget = value; break;
            case 2: Player2Budget = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(playerNum));
        }
    }

    public long GetInsightPool(long playerNum) => playerNum switch
    {
        1 => Player1InsightPool,
        2 => Player2InsightPool,
        _ => throw new ArgumentOutOfRangeException(nameof(playerNum)),
    };
    public void SetInsightPool(long playerNum, long value)
    {
        switch (playerNum)
        {
            case 1: Player1InsightPool = value; break;
            case 2: Player2InsightPool = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(playerNum));
        }
    }

    public Field GetField(long playerNum) => playerNum switch
    {
        1 => Player1Field,
        2 => Player2Field,
        _ => throw new ArgumentOutOfRangeException(nameof(playerNum)),
    };
    public void SetField(long playerNum, Field field)
    {
        switch (playerNum)
        {
            case 1: Player1Field = field; break;
            case 2: Player2Field = field; break;
            default: throw new ArgumentOutOfRangeException(nameof(playerNum));
        }
    }

    public List<UndeployedCard> GetHand(long playerNum) => playerNum switch
    {
        1 => Player1Hand,
        2 => Player2Hand,
        _ => throw new ArgumentOutOfRangeException(nameof(playerNum)),
    };
    public void SetHand(long playerNum, List<UndeployedCard> hand)
    {
        switch (playerNum)
        {
            case 1: Player1Hand = hand; break;
            case 2: Player2Hand = hand; break;
            default: throw new ArgumentOutOfRangeException(nameof(playerNum));
        }
    }

    public List<UndeployedCard> GetRepository(long playerNum) => playerNum switch
    {
        1 => Player1Repository,
        2 => Player2Repository,
        _ => throw new ArgumentOutOfRangeException(nameof(playerNum)),
    };
    public void SetRepository(long playerNum, List<UndeployedCard> repo)
    {
        switch (playerNum)
        {
            case 1: Player1Repository = repo; break;
            case 2: Player2Repository = repo; break;
            default: throw new ArgumentOutOfRangeException(nameof(playerNum));
        }
    }

    public List<UndeployedCard> GetTrash(long playerNum) => playerNum switch
    {
        1 => Player1Trash,
        2 => Player2Trash,
        _ => throw new ArgumentOutOfRangeException(nameof(playerNum)),
    };
    public void SetTrash(long playerNum, List<UndeployedCard> trash)
    {
        switch (playerNum)
        {
            case 1: Player1Trash = trash; break;
            case 2: Player2Trash = trash; break;
            default: throw new ArgumentOutOfRangeException(nameof(playerNum));
        }
    }

    public long GetTimeBank(long playerNum) => playerNum switch
    {
        1 => Player1TimeBank,
        2 => Player2TimeBank,
        _ => throw new ArgumentOutOfRangeException(nameof(playerNum)),
    };
    public void SetTimeBank(long playerNum, long value)
    {
        switch (playerNum)
        {
            case 1: Player1TimeBank = value; break;
            case 2: Player2TimeBank = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(playerNum));
        }
    }

    public bool GetIncidentPlayedThisTurn(long playerNum) => playerNum switch
    {
        1 => Player1IncidentPlayedThisTurn,
        2 => Player2IncidentPlayedThisTurn,
        _ => throw new ArgumentOutOfRangeException(nameof(playerNum)),
    };
    public void SetIncidentPlayedThisTurn(long playerNum, bool value)
    {
        switch (playerNum)
        {
            case 1: Player1IncidentPlayedThisTurn = value; break;
            case 2: Player2IncidentPlayedThisTurn = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(playerNum));
        }
    }

    public bool GetHasHadActiveResource(long playerNum) => playerNum switch
    {
        1 => Player1HasHadActiveResource,
        2 => Player2HasHadActiveResource,
        _ => throw new ArgumentOutOfRangeException(nameof(playerNum)),
    };
    public void SetHasHadActiveResource(long playerNum, bool value)
    {
        switch (playerNum)
        {
            case 1: Player1HasHadActiveResource = value; break;
            case 2: Player2HasHadActiveResource = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(playerNum));
        }
    }

    public long OpponentOf(long playerNum) => playerNum switch
    {
        1 => 2,
        2 => 1,
        _ => throw new ArgumentOutOfRangeException(nameof(playerNum)),
    };

    /// <summary>
    /// Generate the next unique instance ID and increment the sequence.
    /// </summary>
    public string NextInstanceID()
    {
        var id = $"inst_{NextInstanceSeq}";
        NextInstanceSeq++;
        return id;
    }

    /// <summary>
    /// Monotonically increasing sequence for deploy order.
    /// Must be persisted to survive state reload.
    /// </summary>
    public long NextDeployOrderSeq { get; set; }

    /// <summary>
    /// Generate the next deploy order value.
    /// </summary>
    public long NextDeployOrder()
    {
        NextDeployOrderSeq++;
        return NextDeployOrderSeq;
    }
}

/// <summary>
/// GameEvent maps to the GameEvents table.
/// </summary>
public class GameEvent
{
    public string GameID { get; set; } = "";
    public long SequenceNumber { get; set; }
    public string EventType { get; set; } = "";
    public string? PlayerID { get; set; }
    public Dictionary<string, object>? EventData { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// GameAction maps to the game_actions table (append-only action log for replay).
/// </summary>
public class GameAction
{
    public string GameID { get; set; } = "";
    public int Seq { get; set; }
    public string PlayerID { get; set; } = "";
    public string ActionType { get; set; } = "";
    public Dictionary<string, object>? ActionData { get; set; }
    public DateTime CreatedAt { get; set; }
}
