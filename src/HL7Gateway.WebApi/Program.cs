using System.Text;
using System.Threading.Channels;
using HL7Gateway.Core.Data;
using HL7Gateway.Core.DbContexts;
using HL7Gateway.Core.Entities;
using HL7Gateway.Core.Services;
using HL7Gateway.Core.Services.Implementations;
using HL7Gateway.Core.Services.Interfaces;
using HL7Gateway.WebApi.Hubs;
using HL7Gateway.WebApi.Middleware;
using HL7Gateway.WebApi.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "HL7GatewayWebApi";
});

builder.Services.AddControllers(options =>
{
    options.MaxModelBindingCollectionSize = 100;
    // 全局强制鉴权：所有控制器默认需要登录令牌，
    // 仅 [AllowAnonymous] 的控制器（登录 / 健康检查 / 内部 Webhook）放行。
    options.Filters.Add(new Microsoft.AspNetCore.Mvc.Authorization.AuthorizeFilter());
}).AddJsonOptions(options =>
{
    options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
});
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProvider>();
    options.Providers.Add<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProvider>();
});
builder.Services.AddSignalR();

var jwtKey = ResolveJwtSigningKey(builder.Configuration, builder.Environment.ContentRootPath);
// 让 AuthController 等通过 IConfiguration 读取到同一把（已解析/已生成的）密钥
builder.Configuration["Jwt:Key"] = jwtKey;
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = "HL7Gateway",
            ValidAudience = "HL7Gateway",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddMemoryCache();

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
builder.Services.AddSingleton<HttpClient>();
builder.Services.AddSingleton<IWsiService, WsiService>();
builder.Services.AddSingleton<AutoAdtFeatureService>();
builder.Services.AddSingleton<AutoAdtHisBindingSync>();
builder.Services.AddSingleton<IEventPublisher, SignalrEventPublisher>();
builder.Services.AddSingleton<IRawWebSocketManager, RawWebSocketManager>();

var corsOrigins = builder.Configuration.GetValue<string>("Cors:Origins") ?? "";
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (!string.IsNullOrEmpty(corsOrigins))
            policy.WithOrigins(corsOrigins.Split(',', StringSplitOptions.TrimEntries))
                  .AllowAnyHeader().AllowAnyMethod().AllowCredentials();
        else
            policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
    });
});

// Config validation
var configErrors = new List<string>();
if (string.IsNullOrEmpty(builder.Configuration.GetConnectionString("Sqlite")))
    configErrors.Add("ConnectionStrings:Sqlite is missing");
if (string.IsNullOrEmpty(builder.Configuration.GetConnectionString("SqlServer")))
    configErrors.Add("ConnectionStrings:SqlServer is missing");
if (configErrors.Count > 0)
    _ = Task.Run(() => Console.Error.WriteLine("WARNING: Missing config: {0}", string.Join(", ", configErrors)));

var logChannel = Channel.CreateBounded<SystemLogEntry>(new BoundedChannelOptions(2000)
{
    FullMode = BoundedChannelFullMode.DropOldest
});
builder.Services.AddSingleton(logChannel);
builder.Logging.AddProvider(new DbLoggerProvider(logChannel));
builder.Services.AddHostedService<DbLoggerBackgroundService>();
builder.Services.AddHostedService<ServiceStatusMonitorService>();

var app = builder.Build();

app.UseResponseCompression();
app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(30)
});
app.UseMiddleware<ExceptionMiddleware>();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<Hl7GatewayDbContext>();
    await DbInitializer.InitializeAsync(db);
}

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        if (ctx.File.Name == "index.html")
        {
            ctx.Context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
            ctx.Context.Response.Headers["Pragma"] = "no-cache";
            ctx.Context.Response.Headers["Expires"] = "0";
        }
    }
});

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<Hl7MonitorHub>("/hubs/hl7monitor");

app.Map("/ws/raw", async (HttpContext context, IRawWebSocketManager wsManager) =>
{
    if (context.WebSockets.IsWebSocketRequest)
    {
        var ws = await context.WebSockets.AcceptWebSocketAsync();
        await wsManager.HandleWebSocketAsync(ws, context.RequestAborted);
    }
    else
    {
        context.Response.StatusCode = 400;
        await context.Response.WriteAsync("Expected a WebSocket request");
    }
});

app.MapFallbackToFile("index.html");

app.Run();

// 解析 JWT 签名密钥：
// 1) 若 appsettings 配置了非占位、足够长的密钥则直接使用；
// 2) 否则在内容根目录生成并持久化一把强随机密钥（jwt.key），重启后保持稳定，令牌不失效；
// 3) 极端情况下（无法写盘）退回到与机器名相关的稳定派生值，仍优于共享默认值。
static string ResolveJwtSigningKey(IConfiguration config, string contentRoot)
{
    const string placeholder = "HL7GatewayDefaultSecretKey_ChangeMe!";
    var configured = config["Jwt:Key"];
    if (!string.IsNullOrWhiteSpace(configured) && configured != placeholder && configured.Length >= 32)
        return configured;

    var keyFile = Path.Combine(contentRoot, "jwt.key");
    try
    {
        if (File.Exists(keyFile))
        {
            var existing = File.ReadAllText(keyFile).Trim();
            if (existing.Length >= 32) return existing;
        }

        var generated = Convert.ToBase64String(
            System.Security.Cryptography.RandomNumberGenerator.GetBytes(48));
        File.WriteAllText(keyFile, generated);
        Console.Error.WriteLine(
            "[Auth] 检测到 Jwt:Key 缺失或仍为默认占位符，已自动生成强随机密钥并保存到 {0}", keyFile);
        return generated;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine("[Auth] 无法持久化 JWT 密钥（{0}），改用机器派生密钥。", ex.Message);
        var derived = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{Environment.MachineName}:HL7Gateway:{placeholder}"));
        return derived.Length >= 32 ? derived : derived.PadRight(48, 'x');
    }
}
