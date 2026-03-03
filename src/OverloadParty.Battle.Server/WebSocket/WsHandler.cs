using System.Net.WebSockets;
using OverloadParty.Battle.Data;

namespace OverloadParty.Battle.Server.WebSocket;

/// <summary>
/// HTTP → WebSocket upgrade handler.
/// Authenticates the player and hands off to WsManager.
/// </summary>
public class WsHandler
{
    private readonly WsManager _manager;
    private readonly ILogger<WsHandler> _logger;

    public WsHandler(WsManager manager, ILogger<WsHandler> logger)
    {
        _manager = manager;
        _logger = logger;
    }

    /// <summary>
    /// Handles GET /ws?token={...} upgrade request.
    /// playerID must already be resolved by middleware and placed in HttpContext.Items["PlayerID"].
    /// </summary>
    public async Task HandleUpgrade(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsync("WebSocket request expected");
            return;
        }

        var playerID = context.Items["PlayerID"] as string;
        if (string.IsNullOrEmpty(playerID))
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsync("unauthorized");
            return;
        }

        var ws = await context.WebSockets.AcceptWebSocketAsync();
        var conn = new WsConnection(ws, playerID, _logger);

        _manager.Register(conn);

        try
        {
            // Run read and write pumps concurrently
            var readTask = conn.ReadPumpAsync((c, msg) => _manager.HandleMessage(c, msg), context.RequestAborted);
            var writeTask = conn.WritePumpAsync(context.RequestAborted);

            await Task.WhenAny(readTask, writeTask);
        }
        finally
        {
            _manager.Unregister(conn);
            await conn.CloseAsync();
            conn.Dispose();
        }
    }
}
