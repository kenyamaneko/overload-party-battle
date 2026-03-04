using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Server.Middleware;

/// <summary>
/// Development-mode auth middleware.
/// Accepts tokens in format "dev-token-{uid}" without Firebase verification.
/// Auto-creates players if they don't exist.
/// </summary>
public class DevAuthMiddleware
{
    private readonly RequestDelegate _next;

    public DevAuthMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IPlayerRepository playerRepo)
    {
        // For WebSocket: token comes from query string
        var token = context.Request.Query["token"].FirstOrDefault();

        // For REST: token comes from Authorization header
        if (token is null)
        {
            var auth = context.Request.Headers.Authorization.FirstOrDefault();
            if (auth?.StartsWith("Bearer ") == true)
                token = auth["Bearer ".Length..];
        }

        if (token is null || !token.StartsWith("dev-token-"))
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsync("unauthorized: use dev-token-{uid} format");
            return;
        }

        var uid = token["dev-token-".Length..];

        // Look up or create player
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
            var dailyBattle = new PlayerDailyBattle
            {
                PlayerID = player.PlayerID,
                DailyBattleCount = 0,
                LastResetDate = DateOnly.FromDateTime(DateTime.UtcNow),
            };
            await playerRepo.Create(player, dailyBattle);
        }

        context.Items["PlayerID"] = player.PlayerID;
        context.Items["FirebaseUID"] = uid;

        await _next(context);
    }
}
