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
    public GameStatus Status { get; set; } = GameStatus.Waiting;
    public string? WinnerID { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
}

/// <summary>
/// GameState holds the full mutable state of a game in progress.
/// In Go this uses json.RawMessage for sub-structures; in C# we use typed objects.
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
    public List<HandCard> Player1Hand { get; set; } = [];
    public List<HandCard> Player1Repository { get; set; } = [];
    public List<HandCard> Player1Trash { get; set; } = [];
    public long Player1TimeBank { get; set; }

    // Player 2 state
    public long Player2Budget { get; set; }
    public long Player2InsightPool { get; set; }
    public Field Player2Field { get; set; } = new();
    public List<HandCard> Player2Hand { get; set; } = [];
    public List<HandCard> Player2Repository { get; set; } = [];
    public List<HandCard> Player2Trash { get; set; } = [];
    public long Player2TimeBank { get; set; }

    // Shared state
    public List<ChainEntry> ChainStack { get; set; } = [];
    public long? CurrentActionTimer { get; set; }
    public long NextInstanceSeq { get; set; }
    public DateTime UpdatedAt { get; set; }

    // ─── Accessor helpers (by player number) ────────────────

    public long GetBudget(long playerNum) => playerNum == 1 ? Player1Budget : Player2Budget;
    public void SetBudget(long playerNum, long value) { if (playerNum == 1) Player1Budget = value; else Player2Budget = value; }

    public long GetInsightPool(long playerNum) => playerNum == 1 ? Player1InsightPool : Player2InsightPool;
    public void SetInsightPool(long playerNum, long value) { if (playerNum == 1) Player1InsightPool = value; else Player2InsightPool = value; }

    public Field GetField(long playerNum) => playerNum == 1 ? Player1Field : Player2Field;
    public void SetField(long playerNum, Field field) { if (playerNum == 1) Player1Field = field; else Player2Field = field; }

    public List<HandCard> GetHand(long playerNum) => playerNum == 1 ? Player1Hand : Player2Hand;
    public void SetHand(long playerNum, List<HandCard> hand) { if (playerNum == 1) Player1Hand = hand; else Player2Hand = hand; }

    public List<HandCard> GetRepository(long playerNum) => playerNum == 1 ? Player1Repository : Player2Repository;
    public void SetRepository(long playerNum, List<HandCard> repo) { if (playerNum == 1) Player1Repository = repo; else Player2Repository = repo; }

    public List<HandCard> GetTrash(long playerNum) => playerNum == 1 ? Player1Trash : Player2Trash;
    public void SetTrash(long playerNum, List<HandCard> trash) { if (playerNum == 1) Player1Trash = trash; else Player2Trash = trash; }

    public long GetTimeBank(long playerNum) => playerNum == 1 ? Player1TimeBank : Player2TimeBank;
    public void SetTimeBank(long playerNum, long value) { if (playerNum == 1) Player1TimeBank = value; else Player2TimeBank = value; }

    public long OpponentOf(long playerNum) => playerNum == 1 ? 2 : 1;

    /// <summary>
    /// Generate the next unique instance ID and increment the sequence.
    /// </summary>
    public string NextInstanceID()
    {
        var id = $"inst_{NextInstanceSeq}";
        NextInstanceSeq++;
        return id;
    }

    private long _nextDeployOrder;

    /// <summary>
    /// Generate the next deploy order value.
    /// </summary>
    public long NextDeployOrder()
    {
        _nextDeployOrder++;
        return _nextDeployOrder;
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
/// GameConfig key-value pair from the game_configs table.
/// </summary>
public class GameConfig
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
    public DateTime UpdatedAt { get; set; }
}
