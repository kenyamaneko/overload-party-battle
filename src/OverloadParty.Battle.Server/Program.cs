using System.Text.Json;
using System.Text.Json.Serialization;
using Npgsql;
using OverloadParty.Battle.Data;
using OverloadParty.Battle.Data.Json;
using OverloadParty.Battle.Data.Mock;
using OverloadParty.Battle.Data.Pg;
using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;
using OverloadParty.Battle.Npc;
using OverloadParty.Battle.Service;

var builder = WebApplication.CreateBuilder(args);

// Determine mode
var isLocalDev = builder.Environment.IsDevelopment()
    || Environment.GetEnvironmentVariable("BATTLE_MODE") == "local";

builder.Services.AddLogging();

// Serialize enums as camelCase strings (e.g. Rank.Small → "small") so that
// downstream consumers (Gateway, client) receive strings instead of numeric values.
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(
        new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
    options.SerializerOptions.Converters.Add(new ZoneJsonConverterFactory());
});

// ─── Data layer ─────────────────────────────────────────────

if (isLocalDev)
{
    // In-memory mock repositories
    var mockGameRepo = new MockGameRepository();

    builder.Services.AddSingleton<IGameRepository>(mockGameRepo);
    // ICardRepository registered after CardCache is built (below)
}
else
{
    // PostgreSQL repositories
    var connStr = builder.Configuration.GetConnectionString("DefaultConnection")
        ?? Environment.GetEnvironmentVariable("DATABASE_URL")
        ?? throw new InvalidOperationException("DATABASE_URL or ConnectionStrings:DefaultConnection not set");
    var dataSource = NpgsqlDataSource.Create(connStr);
    builder.Services.AddSingleton(dataSource);
    builder.Services.AddSingleton<IGameRepository>(sp => new PgGameRepository(sp.GetRequiredService<NpgsqlDataSource>()));
    builder.Services.AddSingleton<ICardRepository>(sp => new PgCardRepository(sp.GetRequiredService<NpgsqlDataSource>()));
}

// ─── Card cache ─────────────────────────────────────────────

var cardCache = new CardCache();
builder.Services.AddSingleton<ICardCache>(cardCache);
builder.Services.AddSingleton(cardCache);

if (isLocalDev)
{
    builder.Services.AddSingleton<ICardRepository>(new MockCardRepository(cardCache));
}

// ─── Engine ─────────────────────────────────────────────────

builder.Services.AddSingleton(sp =>
{
    var gameRepo = sp.GetRequiredService<IGameRepository>();
    var cc = sp.GetRequiredService<ICardCache>();
    var engine = new GameEngine(gameRepo, cc);

    // Initialize and register effects
    var registry = new EffectRegistry();
    EffectInit.RegisterAllEffects(registry);
    engine.SetEffectRegistry(registry);

    return engine;
});

// ─── Services ───────────────────────────────────────────────

builder.Services.AddSingleton<GameService>(sp =>
{
    var svc = new GameService(
        sp.GetRequiredService<GameEngine>(),
        sp.GetRequiredService<IGameRepository>(),
        sp.GetRequiredService<ICardCache>(),
        sp.GetRequiredService<ILogger<GameService>>());

    // Set default NPC AI
    var cc = sp.GetRequiredService<ICardCache>();
    var engine = sp.GetRequiredService<GameEngine>();
    if (engine.EffectRegistry is not null)
    {
        svc.SetNpcAI(new StandardAi(cc, engine.EffectRegistry));
    }

    return svc;
});

builder.Services.AddSingleton<GameLogService>(sp =>
    new GameLogService(
        sp.GetRequiredService<IGameRepository>(),
        sp.GetRequiredService<ICardCache>()));

var app = builder.Build();

// ─── Load card cache ────────────────────────────────────────

