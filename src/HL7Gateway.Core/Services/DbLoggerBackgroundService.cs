using System.Threading.Channels;
using HL7Gateway.Core.DbContexts;
using HL7Gateway.Core.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HL7Gateway.Core.Services;

public class DbLoggerBackgroundService : BackgroundService
{
    private readonly Channel<SystemLogEntry> _channel;
    private readonly IServiceScopeFactory _scopeFactory;

    public DbLoggerBackgroundService(Channel<SystemLogEntry> channel, IServiceScopeFactory scopeFactory)
    {
        _channel = channel;
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batch = new List<SystemLogEntry>(100);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var first = await _channel.Reader.WaitToReadAsync(stoppingToken);
                if (!first) break;

                while (_channel.Reader.TryRead(out var entry) && batch.Count < 100)
                    batch.Add(entry);

                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<Hl7GatewayDbContext>();
                db.SystemLogs.AddRange(batch);
                await db.SaveChangesAsync(stoppingToken);

                batch.Clear();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                System.Console.Error.WriteLine($"[DbLogger] Failed to write {batch.Count} log entries: {ex.Message}");
                batch.Clear();
                try { await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken); } catch { break; }
            }
        }
    }
}
