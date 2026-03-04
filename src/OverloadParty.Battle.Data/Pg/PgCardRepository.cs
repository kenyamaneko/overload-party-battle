using System.Text.Json;
using Npgsql;
using OverloadParty.Battle.Data.Json;
using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Data.Pg;

/// <summary>
/// PostgreSQL implementation of ICardRepository using Npgsql.
/// Reads card definitions from the card_definitions table.
/// Note: The DB stores stats/effects as JSONB; passive_effects/platform_effects/attachment_effects
/// are not separate DB columns — they are part of the card JSON loaded at build time.
/// </summary>
public class PgCardRepository(NpgsqlDataSource ds) : ICardRepository
{
    public async Task<List<CardDefinition>> FindAll(CancellationToken ct = default)
    {
        await using var conn = await ds.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(SelectCardSql + " WHERE is_active = true ORDER BY card_no", conn);

        var cards = new List<CardDefinition>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            cards.Add(ReadCardDefinition(reader));
        return cards;
    }

    public async Task<CardDefinition?> FindByCardNo(long cardNo, CancellationToken ct = default)
    {
        await using var conn = await ds.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(SelectCardSql + " WHERE card_no = $1", conn);
        cmd.Parameters.AddWithValue(cardNo);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return ReadCardDefinition(reader);
    }

    // ─── Helpers ─────────────────────────────────────────────────

    private const string SelectCardSql = @"
        SELECT card_no, card_name, resource_label, faction, card_type,
               resizable, elastic, stats, effect_text, effects,
               restriction, is_active, created_at, updated_at
        FROM card_definitions";

    private static CardDefinition ReadCardDefinition(NpgsqlDataReader r)
    {
        var cardType = r.GetString(4);
        var statsJson = r.IsDBNull(7) ? null : r.GetString(7);
        // effects column contains trigger-based effects (not passive/platform/attachment)
        // var effectsJson = r.IsDBNull(9) ? null : r.GetString(9);

        var card = new CardDefinition
        {
            CardNo = r.GetInt64(0),
            CardName = r.GetString(1),
            ResourceLabel = r.GetString(2),
            Faction = r.GetString(3),
            CardType = cardType,
            Resizable = r.GetBoolean(5),
            Elastic = r.GetBoolean(6),
            EffectText = r.IsDBNull(8) ? null : r.GetString(8),
            Restriction = r.GetString(10),
            IsActive = r.GetBoolean(11),
            CreatedAt = r.GetDateTime(12),
            UpdatedAt = r.GetDateTime(13),
        };

        // Parse stats JSONB into typed stats based on card type category
        if (statsJson is not null)
        {
            if (card.IsComputeType)
                card.ComputeStats = JsonSerializer.Deserialize<ComputeStats>(statsJson, DbJsonOptions.Default);
            else if (card.IsDataType)
                card.DataStats = JsonSerializer.Deserialize<DataStats>(statsJson, DbJsonOptions.Default);
        }

        return card;
    }
}