if (isLocalDev)
{
    // Load cards from embedded JSON in the OverloadParty.Generated package
    using var stream = EmbeddedCards.GetCardsJsonStream();
    var cards = JsonSerializer.Deserialize<List<CardDefinition>>(stream, new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    });
    if (cards is not null)
    {
        cardCache.LoadFromList(cards);
    }
    app.Logger.LogInformation("Loaded {Count} cards from embedded package", cardCache.Count);
}
else
{
    // Production: load cards from database
    var cardRepo = app.Services.GetRequiredService<ICardRepository>();
    await cardCache.LoadFromRepository(cardRepo);
    app.Logger.LogInformation("Loaded {Count} cards from database", cardCache.Count);
}

// ─── Middleware ──────────────────────────────────────────────

// CORS for development
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

// ─── Routes ─────────────────────────────────────────────────

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// ─── REST API Endpoints ─────────────────────────────────────

var api = app.MapGroup("/api/v1");

// NPC Game Creation
api.MapPost("/games/npc", async (GameService gameSvc, NpcBattleRequest req) =>
{
    try
    {
        var cards = req.Cards.Select(c => new DeckSnapshotCard { CardNo = c.CardNo, ArtNo = c.ArtNo }).ToList();
        var game = await gameSvc.StartNPCBattle(req.PlayerID, req.DeckID, cards, req.NpcFaction);
        return Results.Ok(new { game_id = game.GameID, player1_id = game.Player1ID, player2_id = game.Player2ID });
    }
    catch (GameRuleException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Unexpected error");
        return Results.StatusCode(500);
    }
});

// PvP Game Creation (called by Gateway after matchmaking)
api.MapPost("/games/pvp", async (GameService gameSvc, PvpBattleRequest req) =>
{
    try
    {
        var p1Cards = req.Player1Cards.Select(c => new DeckSnapshotCard { CardNo = c.CardNo, ArtNo = c.ArtNo }).ToList();
        var p2Cards = req.Player2Cards.Select(c => new DeckSnapshotCard { CardNo = c.CardNo, ArtNo = c.ArtNo }).ToList();
        var game = await gameSvc.CreateGameFromMatch(req.Player1ID, req.Player1DeckID, p1Cards, req.Player2ID, req.Player2DeckID, p2Cards);
        return Results.Ok(new { game_id = game.GameID, player1_id = game.Player1ID, player2_id = game.Player2ID });
    }
    catch (GameRuleException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Unexpected error");
        return Results.StatusCode(500);
    }
});

// Game Action
api.MapPost("/games/{gameId}/actions", async (GameService gameSvc, string gameId, GameActionRequest req) =>
{
    try
    {
        var actionType = EnumExtensions.ParseActionType(req.ActionType);
        var actionData = ActionDataDeserializer.Deserialize(actionType, req.Data);
        var result = await gameSvc.ProcessAction(gameId, req.PlayerID, actionType, actionData);
        return Results.Ok(ProjectActionResult(result));
    }
    catch (GameRuleException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Unexpected error");
        return Results.StatusCode(500);
    }
});

// Advance NPC Turn
// Called by the gateway after game_enter to run the NPC's first turn
// so that action events can be delivered via WebSocket.
api.MapPost("/games/{gameId}/advance-npc", async (GameService gameSvc, string gameId, NpcAdvanceRequest req) =>
{
    try
    {
        var result = await gameSvc.AdvanceNpcTurn(gameId, req.PlayerID);
        return Results.Ok(ProjectActionResult(result));
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Unexpected error");
        return Results.StatusCode(500);
    }
});

// Game State Retrieval
api.MapGet("/games/{gameId}/state/{playerId}", async (GameService gameSvc, string gameId, string playerId) =>
{
    try
    {
        var state = await gameSvc.GetGameStateForPlayer(gameId, playerId);
        if (state == null) { return Results.NotFound(); }
        return Results.Ok(state);
    }
    catch (GameRuleException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Unexpected error");
        return Results.StatusCode(500);
    }
});

