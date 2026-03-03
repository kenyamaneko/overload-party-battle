using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace OverloadParty.Battle.Server.WebSocket;

/// <summary>
/// Represents a single WebSocket connection for a player.
/// </summary>
public class WsConnection : IDisposable
{
    private readonly System.Net.WebSockets.WebSocket _ws;
    private readonly Channel<byte[]> _sendChannel;
    private readonly ILogger _logger;
    private bool _closed;
    private readonly Lock _lock = new();

    private static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(15);
    private const int MaxMessageSize = 4096;

    public string PlayerID { get; }

    public WsConnection(System.Net.WebSockets.WebSocket ws, string playerID, ILogger logger)
    {
        _ws = ws;
        PlayerID = playerID;
        _logger = logger;
        _sendChannel = Channel.CreateBounded<byte[]>(64);
    }

    public void SendMessage(object message)
    {
        var data = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions.Default);

        lock (_lock)
        {
            if (_closed) return;
            if (!_sendChannel.Writer.TryWrite(data))
                _logger.LogWarning("Send buffer full for player {PlayerID}, dropping message", PlayerID);
        }
    }

    /// <summary>
    /// Reads messages from the WebSocket and dispatches to the handler.
    /// </summary>
    public async Task ReadPumpAsync(Func<WsConnection, WsMessage, Task> handler, CancellationToken ct)
    {
        var buffer = new byte[MaxMessageSize];

        try
        {
            while (!ct.IsCancellationRequested && _ws.State == WebSocketState.Open)
            {
                var result = await _ws.ReceiveAsync(buffer, ct);

                if (result.MessageType == WebSocketMessageType.Close)
                    break;

                if (result.MessageType != WebSocketMessageType.Text)
                    continue;

                var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                WsMessage? msg;
                try
                {
                    msg = JsonSerializer.Deserialize<WsMessage>(json, JsonOptions.Default);
                }
                catch
                {
                    SendMessage(new { type = WsMsgType.Error, data = new { error_code = "invalid_message", message = "invalid JSON", retryable = false } });
                    continue;
                }

                if (msg is not null)
                    await handler(this, msg);
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
    }

    /// <summary>
    /// Writes queued messages to the WebSocket.
    /// </summary>
    public async Task WritePumpAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var data in _sendChannel.Reader.ReadAllAsync(ct))
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(WriteTimeout);

                await _ws.SendAsync(data, WebSocketMessageType.Text, true, cts.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
    }

    public async Task CloseAsync()
    {
        lock (_lock)
        {
            if (_closed) return;
            _closed = true;
            _sendChannel.Writer.TryComplete();
        }

        try
        {
            if (_ws.State == WebSocketState.Open)
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", CancellationToken.None);
        }
        catch { }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (!_closed)
            {
                _closed = true;
                _sendChannel.Writer.TryComplete();
            }
        }
        _ws.Dispose();
    }
}
