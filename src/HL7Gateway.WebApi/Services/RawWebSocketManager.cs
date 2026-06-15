using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using HL7Gateway.Core.Services.Interfaces;

namespace HL7Gateway.WebApi.Services;

public class RawWebSocketManager : IRawWebSocketManager
{
    private readonly ConcurrentDictionary<string, WebSocket> _sockets = new();
    private readonly ILogger<RawWebSocketManager> _logger;

    public RawWebSocketManager(ILogger<RawWebSocketManager> logger)
    {
        _logger = logger;
    }

    public int ConnectionCount => _sockets.Count;

    public async Task HandleWebSocketAsync(WebSocket ws, CancellationToken ct)
    {
        var connectionId = Guid.NewGuid().ToString("N");

        _sockets.TryAdd(connectionId, ws);
        _logger.LogInformation("WebSocket connected: {ConnectionId}. Total connections: {Count}", connectionId, ConnectionCount);

        try
        {
            var buffer = new byte[4096];
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                WebSocketReceiveResult result;
                try
                {
                    result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                }
                catch (WebSocketException ex)
                {
                    _logger.LogDebug(ex, "WebSocket receive error for {ConnectionId}", connectionId);
                    break;
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _logger.LogInformation("WebSocket close frame received from {ConnectionId}", connectionId);
                    break;
                }

                // We don't expect messages from the client for this push-only socket,
                // but we must keep reading to detect disconnects.
                if (result.Count == 0)
                {
                    // Empty receive likely means the socket is closing
                    break;
                }
            }
        }
        finally
        {
            _sockets.TryRemove(connectionId, out _);
            _logger.LogInformation("WebSocket disconnected: {ConnectionId}. Total connections: {Count}", connectionId, ConnectionCount);

            if (ws.State == WebSocketState.Open || ws.State == WebSocketState.CloseReceived)
            {
                try
                {
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error closing WebSocket {ConnectionId}", connectionId);
                }
            }

            ws.Dispose();
        }
    }

    public async Task BroadcastAsync(string message)
    {
        if (_sockets.IsEmpty)
            return;

        var bytes = Encoding.UTF8.GetBytes(message);
        var segment = new ArraySegment<byte>(bytes);

        var tasks = new List<Task>(_sockets.Count);

        foreach (var kvp in _sockets)
        {
            var ws = kvp.Value;
            if (ws.State != WebSocketState.Open)
                continue;

            tasks.Add(SendAsyncSafe(ws, segment, kvp.Key));
        }

        if (tasks.Count > 0)
            await Task.WhenAll(tasks);
    }

    private async Task SendAsyncSafe(WebSocket ws, ArraySegment<byte> segment, string connectionId)
    {
        try
        {
            await ws.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to send message to WebSocket {ConnectionId}", connectionId);
        }
    }
}
