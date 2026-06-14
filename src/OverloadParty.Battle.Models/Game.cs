namespace OverloadParty.Battle.Models;

/// <summary>
/// Game は games テーブルにマッピングされるゲームメタデータを保持します
/// </summary>
public class Game
{
    public string GameID { get; set; } = "";
    public GameStatus Status { get; set; } = GameStatus.Playing;
    public int FirstPlayer { get; set; }
    public string? Npc1Model { get; set; }
    public string? Npc2Model { get; set; }
    public int? WinningPlayerNum { get; set; }
    public string? WinReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public string EngineVersion { get; set; } = "";
    public string CardDataVersion { get; set; } = "";

    /// <summary>
    /// 指定したプレイヤー番号の NPC モデル名を返します
    /// </summary>
    /// <param name="playerNum">対象プレイヤー番号 (1 または 2)</param>
    /// <returns>NPC モデル名。人間プレイヤーなら null</returns>
    public string? GetNpcModel(long playerNum) => playerNum switch
    {
        1 => Npc1Model,
        2 => Npc2Model,
        _ => throw new ArgumentOutOfRangeException(nameof(playerNum)),
    };
}

/// <summary>
/// BattleGameState holds the full mutable state of a game in progress.
/// The Data layer handles serialization/deserialization to/from the DB.
/// </summary>
public class BattleGameState
{
    public string GameID { get; set; } = "";
    public long Version { get; set; }
    public long CurrentTurn { get; set; }
    public Phase CurrentPhase { get; set; } = Phase.Draw;
    public long ActivePlayer { get; set; }

    // プレイヤー 1 の状態
    public long Player1Budget { get; set; }
    public long Player1InsightPool { get; set; }
    public Field Player1Field { get; set; } = new();
    public List<UndeployedCard> Player1Hand { get; set; } = [];
    public List<UndeployedCard> Player1Repository { get; set; } = [];
    public List<UndeployedCard> Player1Trash { get; set; } = [];
    public long Player1TimeBank { get; set; }
    public bool Player1IncidentPlayedThisTurn { get; set; }
    public bool Player1HasOperated { get; set; }
    public string Player1ProductId { get; set; } = "";
    public string Player1RoutineId { get; set; } = "";
    public string Player1SpecialId { get; set; } = "";
    public bool Player1RoutineUsedThisTurn { get; set; }
    public bool Player1SpecialUsedThisGame { get; set; }

    // プレイヤー 2 の状態
    public long Player2Budget { get; set; }
    public long Player2InsightPool { get; set; }
    public Field Player2Field { get; set; } = new();
    public List<UndeployedCard> Player2Hand { get; set; } = [];
    public List<UndeployedCard> Player2Repository { get; set; } = [];
    public List<UndeployedCard> Player2Trash { get; set; } = [];
    public long Player2TimeBank { get; set; }
    public bool Player2IncidentPlayedThisTurn { get; set; }
    public bool Player2HasOperated { get; set; }
    public string Player2ProductId { get; set; } = "";
    public string Player2RoutineId { get; set; } = "";
    public string Player2SpecialId { get; set; } = "";
    public bool Player2RoutineUsedThisTurn { get; set; }
    public bool Player2SpecialUsedThisGame { get; set; }

    // 共有状態
    public long? CurrentActionTimer { get; set; }
    public DateTime TurnStartedAt { get; set; }
    public long NextInstanceSeq { get; set; }
    public DateTime UpdatedAt { get; set; }
    /// <summary>
    /// 効果デプロイのスロット選択待ちキュー。先頭から順に処理する。
    /// </summary>
    public List<AwaitingSlotSelect> PendingSlotSelects { get; set; } = [];

    /// <summary>
    /// 効果が発動中で、プレイヤーの選択を待っている状態。null なら待ちなし。
    /// </summary>
    public PendingEffectChoice? PendingEffectChoice { get; set; }

    // ─── Accessor helpers (by player number) ────────────────

