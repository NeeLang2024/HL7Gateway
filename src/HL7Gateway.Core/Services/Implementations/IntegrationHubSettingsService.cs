using System.Text.Json;
using HL7Gateway.Core.DbContexts;
using HL7Gateway.Core.Entities;
using HL7Gateway.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace HL7Gateway.Core.Services.Implementations;

public class IntegrationHubSettingsService
{
    public const string SettingsKey = "IntegrationHub.Settings";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public async Task<IntegrationHubSettings> GetSettingsAsync(Hl7GatewayDbContext db, CancellationToken ct = default)
    {
        var row = await db.AutoAdtSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == SettingsKey, ct);
        if (row is null || string.IsNullOrWhiteSpace(row.Value))
            return new IntegrationHubSettings();

        try
        {
            return JsonSerializer.Deserialize<IntegrationHubSettings>(row.Value, JsonOptions)
                   ?? new IntegrationHubSettings();
        }
        catch
        {
            return new IntegrationHubSettings();
        }
    }

    public async Task<IntegrationHubSettings> SaveSettingsAsync(
        Hl7GatewayDbContext db,
        IntegrationHubSettings settings,
        CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        var row = await db.AutoAdtSettings.FirstOrDefaultAsync(s => s.Key == SettingsKey, ct);
        var now = ChinaTime.Now;
        if (row is null)
        {
            db.AutoAdtSettings.Add(new AutoAdtSetting
            {
                Key = SettingsKey,
                Value = json,
                UpdatedAt = now
            });
        }
        else
        {
            row.Value = json;
            row.UpdatedAt = now;
        }

        await db.SaveChangesAsync(ct);
        return settings;
    }

    /// <summary>路由模块是否应参与处理（开关开 + 至少一条启用规则）。</summary>
    public async Task<bool> IsRoutingActiveAsync(Hl7GatewayDbContext db, CancellationToken ct = default)
    {
        var settings = await GetSettingsAsync(db, ct);
        if (!settings.RoutingEnabled)
            return false;

        return await db.RoutingRules.AsNoTracking().AnyAsync(r => r.IsEnabled, ct);
    }
}
