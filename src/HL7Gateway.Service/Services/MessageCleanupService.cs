using HL7Gateway.Core;
using HL7Gateway.Core.DbContexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HL7Gateway.Service.Services;

public class MessageCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MessageCleanupService> _logger;
    private readonly TimeSpan _interval;
    private readonly int _retentionDays;

    public MessageCleanupService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<MessageCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _interval = TimeSpan.FromHours(configuration.GetValue<int>("Cleanup:IntervalHours", 1));
        _retentionDays = configuration.GetValue<int>("Cleanup:RetentionDays", 30);

        var enabled = configuration.GetValue<bool>("Cleanup:Enabled", true);
        if (!enabled)
        {
            _interval = Timeout.InfiniteTimeSpan;
            _logger.LogInformation("Message cleanup disabled via config");
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Message cleanup started (retention: {Days}d, interval: {Interval}h)",
            _retentionDays, _interval.TotalHours);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_interval, stoppingToken);
                await CleanupAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Message cleanup error");
            }
        }
    }

    private async Task CleanupAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Hl7GatewayDbContext>();

        var cutoff = ChinaTime.Now.AddDays(-_retentionDays);

        var deleted = 0;
        var oldMessages = await db.Hl7Messages
            .Where(m => m.ReceivedAt < cutoff)
            .CountAsync(ct);
        if (oldMessages > 0)
        {
            // Delete dependent data first (cascade not configured)
            await db.Observations.Where(o => db.Hl7Messages
                .Where(m => m.ReceivedAt < cutoff)
                .Select(m => m.MessageId)
                .Contains(o.MessageId))
                .ExecuteDeleteAsync(ct);

            await db.VitalSigns.Where(v => db.Hl7Messages
                .Where(m => m.ReceivedAt < cutoff)
                .Select(m => m.MessageId)
                .Contains(v.MessageId))
                .ExecuteDeleteAsync(ct);

            await db.ParsedSegments.Where(s => db.Hl7Messages
                .Where(m => m.ReceivedAt < cutoff)
                .Select(m => m.MessageId)
                .Contains(s.MessageId))
                .ExecuteDeleteAsync(ct);

            deleted = await db.Hl7Messages
                .Where(m => m.ReceivedAt < cutoff)
                .ExecuteDeleteAsync(ct);
        }

        // Also clean old ADT logs
        var logDeleted = await db.AdtLogs
            .Where(l => l.CreatedAt < cutoff)
            .ExecuteDeleteAsync(ct);

        // System logs grow unbounded (high-volume per-message traces), so apply the
        // same retention window here to keep the SystemLogs table bounded.
        var sysLogDeleted = await db.SystemLogs
            .Where(l => l.CreatedAt < cutoff)
            .ExecuteDeleteAsync(ct);

        if (deleted > 0 || logDeleted > 0 || sysLogDeleted > 0)
        {
            _logger.LogInformation(
                "Cleanup: removed {MsgCount} messages, {AdtLogCount} ADT logs, {SysLogCount} system logs (older than {Cutoff:yyyy-MM-dd})",
                deleted, logDeleted, sysLogDeleted, cutoff);
        }
    }
}
