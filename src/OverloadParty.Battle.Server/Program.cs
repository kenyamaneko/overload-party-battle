using System.Text.Json;
using System.Text.Json.Serialization;
using Google.Cloud.Logging.Console;
using Microsoft.AspNetCore.Diagnostics;
using Npgsql;
using OverloadParty.Battle.Data;
using OverloadParty.Battle.Data.Pg;
using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;
using OverloadParty.Battle.Models.Json;
using OverloadParty.Battle.Npc;
using OverloadParty.Battle.Server;
using OverloadParty.Battle.Service;
// Disambiguate: ActionResult exists in both OverloadParty.Battle.Engine (internal engine result)
// and OverloadParty.ApiBattleRpc (wire envelope returned to the gateway).
using ActionResult = OverloadParty.ApiBattleRpc.ActionResult;

var builder = WebApplication.CreateBuilder(args);

// ローカル開発モード判定。クラウド環境 (dev/stg/prod) は ASPNETCORE_ENVIRONMENT 未設定 = Production で動くため false。
var isLocalDev = builder.Environment.IsDevelopment();

// 既定の Console ロガーは severity フィールドを出さず GKE が全行を同一 severity と
// 判定するため、本番は Cloud Logging 互換フォーマッタに差し替える。ローカルは可読性優先でテキスト。
builder.Logging.ClearProviders();
if (isLocalDev)
{
    builder.Logging.AddConsole();
}
else
{
    builder.Logging.AddGoogleCloudConsole();
}

// Serialize enums as camelCase strings (e.g. Rank.Small → "small") so that
// downstream consumers (Gateway, client) receive strings instead of numeric values.
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(
        new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
    options.SerializerOptions.Converters.Add(new ZoneJsonConverterFactory());
});

// ─── Data layer ─────────────────────────────────────────────

var connStr = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? Environment.GetEnvironmentVariable("DATABASE_CONN")
    ?? throw new InvalidOperationException("DATABASE_CONN or ConnectionStrings:DefaultConnection not set");
var dataSource = NpgsqlDataSource.Create(connStr);
builder.Services.AddSingleton(dataSource);
builder.Services.AddSingleton<IGameRepository>(sp => new PgGameRepository(sp.GetRequiredService<NpgsqlDataSource>()));

// ─── Card cache ─────────────────────────────────────────────

var cardCache = new CardCache();
builder.Services.AddSingleton<ICardCache>(cardCache);
builder.Services.AddSingleton(cardCache);

// 施策定義。card サービス (またはローカル JSON) から起動時に読み込む。
// engine ファクトリはカードロード後に遅延実行されるため、この変数はその時点で確定している。
var initiatives = new List<Initiative>();

// ─── Engine ─────────────────────────────────────────────────

builder.Services.AddSingleton<IClock, SystemClock>();

builder.Services.AddSingleton(sp =>
{
    var gameRepo = sp.GetRequiredService<IGameRepository>();
    var cc = sp.GetRequiredService<ICardCache>();

    // カード定義と施策効果を初期化（YAML 駆動）
    var registry = new EffectRegistry();
    var customEffects = new CustomEffectRegistry();
    EffectYamlLoader.LoadEffectSources(cc.All().Values, registry, customEffects);
    InitiativeEffects.LoadIntoRegistry(initiatives, registry, customEffects);

    return new GameEngine(
        gameRepo, cc, registry, new InitiativeCatalog(initiatives),
        sp.GetRequiredService<IClock>());
});

// ─── Services ───────────────────────────────────────────────

var aiConfigDir = Environment.GetEnvironmentVariable("NPC_AI_CONFIG_DIR")
    ?? throw new InvalidOperationException("NPC_AI_CONFIG_DIR not set");
if (!Directory.Exists(aiConfigDir))
{
    throw new DirectoryNotFoundException($"NPC AI config directory not found: {aiConfigDir}");
}
var aiConfigs = AiConfigLoader.LoadAll(aiConfigDir);

builder.Services.AddSingleton<NpcRunner>(sp =>
    new NpcRunner(
        sp.GetRequiredService<GameEngine>(),
        sp.GetRequiredService<IGameRepository>(),
        sp.GetRequiredService<ICardCache>(),
        aiConfigs,
        sp.GetRequiredService<ILogger<NpcRunner>>()));

