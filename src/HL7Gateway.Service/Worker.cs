using HL7Gateway.Core;
using HL7Gateway.Core.DbContexts;
using HL7Gateway.Core.Services.Interfaces;
using HL7Gateway.Service.Services;
using Microsoft.EntityFrameworkCore;

namespace HL7Gateway.Service;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly MllpListenerService _mllpListener;
    private readonly MessageProcessorService _messageProcessor;
    private readonly AdtSenderService _adtSender;
    private readonly IEventPublisher _eventPublisher;
    private readonly IConfiguration _configuration;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PicixConnectionListener _picixListener;

    public Worker(
        ILogger<Worker> logger,
        MllpListenerService mllpListener,
        MessageProcessorService messageProcessor,
        AdtSenderService adtSender,
        IEventPublisher eventPublisher,
        IConfiguration configuration,
        IServiceScopeFactory scopeFactory,
        PicixConnectionListener picixListener)
    {
        _logger = logger;
        _mllpListener = mllpListener;
        _messageProcessor = messageProcessor;
        _adtSender = adtSender;
        _eventPublisher = eventPublisher;
        _configuration = configuration;
        _scopeFactory = scopeFactory;
        _picixListener = picixListener;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await ExecuteCoreAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "HL7 Gateway Service worker crashed");
            StartupDiagnostics.Write("HL7 Gateway Service worker crashed", ex);

            try
            {
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private async Task ExecuteCoreAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("HL7 Gateway Service starting...");
        StartupDiagnostics.Write("Worker starting");

        var listenPort = _configuration.GetValue<int?>("HL7:Listener:Port") ?? 2575;
        var mllpEnabled = _configuration.GetValue<bool?>("HL7:Listener:Enabled") ?? true;
        var adtEnabled = _configuration.GetValue<bool?>("HL7:ADT:Enabled") ?? true;
        var hifBridgeEnabled = _configuration.GetValue<bool?>("HL7:ADT:HifBridge:Enabled") ?? false;
        var picixListenerEnabled = _configuration.GetValue<bool?>("HL7:ADT:ListenerEnabled") ?? !hifBridgeEnabled;
        var mllpStarted = false;
        var picixStarted = false;

        _logger.LogInformation("MLLP listener encoding: {Name} (codepage {CodePage})",
            _mllpListener.Encoding.EncodingName, _mllpListener.Encoding.CodePage);

        _mllpListener.MessageReceived += async (s, e) =>
        {
            try
            {
                await _messageProcessor.ProcessReceivedMessage(e, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message from {Ip}:{Port}", e.SourceIp, e.SourcePort);
            }
        };

        _mllpListener.ClientConnected += async (s, e) =>
        {
            await _eventPublisher.PublishDeviceConnected(e.SourceIp, e.SourcePort, stoppingToken);
        };

        _mllpListener.ClientDisconnected += async (s, e) =>
        {
            await _eventPublisher.PublishDeviceDisconnected(e.SourceIp, e.SourcePort, stoppingToken);
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<Hl7GatewayDbContext>();
                var conn = await db.DeviceConnections
                    .FirstOrDefaultAsync(d => d.SourceIp == e.SourceIp, stoppingToken);
                if (conn is { IsConnected: true })
                {
                    conn.IsConnected = false;
                    conn.DisconnectedAt = ChinaTime.Now;
                    await db.SaveChangesAsync(stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update device disconnect in DB for {Ip}", e.SourceIp);
            }
        };

        if (mllpEnabled)
        {
            try
            {
                await _mllpListener.StartAsync(listenPort, stoppingToken);
                mllpStarted = true;
                _logger.LogInformation("MLLP listener started on port {Port}", listenPort);
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "MLLP listener failed to start on port {Port}. Check whether the port is already in use.", listenPort);
                StartupDiagnostics.Write($"MLLP listener failed to start on port {listenPort}", ex);
            }
        }
        else
        {
            _logger.LogWarning("MLLP listener disabled by configuration");
        }

        if (adtEnabled && picixListenerEnabled)
        {
            try
            {
                _picixListener.Start();
                picixStarted = true;
                _logger.LogInformation("PIC iX listener started on port {Port}", _picixListener.Port);
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "PIC iX listener failed to start on port {Port}. Check whether the port is already in use.", _picixListener.Port);
                StartupDiagnostics.Write($"PIC iX listener failed to start on port {_picixListener.Port}", ex);
            }
        }
        else
        {
            _logger.LogWarning(
                "PIC iX listener disabled by configuration (ADT enabled={AdtEnabled}, HIF bridge enabled={HifBridgeEnabled})",
                adtEnabled,
                hifBridgeEnabled);
        }

        var adtTimer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        _ = Task.Run(async () =>
        {
            while (await adtTimer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    var sent = await _adtSender.ProcessQueueAsync(stoppingToken);
                    if (sent > 0)
                        _logger.LogDebug("ADT queue processed: {Count} sent", sent);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "ADT queue processing error");
                }
            }
        }, stoppingToken);

        _logger.LogInformation("HL7 Gateway Service started");

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) { }

        if (picixStarted)
            _picixListener.Stop();
        if (mllpStarted)
            await _mllpListener.StopAsync(stoppingToken);
        _logger.LogInformation("HL7 Gateway Service stopped");
        StartupDiagnostics.Write("Worker stopped");
    }
}
