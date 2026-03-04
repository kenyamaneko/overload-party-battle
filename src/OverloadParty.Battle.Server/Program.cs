using System.Text.Json;
using Npgsql;
using OverloadParty.Battle.Data;
using OverloadParty.Battle.Data.Mock;
using OverloadParty.Battle.Data.Pg;
using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Matchmaking;
using OverloadParty.Battle.Models;
using OverloadParty.Battle.Npc;
using OverloadParty.Battle.Server.Middleware;
using OverloadParty.Battle.Server.WebSocket;
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

// ─── Matchmaking ────────────────────────────────────────────

builder.Services.AddSingleton<MatchQueue>();

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
        sp.GetRequiredService<MatchQueue>(),
        sp.GetRequiredService<ILogger<GameService>>(),
        sp.GetRequiredService<PlayerService>());

    // Set default NPC AI
    var cc = sp.GetRequiredService<ICardCache>();
    var engine = sp.GetRequiredService<GameEngine>();
    if (engine.EffectRegistry is EffectRegistry reg)
        svc.SetNpcAI(new StandardAi(cc, reg));

    return svc;
});

// ─── WebSocket ──────────────────────────────────────────────

builder.Services.AddSingleton<WsManager>();
builder.Services.AddSingleton<WsHandler>();

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

// ─── Matchmaking background service ─────────────────────────

var matcher = new Matcher(
    app.Services.GetRequiredService<MatchQueue>(),
    async (result, ct) =>
    {
        var gameService = app.Services.GetRequiredService<GameService>();
        var wsManager = app.Services.GetRequiredService<WsManager>();
        var game = await gameService.CreateGameFromMatch(result, ct);

        // Notify matched players
        wsManager.SendToPlayer(result.Player1ID, new
        {
            type = WsMsgType.MatchFound,
            data = new { game_id = game.GameID, player1_id = game.Player1ID, player2_id = game.Player2ID },
        });
        wsManager.SendToPlayer(result.Player2ID, new
        {
            type = WsMsgType.MatchFound,
            data = new { game_id = game.GameID, player1_id = game.Player1ID, player2_id = game.Player2ID },
        });
    },
    app.Services.GetRequiredService<ILogger<Matcher>>());

_ = Task.Run(() => matcher.RunAsync(app.Lifetime.ApplicationStopping));

// ─── Middleware ──────────────────────────────────────────────

app.UseWebSockets();

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

// WebSocket endpoint
app.Map("/ws", async (HttpContext context) =>
{
    if (isLocalDev)
    {
        // Dev auth inline
        var token = context.Request.Query["token"].FirstOrDefault();
        if (token is null || !token.StartsWith("dev-token-"))
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsync("unauthorized: use ?token=dev-token-{uid}");
            return;
        }

        var uid = token["dev-token-".Length..];
        var playerRepo = context.RequestServices.GetRequiredService<IPlayerRepository>();
        var player = await playerRepo.FindByFirebaseUID(uid);
        if (player is null)
        {
            // Auto-create dev player
            player = new Player
            {
                PlayerID = Guid.NewGuid().ToString("N"),
                FirebaseUID = uid,
                Username = $"Dev_{uid}",
                Level = 1,
                IsPremium = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };
            await playerRepo.Create(player, new PlayerDailyBattle
            {
                PlayerID = player.PlayerID,
                LastResetDate = DateOnly.FromDateTime(DateTime.UtcNow),
            });

            // Create a starter deck for the new player
            if (context.RequestServices.GetRequiredService<IDeckRepository>() is MockDeckRepository mockDeck)
            {
                mockDeck.CreateDeckFromCardNos(player.PlayerID, "SD Starter", NpcDecks.SDDeck.Cards);
            }
        }

        context.Items["PlayerID"] = player.PlayerID;
    }
    // TODO: Production Firebase auth

    var handler = context.RequestServices.GetRequiredService<WsHandler>();
    await handler.HandleUpgrade(context);
});

// Dev REST API endpoints (local mode only)
if (isLocalDev)
{
    app.MapGet("/api/dev/cards", (ICardCache cc) =>
        Results.Ok(cc.All().Values.Select(c => new { c.CardNo, c.CardName, c.Faction, c.CardType })));

    app.MapGet("/api/dev/status", (MatchQueue queue) =>
        Results.Ok(new { queue_size = queue.Count }));
}

var port = Environment.GetEnvironmentVariable("PORT") ?? "9002";
app.Urls.Add($"http://0.0.0.0:{port}");

app.Logger.LogInformation("Battle server starting on port {Port} (mode={Mode})",
    port, isLocalDev ? "local" : "production");

app.Run();
