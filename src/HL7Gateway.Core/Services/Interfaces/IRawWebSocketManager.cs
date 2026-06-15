using System.Net.WebSockets;

namespace HL7Gateway.Core.Services.Interfaces;

public interface IRawWebSocketManager
{
    int ConnectionCount { get; }
    Task BroadcastAsync(string message);
    Task HandleWebSocketAsync(WebSocket ws, CancellationToken ct);
}
