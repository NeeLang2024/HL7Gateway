using HL7Gateway.Core.DbContexts;
using HL7Gateway.Core.Entities;
using HL7Gateway.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HL7Gateway.Core.Services.Implementations;

/// <summary>
/// 当 HIS 经 MLLP 发来 ADT 时，按 PV1 床位可选同步 AutoAdtBindings（需开启 HisAutoBindingEnabled）。
/// </summary>
public class AutoAdtHisBindingSync
{
    private readonly AutoAdtFeatureService _features;
    private readonly ILogger<AutoAdtHisBindingSync> _logger;

    public AutoAdtHisBindingSync(AutoAdtFeatureService features, ILogger<AutoAdtHisBindingSync> logger)
    {
        _features = features;
        _logger = logger;
    }

    public async Task TrySyncFromHl7Async(Hl7GatewayDbContext db, string rawHl7, string triggerEvent, string? patientId, string? visitId, CancellationToken ct = default)
    {
        var features = await _features.GetFeaturesAsync(db, ct);
        if (!features.HisAutoBindingEnabled)
            return;

        if (string.IsNullOrWhiteSpace(triggerEvent) || string.IsNullOrWhiteSpace(patientId))
            return;

        var eventType = triggerEvent.Trim().ToUpperInvariant();
        if (eventType is not ("A01" or "A02" or "A03"))
            return;

        var location = ExtractPv1Location(rawHl7);
        var priorLocation = ExtractPv1PriorLocation(rawHl7);
        var mrn = patientId.Trim();
        var visit = (visitId ?? "").Trim();
        if (string.IsNullOrWhiteSpace(visit))
            visit = mrn;

        var now = ChinaTime.Now;

        if (eventType == "A03")
        {
            await CloseVisitBindingAsync(db, visit, now, ct);
            return;
        }

        var targetLoc = eventType == "A02" ? location : location;
        if (string.IsNullOrWhiteSpace(targetLoc))
        {
            _logger.LogDebug("HIS ADT {Type}: no PV1 location, skip binding sync", eventType);
            return;
        }

        var bed = await FindBedByLocationAsync(db, targetLoc, ct);
        if (bed is null)
        {
            _logger.LogInformation("HIS ADT {Type}: location {Loc} has no AutoAdtBed mapping", eventType, targetLoc);
            return;
        }

        if (eventType == "A02" && !string.IsNullOrWhiteSpace(priorLocation))
        {
            var sourceBed = await FindBedByLocationAsync(db, priorLocation, ct);
            if (sourceBed is not null)
                await CloseBedBindingAsync(db, sourceBed.Id, visit, now, ct);
        }

        var occupied = await db.AutoAdtBindings
            .FirstOrDefaultAsync(b => b.BedId == bed.Id && b.BindingStatus == "Active", ct);
        if (occupied is not null && occupied.VisitId != visit)
        {
            occupied.BindingStatus = "Overridden";
            occupied.UnbindTime = now;
            occupied.UpdatedAt = now;
        }

        var existing = await db.AutoAdtBindings
            .FirstOrDefaultAsync(b => b.VisitId == visit && b.BindingStatus == "Active", ct);
        if (existing is not null && existing.BedId != bed.Id)
        {
            existing.BindingStatus = "Transferred";
            existing.UnbindTime = now;
            existing.UpdatedAt = now;
            existing = null;
        }

        if (existing is null)
        {
            db.AutoAdtBindings.Add(new AutoAdtBinding
            {
                PatientId = mrn,
                VisitId = visit,
                BedId = bed.Id,
                DeviceCode = bed.DeviceCode,
                BindingStatus = "Active",
                BindTime = now,
                CreatedAt = now,
                UpdatedAt = now
            });
        }
        else
        {
            existing.PatientId = mrn;
            existing.BedId = bed.Id;
            existing.DeviceCode = bed.DeviceCode;
            existing.BindTime = now;
            existing.UpdatedAt = now;
        }

        await db.SaveChangesAsync(ct);
        _logger.LogInformation("HIS ADT {Type}: synced binding {Mrn}/{Visit} -> bed #{BedId} ({Loc})",
            eventType, mrn, visit, bed.Id, bed.PhilipsLocationValue);
    }

    private static async Task CloseVisitBindingAsync(Hl7GatewayDbContext db, string visitId, DateTime now, CancellationToken ct)
    {
        var bindings = await db.AutoAdtBindings
            .Where(b => b.VisitId == visitId && b.BindingStatus == "Active")
            .ToListAsync(ct);
        foreach (var b in bindings)
        {
            b.BindingStatus = "Discharged";
            b.UnbindTime = now;
            b.UpdatedAt = now;
        }

        if (bindings.Count > 0)
            await db.SaveChangesAsync(ct);
    }

    private static async Task CloseBedBindingAsync(Hl7GatewayDbContext db, long bedId, string visitId, DateTime now, CancellationToken ct)
    {
        var binding = await db.AutoAdtBindings
            .FirstOrDefaultAsync(b => b.BedId == bedId && b.VisitId == visitId && b.BindingStatus == "Active", ct);
        if (binding is null) return;
        binding.BindingStatus = "Transferred";
        binding.UnbindTime = now;
        binding.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
    }

    private static async Task<AutoAdtBed?> FindBedByLocationAsync(Hl7GatewayDbContext db, string location, CancellationToken ct)
    {
        var loc = location.Trim();
        return await db.AutoAdtBeds
            .Where(b => b.IsEnabled)
            .Where(b => b.PhilipsLocationValue == loc
                || b.DeviceCode == loc
                || b.BedBarcode == loc
                || b.DeviceBarcode == loc)
            .FirstOrDefaultAsync(ct);
    }

    private static string? ExtractPv1Location(string raw)
    {
        foreach (var line in raw.Split('\r', '\n'))
        {
            if (!line.StartsWith("PV1|", StringComparison.OrdinalIgnoreCase)) continue;
            var fields = line.Split('|');
            if (fields.Length > 3 && !string.IsNullOrWhiteSpace(fields[3]))
                return fields[3].Trim();
        }
        return null;
    }

    private static string? ExtractPv1PriorLocation(string raw)
    {
        foreach (var line in raw.Split('\r', '\n'))
        {
            if (!line.StartsWith("PV1|", StringComparison.OrdinalIgnoreCase)) continue;
            var fields = line.Split('|');
            if (fields.Length > 6 && !string.IsNullOrWhiteSpace(fields[6]))
                return fields[6].Trim();
        }
        return null;
    }
}
