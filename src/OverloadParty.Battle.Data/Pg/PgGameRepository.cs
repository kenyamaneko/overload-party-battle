using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using OverloadParty.Battle.Data.Json;
using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Data.Pg;

/// <summary>
/// PostgreSQL implementation of IGameRepository using Npgsql.
/// </summary>
public class PgGameRepository(NpgsqlDataSource ds) : IGameRepository
{
    public async Task CreateGame(Game game, GameState state, CancellationToken ct = default)
    {
        await using var conn = await ds.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        await using (var cmd = new NpgsqlCommand(@"
            INSERT INTO games (
                game_id, player1_id, player2_id,
                player1_deck_snapshot, player2_deck_snapshot,
                status, npc1_model, npc2_model, winner_num, win_reason,
                engine_version, card_data_version,
                created_at, updated_at, finished_at
            ) VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13,$14,$15)", conn, tx))
        {
            cmd.Parameters.AddWithValue(game.GameID);
            cmd.Parameters.Add(NullableUuidParam(game.Player1ID));
            cmd.Parameters.Add(NullableUuidParam(game.Player2ID));
            cmd.Parameters.Add(JsonbParam(game.Player1DeckSnapshot));
            cmd.Parameters.Add(JsonbParam(game.Player2DeckSnapshot));
            cmd.Parameters.AddWithValue(game.Status.ToWireString());
            cmd.Parameters.AddWithValue((object?)game.Npc1Model ?? DBNull.Value);
            cmd.Parameters.AddWithValue((object?)game.Npc2Model ?? DBNull.Value);
            cmd.Parameters.AddWithValue((object?)game.WinnerNum ?? DBNull.Value);
            cmd.Parameters.AddWithValue((object?)game.WinReason ?? DBNull.Value);
            cmd.Parameters.AddWithValue(game.EngineVersion);
            cmd.Parameters.AddWithValue(game.CardDataVersion);
            cmd.Parameters.AddWithValue(game.CreatedAt);
            cmd.Parameters.AddWithValue(game.UpdatedAt);
            cmd.Parameters.AddWithValue((object?)game.FinishedAt ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await InsertGameState(conn, tx, state, ct);
        await tx.CommitAsync(ct);
    }

    public async Task<Game?> GetGame(string gameID, CancellationToken ct = default)
    {
        await using var conn = await ds.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(@"
            SELECT game_id, player1_id, player2_id,
                   player1_deck_snapshot, player2_deck_snapshot,
                   status, npc1_model, npc2_model, winner_num, win_reason,
                   created_at, updated_at, finished_at,
                   engine_version, card_data_version
            FROM games WHERE game_id = $1", conn);
        cmd.Parameters.AddWithValue(gameID);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }
        return ReadGame(reader);
    }

    public async Task<GameState?> GetGameState(string gameID, CancellationToken ct = default)
    {
        await using var conn = await ds.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(SelectGameStateSql, conn);
        cmd.Parameters.AddWithValue(gameID);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }
        return ReadGameState(reader);
    }

    public async Task UpdateGameState(string gameID, Func<GameState, Task> fn, PendingAction? pendingAction = null, CancellationToken ct = default)
    {
        await using var conn = await ds.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        GameState state;
        await using (var cmd = new NpgsqlCommand(SelectGameStateSql + " FOR UPDATE", conn, tx))
        {
            cmd.Parameters.AddWithValue(gameID);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
            {
                throw new InvalidOperationException($"game state {gameID} not found");
            }
            state = ReadGameState(reader);
        }

        await fn(state);

        state.Version++;
        state.UpdatedAt = DateTime.UtcNow;

        await using (var cmd = new NpgsqlCommand(@"
            UPDATE game_states SET
                version = $1, current_turn = $2, current_phase = $3, active_player = $4,
                player1_budget = $5, player1_insight_pool = $6, player1_field = $7, player1_hand = $8,
                player1_repository = $9, player1_trash = $10, player1_time_bank = $11,
                player2_budget = $12, player2_insight_pool = $13, player2_field = $14, player2_hand = $15,
                player2_repository = $16, player2_trash = $17, player2_time_bank = $18,
                chain_stack = $19, current_action_timer = $20, next_instance_seq = $21, updated_at = $22
            WHERE game_id = $23", conn, tx))
        {
            AddGameStateParams(cmd, state);
            cmd.Parameters.AddWithValue(state.GameID);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        if (pendingAction is not null)
        {
            await using var cmd = new NpgsqlCommand(@"
                INSERT INTO game_actions (
                    game_id, seq, player_id, action_type, action_data, created_at
                ) VALUES (
                    $1,
                    COALESCE((SELECT MAX(seq) FROM game_actions WHERE game_id = $1), 0) + 1,
                    $2, $3, $4, $5
                )", conn, tx);
            cmd.Parameters.AddWithValue(gameID);
            cmd.Parameters.AddWithValue(pendingAction.PlayerID);
            cmd.Parameters.AddWithValue(pendingAction.ActionType);
            cmd.Parameters.Add(JsonbParam(pendingAction.ActionData));
            cmd.Parameters.AddWithValue(DateTime.UtcNow);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
    }

    public async Task AppendEvent(GameEvent evt, CancellationToken ct = default)
    {
        await using var conn = await ds.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO game_events (
                game_id, sequence_number, event_type, player_id, event_data, created_at
            ) VALUES ($1,$2,$3,$4,$5,$6)", conn);
        cmd.Parameters.AddWithValue(evt.GameID);
        cmd.Parameters.AddWithValue(evt.SequenceNumber);
        cmd.Parameters.AddWithValue(evt.EventType);
        cmd.Parameters.Add(NullableUuidParam(evt.PlayerID));
        cmd.Parameters.Add(JsonbParam(evt.EventData));
        cmd.Parameters.AddWithValue(evt.CreatedAt);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task FinishGame(string gameID, long winnerNum, string winReason, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        await using var conn = await ds.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(@"
            UPDATE games SET status = $1, winner_num = $2, win_reason = $3,
                             finished_at = $4, updated_at = $5
            WHERE game_id = $6", conn);
        cmd.Parameters.AddWithValue(GameStatus.Finished.ToWireString());
        cmd.Parameters.AddWithValue(winnerNum);
        cmd.Parameters.AddWithValue(winReason);
        cmd.Parameters.AddWithValue(now);
        cmd.Parameters.AddWithValue(now);
        cmd.Parameters.AddWithValue(gameID);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<long> GetEventCount(string gameID, CancellationToken ct = default)
    {
        await using var conn = await ds.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM game_events WHERE game_id = $1", conn);
        cmd.Parameters.AddWithValue(gameID);
        return (long)(await cmd.ExecuteScalarAsync(ct))!;
    }

    public async Task UpdateGameStatus(string gameID, GameStatus status, CancellationToken ct = default)
    {
        await using var conn = await ds.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(@"
            UPDATE games SET status = $1, updated_at = $2 WHERE game_id = $3", conn);
        cmd.Parameters.AddWithValue(status.ToWireString());
        cmd.Parameters.AddWithValue(DateTime.UtcNow);
        cmd.Parameters.AddWithValue(gameID);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<List<GameEvent>> GetEvents(string gameID, CancellationToken ct = default)
    {
        await using var conn = await ds.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(@"
            SELECT game_id, sequence_number, event_type, player_id, event_data, created_at
            FROM game_events
            WHERE game_id = $1
            ORDER BY sequence_number", conn);
        cmd.Parameters.AddWithValue(gameID);

        var events = new List<GameEvent>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            events.Add(new GameEvent
            {
                GameID = reader.GetString(0),
                SequenceNumber = reader.GetInt64(1),
                EventType = reader.GetString(2),
                PlayerID = reader.IsDBNull(3) ? null : reader.GetGuid(3).ToString(),
                EventData = reader.IsDBNull(4)
                    ? null
                    : JsonSerializer.Deserialize<Dictionary<string, object>>(reader.GetString(4), DbJsonOptions.Default),
                CreatedAt = reader.GetDateTime(5),
            });
        }
        return events;
    }

    public async Task<GameState?> GetInitialState(string gameID, CancellationToken ct = default)
    {
        await using var conn = await ds.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT initial_state FROM game_states WHERE game_id = $1", conn);
        cmd.Parameters.AddWithValue(gameID);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }
        return reader.IsDBNull(0)
            ? null
            : JsonSerializer.Deserialize<GameState>(reader.GetString(0), DbJsonOptions.Default);
    }

    // ─── SQL constants ──────────────────────────────────────────

    private const string SelectGameStateSql = @"
        SELECT game_id, version, current_turn, current_phase, active_player,
               player1_budget, player1_insight_pool, player1_field, player1_hand,
               player1_repository, player1_trash, player1_time_bank,
               player2_budget, player2_insight_pool, player2_field, player2_hand,
               player2_repository, player2_trash, player2_time_bank,
               chain_stack, current_action_timer, next_instance_seq, updated_at
        FROM game_states WHERE game_id = $1";

    // ─── Helpers ─────────────────────────────────────────────────

    private static Game ReadGame(NpgsqlDataReader r)
    {
        return new Game
        {
            GameID = r.GetString(0),
            Player1ID = r.IsDBNull(1) ? "" : r.GetGuid(1).ToString(),
            Player2ID = r.IsDBNull(2) ? "" : r.GetGuid(2).ToString(),
            Player1DeckSnapshot = r.IsDBNull(3)
                ? null
                : JsonSerializer.Deserialize<DeckSnapshot>(r.GetString(3), DbJsonOptions.Default),
            Player2DeckSnapshot = r.IsDBNull(4)
                ? null
                : JsonSerializer.Deserialize<DeckSnapshot>(r.GetString(4), DbJsonOptions.Default),
            Status = EnumExtensions.ParseGameStatus(r.GetString(5)),
            Npc1Model = r.IsDBNull(6) ? null : r.GetString(6),
            Npc2Model = r.IsDBNull(7) ? null : r.GetString(7),
            WinnerNum = r.IsDBNull(8) ? null : r.GetInt16(8),
            WinReason = r.IsDBNull(9) ? null : r.GetString(9),
            CreatedAt = r.GetDateTime(10),
            UpdatedAt = r.GetDateTime(11),
            FinishedAt = r.IsDBNull(12) ? null : r.GetDateTime(12),
            EngineVersion = r.IsDBNull(13) ? "" : r.GetString(13),
            CardDataVersion = r.IsDBNull(14) ? "" : r.GetString(14),
        };
    }

    private static GameState ReadGameState(NpgsqlDataReader r)
    {
        return new GameState
        {
            GameID = r.GetString(0),
            Version = r.GetInt64(1),
            CurrentTurn = r.GetInt64(2),
            CurrentPhase = EnumExtensions.ParsePhase(r.GetString(3)),
            ActivePlayer = r.GetInt64(4),

            Player1Budget = r.GetInt64(5),
            Player1InsightPool = r.GetInt64(6),
            Player1Field = JsonSerializer.Deserialize<Field>(r.GetString(7), DbJsonOptions.Default) ?? new(),
            Player1Hand = JsonSerializer.Deserialize<List<UndeployedCard>>(r.GetString(8), DbJsonOptions.Default) ?? [],
            Player1Repository = JsonSerializer.Deserialize<List<UndeployedCard>>(r.GetString(9), DbJsonOptions.Default) ?? [],
            Player1Trash = JsonSerializer.Deserialize<List<UndeployedCard>>(r.GetString(10), DbJsonOptions.Default) ?? [],
            Player1TimeBank = r.GetInt64(11),

            Player2Budget = r.GetInt64(12),
            Player2InsightPool = r.GetInt64(13),
            Player2Field = JsonSerializer.Deserialize<Field>(r.GetString(14), DbJsonOptions.Default) ?? new(),
            Player2Hand = JsonSerializer.Deserialize<List<UndeployedCard>>(r.GetString(15), DbJsonOptions.Default) ?? [],
            Player2Repository = JsonSerializer.Deserialize<List<UndeployedCard>>(r.GetString(16), DbJsonOptions.Default) ?? [],
            Player2Trash = JsonSerializer.Deserialize<List<UndeployedCard>>(r.GetString(17), DbJsonOptions.Default) ?? [],
            Player2TimeBank = r.GetInt64(18),

            ChainStack = JsonSerializer.Deserialize<List<ChainEntry>>(r.GetString(19), DbJsonOptions.Default) ?? [],
            CurrentActionTimer = r.IsDBNull(20) ? null : r.GetInt64(20),
            NextInstanceSeq = r.GetInt64(21),
            UpdatedAt = r.GetDateTime(22),
        };
    }

    private async Task InsertGameState(NpgsqlConnection conn, NpgsqlTransaction tx, GameState state, CancellationToken ct)
    {
        var stateJson = JsonSerializer.Serialize(state, DbJsonOptions.Default);

        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO game_states (
                game_id, initial_state, version, current_turn, current_phase, active_player,
                player1_budget, player1_insight_pool, player1_field, player1_hand,
                player1_repository, player1_trash, player1_time_bank,
                player2_budget, player2_insight_pool, player2_field, player2_hand,
                player2_repository, player2_trash, player2_time_bank,
                chain_stack, current_action_timer, next_instance_seq, updated_at
            ) VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13,$14,$15,$16,$17,$18,$19,$20,$21,$22,$23,$24)", conn, tx);
        cmd.Parameters.AddWithValue(state.GameID);
        cmd.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Jsonb, Value = stateJson });
        AddGameStateParams(cmd, state);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static void AddGameStateParams(NpgsqlCommand cmd, GameState state)
    {
        cmd.Parameters.AddWithValue(state.Version);
        cmd.Parameters.AddWithValue(state.CurrentTurn);
        cmd.Parameters.AddWithValue(state.CurrentPhase.ToWireString());
        cmd.Parameters.AddWithValue(state.ActivePlayer);

        cmd.Parameters.AddWithValue(state.Player1Budget);
        cmd.Parameters.AddWithValue(state.Player1InsightPool);
        cmd.Parameters.Add(JsonbParam(state.Player1Field));
        cmd.Parameters.Add(JsonbParam(state.Player1Hand));
        cmd.Parameters.Add(JsonbParam(state.Player1Repository));
        cmd.Parameters.Add(JsonbParam(state.Player1Trash));
        cmd.Parameters.AddWithValue(state.Player1TimeBank);

        cmd.Parameters.AddWithValue(state.Player2Budget);
        cmd.Parameters.AddWithValue(state.Player2InsightPool);
        cmd.Parameters.Add(JsonbParam(state.Player2Field));
        cmd.Parameters.Add(JsonbParam(state.Player2Hand));
        cmd.Parameters.Add(JsonbParam(state.Player2Repository));
        cmd.Parameters.Add(JsonbParam(state.Player2Trash));
        cmd.Parameters.AddWithValue(state.Player2TimeBank);

        cmd.Parameters.Add(JsonbParam(state.ChainStack));
        cmd.Parameters.AddWithValue((object?)state.CurrentActionTimer ?? DBNull.Value);
        cmd.Parameters.AddWithValue(state.NextInstanceSeq);
        cmd.Parameters.AddWithValue(state.UpdatedAt);
    }

    private static NpgsqlParameter JsonbParam<T>(T? value)
    {
        var json = value is null ? (object)DBNull.Value : JsonSerializer.Serialize(value, DbJsonOptions.Default);
        return new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Jsonb, Value = json };
    }

    private static NpgsqlParameter UuidParam(string value)
    {
        return new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Uuid, Value = Guid.Parse(value) };
    }

    private static NpgsqlParameter NullableUuidParam(string? value)
    {
        return new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Uuid,
            Value = string.IsNullOrEmpty(value) ? DBNull.Value : Guid.Parse(value),
        };
    }
}