// Resolve NpcRunner at startup via IHostedService so its constructor's
// AiConfigValidator runs before the HTTP server accepts traffic. Runs AFTER
// the card cache load block in Program.cs because HostedService.StartAsync
// fires from app.Run().
builder.Services.AddHostedService<NpcRunnerHostedService>();

builder.Services.AddSingleton<GameService>(sp =>
    new GameService(
        sp.GetRequiredService<GameEngine>(),
        sp.GetRequiredService<IGameRepository>(),
        sp.GetRequiredService<ICardCache>(),
        sp.GetRequiredService<NpcRunner>(),
        aiConfigs));

builder.Services.AddSingleton<GameLogService>(sp =>
    new GameLogService(
        sp.GetRequiredService<IGameRepository>(),
        sp.GetRequiredService<ICardCache>()));

var app = builder.Build();

// ─── Load card cache ────────────────────────────────────────

// Local dev keeps the JSON file path so offline development doesn't require the card service
// running. Everything else (k8s, CI) must hit the card service.
var localCardsPath = isLocalDev ? Environment.GetEnvironmentVariable("CARDS_JSON_PATH") : null;
var localInitiativesPath = isLocalDev ? Environment.GetEnvironmentVariable("INITIATIVES_JSON_PATH") : null;

var masterJsonOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    PropertyNameCaseInsensitive = true,
};

if (!string.IsNullOrEmpty(localCardsPath))
{
    if (!File.Exists(localCardsPath))
    {
        throw new FileNotFoundException(
            $"Card data not found at {localCardsPath}. Run 'python3 scripts/generate_cards.py' in the common repo.");
    }
    var cards = JsonSerializer.Deserialize<List<CardDefinition>>(
        File.ReadAllText(localCardsPath), masterJsonOptions);
    if (cards is null)
    {
        throw new InvalidOperationException($"failed to deserialize cards from {localCardsPath}");
    }
    cardCache.LoadFromList(cards);
    app.Logger.LogInformation("Loaded {Count} cards from {Path}", cardCache.Count, localCardsPath);

    if (string.IsNullOrEmpty(localInitiativesPath) || !File.Exists(localInitiativesPath))
    {
        throw new FileNotFoundException(
            $"Initiative data not found at {localInitiativesPath}. Set INITIATIVES_JSON_PATH and run 'python3 scripts/generate_products.py' in the card repo.");
    }
    var loadedInitiatives = JsonSerializer.Deserialize<List<Initiative>>(
        File.ReadAllText(localInitiativesPath), masterJsonOptions)
        ?? throw new InvalidOperationException($"failed to deserialize initiatives from {localInitiativesPath}");
    initiatives.AddRange(loadedInitiatives);
    app.Logger.LogInformation("Loaded {Count} initiatives from {Path}", initiatives.Count, localInitiativesPath);
}
else
{
    var cardServiceUrl = Environment.GetEnvironmentVariable("CARD_SERVICE_URL") ?? "http://card:9003";
    // One-shot startup fetch. If the call fails we log and exit(1); k8s restarts the pod,
    // which gives us bounded retry-with-backoff via the cluster instead of a hidden loop here.
    using var cardClient = new CardServiceClient(cardServiceUrl);
    try
    {
        var cards = await cardClient.ListAllCardsAsync();
        cardCache.LoadFromList(cards);
        initiatives.AddRange(await cardClient.ListAllInitiativesAsync());
        app.Logger.LogInformation(
            "Loaded {CardCount} cards and {InitiativeCount} initiatives from card service at {Url}",
            cardCache.Count, initiatives.Count, cardServiceUrl);
    }
    catch (Exception ex)
    {
        app.Logger.LogCritical(
            ex, "Failed to load master data from card service at {Url}; exiting", cardServiceUrl);
        Environment.Exit(1);
    }
}

// ─── Middleware ──────────────────────────────────────────────

// 開発用 CORS
if (isLocalDev)
{
    app.Use(async (context, next) =>
    {
        context.Response.Headers.Append("Access-Control-Allow-Origin", "*");
        context.Response.Headers.Append("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS");
        context.Response.Headers.Append("Access-Control-Allow-Headers", "Content-Type, Authorization");

        if (context.Request.Method == "OPTIONS")
        {
            context.Response.StatusCode = 204;
            return;
        }
        await next();
    });
}

