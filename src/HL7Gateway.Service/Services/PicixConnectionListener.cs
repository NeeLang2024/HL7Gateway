using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Security;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using CoreWCF.Channels;
using HL7Gateway.Service.Services.Wcf;
using Microsoft.Extensions.Logging;

namespace HL7Gateway.Service.Services;

internal static class FramingRecordType
{
    public const byte Version = 0x00;
    public const byte Mode = 0x01;
    public const byte Via = 0x02;
    public const byte KnownEncoding = 0x03;
    public const byte ExtensibleEncoding = 0x04;
    public const byte UnsizedEnvelope = 0x05;
    public const byte SizedEnvelope = 0x06;
    public const byte End = 0x07;
    public const byte Fault = 0x08;
    public const byte UpgradeRequest = 0x09;
    public const byte UpgradeResponse = 0x0A;
    public const byte PreambleAck = 0x0B;
    public const byte PreambleEnd = 0x0C;
}

internal static class FramingEncodingType
{
    public const byte BinarySession = 0x08;
    public const byte Binary = 0x01;
    public const byte Soap12Utf8 = 0x04;
}

public class PicixConnectionListener : IDisposable
{
    private readonly ILogger<PicixConnectionListener> _logger;
    private readonly PicixCallbackManager _callbackManager;
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<string, TcpPicixConnection> _connections = [];
    private readonly MessageEncoderFactory _encoderFactory;
    private readonly BufferManager _bufferManager;
    private readonly string? _callbackActionOverride;
    private readonly string _callbackOperation;
    private readonly string _wcfSendMode;
    private Task? _acceptLoop;
    private readonly int _port;

    private static readonly TimeSpan CleanupInterval = TimeSpan.FromSeconds(30);

    public PicixConnectionListener(ILogger<PicixConnectionListener> logger, PicixCallbackManager callbackManager, IConfiguration configuration)
    {
        _logger = logger;
        _callbackManager = callbackManager;
        _port = configuration.GetValue<int?>("HL7:ADT:ListenerPort") ?? 9912;
        _callbackActionOverride = NormalizeConfigValue(configuration["HL7:ADT:CallbackAction"]);
        _callbackOperation = NormalizeConfigValue(configuration["HL7:ADT:CallbackOperation"]) ?? "OnPIChange";
        _wcfSendMode = NormalizeConfigValue(configuration["HL7:ADT:WcfSendMode"]) ?? "ServiceExecute";

        var bindingElement = new BinaryMessageEncodingBindingElement();
        _encoderFactory = bindingElement.CreateMessageEncoderFactory();
        // PIC iX uses WCF Net.TCP duplex framing with KnownEncoding 0x08 (BinarySession).
        // A plain binary encoder cannot decode the session dictionary records in the
        // first Subscribe envelope. Each TCP connection needs its own session encoder
        // because the binary dictionary is stateful.
        _bufferManager = BufferManager.CreateBufferManager(65536, 65536);

        _listener = new TcpListener(System.Net.IPAddress.Any, _port);
    }

    public int Port => _port;
    public int ConnectionCount => _connections.Count;

    public void Start()
    {
        _listener.Start();
        _logger.LogInformation(
            "PicixConnectionListener started on port {Port}, wcfSendMode={WcfSendMode}, callbackOperation={CallbackOperation}, callbackActionOverride={CallbackActionOverride}",
            _port,
            _wcfSendMode,
            _callbackOperation,
            string.IsNullOrWhiteSpace(_callbackActionOverride) ? "(auto)" : _callbackActionOverride);
        _acceptLoop = Task.Run(AcceptLoopAsync);
        _ = Task.Run(CleanupLoopAsync);
    }

    public void Stop()
    {
        _cts.Cancel();
        _listener.Stop();
        _logger.LogInformation("PicixConnectionListener stopped");
    }

    public void Dispose()
    {
        Stop();
        _cts.Dispose();
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                client.NoDelay = true;
                client.LingerState = new LingerOption(false, 0);
                client.ReceiveBufferSize = 65536;
                client.SendBufferSize = 65536;

                var ep = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
                var conn = new TcpPicixConnection(
                    client,
                    Guid.NewGuid().ToString("N"),
                    ep,
                    _encoderFactory.CreateSessionEncoder(),
                    _bufferManager,
                    _logger,
                    _callbackManager,
                    _callbackActionOverride,
                    _callbackOperation,
                    _wcfSendMode);
                _connections[conn.Id] = conn;
                _logger.LogInformation("PIC iX connection from {Endpoint}, total: {Count}", ep, _connections.Count);

                _ = Task.Run(() => HandleConnectionAsync(conn));
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PIC iX accept error");
            }
        }
    }

    private async Task HandleConnectionAsync(TcpPicixConnection conn)
    {
        try
        {
            await conn.HandleAsync(_cts.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PIC iX connection handler error: {Id}", conn.Id);
        }
        finally
        {
            _connections.TryRemove(conn.Id, out _);
            conn.Dispose();
            _logger.LogInformation("PIC iX connection {Id} closed, remaining: {Count}", conn.Id[..8], _connections.Count);
        }
    }

    private async Task CleanupLoopAsync()
    {
        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(CleanupInterval, _cts.Token);
                var dead = _connections.Values.Where(c => !c.IsConnected).ToList();
                foreach (var c in dead)
                {
                    if (_connections.TryRemove(c.Id, out _))
                        c.Dispose();
                }
                if (dead.Count > 0)
                    _logger.LogInformation("Cleaned up {Count} dead PIC iX connections", dead.Count);
            }
            catch (OperationCanceledException) { break; }
            catch { }
        }
    }

    private static string? NormalizeConfigValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}

