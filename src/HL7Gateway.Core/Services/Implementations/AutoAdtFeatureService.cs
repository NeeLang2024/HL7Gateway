using System.Text.Json;
using HL7Gateway.Core.DbContexts;
using HL7Gateway.Core.Entities;
using HL7Gateway.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace HL7Gateway.Core.Services.Implementations;

public class AutoAdtFeatureService
{
    public const string FeaturesKey = "Features";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public async Task<AutoAdtFeatures> GetFeaturesAsync(Hl7GatewayDbContext db, CancellationToken ct = default)
    {
        var row = await db.AutoAdtSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == FeaturesKey, ct);
        if (row is null || string.IsNullOrWhiteSpace(row.Value))
            return new AutoAdtFeatures();

        try
        {
            return JsonSerializer.Deserialize<AutoAdtFeatures>(row.Value, JsonOptions) ?? new AutoAdtFeatures();
        }
        catch
        {
            return new AutoAdtFeatures();
        }
    }

    public async Task<AutoAdtFeatures> SaveFeaturesAsync(Hl7GatewayDbContext db, AutoAdtFeatures features, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(features, JsonOptions);
        var row = await db.AutoAdtSettings.FirstOrDefaultAsync(s => s.Key == FeaturesKey, ct);
        var now = ChinaTime.Now;
        if (row is null)
        {
            db.AutoAdtSettings.Add(new AutoAdtSetting
            {
                Key = FeaturesKey,
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
        return features;
    }
}
