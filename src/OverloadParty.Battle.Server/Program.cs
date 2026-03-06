using System.Text.Json;
using Npgsql;
using OverloadParty.Battle.Data;
using OverloadParty.Battle.Data.Mock;
using OverloadParty.Battle.Data.Pg;
using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Models;
using OverloadParty.Battle.Npc;
using OverloadParty.Battle.Server.Middleware;
using OverloadParty.Battle.Service;

var builder = WebApplication.CreateBuilder(args);

// Determine mode
var isLocalDev = builder.Environment.IsDevelopment()
    || Environment.GetEnvironmentVariable("BATTLE_MODE") == "local";

builder.Services.AddLogging();

// ─── Data layer ─────────────────────────────────────────────

if (isLocalDev)
{
    // In-memory mock repositories
    var mockGameRepo = new MockGameRepository();
    var mockPlayerRepo = new MockPlayerRepository();
    var mockDeckRepo = new MockDeckRepository();
    var mockGameConfigRepo = new MockGameConfigRepository();

    builder.Services.AddSingleton<IGameRepository>(mockGameRepo);
    builder.Services.AddSingleton<IPlayerRepository>(mockPlayerRepo);
    builder.Services.AddSingleton<IDeckRepository>(mockDeckRepo);
    builder.Services.AddSingleton<IGameConfigRepository>(mockGameConfigRepo);
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
    builder.Services.AddSingleton<IPlayerRepository>(sp => new PgPlayerRepository(sp.GetRequiredService<NpgsqlDataSource>()));
    builder.Services.AddSingleton<IDeckRepository>(sp => new PgDeckRepository(sp.GetRequiredService<NpgsqlDataSource>()));
    builder.Services.AddSingleton<ICardRepository>(sp => new PgCardRepository(sp.GetRequiredService<NpgsqlDataSource>()));
    builder.Services.AddSingleton<IGameConfigRepository>(sp => new PgGameConfigRepository(sp.GetRequiredService<NpgsqlDataSource>()));
}

// ─── Card cache ─────────────────────────────────────────────

var cardCache = new CardCache();
builder.Services.AddSingleton<ICardCache>(cardCache);
builder.Services.AddSingleton(cardCache);

if (isLocalDev)
    builder.Services.AddSingleton<ICardRepository>(new MockCardRepository(cardCache));

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

// ─── Matchmaking (Removed: Now handled by Gateway) ──────────

// ─── Services ───────────────────────────────────────────────

builder.Services.AddSingleton<PlayerService>(sp =>
    new PlayerService(
        sp.GetRequiredService<IPlayerRepository>(),
        sp.GetRequiredService<IGameConfigRepository>()));

builder.Services.AddSingleton<GameService>(sp =>
{
    var svc = new GameService(
        sp.GetRequiredService<GameEngine>(),
        sp.GetRequiredService<IGameRepository>(),
        sp.GetRequiredService<IDeckRepository>(),
        sp.GetRequiredService<ICardCache>(),
        sp.GetRequiredService<ILogger<GameService>>(),
        sp.GetRequiredService<PlayerService>());

    // Set default NPC AI
    var cc = sp.GetRequiredService<ICardCache>();
    var engine = sp.GetRequiredService<GameEngine>();
    if (engine.EffectRegistry is EffectRegistry reg)
        svc.SetNpcAI(new StandardAi(cc, reg));

    return svc;
});

// ─── WebSocket (Removed: Now handled by Gateway) ────────────

var app = builder.Build();

// ─── Load card cache ────────────────────────────────────────

if (isLocalDev)
{
    // Load cards from JSON file (same file as Go version)
    var cardsPath = Path.Combine(app.Environment.ContentRootPath, "..", "..", "data", "cards_gen.json");
    if (!File.Exists(cardsPath))
    {
        // Try alternate location
        cardsPath = Path.Combine(app.Environment.ContentRootPath, "data", "cards_gen.json");
    }

    if (File.Exists(cardsPath))
    {
        var jsonStr = await File.ReadAllTextAsync(cardsPath);
        var cards = JsonSerializer.Deserialize<List<CardDefinition>>(jsonStr, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true,
        });
        if (cards is not null)
            cardCache.LoadFromList(cards);
        app.Logger.LogInformation("Loaded {Count} cards from {Path}", cardCache.Count, cardsPath);
    }
    else
    {
        app.Logger.LogWarning("Cards file not found at {Path}", cardsPath);
    }
}
else
{
    // Production: load cards from database
    var cardRepo = app.Services.GetRequiredService<ICardRepository>();
    await cardCache.LoadFromRepository(cardRepo);
    app.Logger.LogInformation("Loaded {Count} cards from database", cardCache.Count);
}

// ─── Matchmaking background service (Removed) ────────────────

// ─── Middleware ──────────────────────────────────────────────

// app.UseWebSockets(); (Removed)

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

// Middleware for auth can be added here if needed, but Gateway already authenticates
// and passes internal requests. For now, we assume internal network trust or simple auth.

// Game Creation
api.MapPost("/games/npc", async (GameService gameSvc, NpcBattleRequest req) =>
{
    try
    {
        var game = await gameSvc.StartNPCBattle(req.PlayerID, req.DeckID, req.NpcFaction);
        return Results.Ok(new { game_id = game.GameID, player1_id = game.Player1ID, player2_id = game.Player2ID });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

// Game Action
api.MapPost("/games/{gameId}/actions", async (GameService gameSvc, string gameId, GameActionRequest req) =>
{
    try
    {
        var actionType = EnumExtensions.ParseActionType(req.ActionType);
        var result = await gameSvc.ProcessAction(gameId, req.PlayerID, actionType, req.Data);
        return Results.Ok(new { game_over = result.GameOver, winner_num = result.WinnerNum, win_reason = result.WinReason });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

// Game State Retrieval
api.MapGet("/games/{gameId}/state/{playerId}", async (GameService gameSvc, string gameId, string playerId) =>
{
    try
    {
        var state = await gameSvc.GetGameStateForPlayer(gameId, playerId);
        if (state == null) return Results.NotFound();
        return Results.Ok(state);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

// Turn Controls Retrieval
api.MapGet("/games/{gameId}/controls/{playerId}", async (GameService gameSvc, string gameId, string playerId) =>
{
    try
    {
        var controls = await gameSvc.GetTurnControlsForPlayer(gameId, playerId);
        if (controls == null) return Results.Ok(null);
        return Results.Ok(new { can_end_phase = controls.CanEndPhase, discard_required = controls.DiscardRequired });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
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

public record NpcBattleRequest(string PlayerID, long DeckID, string NpcFaction);
public record GameActionRequest(string PlayerID, string ActionType, Dictionary<string, object> Data);