internal class TcpPicixConnection : IDisposable
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly MessageEncoder _encoder;
    private readonly BufferManager _bufferManager;
    private readonly ILogger _logger;
    private readonly PicixCallbackManager _callbackManager;
    private readonly string? _callbackActionOverride;
    private readonly string _callbackOperation;
    private readonly string _wcfSendMode;
    private readonly object _sendLock = new();
    private string? _callbackActionPrefix;
    private string? _outboundSequenceIdentifier;
    private long _outboundMessageNumber = 1;
    private string? _inboundSequenceIdentifier;
    private long _lastInboundMessageNumber;
    private readonly object _faultLock = new();
    private TaskCompletionSource<string>? _pendingFault;

    public string Id { get; }
    public string RemoteEndPoint { get; }
    public string CallbackAction => BuildAdtAction();
    public string WcfSendMode => _wcfSendMode;
    public bool IsConnected
    {
        get
        {
            try { return _client?.Connected == true && _client.Client?.Poll(0, SelectMode.SelectRead) == false; }
            catch { return false; }
        }
    }

    public TcpPicixConnection(
        TcpClient client,
        string id,
        string ep,
        MessageEncoder encoder,
        BufferManager bufferManager,
        ILogger logger,
        PicixCallbackManager callbackManager,
        string? callbackActionOverride,
        string callbackOperation,
        string wcfSendMode)
    {
        _client = client;
        _stream = client.GetStream();
        _encoder = encoder;
        _bufferManager = bufferManager;
        _logger = logger;
        _callbackManager = callbackManager;
        _callbackActionOverride = callbackActionOverride;
        _callbackOperation = string.IsNullOrWhiteSpace(callbackOperation) ? "OnPIChange" : callbackOperation.Trim();
        _wcfSendMode = string.IsNullOrWhiteSpace(wcfSendMode) ? "ServiceExecute" : wcfSendMode.Trim();
        Id = id;
        RemoteEndPoint = ep;
    }

    public async Task HandleAsync(CancellationToken ct)
    {
        try
        {
            // 1. Read preamble using proper record-type state machine
            var preambleMode = await ReadPreambleAsync(ct);
            if (preambleMode == -1)
            {
                _logger.LogWarning("Failed to read preamble from {Ep}", RemoteEndPoint);
                return;
            }

            if (preambleMode == 0)
            {
                await _stream.WriteAsync(new byte[] { FramingRecordType.PreambleAck }, ct);
                await _stream.FlushAsync(ct);
                _logger.LogInformation("PreambleAck sent to {Ep}", RemoteEndPoint);

                await MessageLoopAsync(ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException ex) { _logger.LogWarning("PIC iX connection IO error: {Msg}", ex.Message); }
        catch (Exception ex) { _logger.LogError(ex, "PIC iX handler error: {Msg}", ex.Message); }
    }

    /// <summary>Reads the Net.TCP preamble. Returns -1 on failure, otherwise the framing mode.</summary>
    private async Task<int> ReadPreambleAsync(CancellationToken ct)
    {
        var recordType = await ReadByteAsync(ct);
        if (recordType != FramingRecordType.Version)
        {
            _logger.LogWarning("Expected VersionRecord(0x00), got 0x{0:x2}", recordType);
            return -1;
        }

        var majorVersion = await ReadByteAsync(ct);
        var minorVersion = await ReadByteAsync(ct);
        _logger.LogInformation("Preamble Version: {Major}.{Minor}", majorVersion, minorVersion);

        recordType = await ReadByteAsync(ct);
        if (recordType != FramingRecordType.Mode)
        {
            _logger.LogWarning("Expected ModeRecord(0x01), got 0x{0:x2}", recordType);
            return -1;
        }

        var mode = await ReadByteAsync(ct);
        _logger.LogInformation("Preamble Mode: 0x{Mode:x2} ({ModeName})",
            mode, mode switch { 1 => "Singleton", 2 => "Duplex", 3 => "Simplex", _ => "Unknown" });

        // Standard WCF Net.TCP preamble:
        // [0x02=ViaRecord] [via_len varint] [via_string]
        // [0x03=KnownEncoding] [encoding_byte]
        // [optional upgrade requests]
        // [0x0c=PreambleEnd]

        recordType = await ReadByteAsync(ct);
        if (recordType != FramingRecordType.Via)
        {
            _logger.LogWarning("Expected ViaRecord(0x02), got 0x{0:x2}", recordType);
            return -1;
        }

        var via = await ReadLengthPrefixedStringAsync(ct);
        _logger.LogInformation("Preamble Via: {Via}", via);

        recordType = await ReadByteAsync(ct);
        if (recordType == FramingRecordType.KnownEncoding)
        {
            var encoding = await ReadByteAsync(ct);
            _logger.LogInformation("Preamble KnownEncoding: 0x{Encoding:x2}", encoding);
        }
        else if (recordType == FramingRecordType.ExtensibleEncoding)
        {
            var contentType = await ReadLengthPrefixedStringAsync(ct);
            _logger.LogInformation("Preamble ExtensibleEncoding: {ContentType}", contentType);
        }
        else
        {
            _logger.LogWarning("Expected KnownEncoding(0x08) or ExtensibleEncoding(0x09), got 0x{0:x2}", recordType);
            return -1;
        }

        // Handle optional UpgradeRequest records, then PreambleEnd
        while (true)
        {
            recordType = await ReadByteAsync(ct);
            if (recordType == FramingRecordType.UpgradeRequest)
            {
                var upgrade = await ReadLengthPrefixedStringAsync(ct);
                _logger.LogInformation("Preamble UpgradeRequest: {Upgrade}", upgrade);
            }
            else if (recordType == FramingRecordType.PreambleEnd)
            {
                break;
            }
            else
            {
                _logger.LogWarning("Expected UpgradeRequest(0x07) or PreambleEnd(0x04), got 0x{0:x2}", recordType);
                return -1;
            }
        }

        _logger.LogInformation("Preamble complete");
        return 0;
    }

    private async Task MessageLoopAsync(CancellationToken ct)
    {
        _logger.LogInformation("MessageLoopAsync started, waiting for first record...");
        while (!ct.IsCancellationRequested && _client.Connected)
        {
            var recordType = await ReadByteAsync(ct);
            _logger.LogInformation("MessageLoopAsync: recordType=0x{0:x2}", recordType);

            if (recordType == FramingRecordType.End)
            {
                _logger.LogInformation("PIC iX sent End record, session complete");
                break;
            }

            byte[]? payload = null;
            int payloadOffset = 0;
            int payloadLength = 0;
            var payloadFromBufferManager = false;

            if (recordType == FramingRecordType.UnsizedEnvelope)
            {
                using var ms = new MemoryStream();
                while (true)
                {
                    var b = await ReadByteAsync(ct);
                    if (b == FramingRecordType.End) break;
                    ms.WriteByte(b);
                }
                payload = ms.ToArray();
                payloadLength = payload.Length;
            }
            else if (recordType == FramingRecordType.SizedEnvelope)
            {
                var size = await ReadVarIntAsync(ct);
                if (size <= 0) break;
                if (size > 256 * 1024)
                {
                    _logger.LogWarning("Frame too large: {Size}", size);
                    break;
                }
                payload = await ReadBufferFromManagerAsync(size, ct);
                if (payload == null) break;
                payloadLength = size;
                payloadFromBufferManager = true;
            }
            else
            {
                _logger.LogWarning("Expected envelope/end record, got 0x{0:x2}", recordType);
                continue;
            }

            _logger.LogInformation("Received {Len} bytes from PIC iX", payloadLength);

            Message? request = null;
            try
            {
                request = _encoder.ReadMessage(new ArraySegment<byte>(payload, payloadOffset, payloadLength), _bufferManager);
                payload = null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to decode binary message ({Len} bytes): {Msg}", payloadLength, ex.Message);
                if (payload != null)
                    _logger.LogInformation("Raw hex: {Hex}", BitConverter.ToString(payload, payloadOffset, payloadLength));
                if (payloadFromBufferManager && payload != null)
                    _bufferManager.ReturnBuffer(payload);
                continue;
            }

            if (request == null) continue;

            var action = request.Headers.Action ?? "";
            _logger.LogInformation("SOAP action: {Action}", action);

            try
            {
                if (action.Contains("Subscribe"))
                    await HandleSubscribeAsync(request);
                else if (action.Contains("Unsubscribe"))
                    await HandleUnsubscribeAsync(request);
                else if (action.Contains("Heartbeat"))
                    await HandleHeartbeatAsync(request);
                else if (action.Equals("http://schemas.xmlsoap.org/ws/2005/02/rm/CreateSequence", StringComparison.OrdinalIgnoreCase))
                    await HandleCreateSequenceAsync(request);
                else if (action.Equals("http://schemas.xmlsoap.org/ws/2005/02/rm/SequenceAcknowledgement", StringComparison.OrdinalIgnoreCase))
                    await HandleSequenceAcknowledgementAsync(request);
                else if (action.Equals("http://schemas.xmlsoap.org/ws/2005/02/rm/AckRequested", StringComparison.OrdinalIgnoreCase))
                    await HandleAckRequestedAsync(request);
                else if (action.Contains("/rm/TerminateSequence", StringComparison.OrdinalIgnoreCase))
                    await HandleTerminateSequenceAsync(request);
                else if (IsSoapFaultAction(action))
                    await HandleSoapFaultAsync(request);
                else
                {
                    _logger.LogWarning("Unknown SOAP action: {Action}", action);
                    SendEmptyResponse(request, action);
                }
            }
            finally
            {
                request.Close();
            }
        }
    }

    private async Task HandleSubscribeAsync(Message request)
    {
        var clientId = ExtractClientId(request) ?? Id;
        _callbackActionPrefix = GetActionPrefix(request.Headers.Action);
        _logger.LogInformation(
            "Subscribe: clientId={ClientId}, callbackActionPrefix={Prefix}, callbackAction={CallbackAction}",
            clientId,
            _callbackActionPrefix ?? "(none)",
            CallbackAction);
        var callback = new TcpPicixCallback(this, clientId, _logger);
        _callbackManager.AddSubscriber(clientId, callback);
        SendEmptyResponse(request, "SubscribeResponse");
        await Task.CompletedTask;
    }

    private async Task HandleUnsubscribeAsync(Message request)
    {
        var clientId = ExtractClientId(request);
        if (!string.IsNullOrEmpty(clientId))
            _callbackManager.RemoveSubscriber(clientId);
        _logger.LogInformation("Unsubscribe: clientId={ClientId}", clientId ?? "unknown");
        SendEmptyResponse(request, "UnsubscribeResponse");
        await Task.CompletedTask;
    }

    private async Task HandleHeartbeatAsync(Message request)
    {
        _logger.LogDebug("Heartbeat from {Ep}", RemoteEndPoint);
        SendEmptyResponse(request, "HeartbeatResponse");
        await Task.CompletedTask;
    }

    private async Task HandleCreateSequenceAsync(Message request)
    {
        const string rmNs = "http://schemas.xmlsoap.org/ws/2005/02/rm";
        const string defaultAddressingNs = "http://www.w3.org/2005/08/addressing";
        const string defaultAnonymousAddress = "http://www.w3.org/2005/08/addressing/anonymous";
        var identifier = $"urn:uuid:{Guid.NewGuid()}";
        var hasOffer = false;
        string? offerIdentifier = null;
        var addressingNs = defaultAddressingNs;
        var requestedAcksToAddress = defaultAnonymousAddress;
        var applicationAddress = request.Headers.To?.ToString()
            ?? request.Headers.ReplyTo?.Uri?.ToString()
            ?? defaultAnonymousAddress;

        try
        {
            var requestBodyXml = ReadBodyXml(request);
            var requestBody = XElement.Parse(requestBodyXml);
            var offerElement = requestBody.Descendants(XName.Get("Offer", rmNs)).FirstOrDefault();
            hasOffer = offerElement != null;
            offerIdentifier = offerElement?.Element(XName.Get("Identifier", rmNs))?.Value?.Trim();
            var addressElement = requestBody
                .Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "Address" && !string.IsNullOrWhiteSpace(e.Value));
            if (addressElement != null && !string.IsNullOrWhiteSpace(addressElement.Name.NamespaceName))
            {
                addressingNs = addressElement.Name.NamespaceName;
                requestedAcksToAddress = addressElement.Value.Trim();
            }

            _logger.LogInformation("WS-RM CreateSequence body: {Body}", requestBodyXml);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to inspect WS-RM CreateSequence body: {Msg}", ex.Message);
        }

        var accept = hasOffer
            ? $"""
                <Accept>
                  <AcksTo>
                    <a:Address>{SecurityElement.Escape(applicationAddress)}</a:Address>
                  </AcksTo>
                </Accept>
                """
            : "";

        if (!string.IsNullOrWhiteSpace(offerIdentifier))
        {
            _outboundSequenceIdentifier = offerIdentifier;
            _outboundMessageNumber = 1;
        }

        var body = $"""
            <CreateSequenceResponse xmlns="{rmNs}" xmlns:a="{addressingNs}">
              <Identifier>{identifier}</Identifier>
              {accept}
            </CreateSequenceResponse>
            """;

        _logger.LogInformation(
            "WS-RM CreateSequence from {Ep}, identifier={Identifier}, offer={Offer}, offerIdentifier={OfferIdentifier}, requestedAcksTo={RequestedAcksTo}, acceptAcksTo={AcceptAcksTo}, to={To}, replyTo={ReplyTo}",
            RemoteEndPoint,
            identifier,
            hasOffer,
            offerIdentifier ?? "(none)",
            requestedAcksToAddress,
            applicationAddress,
            request.Headers.To,
            request.Headers.ReplyTo?.Uri);
        SendMessage(body, $"{rmNs}/CreateSequenceResponse", request.Headers.MessageId);
        await Task.CompletedTask;
    }

    private async Task HandleSoapFaultAsync(Message request)
    {
        try
        {
            var body = ReadBodyXml(request);
            _logger.LogWarning("SOAP Fault from PIC iX: {Body}", body);
            NotifySoapFault(body);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SOAP Fault from PIC iX, but failed to read body: {Msg}", ex.Message);
            NotifySoapFault(ex.Message);
        }

        await Task.CompletedTask;
    }

    private void NotifySoapFault(string fault)
    {
        lock (_faultLock)
        {
            _pendingFault?.TrySetResult(fault);
        }
    }

    private async Task HandleSequenceAcknowledgementAsync(Message request)
    {
        try
        {
            var body = ReadBodyXml(request);
            _logger.LogInformation("WS-RM SequenceAcknowledgement from {Ep}: {Body}", RemoteEndPoint, body);
        }
        catch (Exception ex)
        {
            _logger.LogInformation("WS-RM SequenceAcknowledgement from {Ep}, body unreadable: {Msg}", RemoteEndPoint, ex.Message);
        }

        await Task.CompletedTask;
    }

    private async Task HandleAckRequestedAsync(Message request)
    {
        var ack = ExtractAckRequestedInfo(request)
            ?? (!string.IsNullOrWhiteSpace(_inboundSequenceIdentifier) && _lastInboundMessageNumber > 0
                ? new WsRmSequenceInfo(_inboundSequenceIdentifier, _lastInboundMessageNumber)
                : null);

        if (ack == null)
        {
            _logger.LogWarning("WS-RM AckRequested from {Ep}, but no inbound sequence is known", RemoteEndPoint);
            await Task.CompletedTask;
            return;
        }

        _logger.LogInformation("WS-RM AckRequested from {Ep}, replying with ack identifier={Identifier}, messageNumber={MessageNumber}",
            RemoteEndPoint, ack.Identifier, ack.MessageNumber);
        SendMessage((BodyWriter?)null, "http://schemas.xmlsoap.org/ws/2005/02/rm/SequenceAcknowledgement", acknowledgement: ack);
        await Task.CompletedTask;
    }

    private async Task HandleTerminateSequenceAsync(Message request)
    {
        _logger.LogInformation("WS-RM TerminateSequence from {Ep}", RemoteEndPoint);
        await Task.CompletedTask;
    }

    private string? ExtractClientId(Message request)
    {
        try
        {
            var bodyXml = ReadBodyXml(request);
            _logger.LogInformation("Subscribe body from {Ep}: {Body}", RemoteEndPoint, bodyXml);
            var doc = XDocument.Parse(bodyXml);
            var bodyNs = doc.Root?.Name.Namespace;
            if (bodyNs == null) return null;
            var el = doc.Root?.Name.LocalName is "Subscribe" or "Unsubscribe"
                ? doc.Root
                : doc.Root?.Element(bodyNs + "Subscribe") ?? doc.Root?.Element(bodyNs + "Unsubscribe");
            return el?.Elements().FirstOrDefault(e => e.Name.LocalName == "clientId")?.Value;
        }
        catch { return null; }
    }

    private void SendEmptyResponse(Message request, string actionSuffix)
    {
        var requestAction = request.Headers.Action ?? "";
        var responseAction = requestAction.Contains("/")
            ? requestAction[..(requestAction.LastIndexOf('/') + 1)] + actionSuffix
            : actionSuffix;
        SendMessage((BodyWriter?)null, responseAction, request.Headers.MessageId, ExtractSequenceInfo(request));
    }

    private static string? GetActionPrefix(string? action)
    {
        if (string.IsNullOrWhiteSpace(action)) return null;
        var slash = action.LastIndexOf('/');
        if (slash < 0) return null;
        return action[..(slash + 1)];
    }

    private static bool IsSoapFaultAction(string action)
    {
        return action.Equals("http://www.w3.org/2005/08/addressing/soap/fault", StringComparison.OrdinalIgnoreCase)
            || action.Equals("http://schemas.xmlsoap.org/ws/2004/08/addressing/fault", StringComparison.OrdinalIgnoreCase)
            || action.EndsWith("/soap/fault", StringComparison.OrdinalIgnoreCase)
            || action.EndsWith("/fault", StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadBodyXml(Message message)
    {
        using var reader = message.GetReaderAtBodyContents();
        reader.MoveToContent();
        return reader.ReadOuterXml();
    }

    private string BuildAdtAction()
    {
        if (!string.IsNullOrWhiteSpace(_callbackActionOverride))
            return _callbackActionOverride;

        if (_wcfSendMode.Equals("ServiceExecute", StringComparison.OrdinalIgnoreCase))
            return "http://Philips.HIF.Contracts/IPIDuplexService/Execute";

        if (_callbackOperation.Equals("OnPIChange", StringComparison.Ordinal))
            return "http://Philips.HIF.Contracts/IPIClientCallback/OnPIChange";

        if (string.IsNullOrWhiteSpace(_callbackActionPrefix))
            return _callbackOperation;

        var prefix = _callbackActionPrefix;
        if (prefix.EndsWith("Service/", StringComparison.Ordinal))
            prefix = prefix[..^1] + "Callback/";

        return prefix + _callbackOperation;
    }

    public void SendMessage(string? bodyXml, string action, System.Xml.UniqueId? relatesTo = null, WsRmSequenceInfo? acknowledgement = null)
    {
        SendMessage(bodyXml is null ? null : new RawXmlBodyWriter(bodyXml), action, relatesTo, acknowledgement);
    }

    public async Task<PicixPushResult> SendAdtXmlAsync(string hl7Xml, CancellationToken ct)
    {
        var action = CallbackAction;
        var bodyWriter = _wcfSendMode.Equals("ServiceExecute", StringComparison.OrdinalIgnoreCase)
            ? new PiChangeBodyWriter(hl7Xml, "Execute", includeSeparateDescriptor: false)
            : new PiChangeBodyWriter(hl7Xml, "OnPIChange", includeSeparateDescriptor: true);
        TaskCompletionSource<string> faultSource;
        lock (_faultLock)
        {
            _pendingFault = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            faultSource = _pendingFault;
        }

        try
        {
            SendMessage(bodyWriter, action);

            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(4), ct);
            var completed = await Task.WhenAny(faultSource.Task, timeoutTask);
            if (completed == faultSource.Task)
            {
                var fault = await faultSource.Task;
                return new PicixPushResult(
                    false,
                    null,
                    $"PIC iX SOAP Fault after WCF push: {SummarizeSoapFault(fault)}");
            }

            ct.ThrowIfCancellationRequested();
            return new PicixPushResult(
                true,
                "WCF frame sent; no PIC iX SOAP Fault observed within 4 seconds; business acceptance still unconfirmed",
                null);
        }
        finally
        {
            lock (_faultLock)
            {
                if (ReferenceEquals(_pendingFault, faultSource))
                    _pendingFault = null;
            }
        }
    }

    private static string SummarizeSoapFault(string fault)
    {
        try
        {
            var doc = XDocument.Parse(fault);
            var reason = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Text")?.Value?.Trim();
            var subcode = doc.Descendants().Where(e => e.Name.LocalName == "Value").Skip(1).FirstOrDefault()?.Value?.Trim();
            var detail = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Detail")?.Value?.Trim();
            return string.Join(" | ", new[] { subcode, reason, detail }.Where(s => !string.IsNullOrWhiteSpace(s)));
        }
        catch
        {
            return fault.Length <= 1000 ? fault : fault[..1000];
        }
    }

    private void SendMessage(BodyWriter? bodyWriter, string action, System.Xml.UniqueId? relatesTo = null, WsRmSequenceInfo? acknowledgement = null)
    {
        if (!IsConnected) return;

        Message message;
        if (bodyWriter != null)
        {
            message = Message.CreateMessage(_encoder.MessageVersion, action, bodyWriter);
        }
        else
        {
            message = Message.CreateMessage(_encoder.MessageVersion, action);
        }

        message.Headers.MessageId = new System.Xml.UniqueId();
        if (relatesTo != null)
            message.Headers.RelatesTo = relatesTo;
        if (ShouldAddOutboundSequence(action))
        {
            var messageNumber = _outboundMessageNumber++;
            message.Headers.Add(new WsRmSequenceHeader(_outboundSequenceIdentifier!, messageNumber));
            _logger.LogInformation("Added WS-RM Sequence header: identifier={Identifier}, messageNumber={MessageNumber}, action={Action}",
                _outboundSequenceIdentifier, messageNumber, action);
        }
        if (acknowledgement != null)
        {
            message.Headers.Add(new WsRmSequenceAcknowledgementHeader(acknowledgement.Identifier, acknowledgement.MessageNumber));
            _logger.LogInformation("Added WS-RM SequenceAcknowledgement header: identifier={Identifier}, messageNumber={MessageNumber}, action={Action}",
                acknowledgement.Identifier, acknowledgement.MessageNumber, action);
        }

        var buffer = default(ArraySegment<byte>);
        try
        {
            buffer = _encoder.WriteMessage(message, int.MaxValue, _bufferManager, 0);

            lock (_sendLock)
            {
                if (!IsConnected) return;

                _stream.WriteByte(FramingRecordType.SizedEnvelope);
                var sizeBytes = WriteVarInt32(buffer.Count);
                _stream.Write(sizeBytes, 0, sizeBytes.Length);
                _stream.Write(buffer.Array!, buffer.Offset, buffer.Count);
                _stream.Flush();
            }

            _logger.LogInformation("Sent WCF response {Len} bytes, action: {Action}", buffer.Count, action);
        }
        finally
        {
            message.Close();
            if (buffer.Array != null)
                _bufferManager.ReturnBuffer(buffer.Array);
        }
    }

    private bool ShouldAddOutboundSequence(string action)
    {
        if (string.IsNullOrWhiteSpace(_outboundSequenceIdentifier)) return false;
        if (action.Contains("/ws/2005/02/rm/", StringComparison.OrdinalIgnoreCase)) return false;
        if (IsSoapFaultAction(action)) return false;
        return true;
    }

    private WsRmSequenceInfo? ExtractSequenceInfo(Message message)
    {
        const string rmNs = "http://schemas.xmlsoap.org/ws/2005/02/rm";
        try
        {
            var index = message.Headers.FindHeader("Sequence", rmNs);
            if (index < 0) return null;

            using var reader = message.Headers.GetReaderAtHeader(index);
            reader.MoveToContent();
            var headerXml = reader.ReadOuterXml();
            var header = XElement.Parse(headerXml);
            var identifier = header.Element(XName.Get("Identifier", rmNs))?.Value?.Trim();
            var numberText = header.Element(XName.Get("MessageNumber", rmNs))?.Value?.Trim();

            if (string.IsNullOrWhiteSpace(identifier) || !long.TryParse(numberText, out var messageNumber))
                return null;

            _inboundSequenceIdentifier = identifier;
            _lastInboundMessageNumber = Math.Max(_lastInboundMessageNumber, messageNumber);

            _logger.LogInformation("Incoming WS-RM Sequence header: identifier={Identifier}, messageNumber={MessageNumber}, header={Header}",
                identifier, messageNumber, headerXml);
            return new WsRmSequenceInfo(identifier, messageNumber);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to inspect incoming WS-RM Sequence header: {Msg}", ex.Message);
            return null;
        }
    }

    private WsRmSequenceInfo? ExtractAckRequestedInfo(Message message)
    {
        const string rmNs = "http://schemas.xmlsoap.org/ws/2005/02/rm";
        try
        {
            var index = message.Headers.FindHeader("AckRequested", rmNs);
            if (index < 0) return null;

            using var reader = message.Headers.GetReaderAtHeader(index);
            reader.MoveToContent();
            var headerXml = reader.ReadOuterXml();
            var header = XElement.Parse(headerXml);
            var identifier = header.Element(XName.Get("Identifier", rmNs))?.Value?.Trim();

            if (string.IsNullOrWhiteSpace(identifier))
                return null;

            var messageNumber = identifier == _inboundSequenceIdentifier && _lastInboundMessageNumber > 0
                ? _lastInboundMessageNumber
                : 1;

            _logger.LogInformation("Incoming WS-RM AckRequested header: identifier={Identifier}, ackMessageNumber={MessageNumber}, header={Header}",
                identifier, messageNumber, headerXml);
            return new WsRmSequenceInfo(identifier, messageNumber);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to inspect incoming WS-RM AckRequested header: {Msg}", ex.Message);
            return null;
        }
    }

    // ---- Low-level reading helpers ----

    private async Task<byte> ReadByteAsync(CancellationToken ct)
    {
        var buf = new byte[1];
        var read = await _stream.ReadAsync(buf, ct);
        if (read <= 0) throw new EndOfStreamException("Connection closed");
        return buf[0];
    }

    private async Task<string> ReadLengthPrefixedStringAsync(CancellationToken ct)
    {
        var length = await ReadVarIntAsync(ct);
        if (length <= 0) return "";
        var bytes = await ReadBytesAsync(length, ct);
        return bytes != null ? Encoding.UTF8.GetString(bytes) : "";
    }

    private async Task<int> ReadVarIntAsync(CancellationToken ct)
    {
        int value = 0;
        int shift = 0;
        while (shift < 32)
        {
            var b = new byte[1];
            var read = await _stream.ReadAsync(b, ct);
            if (read <= 0) return -1;

            value |= (b[0] & 0x7F) << shift;
            if ((b[0] & 0x80) == 0) return value;
            shift += 7;
        }
        return value;
    }

    private async Task<byte[]?> ReadBytesAsync(int count, CancellationToken ct)
    {
        if (count <= 0) return [];
        var buffer = new byte[count];
        var offset = 0;
        while (offset < count)
        {
            var read = await _stream.ReadAsync(buffer, offset, count - offset, ct);
            if (read <= 0) return null;
            offset += read;
        }
        return buffer;
    }

    private async Task<byte[]?> ReadBufferFromManagerAsync(int count, CancellationToken ct)
    {
        if (count <= 0) return [];
        var buffer = _bufferManager.TakeBuffer(count);
        var offset = 0;
        while (offset < count)
        {
            var read = await _stream.ReadAsync(buffer, offset, count - offset, ct);
            if (read <= 0)
            {
                _bufferManager.ReturnBuffer(buffer);
                return null;
            }
            offset += read;
        }
        return buffer;
    }

    private static byte[] WriteVarInt32(int value)
    {
        var bytes = new List<byte>(5);
        while (value >= 0x80)
        {
            bytes.Add((byte)((value & 0x7F) | 0x80));
            value >>= 7;
        }
        bytes.Add((byte)value);
        return [.. bytes];
    }

    public void Dispose()
    {
        _client?.Close();
        _client?.Dispose();
        _stream?.Dispose();
    }
}

internal sealed class RawXmlBodyWriter : BodyWriter
{
    private readonly string _bodyXml;

    public RawXmlBodyWriter(string bodyXml) : base(isBuffered: true)
    {
        _bodyXml = bodyXml;
    }

    protected override void OnWriteBodyContents(XmlDictionaryWriter writer)
    {
        using var reader = XmlReader.Create(new StringReader(_bodyXml), new XmlReaderSettings
        {
            ConformanceLevel = ConformanceLevel.Fragment,
            IgnoreWhitespace = true
        });

        reader.MoveToContent();
        writer.WriteNode(reader, false);
    }
}

internal sealed class PiChangeBodyWriter : BodyWriter
{
    private const string HifNs = "http://Philips.HIF.Contracts";
    private const string XsiNs = "http://www.w3.org/2001/XMLSchema-instance";
    private readonly string _hl7Xml;
    private readonly string _operationElementName;
    private readonly bool _includeSeparateDescriptor;
    private readonly Guid _changeId = Guid.NewGuid();
    private readonly DateTime _triggerTime = DateTime.UtcNow;

    public PiChangeBodyWriter(string hl7Xml, string operationElementName, bool includeSeparateDescriptor) : base(isBuffered: true)
    {
        _hl7Xml = hl7Xml;
        _operationElementName = string.IsNullOrWhiteSpace(operationElementName) ? "OnPIChange" : operationElementName;
        _includeSeparateDescriptor = includeSeparateDescriptor;
    }

    protected override void OnWriteBodyContents(XmlDictionaryWriter writer)
    {
        var trigger = InferTrigger(_hl7Xml);

        writer.WriteStartElement(_operationElementName, HifNs);
        WritePiChange(writer, "change", trigger, includeDescriptor: true);
        if (_includeSeparateDescriptor)
            WriteDescriptor(writer, "descriptor");
        writer.WriteEndElement();
    }

    private void WritePiChange(XmlDictionaryWriter writer, string name, string trigger, bool includeDescriptor)
    {
        writer.WriteStartElement(name, HifNs);
        writer.WriteAttributeString("xmlns", "i", null, XsiNs);

        WriteNilElement(writer, "After");
        WriteNilElement(writer, "Before");
        writer.WriteElementString("ChangeTrigger", HifNs, trigger);
        if (includeDescriptor)
            WriteDescriptor(writer, "Descriptor");
        else
            WriteNilElement(writer, "Descriptor");
        writer.WriteElementString("Id", HifNs, _changeId.ToString("D"));
        writer.WriteElementString("IsTest", HifNs, "false");
        WriteSource(writer);
        writer.WriteElementString("TriggerTime", HifNs, XmlConvert.ToString(_triggerTime, XmlDateTimeSerializationMode.Utc));

        writer.WriteEndElement();
    }

    private void WriteDescriptor(XmlDictionaryWriter writer, string name)
    {
        writer.WriteStartElement(name, HifNs);
        writer.WriteAttributeString("xmlns", "i", null, XsiNs);
        WriteNilElement(writer, "ChangedAttributes");
        WriteNilElement(writer, "ChangedLocations");
        WriteNilElement(writer, "ChangedNumbers");
        writer.WriteElementString("HL7Msg", HifNs, _hl7Xml);
        writer.WriteEndElement();
    }

    private static void WriteSource(XmlDictionaryWriter writer)
    {
        writer.WriteStartElement("Source", HifNs);
        writer.WriteElementString("Domain", HifNs, "IIC");
        writer.WriteElementString("Name", HifNs, "HL7Gateway");
        writer.WriteEndElement();
    }

    private static void WriteNilElement(XmlDictionaryWriter writer, string name)
    {
        writer.WriteStartElement(name, HifNs);
        writer.WriteAttributeString("i", "nil", XsiNs, "true");
        writer.WriteEndElement();
    }

    private static string InferTrigger(string xml)
    {
        if (xml.Contains("ADT_A03", StringComparison.OrdinalIgnoreCase) || xml.Contains("ADT^A03", StringComparison.OrdinalIgnoreCase))
            return "Discharge";
        if (xml.Contains("ADT_A02", StringComparison.OrdinalIgnoreCase) || xml.Contains("ADT^A02", StringComparison.OrdinalIgnoreCase))
            return "Transfer";
        if (xml.Contains("ADT_A08", StringComparison.OrdinalIgnoreCase) || xml.Contains("ADT^A08", StringComparison.OrdinalIgnoreCase))
            return "UpdateInformation";
        if (xml.Contains("ADT_A04", StringComparison.OrdinalIgnoreCase) || xml.Contains("ADT^A04", StringComparison.OrdinalIgnoreCase))
            return "Register";
        return "Admit";
    }
}

internal sealed class WsRmSequenceHeader : MessageHeader
{
    private const string RmNs = "http://schemas.xmlsoap.org/ws/2005/02/rm";
    private readonly string _identifier;
    private readonly long _messageNumber;

    public WsRmSequenceHeader(string identifier, long messageNumber)
    {
        _identifier = identifier;
        _messageNumber = messageNumber;
    }

    public override string Name => "Sequence";
    public override string Namespace => RmNs;
    public override bool MustUnderstand => true;

    protected override void OnWriteHeaderContents(XmlDictionaryWriter writer, MessageVersion messageVersion)
    {
        writer.WriteElementString("Identifier", RmNs, _identifier);
        writer.WriteElementString("MessageNumber", RmNs, _messageNumber.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}

internal sealed record WsRmSequenceInfo(string Identifier, long MessageNumber);

internal sealed class WsRmSequenceAcknowledgementHeader : MessageHeader
{
    private const string RmNs = "http://schemas.xmlsoap.org/ws/2005/02/rm";
    private readonly string _identifier;
    private readonly long _messageNumber;

    public WsRmSequenceAcknowledgementHeader(string identifier, long messageNumber)
    {
        _identifier = identifier;
        _messageNumber = messageNumber;
    }

    public override string Name => "SequenceAcknowledgement";
    public override string Namespace => RmNs;
    public override bool MustUnderstand => true;

    protected override void OnWriteHeaderContents(XmlDictionaryWriter writer, MessageVersion messageVersion)
    {
        writer.WriteElementString("Identifier", RmNs, _identifier);
        writer.WriteStartElement("AcknowledgementRange", RmNs);
        writer.WriteAttributeString("Lower", _messageNumber.ToString(System.Globalization.CultureInfo.InvariantCulture));
        writer.WriteAttributeString("Upper", _messageNumber.ToString(System.Globalization.CultureInfo.InvariantCulture));
        writer.WriteEndElement();
    }
}

internal class TcpPicixCallback : IPicixSubscriber
{
    private readonly TcpPicixConnection _connection;
    private readonly ILogger _logger;

    public TcpPicixCallback(TcpPicixConnection connection, string clientId, ILogger logger)
    {
        _connection = connection;
        ClientId = clientId;
        _logger = logger;
    }

    public string ClientId { get; }
    public string RemoteEndPoint => _connection.RemoteEndPoint;
    public string CallbackAction => _connection.CallbackAction;
    public string WcfSendMode => _connection.WcfSendMode;
    public bool IsConnected => _connection.IsConnected;

    public Task<PicixPushResult> OnMessageReceivedAsync(string xmlMessage, CancellationToken ct)
    {
        return _connection.SendAdtXmlAsync(xmlMessage, ct);
    }
}