// 例外を {"error": "..."} 形式のボディに統一してステータスコードを割り当てる。
// GameRuleException はルール違反として 400、それ以外は 500。
// Battle Server は Gateway からのみ呼ばれる内部サービスのため、500 時も ex.Message を透過する。
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var ex = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        var (status, message) = ex switch
        {
            GameRuleException e => (StatusCodes.Status400BadRequest, e.Message),
            _ => (StatusCodes.Status500InternalServerError, ex?.Message ?? "internal error"),
        };
        if (status == StatusCodes.Status500InternalServerError)
        {
            app.Logger.LogError(ex, "Unexpected error");
        }
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(new { error = message });
    });
});

// ─── Routes ─────────────────────────────────────────────────

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// ─── REST API Endpoints ─────────────────────────────────────

// JSON options used to convert engine-native payloads (Dictionary<string, object>, ClientGameState)
// into JsonElement for the generated ActionEvent envelope. Must mirror the HTTP serializer options
// (JsonStringEnumConverter + ZoneJsonConverterFactory) so the wire format is byte-identical to the
// previous anonymous-type projection.
var envelopeJsonOptions = new JsonSerializerOptions
{
    Converters =
    {
        new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower),
        new ZoneJsonConverterFactory(),
    },
};

var api = app.MapGroup("/api/v1");

// NPC モデル一覧
api.MapGet("/npc/models", () =>
{
    var models = aiConfigs.Values.Select(c => new
    {
        model = c.Model,
        faction = c.Faction,
        difficulty = c.Model[(c.Faction.Length + 1)..],
        display_name = c.DisplayName,
    });
    return Results.Ok(new { models });
});

// NPC ゲーム作成
api.MapPost("/games/npc", async (GameService gameSvc, NpcBattleRequest req) =>
{
    var cards = req.DeckCards.Select(c => new DeckSnapshotCard { CardId = c.CardId, ArtNo = c.ArtNo }).ToList();
    var summaries = new List<PlayerSummarySnapshot>
    {
        new() { PlayerNum = 1, Name = req.Player1Summary.Name, Level = req.Player1Summary.Level },
        new() { PlayerNum = 2, Name = req.Player2Summary.Name, Level = req.Player2Summary.Level },
    };
    var game = await gameSvc.StartNPCBattle(cards, req.RoutineId, req.SpecialId, req.NpcModel, summaries);
    return Results.Ok(new GameCreatedResult { GameId = game.GameID });
});

// PvP ゲーム作成（マッチメイキング後に Gateway が呼び出す）
api.MapPost("/games/pvp", async (GameService gameSvc, PvpBattleRequest req) =>
{
    var p1Cards = req.Deck1Cards.Select(c => new DeckSnapshotCard { CardId = c.CardId, ArtNo = c.ArtNo }).ToList();
    var p2Cards = req.Deck2Cards.Select(c => new DeckSnapshotCard { CardId = c.CardId, ArtNo = c.ArtNo }).ToList();
    var summaries = new List<PlayerSummarySnapshot>
    {
        new() { PlayerNum = 1, Name = req.Player1Summary.Name, Level = req.Player1Summary.Level },
        new() { PlayerNum = 2, Name = req.Player2Summary.Name, Level = req.Player2Summary.Level },
    };
    var game = await gameSvc.CreateGameFromMatch(
        p1Cards, req.Deck1RoutineId, req.Deck1SpecialId,
        p2Cards, req.Deck2RoutineId, req.Deck2SpecialId, summaries);
    return Results.Ok(new GameCreatedResult { GameId = game.GameID });
});

// ゲームアクション
api.MapPost("/games/{gameId}/actions", async (GameService gameSvc, string gameId, GameActionRequest req) =>
{
    var actionType = EnumExtensions.ParseActionType(req.ActionType);
    var actionData = ActionDataDeserializer.Deserialize(actionType, req.Data);
    var result = await gameSvc.ProcessAction(gameId, req.PlayerNum, actionType, actionData);
    return Results.Ok(ProjectActionResult(result));
});

// NPC ターン進行
// Called by the gateway after game_enter to run the NPC's first turn
// so that action events can be delivered via WebSocket.
api.MapPost("/games/{gameId}/advance-npc", async (GameService gameSvc, string gameId) =>
{
    var result = await gameSvc.AdvanceNpcTurn(gameId);
    return Results.Ok(ProjectActionResult(result));
});

