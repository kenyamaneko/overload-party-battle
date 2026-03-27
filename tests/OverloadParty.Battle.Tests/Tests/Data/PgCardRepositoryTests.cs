using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using OverloadParty.Battle.Data.Pg;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Data;

public class PgCardRepositoryTests
{
    private readonly NpgsqlDataSource? _ds = PgTestFixture.DataSource;

    private PgCardRepository? TryCreateRepo()
    {
        if (_ds is null) return null;
        return new PgCardRepository(_ds);
    }

    private async Task SeedCards(params CardSeed[] cards)
    {
        await using var conn = await _ds!.OpenConnectionAsync();
        foreach (var c in cards)
        {
            await using var cmd = new NpgsqlCommand(@"
                INSERT INTO card_definitions
                    (card_id, card_name, resource_label, faction, card_type,
                     resizable, elastic, stats, effect_text, effects,
                     restriction, is_active, created_at, updated_at)
                VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,NOW(),NOW())
                ON CONFLICT (card_id) DO NOTHING", conn);
            cmd.Parameters.AddWithValue(c.CardId);
            cmd.Parameters.AddWithValue(c.CardName);
            cmd.Parameters.AddWithValue(c.ResourceLabel);
            cmd.Parameters.AddWithValue(c.Faction);
            cmd.Parameters.AddWithValue(c.CardType);
            cmd.Parameters.AddWithValue(c.Resizable);
            cmd.Parameters.AddWithValue(c.Elastic);
            cmd.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Jsonb, Value = c.StatsJson });
            cmd.Parameters.AddWithValue((object?)c.EffectText ?? DBNull.Value);
            cmd.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Jsonb, Value = DBNull.Value });
            cmd.Parameters.AddWithValue(c.Restriction);
            cmd.Parameters.AddWithValue(c.IsActive);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private record CardSeed(
        string CardId, string CardName, string ResourceLabel, string Faction,
        string CardType, bool Resizable, bool Elastic, string StatsJson,
        string? EffectText, string Restriction, bool IsActive);

    private static CardSeed ComputeSeed(string cardId, string name = "TestVM", bool active = true) => new(
        cardId, name, "VM", "SHE", "Compute", true, false,
        JsonSerializer.Serialize(new { throughput = 600, availability = 1400, maintenance_cost = 150, sla_penalty = 400 }),
        null, "unlimited", active);

    private static CardSeed DataSeed(string cardId, string name = "TestDB", bool active = true) => new(
        cardId, name, "DB", "SHE", "Database", false, false,
        JsonSerializer.Serialize(new { yield = 400, availability = 800, maintenance_cost = 100, sla_penalty = 300 }),
        null, "unlimited", active);

    // ─── FindAll ────────────────────────────────────────────

    [Fact]
    public async Task FindAll_returns_only_active_cards_sorted_by_card_id()
    {
        var repo = TryCreateRepo();
        if (repo is null) return;
        await SeedCards(
            ComputeSeed("SH-0009", "Card10"),
            ComputeSeed("SH-0019", "Card20"),
            ComputeSeed("SH-0004", "Card5"),
            ComputeSeed("NT-0008", "Inactive", active: false));

        var cards = await repo.FindAll();

        cards.Should().HaveCount(3);
        cards[0].CardId.Should().Be("SH-0004");
        cards[1].CardId.Should().Be("SH-0009");
        cards[2].CardId.Should().Be("SH-0019");
        cards.Should().OnlyContain(c => c.IsActive);
    }

    // ─── FindByCardId ───────────────────────────────────────

    [Fact]
    public async Task FindByCardId_returns_card_with_parsed_compute_stats()
    {
        var repo = TryCreateRepo();
        if (repo is null) return;
        await SeedCards(ComputeSeed("SH-0001", "VM Instance"));

        var card = await repo.FindByCardId("SH-0001");

        card.Should().NotBeNull();
        card!.CardId.Should().Be("SH-0001");
        card.CardName.Should().Be("VM Instance");
        card.CardType.Should().Be("Compute");
        card.Resizable.Should().BeTrue();
        card.IsComputeType.Should().BeTrue();
        card.ComputeStats.Should().NotBeNull();
        card.ComputeStats!.Throughput.Should().Be(600);
        card.ComputeStats.Availability.Should().Be(1400);
        card.ComputeStats.MaintenanceCost.Should().Be(150);
        card.ComputeStats.SLAPenalty.Should().Be(400);
    }

    [Fact]
    public async Task FindByCardId_returns_card_with_parsed_data_stats()
    {
        var repo = TryCreateRepo();
        if (repo is null) return;
        await SeedCards(DataSeed("NT-0009", "SQL Database"));

        var card = await repo.FindByCardId("NT-0009");

        card.Should().NotBeNull();
        card!.CardType.Should().Be("Database");
        card.IsDataType.Should().BeTrue();
        card.DataStats.Should().NotBeNull();
        card.DataStats!.Yield.Should().Be(400);
        card.DataStats.Availability.Should().Be(800);
    }

    [Fact]
    public async Task FindByCardId_returns_null_when_not_found()
    {
        var repo = TryCreateRepo();
        if (repo is null) return;

        var card = await repo.FindByCardId("TEST-0999");
        card.Should().BeNull();
    }

    [Fact]
    public async Task FindByCardId_returns_inactive_card()
    {
        var repo = TryCreateRepo();
        if (repo is null) return;
        await SeedCards(ComputeSeed("SL-0004", "Old Card", active: false));

        // FindByCardId does not filter by is_active
        var card = await repo.FindByCardId("SL-0004");
        card.Should().NotBeNull();
        card!.IsActive.Should().BeFalse();
    }
}
