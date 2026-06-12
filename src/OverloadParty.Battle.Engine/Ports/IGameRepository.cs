using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Ports;

/// <summary>
/// Action data to be atomically appended within an UpdateGameState transaction.
/// </summary>
public record PendingAction(long PlayerNum, string ActionType, object ActionData);

/// <summary>
/// Data access contract for the game engine.
/// Engine depends only on this interface; implementation lives in the Data layer.
/// </summary>
public interface IGameRepository
{
    /// <summary>Creates a new game with its initial state.</summary>
    /// <param name="game">The game metadata.</param>
    /// <param name="state">The initial game state.</param>
    /// <param name="ct">キャンセル用トークン。</param>
    Task CreateGame(Game game, BattleGameState state, CancellationToken ct = default);

    /// <summary>Returns the game metadata, or <c>null</c> if not found.</summary>
    /// <param name="gameID">対象のゲーム ID。</param>
    /// <param name="ct">キャンセル用トークン。</param>
    /// <returns>ゲームメタデータ。存在しなければ null。</returns>
    Task<Game?> GetGame(string gameID, CancellationToken ct = default);

    /// <summary>Returns the current game state, or <c>null</c> if not found.</summary>
    /// <param name="gameID">対象のゲーム ID。</param>
    /// <param name="ct">キャンセル用トークン。</param>
    /// <returns>現在のゲーム状態。存在しなければ null。</returns>
    Task<BattleGameState?> GetGameState(string gameID, CancellationToken ct = default);

    /// <summary>
    /// Updates BattleGameState within a read-write transaction.
    /// The callback receives the current state; it must modify it in place.
    /// The implementation handles optimistic locking (version check + increment).
    /// If <paramref name="pendingAction"/> is provided, it is appended atomically
    /// within the same transaction with a safe auto-incremented seq number.
    /// </summary>
    /// <param name="gameID">対象のゲーム ID。</param>
    /// <param name="fn">状態を書き換えるコールバック。</param>
    /// <param name="pendingAction">同一トランザクションで追記するアクション。</param>
    /// <param name="ct">キャンセル用トークン。</param>
    Task UpdateGameState(string gameID, Func<BattleGameState, Task> fn, PendingAction? pendingAction = null, CancellationToken ct = default);

    /// <summary>Persists a game event to the event log.</summary>
    /// <param name="evt">The event to append.</param>
    /// <param name="ct">キャンセル用トークン。</param>
    Task AppendEvent(GameEvent evt, CancellationToken ct = default);

    /// <summary>Marks the game as finished and records the winner and reason.</summary>
    /// <param name="gameID">対象のゲーム ID。</param>
    /// <param name="winnerNum">The winner number (0=draw, 1 or 2).</param>
    /// <param name="winReason">The wire-format win reason string.</param>
    /// <param name="ct">キャンセル用トークン。</param>
    Task FinishGame(string gameID, long winnerNum, string winReason, CancellationToken ct = default);

    /// <summary>Returns the total number of events recorded for a game.</summary>
    /// <param name="gameID">対象のゲーム ID。</param>
    /// <param name="ct">キャンセル用トークン。</param>
    /// <returns>記録されているイベント総数。</returns>
    Task<long> GetEventCount(string gameID, CancellationToken ct = default);

    /// <summary>Returns all events for a game in order.</summary>
    /// <param name="gameID">対象のゲーム ID。</param>
    /// <param name="ct">キャンセル用トークン。</param>
    /// <returns>順序付きのイベント一覧。</returns>
    Task<List<GameEvent>> GetEvents(string gameID, CancellationToken ct = default);

    /// <summary>Returns the initial game state (for replay).</summary>
    /// <param name="gameID">対象のゲーム ID。</param>
    /// <param name="ct">キャンセル用トークン。</param>
    /// <returns>初期ゲーム状態。存在しなければ null。</returns>
    Task<BattleGameState?> GetInitialState(string gameID, CancellationToken ct = default);

    /// <summary>
    /// 対戦者 2 名分の player display スナップショットを永続化する。
    /// 外部から渡された name / level を信頼してそのまま保存する (account 同期依存なし)。
    /// </summary>
    /// <param name="gameID">対象のゲーム ID。</param>
    /// <param name="summaries">保存する player display スナップショット。</param>
    /// <param name="ct">キャンセル用トークン。</param>
    Task SavePlayerSummaries(string gameID, IReadOnlyList<PlayerSummarySnapshot> summaries, CancellationToken ct = default);

    /// <summary>
    /// 指定 game の player display スナップショットを返す。存在しない (NPC 戦等) 場合は空リスト。
    /// </summary>
    /// <param name="gameID">対象のゲーム ID。</param>
    /// <param name="ct">キャンセル用トークン。</param>
    /// <returns>player display スナップショット。存在しない場合は空リスト。</returns>
    Task<List<PlayerSummarySnapshot>> GetPlayerSummaries(string gameID, CancellationToken ct = default);
}
