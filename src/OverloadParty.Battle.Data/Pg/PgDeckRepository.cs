using System.Text;
using Npgsql;
using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Data.Pg;

/// <summary>
/// PostgreSQL implementation of IDeckRepository using Npgsql.
/// </summary>
public class PgDeckRepository(NpgsqlDataSource ds) : IDeckRepository
{
    public async Task Create(Deck deck, List<DeckCard> cards, CancellationToken ct = default)
    {
        await using var conn = await ds.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        await using (var cmd = new NpgsqlCommand(@"
            INSERT INTO decks (player_id, deck_name, is_valid, playmat_no, sleeve_no, created_at, updated_at)
            VALUES ($1,$2,$3,$4,$5,$6,$7) RETURNING deck_id", conn, tx))
        {
            cmd.Parameters.AddWithValue(deck.PlayerID);
            cmd.Parameters.AddWithValue(deck.DeckName);
            cmd.Parameters.AddWithValue(deck.IsValid);
            cmd.Parameters.AddWithValue((object?)deck.PlaymatNo ?? DBNull.Value);
            cmd.Parameters.AddWithValue((object?)deck.SleeveNo ?? DBNull.Value);
            cmd.Parameters.AddWithValue(deck.CreatedAt);
            cmd.Parameters.AddWithValue(deck.UpdatedAt);
            deck.DeckID = (long)(await cmd.ExecuteScalarAsync(ct))!;
        }

        foreach (var c in cards)
            c.DeckID = deck.DeckID;

        await BulkInsertDeckCards(conn, tx, cards, ct);
        await tx.CommitAsync(ct);
    }

    public async Task<List<Deck>> FindByPlayerID(string playerID, CancellationToken ct = default)
    {
        await using var conn = await ds.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            SelectDeckSql + " WHERE player_id = $1 ORDER BY updated_at DESC", conn);
        cmd.Parameters.AddWithValue(playerID);

        var decks = new List<Deck>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            decks.Add(ReadDeck(reader));
        return decks;
    }

    public async Task<Deck?> FindByID(string playerID, long deckID, CancellationToken ct = default)
    {
        await using var conn = await ds.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            SelectDeckSql + " WHERE player_id = $1 AND deck_id = $2", conn);
        cmd.Parameters.AddWithValue(playerID);
        cmd.Parameters.AddWithValue(deckID);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return ReadDeck(reader);
    }

    public async Task<List<DeckCard>> GetDeckCards(string playerID, long deckID, CancellationToken ct = default)
    {
        await using var conn = await ds.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(@"
            SELECT player_id, deck_id, card_no, illustration_variant, count
            FROM deck_cards WHERE player_id = $1 AND deck_id = $2", conn);
        cmd.Parameters.AddWithValue(playerID);
        cmd.Parameters.AddWithValue(deckID);

        var cards = new List<DeckCard>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            cards.Add(new DeckCard
            {
                PlayerID = reader.GetString(0),
                DeckID = reader.GetInt64(1),
                CardNo = reader.GetInt64(2),
                IllustrationVariant = reader.GetInt64(3),
                Count = reader.GetInt32(4),
            });
        }
        return cards;
    }

