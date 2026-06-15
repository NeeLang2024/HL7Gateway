using System.Threading.Channels;
using HL7Gateway.Core.Data;
using HL7Gateway.Core.DbContexts;
using HL7Gateway.Core.Entities;
using HL7Gateway.Core.Services;
using HL7Gateway.Core.Services.Implementations;
using HL7Gateway.Core.Services.Interfaces;
using HL7Gateway.Service;
using HL7Gateway.Service.Services;
using HL7Gateway.Service.Services.Wcf;
using Microsoft.EntityFrameworkCore;

StartupDiagnostics.Register();
StartupDiagnostics.Write("HL7Gateway.Service process starting");

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "HL7GatewayService";
});
builder.Services.Configure<HostOptions>(options =>
{
    options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore;
});

var provider = builder.Configuration.GetValue<string>("DatabaseProvider") ?? "Sqlite";
var connStr = builder.Configuration.GetConnectionString(provider == "SqlServer" ? "SqlServer" : "Sqlite");

if (provider == "SqlServer")
{
    builder.Services.AddDbContext<Hl7GatewayDbContext>(options =>
        options.UseSqlServer(connStr, o => o.CommandTimeout(30)));
}
else
{
    builder.Services.AddDbContext<Hl7GatewayDbContext>(options =>
        options.UseSqlite(connStr));
}

builder.Services.AddSingleton<IHl7ParserService, Hl7ParserService>();

var webhookEnabled = builder.Configuration.GetValue<bool?>("Webhook:Enabled") ?? false;
var webhookUrl = builder.Configuration.GetValue<string>("Webhook:Url") ?? "http://localhost:5002";
builder.Services.AddSingleton<IEventPublisher>(sp =>
{
    if (!webhookEnabled)
    {
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("HL7Gateway.Service.Webhook");
        logger.LogInformation("Webhook event publisher disabled");
        return new NoopEventPublisher();
    }

    var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
    var webhookLogger = sp.GetRequiredService<ILogger<WebhookEventPublisher>>();
    return new WebhookEventPublisher(http, webhookLogger, webhookUrl);
});

builder.Services.AddSingleton<PicixCallbackManager>();
builder.Services.AddSingleton<MllpListenerService>();
builder.Services.AddSingleton<PicixConnectionListener>();
builder.Services.AddSingleton<IWsiService, WsiService>();
builder.Services.AddSingleton<AutoAdtFeatureService>();
builder.Services.AddSingleton<AutoAdtHisBindingSync>();
builder.Services.AddSingleton<MessageProcessorService>();
builder.Services.AddSingleton<PhilipsHifBridgeClient>();
builder.Services.AddSingleton<AdtSenderService>();
builder.Services.AddSingleton<IAdtSenderService>(sp => sp.GetRequiredService<AdtSenderService>());
builder.Services.AddSingleton<IMllpSenderService, MllpSenderService>();

builder.Services.AddHttpClient();
builder.Services.AddSingleton<IWsiService, WsiService>();

builder.Services.AddHostedService<Worker>();
builder.Services.AddHostedService<MessageCleanupService>();

var logChannel = Channel.CreateBounded<SystemLogEntry>(new BoundedChannelOptions(2000)
{
    FullMode = BoundedChannelFullMode.DropOldest
});
builder.Services.AddSingleton(logChannel);
builder.Logging.AddProvider(new DbLoggerProvider(logChannel));
builder.Services.AddHostedService<DbLoggerBackgroundService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<Hl7GatewayDbContext>();
        await DbInitializer.InitializeAsync(db, applySchemaMigrations: false);
    }
    catch (Exception ex)
    {
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("HL7Gateway.Service.Startup");
        logger.LogCritical(ex, "Database initialization failed during service startup");
        StartupDiagnostics.Write("Database initialization failed", ex);
    }
}

try
{
    await app.RunAsync();
}
catch (Exception ex)
{
    StartupDiagnostics.Write("Service host failed", ex);
    throw;
}
