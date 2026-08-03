using System.Text.Json;
using ApiCard = OverloadParty.ApiCard;
using OverloadParty.Battle.Data;
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
            CardType = CardTypes.DataResource,
            Subtype = subtype,
            Faction = faction,
            DeployTurns = deployTurns,
            Resizable = resizable,
            Elastic = elastic,
            ElasticIncrement = elasticIncrement,
            FreeTier = freeTier,
            CostPerRequest = costPerRequest,
            DataResourceStats = new DataResourceStats
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
    /// Reactive カードを生成します
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

    // ─── Wire (ClientGameState) ビルダー ──────────────────────

    /// <summary>
    /// 自フィールドの空の wire ビューを生成します (各ゾーン 3 スロット, 全て null)。
    /// </summary>
    public static OverloadParty.GameState.Field MakeWireField()
    {
        return new OverloadParty.GameState.Field
        {
            Frontend = new List<OverloadParty.GameState.DeployedResource?> { null, null, null },
            Backend = new List<OverloadParty.GameState.DeployedResource?> { null, null, null },
            Support = new List<OverloadParty.GameState.DeployedSupport?> { null, null, null },
        };
    }

    /// <summary>
    /// 相手フィールドの空の wire ビューを生成します (情報秘匿済み)。
    /// </summary>
    public static OverloadParty.GameState.OpponentField MakeWireOpponentField()
    {
        return new OverloadParty.GameState.OpponentField
        {
            Frontend = new List<OverloadParty.GameState.DeployedResource?> { null, null, null },
            Backend = new List<OverloadParty.GameState.DeployedResource?> { null, null, null },
            Support = new List<OverloadParty.GameState.HiddenDeployedSupport?> { null, null, null },
        };
    }

    /// <summary>
    /// wire リソースを生成します。
    /// </summary>
    public static OverloadParty.GameState.DeployedResource MakeWireResource(
        string cardId = "TST-0001",
        string instanceId = "inst_1",
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
        return new OverloadParty.GameState.DeployedResource
        {
            CardID = cardId,
            InstanceID = instanceId,
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

    /// <summary>
    /// 自フィールド向け wire サポートを生成します。
    /// </summary>
    public static OverloadParty.GameState.DeployedSupport MakeWireSupport(
        string instanceId,
        string cardId = "TST-0200",
        bool faceUp = true,
        string? targetInstanceId = null)
    {
        return new OverloadParty.GameState.DeployedSupport
        {
            InstanceID = instanceId,
            CardID = cardId,
            FaceUp = faceUp,
            TargetInstanceID = targetInstanceId,
        };
    }

    /// <summary>
    /// 相手フィールド向け wire サポート (情報秘匿) を生成します。
    /// </summary>
    public static OverloadParty.GameState.HiddenDeployedSupport MakeHiddenSupport(
        string instanceId,
        string? cardId = null,
        bool faceDown = true,
        bool peeked = false)
    {
        return new OverloadParty.GameState.HiddenDeployedSupport
        {
            InstanceID = instanceId,
            CardID = cardId,
            FaceDown = faceDown,
            Peeked = peeked,
        };
    }

    /// <summary>
    /// 手札用 wire カードを生成します。
    /// </summary>
    public static OverloadParty.GameState.UndeployedCard MakeWireUndeployed(
        string cardId = "TST-0001", string instanceId = "h_1") =>
        new() { CardID = cardId, InstanceID = instanceId };

    /// <summary>
    /// 最小の ClientGameState を組み立てます (NPC テスト用)。
    /// </summary>
    public static OverloadParty.GameState.ClientGameState MakeClientState(
        OverloadParty.GameState.Field? myField = null,
        OverloadParty.GameState.OpponentField? oppField = null,
        List<OverloadParty.GameState.UndeployedCard>? myHand = null,
        long myBudget = 5000,
        long myInsightPool = 0,
        long oppBudget = 5000,
        long turn = 1,
        string phase = "main",
        long myPlayerNum = 1,
        long oppPlayerNum = 2,
        long activePlayer = 1,
        List<OverloadParty.GameState.AvailableAction>? availableActions = null,
        OverloadParty.GameState.PendingSlotSelectView? pendingSlotSelect = null,
        OverloadParty.GameState.PendingEffectChoiceView? pendingEffectChoice = null)
    {
        return new OverloadParty.GameState.ClientGameState
        {
            GameID = "test-game",
            CurrentTurn = turn,
            CurrentPhase = phase,
            ActivePlayer = activePlayer,
            IsMyTurn = activePlayer == myPlayerNum,
            TurnStartedAt = DateTimeOffset.UtcNow,
            MyView = new OverloadParty.GameState.PlayerView
            {
                PlayerNum = myPlayerNum,
                Budget = myBudget,
                InsightPool = myInsightPool,
                TimeBank = 480,
                Field = myField ?? MakeWireField(),
                Hand = myHand ?? new List<OverloadParty.GameState.UndeployedCard>(),
                Trash = new List<OverloadParty.GameState.UndeployedCard>(),
                AvailableActions = availableActions,
                PendingSlotSelect = pendingSlotSelect,
            },
            OppView = new OverloadParty.GameState.OpponentView
            {
                PlayerNum = oppPlayerNum,
                Budget = oppBudget,
                Field = oppField ?? MakeWireOpponentField(),
                Trash = new List<OverloadParty.GameState.UndeployedCard>(),
            },
            Player1Summary = new OverloadParty.GameState.PlayerSummary { Name = "p1" },
            Player2Summary = new OverloadParty.GameState.PlayerSummary { Name = "p2" },
            PendingEffectChoice = pendingEffectChoice,
        };
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
    /// デッキ規約 (30 枚ちょうど・同名 3 枚まで) を満たす DeckSnapshot を組む。
    /// 指定カードを 3 枚ずつ入れ、30 枚に満たない分は埋め草カードで埋めて <paramref name="cc"/> に登録する。
    /// </summary>
    /// <param name="cc">埋め草カードの登録先。</param>
    /// <param name="cardIds">3 枚ずつデッキに入れるカード ID。</param>
    /// <returns>30 枚のデッキスナップショット。</returns>
    public static DeckSnapshot MakeDeck(TestCardCache cc, params string[] cardIds)
    {
        var cards = new List<DeckSnapshotCard>();

        foreach (var cardId in cardIds)
        {
            for (int i = 0; i < BattleConstants.MaxCopiesPerCardName; i++)
            {
                cards.Add(new DeckSnapshotCard { CardId = cardId });
            }
        }

        if (cards.Count > InitialValues.DeckSize)
        {
            throw new ArgumentException(
                $"{cardIds.Length} 種 × {BattleConstants.MaxCopiesPerCardName} 枚はデッキ上限 {InitialValues.DeckSize} 枚を超える",
                nameof(cardIds));
        }

        int fillerNo = 1;
        while (cards.Count < InitialValues.DeckSize)
        {
            var fillerId = $"TST-9{fillerNo:D3}";
            cc.Add(ComputeCard(cardId: fillerId, name: $"Filler{fillerNo}"));
            int copies = Math.Min(BattleConstants.MaxCopiesPerCardName, InitialValues.DeckSize - cards.Count);
            for (int i = 0; i < copies; i++)
            {
                cards.Add(new DeckSnapshotCard { CardId = fillerId });
            }
            fillerNo++;
        }

        return new DeckSnapshot { DeckID = "deck-1", Cards = cards };
    }

    /// <summary>
    /// 対象プレイヤーの手札の先頭を指定カードに差し替える。シャッフルに左右されず特定カードを持たせたいときに使う。
    /// </summary>
    /// <param name="state">対象のゲーム状態。</param>
    /// <param name="playerNum">対象プレイヤー番号 (1 または 2)。</param>
    /// <param name="cardId">差し替え後のカード ID。</param>
    /// <returns>差し替えたカードのインスタンス ID。</returns>
    public static string ReplaceFirstHandCard(BattleGameState state, long playerNum, string cardId)
    {
        var instanceId = state.NextInstanceID();
        state.GetHand(playerNum)[0] = new UndeployedCard { InstanceID = instanceId, CardID = cardId };
        return instanceId;
    }

    /// <summary>テスト用の標準施策カタログ要素 (4 プロダクト × routine/special = 8 件)。</summary>
    /// <returns>IN-0001〜IN-0008 の施策一覧。</returns>
    public static List<Initiative> StandardInitiatives() =>
    [
        new() { InitiativeId = "IN-0001", ProductId = "PD-0001", Kind = InitiativeKinds.Routine, Name = "R1", InsightCost = 0 },
        new() { InitiativeId = "IN-0002", ProductId = "PD-0001", Kind = InitiativeKinds.Special, Name = "S1", InsightCost = 0 },
        new() { InitiativeId = "IN-0003", ProductId = "PD-0002", Kind = InitiativeKinds.Routine, Name = "R2", InsightCost = 0 },
        new() { InitiativeId = "IN-0004", ProductId = "PD-0002", Kind = InitiativeKinds.Special, Name = "S2", InsightCost = 0 },
        new() { InitiativeId = "IN-0005", ProductId = "PD-0003", Kind = InitiativeKinds.Routine, Name = "R3", InsightCost = 0 },
        new() { InitiativeId = "IN-0006", ProductId = "PD-0003", Kind = InitiativeKinds.Special, Name = "S3", InsightCost = 0 },
        new() { InitiativeId = "IN-0007", ProductId = "PD-0004", Kind = InitiativeKinds.Routine, Name = "R4", InsightCost = 0 },
        new() { InitiativeId = "IN-0008", ProductId = "PD-0004", Kind = InitiativeKinds.Special, Name = "S4", InsightCost = 0 },
    ];
}

/// <summary>
/// カード記載の回数制限を宣言した起動効果を、本番と同じ loader 経由で登録します。
/// 回数制限の op 合成と効果レジストリへの記録を loader に任せるため、
/// 列挙と検証がどちらも本番と同じ登録内容を読みます。
/// </summary>
public static class TestUseLimitEffects
{
    /// <summary>効果 1 回あたりに得るバジェット。使用回数をバジェットの増分で数えられるようにする。</summary>
    public const long GainPerUse = 100;

    /// <summary>
    /// バジェットを得るだけの効果をカードに持たせ、カードキャッシュと効果レジストリに登録します。
    /// </summary>
    /// <param name="cc">カード定義の登録先。</param>
    /// <param name="registry">効果の登録先。</param>
    /// <param name="card">効果を持たせるカード定義。</param>
    /// <param name="useLimit">宣言する回数制限 (<see cref="UseLimits"/> の値)。制限なしなら null。</param>
    /// <param name="trigger">効果の発動契機 (<see cref="TriggerTypes"/> の値)。</param>
    public static void RegisterBudgetGain(
        TestCardCache cc, EffectRegistry registry, CardDefinition card,
        string? useLimit = null, string trigger = TriggerTypes.Ignition)
    {
        RegisterEffect(cc, registry, card, useLimit, trigger, BuildGainBudgetOp());
    }

    /// <summary>
    /// 相手の伏せリアクティブを 1 枚選んで開示する起動効果をカードに持たせ、
    /// カードキャッシュと効果レジストリに登録します。伏せリアクティブが 2 枚以上あると選択待ちに入るため、
    /// 選択を挟んで効果が再開する経路を公開経路から辿れます。
    /// </summary>
    /// <param name="cc">カード定義の登録先。</param>
    /// <param name="registry">効果の登録先。</param>
    /// <param name="card">効果を持たせるカード定義。</param>
    /// <param name="useLimit">宣言する回数制限 (<see cref="UseLimits"/> の値)。制限なしなら null。</param>
    public static void RegisterRevealReactive(
        TestCardCache cc, EffectRegistry registry, CardDefinition card, string? useLimit = null)
    {
        RegisterEffect(
            cc, registry, card, useLimit, TriggerTypes.Ignition,
            JsonSerializer.SerializeToElement(new Dictionary<string, object>
            {
                [EffectOps.RevealReactive] = new Dictionary<string, object>(),
            }));
    }

    private static void RegisterEffect(
        TestCardCache cc, EffectRegistry registry, CardDefinition card,
        string? useLimit, string trigger, JsonElement op)
    {
        card.Effects =
        [
            new EffectDef
            {
                Trigger = trigger,
                UseLimit = useLimit,
                Ops = [op],
            },
        ];

        cc.Add(card);
        EffectYamlLoader.LoadEffectSources([card], registry, new CustomEffectRegistry());
    }

    private static JsonElement BuildGainBudgetOp() =>
        JsonSerializer.SerializeToElement(new Dictionary<string, object>
        {
            ["gain_budget"] = new Dictionary<string, object>
            {
                ["target"] = "myself",
                ["amount"] = GainPerUse,
            },
        });
}

/// <summary>
/// card が配布するマスターデータをファイルから読み込みます。読み込み元は CARDS_JSON_PATH /
/// INITIATIVES_JSON_PATH で差し替えられ、未指定ならリポジトリ内のキャッシュを使います。
/// </summary>
public static class MasterData
{
    private const string CardsFileName = "cards_gen.json";
    private const string InitiativesFileName = "initiatives_gen.json";

    /// <summary>全カード定義を読み込みます。</summary>
    /// <returns>マスターデータ上の全カード定義。</returns>
    public static List<CardDefinition> LoadCards() =>
        ReadEntries<ApiCard.CardDefinition>(CardsPath())
            .Select(CardDefinitionMapper.ToCardDefinition)
            .ToList();

    /// <summary>全施策定義を読み込みます。</summary>
    /// <returns>マスターデータ上の全施策定義。</returns>
    public static List<Initiative> LoadInitiatives() =>
        ReadEntries<ApiCard.Initiative>(InitiativesPath())
            .Select(CardDefinitionMapper.ToInitiative)
            .ToList();

    /// <summary>カード定義ファイルの位置を返します。</summary>
    /// <returns>カード定義ファイルのパス。</returns>
    public static string CardsPath() =>
        Environment.GetEnvironmentVariable("CARDS_JSON_PATH")
        ?? FindInCache(CardsFileName)
        ?? throw new FileNotFoundException(
            $"{CardsFileName} not found. Set CARDS_JSON_PATH to the card master data.");

    // 施策は card の同じディレクトリで配布されるため、カード定義の隣を既定の読み込み元にする。
    /// <summary>施策定義ファイルの位置を返します。</summary>
    /// <returns>施策定義ファイルのパス。</returns>
    public static string InitiativesPath() =>
        Environment.GetEnvironmentVariable("INITIATIVES_JSON_PATH")
        ?? Path.Combine(Path.GetDirectoryName(CardsPath())!, InitiativesFileName);

    private static List<T> ReadEntries<T>(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"master data not found at {path}", path);
        }

        var entries = JsonSerializer.Deserialize<List<T>>(File.ReadAllText(path))
            ?? throw new InvalidOperationException($"failed to deserialize master data from {path}");
        if (entries.Count == 0)
        {
            throw new InvalidOperationException($"master data at {path} has no entries");
        }
        return entries;
    }

    private static string? FindInCache(string fileName)
    {
        // Walk up from the test binary to find the battle repo's own cache.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "overload-party-battle",
                "packages", "game-state-dotnet", "cache", fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        return null;
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
        var cards = MasterData.LoadCards();

        var cardCache = new TestCardCache();
        foreach (var card in cards)
        {
            cardCache.Add(card);
        }

        var registry = new EffectRegistry();
        var customEffects = new CustomEffectRegistry();
        EffectYamlLoader.LoadEffectSources(cards, registry, customEffects);

        return (registry, cardCache);
    }
}
