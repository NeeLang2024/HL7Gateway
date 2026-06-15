using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;

namespace HL7Gateway.WebApi.Controllers;

[ApiController]
[Route("api/system")]
public class SystemSettingsController : ControllerBase
{
    private readonly ILogger<SystemSettingsController> _logger;
    private static readonly JsonDocumentOptions _jsonReadOptions = new() { AllowTrailingCommas = true };
    private static readonly JsonSerializerOptions _jsonWriteOptions = new() { WriteIndented = true };

    public SystemSettingsController(ILogger<SystemSettingsController> logger) => _logger = logger;

    [HttpGet("settings")]
    public IActionResult GetSettings()
    {
        var path = ResolveConfigPath();
        if (path is null)
            return NotFound(new { error = "Service config not found" });

        try
        {
            var json = System.IO.File.ReadAllText(path);
            var doc = JsonNode.Parse(json);
            var adtPort = doc?["HL7"]?["ADT"]?["ListenerPort"]?.GetValue<int>() ?? 9912;
            var adtListenerEnabled = doc?["HL7"]?["ADT"]?["ListenerEnabled"]?.GetValue<bool>() ?? false;
            var mllpPort = doc?["HL7"]?["Listener"]?["Port"]?.GetValue<int>() ?? 2575;
            var adtTargetHost = doc?["HL7"]?["ADT"]?["TargetHost"]?.GetValue<string>() ?? "";
            var adtTargetPort = doc?["HL7"]?["ADT"]?["TargetPort"]?.GetValue<string>() ?? "";
            var hifBridgeEnabled = doc?["HL7"]?["ADT"]?["HifBridge"]?["Enabled"]?.GetValue<bool>() ?? true;
            var hifBridgeBaseUrl = doc?["HL7"]?["ADT"]?["HifBridge"]?["BaseUrl"]?.GetValue<string>() ?? "http://localhost:5080/";
            var hifBridgeTimeoutSeconds = doc?["HL7"]?["ADT"]?["HifBridge"]?["TimeoutSeconds"]?.GetValue<int>() ?? 10;
            var adtWcfSendMode = doc?["HL7"]?["ADT"]?["WcfSendMode"]?.GetValue<string>() ?? "ServiceExecute";
            var adtCallbackOperation = doc?["HL7"]?["ADT"]?["CallbackOperation"]?.GetValue<string>() ?? "OnPIChange";
            var adtCallbackAction = doc?["HL7"]?["ADT"]?["CallbackAction"]?.GetValue<string>() ?? "";

            return Ok(new
            {
                adtListenerPort = adtPort,
                adtListenerEnabled,
                mllpListenerPort = mllpPort,
                adtTargetHost,
                adtTargetPort,
                hifBridgeEnabled,
                hifBridgeBaseUrl,
                hifBridgeTimeoutSeconds,
                adtWcfSendMode,
                adtCallbackOperation,
                adtCallbackAction,
                configPath = path
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read service config at {Path}", path);
            return StatusCode(500, new { error = "Failed to read config" });
        }
    }

    [HttpPut("settings")]
    public async Task<IActionResult> UpdateSettings([FromBody] SystemSettingsUpdate request)
    {
        var path = ResolveConfigPath();
        if (path is null)
            return NotFound(new { error = "Service config not found" });

        try
        {
            var json = await System.IO.File.ReadAllTextAsync(path);
            var doc = JsonNode.Parse(json)!.AsObject();

            doc["HL7"] ??= new JsonObject();
            doc["HL7"]!["ADT"] ??= new JsonObject();

            if (request.AdtListenerPort.HasValue)
                doc["HL7"]!["ADT"]!["ListenerPort"] = request.AdtListenerPort.Value;
            if (request.AdtListenerEnabled.HasValue)
                doc["HL7"]!["ADT"]!["ListenerEnabled"] = request.AdtListenerEnabled.Value;
            if (request.AdtTargetHost is not null)
                doc["HL7"]!["ADT"]!["TargetHost"] = request.AdtTargetHost;
            if (request.AdtTargetPort is not null)
                doc["HL7"]!["ADT"]!["TargetPort"] = request.AdtTargetPort;
            if (request.HifBridgeEnabled.HasValue || request.HifBridgeBaseUrl is not null || request.HifBridgeTimeoutSeconds.HasValue)
                doc["HL7"]!["ADT"]!["HifBridge"] ??= new JsonObject();
            if (request.HifBridgeEnabled.HasValue)
                doc["HL7"]!["ADT"]!["HifBridge"]!["Enabled"] = request.HifBridgeEnabled.Value;
            if (request.HifBridgeBaseUrl is not null)
                doc["HL7"]!["ADT"]!["HifBridge"]!["BaseUrl"] = request.HifBridgeBaseUrl;
            if (request.HifBridgeTimeoutSeconds.HasValue)
                doc["HL7"]!["ADT"]!["HifBridge"]!["TimeoutSeconds"] = request.HifBridgeTimeoutSeconds.Value;
            if (request.AdtWcfSendMode is not null)
                doc["HL7"]!["ADT"]!["WcfSendMode"] = request.AdtWcfSendMode;
            if (request.AdtCallbackOperation is not null)
                doc["HL7"]!["ADT"]!["CallbackOperation"] = request.AdtCallbackOperation;
            if (request.AdtCallbackAction is not null)
                doc["HL7"]!["ADT"]!["CallbackAction"] = request.AdtCallbackAction;

            await System.IO.File.WriteAllTextAsync(path, doc.ToJsonString(_jsonWriteOptions));
            _logger.LogInformation("Service config updated: {Path}", path);

            return Ok(new { updated = true, configPath = path });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write service config at {Path}", path);
            return StatusCode(500, new { error = "Failed to write config" });
        }
    }

    [HttpPost("restart-service")]
    public async Task<IActionResult> RestartService()
    {
        const string serviceName = "HL7GatewayService";

        try
        {
            _logger.LogInformation("Restarting service {Name}...", serviceName);

            // Stop
            var stop = Process.Start(new ProcessStartInfo
            {
                FileName = "sc",
                Arguments = $"stop {serviceName}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });

            if (stop is null)
                return StatusCode(500, new { error = "Failed to start sc stop" });

            using (var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
                await stop.WaitForExitAsync(stopCts.Token);
            var stopOut = await stop.StandardOutput.ReadToEndAsync();
            var stopErr = await stop.StandardError.ReadToEndAsync();

            if (stop.ExitCode != 0)
            {
                _logger.LogWarning("sc stop failed: {Err}", stopErr);
                return StatusCode(403, new
                {
                    error = $"sc stop failed (need admin): {stopErr}",
                    suggestion = "以管理员身份运行: sc stop HL7GatewayService && sc start HL7GatewayService"
                });
            }

            await Task.Delay(3000);

            // Start
            var start = Process.Start(new ProcessStartInfo
            {
                FileName = "sc",
                Arguments = $"start {serviceName}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });

            if (start is null)
                return StatusCode(500, new { error = "Failed to start sc start" });

            using (var startCts = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
                await start.WaitForExitAsync(startCts.Token);
            var startOut = await start.StandardOutput.ReadToEndAsync();
            var startErr = await start.StandardError.ReadToEndAsync();

            if (start.ExitCode != 0)
            {
                _logger.LogWarning("sc start failed: {Err}", startErr);
                return StatusCode(403, new
                {
                    error = $"sc start failed (need admin): {startErr}",
                    suggestion = "以管理员身份运行: sc start HL7GatewayService"
                });
            }

            _logger.LogInformation("Service {Name} restarted successfully", serviceName);
            return Ok(new { restarted = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restart service {Name}", serviceName);
            return StatusCode(500, new
            {
                error = ex.Message,
                suggestion = "以管理员身份运行: sc stop HL7GatewayService && sc start HL7GatewayService"
            });
        }
    }

    private string? ResolveConfigPath()
    {
        var baseDir = AppContext.BaseDirectory;

        // Production: installed via install.ps1 — sibling directory
        //   WebApi: C:\HL7Gateway\web\HL7Gateway.WebApi.exe
        //   Service: C:\HL7Gateway\service\appsettings.json
        var prodSibling = Path.GetFullPath(Path.Combine(baseDir, "..", "service", "appsettings.json"));
        if (System.IO.File.Exists(prodSibling))
            return prodSibling;

        // Alternative layout: both services share the same directory
        var prodSame = Path.Combine(baseDir, "appsettings.json");
        if (System.IO.File.Exists(prodSame))
            return prodSame;

        // Development: from WebApi project to Service project
        var dev = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "HL7Gateway.Service", "appsettings.json"));
        if (System.IO.File.Exists(dev))
            return dev;

        return null;
    }
}

public class SystemSettingsUpdate
{
    public int? AdtListenerPort { get; set; }
    public bool? AdtListenerEnabled { get; set; }
    public string? AdtTargetHost { get; set; }
    public string? AdtTargetPort { get; set; }
    public bool? HifBridgeEnabled { get; set; }
    public string? HifBridgeBaseUrl { get; set; }
    public int? HifBridgeTimeoutSeconds { get; set; }
    public string? AdtWcfSendMode { get; set; }
    public string? AdtCallbackOperation { get; set; }
    public string? AdtCallbackAction { get; set; }
}
