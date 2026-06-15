using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace HL7Gateway.Service.Services.Wcf;

public interface IPicixSubscriber
{
    string ClientId { get; }
    string RemoteEndPoint { get; }
    string CallbackAction { get; }
    string WcfSendMode { get; }
    Task<PicixPushResult> OnMessageReceivedAsync(string xmlMessage, CancellationToken ct);
    bool IsConnected { get; }
}

public sealed record PicixPushResult(bool Success, string? Response, string? Error);

public class PicixCallbackManager
{
    private readonly ILogger<PicixCallbackManager> _logger;
    private readonly ConcurrentDictionary<string, IPicixSubscriber> _subscribers = [];

    public PicixCallbackManager(ILogger<PicixCallbackManager> logger)
    {
        _logger = logger;
    }

    public int SubscriberCount => _subscribers.Count;

    public void AddSubscriber(string clientId, IPicixSubscriber callback)
    {
        _subscribers[clientId] = callback;
    }

    public void RemoveSubscriber(string clientId)
    {
        _subscribers.TryRemove(clientId, out _);
    }

    public IPicixSubscriber? GetSubscriber(string clientId)
    {
        _subscribers.TryGetValue(clientId, out var cb);
        return cb;
    }

    public void Clear()
    {
        _subscribers.Clear();
    }

    public async Task<PicixPushResult> PushAdtXmlAsync(string xmlMessage, CancellationToken ct = default)
    {
        if (_subscribers.IsEmpty)
        {
            _logger.LogDebug("No PIC iX subscribers connected, skipping ADT push");
            return new PicixPushResult(false, null, "No PIC iX subscribers connected");
        }

        var deadKeys = new List<string>();
        PicixPushResult? lastFailure = null;

        foreach (var (clientId, subscriber) in _subscribers)
        {
            try
            {
                if (!subscriber.IsConnected)
                {
                    deadKeys.Add(clientId);
                    continue;
                }
                var result = await subscriber.OnMessageReceivedAsync(xmlMessage, ct);
                if (result.Success)
                {
                    _logger.LogInformation(
                        "ADT XML pushed to PIC iX subscriber {ClientId} at {Endpoint}, mode={Mode}, action={Action}, xmlLength={Length}: {Response}",
                        clientId, subscriber.RemoteEndPoint, subscriber.WcfSendMode, subscriber.CallbackAction, xmlMessage.Length, result.Response);
                    return result;
                }

                lastFailure = result;
                _logger.LogWarning(
                    "ADT XML push rejected by PIC iX subscriber {ClientId} at {Endpoint}, mode={Mode}, action={Action}: {Error}",
                    clientId, subscriber.RemoteEndPoint, subscriber.WcfSendMode, subscriber.CallbackAction, result.Error);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to push ADT to PIC iX subscriber {ClientId}", clientId);
                deadKeys.Add(clientId);
                lastFailure = new PicixPushResult(false, null, ex.Message);
            }
        }

        foreach (var key in deadKeys)
            _subscribers.TryRemove(key, out _);

        return lastFailure ?? new PicixPushResult(false, null, "No connected PIC iX subscribers accepted ADT push");
    }
}
