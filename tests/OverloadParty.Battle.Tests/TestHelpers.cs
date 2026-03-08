using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests;

/// <summary>
/// In-memory ICardCache for tests.
/// </summary>
public class TestCardCache : ICardCache
{
    private readonly Dictionary<long, CardDefinition> _cards = new();

    public void Add(CardDefinition card) => _cards[card.CardNo] = card;

    public CardDefinition? Get(long cardNo) => _cards.GetValueOrDefault(cardNo);
    public CardDefinition MustGet(long cardNo) =>
        _cards.TryGetValue(cardNo, out var c) ? c : throw new KeyNotFoundException($"Card {cardNo} not found");
    public IReadOnlyDictionary<long, CardDefinition> All() => _cards;
    public int Count => _cards.Count;
}

/// <summary>
/// Factory helpers for building test fixtures.
/// </summary>
public static class TestFactory
{
    // ─── Card Builders ────────────────────────────────────────

    /// <summary>
    /// Create a Compute-type card (TP-based, frontend or backend).
    /// </summary>
    public static CardDefinition ComputeCard(
        long cardNo = 1,
        string cardType = "Compute",
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
        string faction = "SD",
        string name = "TestCompute")
    {
        return new CardDefinition
        {
            CardNo = cardNo,
            CardName = name,
            CardType = cardType,
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
    /// Create a Data-type card (Yield-based, backend only).
    /// </summary>
    public static CardDefinition DataCard(
        long cardNo = 100,
        string cardType = "Database",
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
        string faction = "SD",
        string name = "TestDB")
    {
        return new CardDefinition
        {
            CardNo = cardNo,
            CardName = name,
            CardType = cardType,
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
    public static CardDefinition ElasticContainerCard(long cardNo = 2)
    {
        return ComputeCard(
            cardNo: cardNo,
            cardType: "Container",
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
    public static CardDefinition ServerlessCard(long cardNo = 3)
    {
        return ComputeCard(
            cardNo: cardNo,
            cardType: "Serverless",
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
    public static CardDefinition OrchestratorCard(long cardNo = 4)
    {
        return ComputeCard(
            cardNo: cardNo,
            cardType: "Orchestrator",
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
        long cardNo = 200,
        string name = "TestPlatform",
        List<PlatformEffect>? platformEffects = null)
    {
        return new CardDefinition
        {
            CardNo = cardNo,
            CardName = name,
            CardType = "Platform",
            DeployTurns = 2,
            PlatformEffects = platformEffects ?? [],
        };
    }

    /// <summary>
    /// Create an Attachment card.
    /// </summary>
    public static CardDefinition AttachmentCard(
        long cardNo = 300,
        string name = "TestAttachment",
        List<AttachmentEffect>? attachmentEffects = null)
    {
        return new CardDefinition
        {
            CardNo = cardNo,
            CardName = name,
            CardType = "Attachment",
            DeployTurns = 0,
            AttachmentEffects = attachmentEffects ?? [],
        };
    }

    // ─── Resource Instance Builder ────────────────────────────

    public static ResourceInstance MakeResource(
        long cardId = 1,
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
        return new ResourceInstance
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

    // ─── GameState Builder ────────────────────────────────────

    /// <summary>
    /// Create a minimal GameState for testing.
    /// </summary>
    public static GameState MakeGameState(
        long turn = 1,
        Phase phase = Phase.Main,
        long activePlayer = 1,
        long p1Budget = 5000,
        long p2Budget = 5000)
    {
        return new GameState
        {
            GameID = "test-game",
            CurrentTurn = turn,
            CurrentPhase = phase,
            ActivePlayer = activePlayer,
            Player1Budget = p1Budget,
            Player2Budget = p2Budget,
            Player1TimeBank = 480,
            Player2TimeBank = 480,
            NextInstanceSeq = 1,
        };
    }

    /// <summary>
    /// Create a minimal Game for testing.
    /// </summary>
    public static Game MakeGame(string p1 = "player1", string p2 = "player2")
    {
        return new Game
        {
            GameID = "test-game",
            Player1ID = p1,
            Player2ID = p2,
            Status = GameStatus.Playing,
        };
    }

    /// <summary>
    /// Build a DeckSnapshot with 30 cards (repeating the given card IDs).
    /// </summary>
    public static DeckSnapshot MakeDeck(params long[] cardNos)
    {
        var cards = new List<DeckSnapshotCard>();
        int idx = 0;
        while (cards.Count < GameConstants.DeckSize)
        {
            cards.Add(new DeckSnapshotCard { CardNo = cardNos[idx % cardNos.Length] });
            idx++;
        }
        return new DeckSnapshot { DeckID = "deck-1", Cards = cards };
    }
}

/// <summary>
/// Simple in-memory IEffectRegistry for tests.
/// </summary>
public class TestEffectRegistry : IEffectRegistry
{
    private readonly Dictionary<(long, TriggerType), EffectHandler> _handlers = new();

    public void Register(long cardNo, TriggerType trigger, EffectHandler handler)
        => _handlers[(cardNo, trigger)] = handler;

    public EffectHandler? Get(long cardNo, TriggerType trigger)
        => _handlers.GetValueOrDefault((cardNo, trigger));

    public bool Has(long cardNo, TriggerType trigger)
        => _handlers.ContainsKey((cardNo, trigger));

    public BudgetRequirement? GetBudgetRequirement(long cardNo, TriggerType trigger) => null;
    public EffectInfo? GetEffectInfo(long cardNo, TriggerType trigger) => null;
    public List<string>? GetChoiceOptions(long cardNo, TriggerType trigger) => null;
}
