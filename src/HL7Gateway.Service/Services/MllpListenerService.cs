using HL7Gateway.Core;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using HL7Gateway.Core.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HL7Gateway.Service.Services;

public class MllpListenerService : IMllpListenerService, IDisposable
{
    private readonly ILogger<MllpListenerService> _logger;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private readonly ConcurrentDictionary<string, TcpClient> _clients = [];
    private readonly Encoding _strictUtf8 = new UTF8Encoding(false, true);

    public event EventHandler<MllpMessageReceivedEventArgs>? MessageReceived;
    public event EventHandler<MllpClientEventArgs>? ClientConnected;
    public event EventHandler<MllpClientEventArgs>? ClientDisconnected;

    public bool IsRunning { get; private set; }
    public int Port { get; private set; }

    private static readonly Encoding _defaultEncoding;

    static MllpListenerService()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        _defaultEncoding = Encoding.GetEncoding("gbk");
    }

    public Encoding Encoding { get; set; } = _defaultEncoding;

    public MllpListenerService(ILogger<MllpListenerService> logger, IConfiguration configuration)
    {
        _logger = logger;
        Encoding = GetConfiguredEncoding(configuration["HL7:Listener:Encoding"]) ?? _defaultEncoding;
    }

    public Task StartAsync(int port, CancellationToken cancellationToken)
    {
        if (IsRunning) return Task.CompletedTask;

        Port = port;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();

        IsRunning = true;
        _logger.LogInformation("MLLP Listener started on 0.0.0.0:{Port}", port);

        _ = Task.Run(() => AcceptClientsAsync(_cts.Token), _cts.Token);
        return Task.CompletedTask;
    }

    private async Task AcceptClientsAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var client = await _listener!.AcceptTcpClientAsync(ct);
                var ep = (IPEndPoint?)client.Client.RemoteEndPoint;
                var key = $"{ep?.Address}:{ep?.Port}";
                _clients.TryAdd(key, client);

                ClientConnected?.Invoke(this, new MllpClientEventArgs
                {
                    SourceIp = ep?.Address.ToString() ?? "unknown",
                    SourcePort = ep?.Port ?? 0
                });

                _ = Task.Run(() => HandleClientAsync(client, key, ct), ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error accepting client");
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, string key, CancellationToken ct)
    {
        var ep = (IPEndPoint?)client.Client.RemoteEndPoint;
        var sourceIp = ep?.Address.ToString() ?? "unknown";
        var sourcePort = ep?.Port ?? 0;

        try
        {
            using var stream = client.GetStream();
            var buffer = new byte[8192];
            var pending = new List<byte>();

            while (!ct.IsCancellationRequested && client.Connected)
            {
                var bytesRead = await stream.ReadAsync(buffer, ct);
                if (bytesRead == 0) break;

                for (int i = 0; i < bytesRead; i++)
                {
                    pending.Add(buffer[i]);

                    if (buffer[i] == 0x1c)  // FS = end of MLLP message
                    {
                        var vtIdx = pending.FindIndex(b => b == 0x0b);  // VT = start
                        if (vtIdx >= 0)
                        {
                            var msgBytes = pending.GetRange(vtIdx + 1, pending.Count - vtIdx - 2).ToArray();
                            var (msg, messageEncoding, charset) = DecodeHl7Message(msgBytes);

                            if (msg.EndsWith('\r'))
                                msg = msg[..^1];

                            // Per-message decode trace. Kept at Debug so it does not flood the
                            // SystemLogs table / frontend (this used to fire once per inbound frame
                            // because devices send MSH-18=UNICODE while the listener default is GBK).
                            if ((!string.IsNullOrWhiteSpace(charset) ||
                                 messageEncoding.CodePage != Encoding.CodePage)
                                && _logger.IsEnabled(LogLevel.Debug))
                            {
                                _logger.LogDebug(
                                    "MLLP message decoded using {Encoding} (codepage {CodePage}), MSH-18={Charset}",
                                    messageEncoding.EncodingName,
                                    messageEncoding.CodePage,
                                    string.IsNullOrWhiteSpace(charset) ? "(empty)" : charset);
                            }

                            // Debug-level receipt trace. Intentionally NOT Information: DbLogger
                            // drops Debug entirely, so this never writes to the SystemLogs table or
                            // the frontend. It is only visible via a console/file sink when deep
                            // debugging, so it cannot bloat the database.
                            if (_logger.IsEnabled(LogLevel.Debug))
                            {
                                var head = msg.Length > 80 ? msg[..80] : msg;
                                _logger.LogDebug(
                                    "MLLP frame received from {Ip}:{Port}, length={Len}, head={Head}",
                                    sourceIp, sourcePort, msg.Length, head.Replace('\r', ' ').Replace('\n', ' '));
                            }

                            var args = new MllpMessageReceivedEventArgs
                            {
                                RawMessage = msg,
                                SourceIp = sourceIp,
                                SourcePort = sourcePort,
                                ReceivedAt = ChinaTime.Now
                            };
                            MessageReceived?.Invoke(this, args);

                            var ack = args.AckMessage ?? BuildAck(msg);
                            var ackBytes = messageEncoding.GetBytes($"\x0b{ack}\x1c\x0d");
                            await stream.WriteAsync(ackBytes, ct);
                        }
                        pending.Clear();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Client {Key} disconnected", key);
        }
        finally
        {
            _clients.TryRemove(key, out _);
            client.Dispose();
            ClientDisconnected?.Invoke(this, new MllpClientEventArgs
            {
                SourceIp = sourceIp,
                SourcePort = sourcePort
            });
        }
    }

    private (string Message, Encoding MessageEncoding, string? Charset) DecodeHl7Message(byte[] bytes)
    {
        var asciiPreview = Encoding.ASCII.GetString(bytes);
        var charset = ExtractMsh18(asciiPreview);

        if (IsUtf8Charset(charset) && TryDecode(bytes, _strictUtf8, out var utf8Message))
            return (utf8Message, Encoding.UTF8, charset);

        if (TryDecode(bytes, _strictUtf8, out utf8Message) && LooksLikeUtf8Preferred(asciiPreview, utf8Message))
            return (utf8Message, Encoding.UTF8, charset);

        return (Encoding.GetString(bytes), Encoding, charset);
    }

    private static bool TryDecode(byte[] bytes, Encoding encoding, out string message)
    {
        try
        {
            message = encoding.GetString(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            message = string.Empty;
            return false;
        }
    }

    private static bool LooksLikeUtf8Preferred(string asciiPreview, string decoded)
    {
        var charset = ExtractMsh18(asciiPreview);
        if (IsUtf8Charset(charset)) return true;
        return decoded.Contains('\u4e00') || decoded.Any(c => c >= '\u4e00' && c <= '\u9fff');
    }

    private static string? ExtractMsh18(string message)
    {
        var firstSegmentEnd = message.IndexOf('\r');
        var msh = firstSegmentEnd >= 0 ? message[..firstSegmentEnd] : message;
        if (!msh.StartsWith("MSH", StringComparison.OrdinalIgnoreCase)) return null;

        var separator = msh.Length > 3 ? msh[3] : '|';
        var fields = msh.Split(separator);

        // Split index 17 corresponds to MSH-18 because MSH-1 is the field separator itself.
        return fields.Length > 17 ? fields[17]?.Trim() : null;
    }

    private static bool IsUtf8Charset(string? charset)
    {
        if (string.IsNullOrWhiteSpace(charset)) return false;
        var normalized = charset.Replace("-", "", StringComparison.Ordinal)
            .Replace("_", "", StringComparison.Ordinal)
            .Replace(" ", "", StringComparison.Ordinal)
            .ToUpperInvariant();
        return normalized.Contains("UTF8", StringComparison.Ordinal)
            || normalized.Contains("UNICODEUTF8", StringComparison.Ordinal);
    }

    private static Encoding? GetConfiguredEncoding(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Equals("auto", StringComparison.OrdinalIgnoreCase))
            return null;

        try
        {
            return Encoding.GetEncoding(name);
        }
        catch
        {
            return null;
        }
    }

    private static string BuildAck(string receivedMessage)
    {
        var controlId = "unknown";
        var lines = receivedMessage.Split('\r');
        if (lines.Length > 0)
        {
            var mshFields = lines[0].Split('|');
            if (mshFields.Length > 9)
                controlId = mshFields[9];

            var sendingApp = mshFields.Length > 2 ? mshFields[2] : "";
            var sendingFacility = mshFields.Length > 3 ? mshFields[3] : "";
            var receivingApp = mshFields.Length > 5 ? mshFields[5] : "";
            var receivingFacility = mshFields.Length > 6 ? mshFields[6] : "";

            return $"MSH|^~\\&|{receivingApp}|{receivingFacility}|{sendingApp}|{sendingFacility}|{ChinaTime.Now:yyyyMMddHHmmss}||ACK|{Guid.NewGuid():N}|P|2.4\rMSA|AA|{controlId}";
        }

        return $"MSH|^~\\&|||||{ChinaTime.Now:yyyyMMddHHmmss}||ACK|{Guid.NewGuid():N}|P|2.4\rMSA|AA|{controlId}";
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        IsRunning = false;
        _cts?.Cancel();

        foreach (var (_, client) in _clients)
            client.Dispose();
        _clients.Clear();

        _listener?.Stop();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _listener?.Stop();
        foreach (var (_, client) in _clients)
            client.Dispose();
        _clients.Clear();
    }
}
