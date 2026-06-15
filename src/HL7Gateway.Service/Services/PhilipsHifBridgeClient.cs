using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HL7Gateway.Service.Services;

public sealed record PhilipsHifBridgeResult(bool Success, string? Response, string? Error);

public class PhilipsHifBridgeClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PhilipsHifBridgeClient> _logger;

    public PhilipsHifBridgeClient(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<PhilipsHifBridgeClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public bool IsEnabled => _configuration.GetValue<bool?>("HL7:ADT:HifBridge:Enabled") ?? false;

    public string BaseUrl
    {
        get
        {
            var configured = _configuration["HL7:ADT:HifBridge:BaseUrl"];
            return string.IsNullOrWhiteSpace(configured)
                ? "http://localhost:5080/"
                : EnsureTrailingSlash(configured.Trim());
        }
    }

    public async Task<PhilipsHifBridgeResult> PushAdtAsync(string adtMessage, CancellationToken ct)
    {
        if (!IsEnabled)
            return new PhilipsHifBridgeResult(false, null, "Philips HIF bridge disabled");

        var timeoutSeconds = _configuration.GetValue<int?>("HL7:ADT:HifBridge:TimeoutSeconds") ?? 10;
        var url = new Uri(new Uri(BaseUrl), "adt");

        try
        {
            var client = _httpClientFactory.CreateClient(nameof(PhilipsHifBridgeClient));
            client.Timeout = TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds));

            using var content = new StringContent(adtMessage, Encoding.UTF8, "text/plain");
            content.Headers.ContentType = new MediaTypeHeaderValue("text/plain")
            {
                CharSet = "utf-8"
            };

            using var response = await client.PostAsync(url, content, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation(
                    "ADT pushed through Philips HIF bridge {Url}: {Result}",
                    url,
                    body);
                return new PhilipsHifBridgeResult(true, body, null);
            }

            _logger.LogWarning(
                "Philips HIF bridge rejected ADT at {Url}: HTTP {Status} {Body}",
                url,
                (int)response.StatusCode,
                body);
            return new PhilipsHifBridgeResult(false, body, $"Bridge HTTP {(int)response.StatusCode}: {body}");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            var error = $"Bridge timeout after {timeoutSeconds}s";
            _logger.LogWarning("Philips HIF bridge ADT push timeout at {Url}", url);
            return new PhilipsHifBridgeResult(false, null, error);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Philips HIF bridge ADT push failed at {Url}: {Message}", url, ex.Message);
            return new PhilipsHifBridgeResult(false, null, ex.Message);
        }
    }

    public async Task<PhilipsHifBridgeResult> GetStatusAsync(CancellationToken ct)
    {
        if (!IsEnabled)
            return new PhilipsHifBridgeResult(false, null, "Philips HIF bridge disabled");

        var url = new Uri(new Uri(BaseUrl), "status");
        try
        {
            var client = _httpClientFactory.CreateClient(nameof(PhilipsHifBridgeClient));
            client.Timeout = TimeSpan.FromSeconds(3);
            var body = await client.GetStringAsync(url, ct);
            return new PhilipsHifBridgeResult(true, body, null);
        }
        catch (Exception ex)
        {
            return new PhilipsHifBridgeResult(false, null, ex.Message);
        }
    }

    private static string EnsureTrailingSlash(string value)
        => value.EndsWith("/", StringComparison.Ordinal) ? value : value + "/";
}
