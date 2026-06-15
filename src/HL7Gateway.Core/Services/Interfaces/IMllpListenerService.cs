namespace HL7Gateway.Core.Services.Interfaces;

public interface IMllpListenerService
{
    event EventHandler<MllpMessageReceivedEventArgs>? MessageReceived;
    event EventHandler<MllpClientEventArgs>? ClientConnected;
    event EventHandler<MllpClientEventArgs>? ClientDisconnected;

    bool IsRunning { get; }
    int Port { get; }
    Task StartAsync(int port, CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}

public class MllpMessageReceivedEventArgs : EventArgs
{
    public string RawMessage { get; set; } = string.Empty;
    public string SourceIp { get; set; } = string.Empty;
    public int SourcePort { get; set; }
    public DateTime ReceivedAt { get; set; } = HL7Gateway.Core.ChinaTime.Now;
    public string? AckMessage { get; set; }
}

public class MllpClientEventArgs : EventArgs
{
    public string SourceIp { get; set; } = string.Empty;
    public int SourcePort { get; set; }
    public string? DeviceInfo { get; set; }
}