    public async Task<List<long>> GetDeckCardNos(string playerID, long deckID, CancellationToken ct = default)
    {
        await using var conn = await ds.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT card_no, count FROM deck_cards WHERE player_id = $1 AND deck_id = $2", conn);
        cmd.Parameters.AddWithValue(playerID);
        cmd.Parameters.AddWithValue(deckID);

        var cardNos = new List<long>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var cardNo = reader.GetInt64(0);
            var count = reader.GetInt32(1);
            for (int i = 0; i < count; i++)
                cardNos.Add(cardNo);
        }
        return cardNos;
    }

    public async Task<List<PlayerCard>> GetPlayerCards(string playerID, CancellationToken ct = default)
    {
        await using var conn = await ds.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(@"
            SELECT player_id, card_no, illustration_variant, count
            FROM player_cards WHERE player_id = $1
            ORDER BY card_no, illustration_variant", conn);
        cmd.Parameters.AddWithValue(playerID);

        var cards = new List<PlayerCard>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            cards.Add(new PlayerCard
            {
                PlayerID = reader.GetString(0),
                CardNo = reader.GetInt64(1),
                IllustrationVariant = reader.GetInt64(2),
                Count = reader.GetInt32(3),
            });
        }
        return cards;
    }

    public async Task Update(Deck deck, List<DeckCard> cards, CancellationToken ct = default)
    {
        await using var conn = await ds.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        await using (var cmd = new NpgsqlCommand(
            "DELETE FROM deck_cards WHERE player_id = $1 AND deck_id = $2", conn, tx))
        {
            cmd.Parameters.AddWithValue(deck.PlayerID);
            cmd.Parameters.AddWithValue(deck.DeckID);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        deck.UpdatedAt = DateTime.UtcNow;
        await using (var cmd = new NpgsqlCommand(@"
            UPDATE decks SET deck_name = $1, is_valid = $2, playmat_no = $3, sleeve_no = $4, updated_at = $5
            WHERE player_id = $6 AND deck_id = $7", conn, tx))
        {
            cmd.Parameters.AddWithValue(deck.DeckName);
            cmd.Parameters.AddWithValue(deck.IsValid);
            cmd.Parameters.AddWithValue((object?)deck.PlaymatNo ?? DBNull.Value);
            cmd.Parameters.AddWithValue((object?)deck.SleeveNo ?? DBNull.Value);
            cmd.Parameters.AddWithValue(deck.UpdatedAt);
            cmd.Parameters.AddWithValue(deck.PlayerID);
            cmd.Parameters.AddWithValue(deck.DeckID);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await BulkInsertDeckCards(conn, tx, cards, ct);
        await tx.CommitAsync(ct);
    }

    public async Task Delete(string playerID, long deckID, CancellationToken ct = default)
    {
        await using var conn = await ds.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "DELETE FROM decks WHERE player_id = $1 AND deck_id = $2", conn);
        cmd.Parameters.AddWithValue(playerID);
        cmd.Parameters.AddWithValue(deckID);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ─── Helpers ─────────────────────────────────────────────────

    private const string SelectDeckSql = @"
        SELECT player_id, deck_id, deck_name, is_valid, playmat_no, sleeve_no, created_at, updated_at
        FROM decks";

    private static Deck ReadDeck(NpgsqlDataReader r)
    {
        return new Deck
        {
            PlayerID = r.GetString(0),
            DeckID = r.GetInt64(1),
            DeckName = r.GetString(2),
            IsValid = r.GetBoolean(3),
            PlaymatNo = r.IsDBNull(4) ? null : r.GetInt64(4),
            SleeveNo = r.IsDBNull(5) ? null : r.GetInt64(5),
            CreatedAt = r.GetDateTime(6),
            UpdatedAt = r.GetDateTime(7),
        };
    }

    private static async Task BulkInsertDeckCards(
        NpgsqlConnection conn, NpgsqlTransaction tx, List<DeckCard> cards, CancellationToken ct)
    {
        if (cards.Count == 0) return;

        var sb = new StringBuilder(
            "INSERT INTO deck_cards (player_id, deck_id, card_no, illustration_variant, count) VALUES ");

        var cmd = new NpgsqlCommand { Connection = conn, Transaction = tx };
        for (int i = 0; i < cards.Count; i++)
        {
            if (i > 0) sb.Append(',');
            var b = i * 5 + 1;
            sb.Append($"(${b},${b + 1},${b + 2},${b + 3},${b + 4})");
            cmd.Parameters.AddWithValue(cards[i].PlayerID);
            cmd.Parameters.AddWithValue(cards[i].DeckID);
            cmd.Parameters.AddWithValue(cards[i].CardNo);
            cmd.Parameters.AddWithValue(cards[i].IllustrationVariant);
            cmd.Parameters.AddWithValue(cards[i].Count);
        }

        cmd.CommandText = sb.ToString();
        await using (cmd)
        {
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }
}
