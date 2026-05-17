using System.Text.Json;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Ports;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests;

/// <summary>
/// In-memory ICardCache for tests.
/// </summary>
public class TestCardCache : ICardCache
{
    private readonly Dictionary<string, CardDefinition> _cards = new();

    public void Add(CardDefinition card) => _cards[card.CardId] = card;

    public CardDefinition? Get(string cardId) => _cards.GetValueOrDefault(cardId);
    public CardDefinition MustGet(string cardId) =>
        _cards.TryGetValue(cardId, out var c) ? c : throw new KeyNotFoundException($"Card {cardId} not found");
    public IReadOnlyDictionary<string, CardDefinition> All() => _cards;
    public int Count => _cards.Count;
}

/// <summary>
/// Factory helpers for building test fixtures.
/// </summary>
public static class TestFactory
{
    // ─── Card Builders ────────────────────────────────────────

    /// <summary>
    /// Create a Compute-type card (TP-based, frontend or backend). Subtype defaults to VM.
    /// </summary>
    public static CardDefinition ComputeCard(
        string cardId = "TST-0001",
        string subtype = "VM",
        long tp = 600,
        long av = 1400,
        long mc = 150,
        long slaPenalty = 400,
        long deployTurns = 1,
        bool resizable = true,
        bool elastic = false,
        long elasticIncrement = 0,
        long freeTier = 0,
        long costPerRequest = 0,
        string faction = "SHE",
        string name = "TestCompute")
    {
        return new CardDefinition
        {
            CardId = cardId,
            CardName = name,
            CardType = CardTypes.Compute,
            Subtype = subtype,
            Faction = faction,
            DeployTurns = deployTurns,
            Resizable = resizable,
            Elastic = elastic,
            ElasticIncrement = elasticIncrement,
            FreeTier = freeTier,
            CostPerRequest = costPerRequest,
            ComputeStats = new ComputeStats
            {
                Throughput = tp,
                Availability = av,
                MaintenanceCost = mc,
                SLAPenalty = slaPenalty,
            },
        };
    }

    /// <summary>
    /// Create a Data-type card (Yield-based, backend only). Subtype defaults to Database.
    /// </summary>
    public static CardDefinition DataCard(
        string cardId = "TST-0100",
        string subtype = "Database",
        long yield = 400,
        long av = 800,
        long mc = 100,
        long slaPenalty = 300,
        long deployTurns = 0,
        bool resizable = false,
        bool elastic = false,
        long elasticIncrement = 0,
        long freeTier = 0,
        long costPerRequest = 0,
        string faction = "SHE",
        string name = "TestDB")
    {
        return new CardDefinition
        {
            CardId = cardId,
            CardName = name,
            CardType = CardTypes.Data,
            Subtype = subtype,
            Faction = faction,
            DeployTurns = deployTurns,
            Resizable = resizable,
            Elastic = elastic,
            ElasticIncrement = elasticIncrement,
            FreeTier = freeTier,
            CostPerRequest = costPerRequest,
            DataStats = new DataStats
            {
                Yield = yield,
                Availability = av,
                MaintenanceCost = mc,
                SLAPenalty = slaPenalty,
            },
        };
    }

    /// <summary>
    /// Create an Elastic Container card matching the rulebook example.
    /// base TP=500, increment=100, free_tier=500, cost_per_request=10
    /// </summary>
    public static CardDefinition ElasticContainerCard(string cardId = "TST-0002")
    {
        return ComputeCard(
            cardId: cardId,
            subtype: "Container",
            tp: 500,
            av: 1200,
            mc: 0,
            deployTurns: 0,
            resizable: false,
            elastic: true,
            elasticIncrement: 100,
            freeTier: 500,
            costPerRequest: 10,
            name: "TestContainer");
    }

    /// <summary>
    /// Create a Serverless card (deploy=0, elastic, cost_per_request=0 → MC always 0).
    /// </summary>
    public static CardDefinition ServerlessCard(string cardId = "TST-0003")
    {
        return ComputeCard(
            cardId: cardId,
            subtype: "Serverless",
            tp: 300,
            av: 600,
            mc: 0,
            deployTurns: 0,
            resizable: false,
            elastic: true,
            elasticIncrement: 50,
            freeTier: 300,
            costPerRequest: 0,
            name: "TestServerless");
    }

    /// <summary>
    /// Create an R+E Orchestrator card matching the rulebook example.
    /// </summary>
    public static CardDefinition OrchestratorCard(string cardId = "TST-0004")
    {
        return ComputeCard(
            cardId: cardId,
            subtype: "Orchestrator",
            tp: 600,
            av: 1800,
            mc: 200,
            deployTurns: 2,
            resizable: true,
            elastic: true,
            elasticIncrement: 100,
            freeTier: 600,
            costPerRequest: 10,
            name: "TestOrchestrator");
    }

    /// <summary>
    /// Create a Platform card for support zone.
    /// </summary>
    public static CardDefinition PlatformCard(
        string cardId = "TST-0200",
        string name = "TestPlatform")
    {
        return new CardDefinition
        {
            CardId = cardId,
            CardName = name,
            CardType = "Platform",
            DeployTurns = 2,
        };
    }