// Turn Controls Retrieval
api.MapGet("/games/{gameId}/controls/{playerId}", async (GameService gameSvc, string gameId, string playerId) =>
{
    try
    {
        var controls = await gameSvc.GetTurnControlsForPlayer(gameId, playerId);
        if (controls == null) { return Results.Ok(null); }
        return Results.Ok(new { can_end_phase = controls.CanEndPhase, discard_required = controls.DiscardRequired });
    }
    catch (GameRuleException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Unexpected error");
        return Results.StatusCode(500);
    }
});

// Game Log (Replay)
api.MapGet("/games/{gameId}/log", async (GameLogService logSvc, string gameId) =>
{
    try
    {
        var log = await logSvc.GetGameLog(gameId);
        if (log is null) return Results.NotFound(new { error = "game not found" });
        return Results.Bytes(logSvc.SerializeToJson(log), "application/json");
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Unexpected error");
        return Results.StatusCode(500);
    }
});

api.MapGet("/games/{gameId}/log/text", async (GameLogService logSvc, string gameId) =>
{
    try
    {
        var text = await logSvc.GetGameLogText(gameId);
        if (text is null) return Results.NotFound(new { error = "game not found" });
        return Results.Text(text, "text/plain");
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Unexpected error");
        return Results.StatusCode(500);
    }
});

// Dev REST API endpoints (local mode only)
if (isLocalDev)
{
    app.MapGet("/api/dev/cards", (ICardCache cc) =>
        Results.Ok(cc.All().Values.Select(c => new { c.CardNo, c.CardName, c.Faction, c.CardType })));
}

var port = Environment.GetEnvironmentVariable("PORT") ?? "9002";
app.Urls.Add($"http://0.0.0.0:{port}");

app.Logger.LogInformation("Battle server starting on port {Port} (mode={Mode})",
    port, isLocalDev ? "local" : "production");

app.Run();

static object ProjectActionResult(GameActionResult result) => new
{
    game_over = result.GameOver is not null,
    winner_num = result.GameOver?.WinnerNum ?? 0,
    win_reason = result.GameOver?.Reason,
    events = result.Events.Select(e => new
    {
        sequence = e.Event.SequenceNumber,
        event_type = e.Event.EventType,
        player_id = e.Event.PlayerID,
        event_data = e.Event.EventData,
        state = e.State,
    }),
};

public record DeckCard(long CardNo, long ArtNo);
public record NpcBattleRequest(string PlayerID, long DeckID, List<DeckCard> Cards, string NpcFaction);
public record PvpBattleRequest(string Player1ID, long Player1DeckID, List<DeckCard> Player1Cards, string Player2ID, long Player2DeckID, List<DeckCard> Player2Cards);
public record GameActionRequest(string PlayerID, string ActionType, JsonElement Data);
public record NpcAdvanceRequest(string PlayerID);

public static class ActionDataDeserializer
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static object Deserialize(ActionType actionType, JsonElement data) => actionType switch
    {
        ActionType.PlayCard => data.Deserialize<PlayCardRequest>(JsonOpts)!,
        ActionType.Attack => data.Deserialize<AttackRequest>(JsonOpts)!,
        ActionType.ScaleUp => data.Deserialize<ScaleUpRequest>(JsonOpts)!,
        ActionType.Monetize => data.Deserialize<MonetizeRequest>(JsonOpts)!,
        ActionType.DiscardHand => data.Deserialize<DiscardHandRequest>(JsonOpts)!,
        ActionType.ActivateEffect => data.Deserialize<ActivateEffectRequest>(JsonOpts)!,
        ActionType.Migrate => data.Deserialize<MigrateRequest>(JsonOpts)!,
        ActionType.EndPhase => new object(),
        ActionType.SetReactive => new object(),
        ActionType.Forfeit => new object(),
        _ => throw new ArgumentException($"unknown action type: {actionType}"),
    };
}