// ゲーム状態取得
api.MapGet("/games/{gameId}/state/{playerNum:int}", async (GameService gameSvc, string gameId, int playerNum) =>
{
    var state = await gameSvc.GetGameStateForPlayer(gameId, playerNum);
    return Results.Ok(state);
});

// ターン制御情報取得
api.MapGet("/games/{gameId}/controls/{playerNum:int}", async (GameService gameSvc, string gameId, int playerNum) =>
{
    var controls = await gameSvc.GetTurnControlsForPlayer(gameId, playerNum);
    return Results.Ok(controls);
});

// ゲームログ（リプレイ）
api.MapGet("/games/{gameId}/log", async (GameLogService logSvc, string gameId) =>
{
    var log = await logSvc.GetGameLog(gameId);
    if (log is null)
    {
        return Results.NotFound(new { error = "game not found" });
    }

    return Results.Bytes(logSvc.SerializeToJson(log), "application/json");
});

api.MapGet("/games/{gameId}/log/text", async (GameLogService logSvc, string gameId) =>
{
    var text = await logSvc.GetGameLogText(gameId);
    if (text is null)
    {
        return Results.NotFound(new { error = "game not found" });
    }

    return Results.Text(text, "text/plain");
});

// 開発用 REST API エンドポイント（ローカル開発モードのみ）
if (isLocalDev)
{
    app.MapGet("/api/dev/cards", (ICardCache cc) =>
        Results.Ok(cc.All().Values.Select(c => new { c.CardId, c.CardName, c.Faction, c.CardType })));
}

var port = Environment.GetEnvironmentVariable("PORT") ?? "9002";
app.Urls.Add($"http://0.0.0.0:{port}");

app.Logger.LogInformation("Battle server starting on port {Port} (mode={Mode})",
    port, isLocalDev ? "local" : "production");

app.Run();

ActionResult ProjectActionResult(GameActionResult result) => new()
{
    GameOver = result.GameOver is not null,
    WinningPlayerNum = result.GameOver?.WinnerNum ?? 0,
    WinReason = result.GameOver?.Reason ?? "",
    NpcPending = result.IsNpcPending,
    Events = result.Events.Select(e => new ActionEvent
    {
        Sequence = e.Event.SequenceNumber,
        EventType = e.Event.EventType,
        PlayerNum = e.Event.PlayerNum,
        EventData = EventDataSerializer.SerializeToElement(e.Event.EventData),
        State = JsonSerializer.SerializeToElement(e.State, envelopeJsonOptions),
    }).ToList(),
};

/// <summary>WebApplicationFactory&lt;Program&gt; がテストホストを構築するための型マーカー。</summary>
public partial class Program { }

public static class ActionDataDeserializer
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>アクション種別に応じて JSON ペイロードを対応するリクエスト型へデシリアライズする。</summary>
    /// <param name="actionType">アクション種別。</param>
    /// <param name="data">アクションのペイロード JSON。</param>
    /// <returns>アクション種別に対応するリクエストオブジェクト。</returns>
    public static object Deserialize(ActionType actionType, JsonElement data) => actionType switch
    {
        ActionType.PlayCard => data.Deserialize<PlayCardRequest>(JsonOpts)!,
        ActionType.Attack => data.Deserialize<AttackRequest>(JsonOpts)!,
        ActionType.ScaleUp => data.Deserialize<ScaleUpRequest>(JsonOpts)!,
        ActionType.Monetize => data.Deserialize<MonetizeRequest>(JsonOpts)!,
        ActionType.DiscardHand => data.Deserialize<DiscardHandRequest>(JsonOpts)!,
        ActionType.UseIgnition => data.Deserialize<UseIgnitionRequest>(JsonOpts)!,
        ActionType.UseInitiative => data.Deserialize<UseInitiativeRequest>(JsonOpts)!,
        ActionType.EndPhase => new object(),
        ActionType.Forfeit => data.Deserialize<ForfeitRequest>(JsonOpts)!,
        ActionType.ForfeitBoth => new object(),
        _ => throw new ArgumentException($"unknown action type: {actionType}"),
    };
}