    /// <summary>
    /// Create an Attachment card.
    /// </summary>
    public static CardDefinition AttachmentCard(
        string cardId = "TST-0300",
        string name = "TestAttachment")
    {
        return new CardDefinition
        {
            CardId = cardId,
            CardName = name,
            CardType = "Attachment",
            DeployTurns = 0,
        };
    }

    /// <summary>
    /// Create a Reactive card (single-use support that flips face-up and trashes when it fires).
    /// </summary>
    public static CardDefinition ReactiveCard(
        string cardId = "TST-0400",
        string name = "TestReactive")
    {
        return new CardDefinition
        {
            CardId = cardId,
            CardName = name,
            CardType = "Reactive",
            DeployTurns = 0,
        };
    }

    // ─── Resource Instance Builder ────────────────────────────

    public static DeployedResource MakeResource(
        string cardId = "TST-0001",
        string instanceId = "inst_1",
        Rank? rank = Rank.Small,
        InstanceFamily? family = null,
        bool faceUp = true,
        long deployLeft = 0,
        long maxAV = 1400,
        long currentAV = 1400,
        long? maxTP = 600,
        long? currentTP = 600,
        long? maxYield = null,
        long? currentYield = null,
        long damage = 0,
        long elasticBonus = 0)
    {
        return new DeployedResource
        {
            CardID = cardId,
            InstanceID = instanceId,
            Rank = rank,
            InstanceFamily = family,
            FaceUp = faceUp,
            DeployingTurnsLeft = deployLeft,
            MaxAV = maxAV,
            CurrentAV = currentAV,
            MaxTP = maxTP,
            CurrentTP = currentTP,
            MaxYield = maxYield,
            CurrentYield = currentYield,
            Damage = damage,
            ElasticBonus = elasticBonus,
        };
    }

    // ─── Field Builder ────────────────────────────────────────

    public static Field MakeField()
    {
        return new Field();
    }

    // ─── BattleGameState Builder ────────────────────────────────────

    /// <summary>
    /// Create a minimal BattleGameState for testing.
    /// </summary>
    public static BattleGameState MakeGameState(
        long turn = 1,
        Phase phase = Phase.Main,
        long activePlayer = 1,
        long p1Budget = 5000,
        long p2Budget = 5000)
    {
        return new BattleGameState
        {
            GameID = "test-game",
            CurrentTurn = turn,
            CurrentPhase = phase,
            ActivePlayer = activePlayer,
            Player1Budget = p1Budget,
            Player2Budget = p2Budget,
            Player1TimeBank = 480,
            Player2TimeBank = 480,
            TurnStartedAt = DateTime.UtcNow,
            NextInstanceSeq = 1,
        };
    }

    /// <summary>
    /// Create a minimal Game for testing.
    /// </summary>
    public static Game MakeGame()
    {
        return new Game
        {
            GameID = "test-game",
            FirstPlayer = 1,
            Status = GameStatus.Playing,
        };
    }

    /// <summary>
    /// Build a DeckSnapshot with 30 cards (repeating the given card IDs).
    /// </summary>
    public static DeckSnapshot MakeDeck(params string[] cardIds)
    {
        var cards = new List<DeckSnapshotCard>();
        int idx = 0;
        while (cards.Count < InitialValues.DeckSize)
        {
            cards.Add(new DeckSnapshotCard { CardId = cardIds[idx % cardIds.Length] });
            idx++;
        }
        return new DeckSnapshot { DeckID = "deck-1", Cards = cards };
    }
}

/// <summary>
/// Builds an EffectRegistry from embedded cards_gen.json via EffectYamlLoader.
/// </summary>
public static class TestEffectSetup
{
    private static readonly Lazy<(EffectRegistry Registry, ICardCache CardCache)> _cached = new(Build);

    /// <summary>
    /// Returns a shared (EffectRegistry, ICardCache) built from embedded card data.
    /// </summary>
    public static (EffectRegistry Registry, ICardCache CardCache) Get() => _cached.Value;

    private static (EffectRegistry, ICardCache) Build()
    {
        var cardsPath = Environment.GetEnvironmentVariable("CARDS_JSON_PATH")
            ?? FindCardsJson()
            ?? throw new FileNotFoundException(
                "cards_gen.json not found. Set CARDS_JSON_PATH or run generate_from_yaml.py in the common repo.");

        var cards = JsonSerializer.Deserialize<List<CardDefinition>>(
            File.ReadAllText(cardsPath), new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                PropertyNameCaseInsensitive = true,
            })!;

        var cardCache = new TestCardCache();
        foreach (var card in cards)
        {
            cardCache.Add(card);
        }

        var registry = new EffectRegistry();
        var customEffects = new CustomEffectRegistry();
        EffectYamlLoader.LoadFromCards(cards, registry, customEffects);

        return (registry, cardCache);
    }

    private static string? FindCardsJson()
    {
        // Walk up from the test binary to find the battle repo's own cache.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "overload-party-battle",
                "packages", "game-state-dotnet", "cache", "cards_gen.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        return null;
    }
}