    /// <summary>
    /// 指定したプレイヤーのバジェットを返します
    /// </summary>
    /// <param name="playerNum">対象プレイヤー番号</param>
    /// <returns>バジェット値</returns>
    public long GetBudget(long playerNum) => playerNum switch
    {
        1 => Player1Budget,
        2 => Player2Budget,
        _ => throw new ArgumentOutOfRangeException(nameof(playerNum)),
    };
    /// <summary>
    /// 指定したプレイヤーのバジェットを設定します
    /// </summary>
    /// <param name="playerNum">対象プレイヤー番号</param>
    /// <param name="value">設定するバジェット値</param>
    public void SetBudget(long playerNum, long value)
    {
        switch (playerNum)
        {
            case 1: Player1Budget = value; break;
            case 2: Player2Budget = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(playerNum));
        }
    }

    /// <summary>
    /// 指定したプレイヤーのインサイトプール残量を返します
    /// </summary>
    /// <param name="playerNum">対象プレイヤー番号</param>
    /// <returns>インサイトプール残量</returns>
    public long GetInsightPool(long playerNum) => playerNum switch
    {
        1 => Player1InsightPool,
        2 => Player2InsightPool,
        _ => throw new ArgumentOutOfRangeException(nameof(playerNum)),
    };
    /// <summary>
    /// 指定したプレイヤーのインサイトプール残量を設定します
    /// </summary>
    /// <param name="playerNum">対象プレイヤー番号</param>
    /// <param name="value">設定するインサイトプール残量</param>
    public void SetInsightPool(long playerNum, long value)
    {
        switch (playerNum)
        {
            case 1: Player1InsightPool = value; break;
            case 2: Player2InsightPool = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(playerNum));
        }
    }

    /// <summary>
    /// 指定したプレイヤーのフィールドを返します
    /// </summary>
    /// <param name="playerNum">対象プレイヤー番号</param>
    /// <returns>プレイヤーのフィールド</returns>
    public Field GetField(long playerNum) => playerNum switch
    {
        1 => Player1Field,
        2 => Player2Field,
        _ => throw new ArgumentOutOfRangeException(nameof(playerNum)),
    };
    /// <summary>
    /// 指定したプレイヤーのフィールドを設定します
    /// </summary>
    /// <param name="playerNum">対象プレイヤー番号</param>
    /// <param name="field">設定するフィールド</param>
    public void SetField(long playerNum, Field field)
    {
        switch (playerNum)
        {
            case 1: Player1Field = field; break;
            case 2: Player2Field = field; break;
            default: throw new ArgumentOutOfRangeException(nameof(playerNum));
        }
    }

    /// <summary>
    /// 指定したプレイヤーの手札を返します
    /// </summary>
    /// <param name="playerNum">対象プレイヤー番号</param>
    /// <returns>手札のカード一覧</returns>
    public List<UndeployedCard> GetHand(long playerNum) => playerNum switch
    {
        1 => Player1Hand,
        2 => Player2Hand,
        _ => throw new ArgumentOutOfRangeException(nameof(playerNum)),
    };
    /// <summary>
    /// 指定したプレイヤーの手札を設定します
    /// </summary>
    /// <param name="playerNum">対象プレイヤー番号</param>
    /// <param name="hand">設定する手札のカード一覧</param>
    public void SetHand(long playerNum, List<UndeployedCard> hand)
    {
        switch (playerNum)
        {
            case 1: Player1Hand = hand; break;
            case 2: Player2Hand = hand; break;
            default: throw new ArgumentOutOfRangeException(nameof(playerNum));
        }
    }

    /// <summary>
    /// 指定したプレイヤーのリポジトリを返します
    /// </summary>
    /// <param name="playerNum">対象プレイヤー番号</param>
    /// <returns>リポジトリのカード一覧</returns>
    public List<UndeployedCard> GetRepository(long playerNum) => playerNum switch
    {
        1 => Player1Repository,
        2 => Player2Repository,
        _ => throw new ArgumentOutOfRangeException(nameof(playerNum)),
    };
    /// <summary>
    /// 指定したプレイヤーのリポジトリを設定します
    /// </summary>
    /// <param name="playerNum">対象プレイヤー番号</param>
    /// <param name="repo">設定するリポジトリのカード一覧</param>
    public void SetRepository(long playerNum, List<UndeployedCard> repo)
    {
        switch (playerNum)
        {
            case 1: Player1Repository = repo; break;
            case 2: Player2Repository = repo; break;
            default: throw new ArgumentOutOfRangeException(nameof(playerNum));
        }
    }

    /// <summary>
    /// 指定したプレイヤーのトラッシュを返します
    /// </summary>
    /// <param name="playerNum">対象プレイヤー番号</param>
    /// <returns>トラッシュのカード一覧</returns>
    public List<UndeployedCard> GetTrash(long playerNum) => playerNum switch
    {
        1 => Player1Trash,
        2 => Player2Trash,
        _ => throw new ArgumentOutOfRangeException(nameof(playerNum)),
    };
    /// <summary>
    /// 指定したプレイヤーのトラッシュを設定します
    /// </summary>
    /// <param name="playerNum">対象プレイヤー番号</param>
    /// <param name="trash">設定するトラッシュのカード一覧</param>
    public void SetTrash(long playerNum, List<UndeployedCard> trash)
    {
        switch (playerNum)
        {
            case 1: Player1Trash = trash; break;
            case 2: Player2Trash = trash; break;
            default: throw new ArgumentOutOfRangeException(nameof(playerNum));
        }
    }

    /// <summary>
    /// 指定したプレイヤーのタイムバンク残量を返します
    /// </summary>
    /// <param name="playerNum">対象プレイヤー番号</param>
    /// <returns>タイムバンク残量</returns>
    public long GetTimeBank(long playerNum) => playerNum switch
    {
        1 => Player1TimeBank,
        2 => Player2TimeBank,
        _ => throw new ArgumentOutOfRangeException(nameof(playerNum)),
    };
    /// <summary>
    /// 指定したプレイヤーのタイムバンク残量を設定します
    /// </summary>
    /// <param name="playerNum">対象プレイヤー番号</param>
    /// <param name="value">設定するタイムバンク残量</param>
    public void SetTimeBank(long playerNum, long value)
    {
        switch (playerNum)
        {
            case 1: Player1TimeBank = value; break;
            case 2: Player2TimeBank = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(playerNum));
        }
    }

    /// <summary>
    /// 指定したプレイヤーが現ターンにインシデントを使用済みかを返します
    /// </summary>
    /// <param name="playerNum">対象プレイヤー番号</param>
    /// <returns>現ターンにインシデントを使用済みなら true</returns>
    public bool GetIncidentPlayedThisTurn(long playerNum) => playerNum switch
    {
        1 => Player1IncidentPlayedThisTurn,
        2 => Player2IncidentPlayedThisTurn,
        _ => throw new ArgumentOutOfRangeException(nameof(playerNum)),
    };
    /// <summary>
    /// 指定したプレイヤーの現ターンのインシデント使用フラグを設定します
    /// </summary>
    /// <param name="playerNum">対象プレイヤー番号</param>
    /// <param name="value">設定するフラグ値</param>
    public void SetIncidentPlayedThisTurn(long playerNum, bool value)
    {
        switch (playerNum)
        {
            case 1: Player1IncidentPlayedThisTurn = value; break;
            case 2: Player2IncidentPlayedThisTurn = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(playerNum));
        }
    }

    /// <summary>
    /// 指定したプレイヤーの稼働実績フラグを返します
    /// </summary>
    /// <param name="playerNum">対象プレイヤー番号</param>
    /// <returns>稼働実績ありなら true</returns>
    public bool GetHasOperated(long playerNum) => playerNum switch
    {
        1 => Player1HasOperated,
        2 => Player2HasOperated,
        _ => throw new ArgumentOutOfRangeException(nameof(playerNum)),
    };
    /// <summary>
    /// 指定したプレイヤーの稼働実績フラグを設定します
    /// </summary>
    /// <param name="playerNum">対象プレイヤー番号</param>
    /// <param name="value">設定する稼働実績フラグ</param>
    public void SetHasOperated(long playerNum, bool value)
    {
        switch (playerNum)
        {
            case 1: Player1HasOperated = value; break;
            case 2: Player2HasOperated = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(playerNum));
        }
    }

    /// <summary>
    /// 指定したプレイヤーが選んだプロダクトの ID を返します
    /// </summary>
    /// <param name="playerNum">対象プレイヤー番号</param>
    /// <returns>選んだプロダクトの ID</returns>
    public string GetProductId(long playerNum) => playerNum switch
    {
        1 => Player1ProductId,
        2 => Player2ProductId,
        _ => throw new ArgumentOutOfRangeException(nameof(playerNum)),
    };

    /// <summary>
    /// 指定したプレイヤーがセットした施策の ID を返します
    /// </summary>
    /// <param name="playerNum">対象プレイヤー番号</param>
    /// <param name="kind">施策の区分 (ルーチン / スペシャル)</param>
    /// <returns>セットした施策の ID</returns>
    public string GetInitiativeId(long playerNum, string kind) => (playerNum, kind) switch
    {
        (1, InitiativeKinds.Routine) => Player1RoutineId,
        (1, InitiativeKinds.Special) => Player1SpecialId,
        (2, InitiativeKinds.Routine) => Player2RoutineId,
        (2, InitiativeKinds.Special) => Player2SpecialId,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>
    /// 指定したプレイヤーが現ターンにルーチンを使用済みかを返します
    /// </summary>
    /// <param name="playerNum">対象プレイヤー番号</param>
    /// <returns>現ターンにルーチンを使用済みなら true</returns>
    public bool GetRoutineUsedThisTurn(long playerNum) => playerNum switch
    {
        1 => Player1RoutineUsedThisTurn,
        2 => Player2RoutineUsedThisTurn,
        _ => throw new ArgumentOutOfRangeException(nameof(playerNum)),
    };
    /// <summary>
    /// 指定したプレイヤーの現ターンのルーチン使用フラグを設定します
    /// </summary>
    /// <param name="playerNum">対象プレイヤー番号</param>
    /// <param name="value">設定するフラグ値</param>
    public void SetRoutineUsedThisTurn(long playerNum, bool value)
    {
        switch (playerNum)
        {
            case 1: Player1RoutineUsedThisTurn = value; break;
            case 2: Player2RoutineUsedThisTurn = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(playerNum));
        }
    }

    /// <summary>
    /// 指定したプレイヤーが本ゲームでスペシャルを使用済みかを返します
    /// </summary>
    /// <param name="playerNum">対象プレイヤー番号</param>
    /// <returns>本ゲームでスペシャルを使用済みなら true</returns>
    public bool GetSpecialUsedThisGame(long playerNum) => playerNum switch
    {
        1 => Player1SpecialUsedThisGame,
        2 => Player2SpecialUsedThisGame,
        _ => throw new ArgumentOutOfRangeException(nameof(playerNum)),
    };
    /// <summary>
    /// 指定したプレイヤーの本ゲームのスペシャル使用フラグを設定します
    /// </summary>
    /// <param name="playerNum">対象プレイヤー番号</param>
    /// <param name="value">設定するフラグ値</param>
    public void SetSpecialUsedThisGame(long playerNum, bool value)
    {
        switch (playerNum)
        {
            case 1: Player1SpecialUsedThisGame = value; break;
            case 2: Player2SpecialUsedThisGame = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(playerNum));
        }
    }

    /// <summary>
    /// 指定したプレイヤーの相手のプレイヤー番号を返します
    /// </summary>
    /// <param name="playerNum">基準となるプレイヤー番号</param>
    /// <returns>相手のプレイヤー番号</returns>
    public long OpponentOf(long playerNum) => playerNum switch
    {
        1 => 2,
        2 => 1,
        _ => throw new ArgumentOutOfRangeException(nameof(playerNum)),
    };

    /// <summary>
    /// Generate the next unique instance ID and increment the sequence.
    /// </summary>
    /// <returns>新しいインスタンス ID 文字列</returns>
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
    /// <returns>採番された新しいデプロイ順</returns>
    public long NextDeployOrder()
    {
        NextDeployOrderSeq++;
        return NextDeployOrderSeq;
    }
}

/// <summary>
/// GameEvent は game_events テーブルにマッピングされるゲームイベントを表現します
/// </summary>
public class GameEvent
{
    public string GameID { get; set; } = "";
    public long SequenceNumber { get; set; }
    public string EventType { get; set; } = "";
    /// <summary>null = system event (turn_start)、1 or 2 = player event。</summary>
    public long? PlayerNum { get; set; }
    public IEventData? EventData { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Engine-internal TurnStart event data. GameService.MapEventData converts this to the
/// viewer-facing TurnStartEventData (with is_my_turn computed per viewer) before wire serialization.
/// Property names are pinned with [JsonPropertyName] to match the legacy hand-rolled
/// Dictionary&lt;string, object&gt; keys (snake_case) so existing JSONB rows remain readable.
/// </summary>
public class TurnStartInternalEventData : IEventData
{
    [System.Text.Json.Serialization.JsonPropertyName("turn")]
    public required long Turn { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("active_player")]
    public required long ActivePlayer { get; init; }
}
