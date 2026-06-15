using System.Runtime.Versioning;
using System.ServiceProcess;
using HL7Gateway.Core;
using HL7Gateway.Core.DbContexts;
using HL7Gateway.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace HL7Gateway.WebApi.Services;

public class ServiceStatusMonitorService : BackgroundService
{
    private static readonly string[] ServiceNames = ["HL7GatewayService", "HL7GatewayWebApi", "PhilipsHifBridge"];
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ServiceStatusMonitorService> _logger;
    private readonly Dictionary<string, string> _lastStatuses = [];

    public ServiceStatusMonitorService(IServiceScopeFactory scopeFactory, ILogger<ServiceStatusMonitorService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!OperatingSystem.IsWindows())
            return;

        await CheckServicesAsync(stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(60));
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await CheckServicesAsync(stoppingToken);
    }

    [SupportedOSPlatform("windows")]
    private Task CheckServicesAsync(CancellationToken ct) =>
        CheckServicesAsync(writeInitial: false, ct);

    [SupportedOSPlatform("windows")]
    private async Task CheckServicesAsync(bool writeInitial, CancellationToken ct)
    {
        foreach (var serviceName in ServiceNames)
        {
            try
            {
                using var controller = new ServiceController(serviceName);
                var status = controller.Status.ToString();
                var isHealthy = status.Equals("Running", StringComparison.OrdinalIgnoreCase) ||
                    status.Equals("StartPending", StringComparison.OrdinalIgnoreCase);

                if (!writeInitial &&
                    _lastStatuses.TryGetValue(serviceName, out var previous) &&
                    previous == status)
                {
                    continue;
                }

                _lastStatuses[serviceName] = status;

                // 正常运行时不写库，避免 SystemLogs 膨胀拖慢系统
                if (isHealthy)
                    continue;

                await WriteLogAsync(serviceName, status, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to query Windows service status: {ServiceName}", serviceName);
                await WriteLogAsync(serviceName, "Unknown", ct, ex.Message);
            }
        }
    }

    private async Task WriteLogAsync(string serviceName, string status, CancellationToken ct, string? detail = null)
    {
        var message = detail is null
            ? $"{serviceName} service status: {status}"
            : $"{serviceName} service status: {status}; {detail}";

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<Hl7GatewayDbContext>();

            db.SystemLogs.Add(new SystemLogEntry
            {
                Level = (byte)LogLevel.Warning,
                Category = nameof(ServiceStatusMonitorService),
                Message = message,
                CreatedAt = ChinaTime.Now
            });
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write service status log");
        }
    }
}
