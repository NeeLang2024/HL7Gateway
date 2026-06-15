using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using HL7Gateway.Core;
using HL7Gateway.Core.DbContexts;
using HL7Gateway.Core.Entities;
using HL7Gateway.Core.Models;
using HL7Gateway.Core.Services.Implementations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace HL7Gateway.WebApi.Controllers;

[ApiController]
[Route("api/auto-adt")]
public class AutoADTController : ControllerBase
{
    private readonly Hl7GatewayDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly AutoAdtFeatureService _featureService;
    private readonly IntegrationTraceService _trace;
    private readonly IConfiguration _configuration;

    private static readonly JsonSerializerOptions SnapshotJsonOptions = new()
    {
        ReferenceHandler = ReferenceHandler.IgnoreCycles
    };

    public AutoADTController(Hl7GatewayDbContext db, IMemoryCache cache, AutoAdtFeatureService featureService, IntegrationTraceService trace, IConfiguration configuration)
    {
        _db = db;
        _cache = cache;
        _featureService = featureService;
        _trace = trace;
        _configuration = configuration;
    }

    [HttpGet("beds")]
    public async Task<IActionResult> GetBeds([FromQuery] string? search, [FromQuery] bool includeDisabled = true)
    {
        var query = _db.AutoAdtBeds.AsNoTracking().AsQueryable();
        if (!includeDisabled)
            query = query.Where(b => b.IsEnabled);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            query = query.Where(b =>
                (b.CareArea != null && b.CareArea.Contains(s)) ||
                (b.Room != null && b.Room.Contains(s)) ||
                (b.Bed != null && b.Bed.Contains(s)) ||
                (b.BedLabel != null && b.BedLabel.Contains(s)) ||
                (b.DeviceCode != null && b.DeviceCode.Contains(s)) ||
                (b.DeviceBarcode != null && b.DeviceBarcode.Contains(s)) ||
                (b.BedBarcode != null && b.BedBarcode.Contains(s)) ||
                b.PhilipsLocationValue.Contains(s));
        }

        var items = await query.OrderBy(b => b.CareArea).ThenBy(b => b.Room).ThenBy(b => b.Bed).ToListAsync();
        return Ok(items);
    }

    [HttpPost("beds")]
    public async Task<IActionResult> CreateBed([FromBody] AutoAdtBedRequest request)
    {
        var validation = ValidateBedRequest(request);
        if (validation is not null) return validation;

        var now = ChinaTime.Now;
        var bed = new AutoAdtBed
        {
            CareArea = Clean(request.CareArea),
            Room = Clean(request.Room),
            Bed = Clean(request.Bed),
            BedLabel = Clean(request.BedLabel),
            DeviceCode = Clean(request.DeviceCode),
            DeviceBarcode = Clean(request.DeviceBarcode),
            BedBarcode = Clean(request.BedBarcode),
            PhilipsLocationValue = request.PhilipsLocationValue.Trim(),
            IsEnabled = request.IsEnabled ?? true,
            Remark = Clean(request.Remark),
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.AutoAdtBeds.Add(bed);
        await _db.SaveChangesAsync();
        return Ok(bed);
    }

    [HttpPut("beds/{id:long}")]
    public async Task<IActionResult> UpdateBed(long id, [FromBody] AutoAdtBedRequest request)
    {
        var bed = await _db.AutoAdtBeds.FindAsync(id);
        if (bed is null) return NotFound();

        var validation = ValidateBedRequest(request);
        if (validation is not null) return validation;

        bed.CareArea = Clean(request.CareArea);
        bed.Room = Clean(request.Room);
        bed.Bed = Clean(request.Bed);
        bed.BedLabel = Clean(request.BedLabel);
        bed.DeviceCode = Clean(request.DeviceCode);
        bed.DeviceBarcode = Clean(request.DeviceBarcode);
        bed.BedBarcode = Clean(request.BedBarcode);
        bed.PhilipsLocationValue = request.PhilipsLocationValue.Trim();
        bed.IsEnabled = request.IsEnabled ?? true;
        bed.Remark = Clean(request.Remark);
        bed.UpdatedAt = ChinaTime.Now;

        await _db.SaveChangesAsync();
        return Ok(bed);
    }

    [HttpDelete("beds/{id:long}")]
    public async Task<IActionResult> DeleteBed(long id)
    {
        var bed = await _db.AutoAdtBeds.FindAsync(id);
        if (bed is null) return NotFound();

        var hasActiveBinding = await _db.AutoAdtBindings.AnyAsync(b => b.BedId == id && b.BindingStatus == "Active");
        if (hasActiveBinding)
            return BadRequest(new { message = "该床位存在活动绑定，不能删除" });

        _db.AutoAdtBeds.Remove(bed);
        await _db.SaveChangesAsync();
        return Ok(new { deleted = true });
    }

    [HttpPost("beds/import")]
    public async Task<IActionResult> ImportBeds([FromBody] BedImportRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Csv))
            return BadRequest(new { message = "CSV 内容不能为空" });

        var rows = ParseBedImportCsv(request.Csv);
        if (rows.Count == 0)
            return BadRequest(new { message = "未解析到有效行，请检查 CSV 格式" });

        var now = ChinaTime.Now;
        var created = 0;
        var updated = 0;
        var skipped = 0;
        var errors = new List<string>();

        foreach (var (lineNo, row) in rows)
        {
            if (string.IsNullOrWhiteSpace(row.PhilipsLocationValue))
            {
                errors.Add($"第 {lineNo} 行: PhilipsLocationValue 必填");
                skipped++;
                continue;
            }

            var loc = row.PhilipsLocationValue.Trim();
            var existing = await _db.AutoAdtBeds.FirstOrDefaultAsync(b => b.PhilipsLocationValue == loc);
            if (existing is null)
            {
                _db.AutoAdtBeds.Add(new AutoAdtBed
                {
                    CareArea = Clean(row.CareArea),
                    Room = Clean(row.Room),
                    Bed = Clean(row.Bed),
                    BedLabel = Clean(row.BedLabel),
                    DeviceCode = Clean(row.DeviceCode),
                    DeviceBarcode = Clean(row.DeviceBarcode),
                    BedBarcode = Clean(row.BedBarcode),
                    PhilipsLocationValue = loc,
                    IsEnabled = row.IsEnabled ?? true,
                    Remark = Clean(row.Remark),
                    CreatedAt = now,
                    UpdatedAt = now
                });
                created++;
            }
            else if (request.UpdateExisting)
            {
                existing.CareArea = Clean(row.CareArea);
                existing.Room = Clean(row.Room);
                existing.Bed = Clean(row.Bed);
                existing.BedLabel = Clean(row.BedLabel);
                existing.DeviceCode = Clean(row.DeviceCode);
                existing.DeviceBarcode = Clean(row.DeviceBarcode);
                existing.BedBarcode = Clean(row.BedBarcode);
                if (row.IsEnabled.HasValue) existing.IsEnabled = row.IsEnabled.Value;
                existing.Remark = Clean(row.Remark);
                existing.UpdatedAt = now;
                updated++;
            }
            else
            {
                skipped++;
            }
        }

        await _db.SaveChangesAsync();
        return Ok(new { created, updated, skipped, errors, total = rows.Count });
    }

    [HttpGet("features")]
    public async Task<IActionResult> GetFeatures()
        => Ok(await _featureService.GetFeaturesAsync(_db));

    [HttpPut("features")]
    public async Task<IActionResult> UpdateFeatures([FromBody] AutoAdtFeatures features)
        => Ok(await _featureService.SaveFeaturesAsync(_db, features));

    [HttpGet("preflight")]
    public async Task<IActionResult> Preflight()
    {
        var checks = new List<object>();
        var allOk = true;

        var dbOk = false;
        try
        {
            dbOk = await _db.Database.CanConnectAsync();
            checks.Add(new { name = "数据库", ok = dbOk, detail = dbOk ? "连接正常" : "无法连接" });
        }
        catch (Exception ex)
        {
            checks.Add(new { name = "数据库", ok = false, detail = ex.Message });
        }
        if (!dbOk) allOk = false;

        var mllpPort = ResolveMllpPortFromServiceConfig();
        var mllpOpen = await IsTcpPortOpenAsync("127.0.0.1", mllpPort);
        checks.Add(new { name = "MLLP 监听", ok = mllpOpen, detail = mllpOpen ? $"127.0.0.1:{mllpPort} 可连接" : $"127.0.0.1:{mllpPort} 未监听（请确认 HL7GatewayService 已启动）" });
        if (!mllpOpen) allOk = false;

        var bridgeUrl = ResolveBridgeUrlFromServiceConfig();
        var bridge = await ProbeBridgeAsync(bridgeUrl);
        checks.Add(new { name = "Philips 桥接", ok = bridge.ok, detail = bridge.detail });
        if (!bridge.ok) allOk = false;

        var bedCount = await _db.AutoAdtBeds.CountAsync(b => b.IsEnabled);
        checks.Add(new { name = "床位映射", ok = bedCount > 0, detail = $"已启用床位 {bedCount} 条" });

        return Ok(new { ok = allOk, checkedAt = ChinaTime.Now, checks });
    }

    [HttpPost("patients")]
    public async Task<IActionResult> UpsertPatient([FromBody] AutoAdtPatientRequest request)
    {
        var result = await UpsertPatientAndVisitAsync(request, null, false);
        return Ok(result);
    }

    [HttpPost("scan/patient")]
    public async Task<IActionResult> ScanPatient([FromBody] ScanRequest request)
    {
        var parsed = await ResolvePatientScanAsync(request.RawText);
        if (string.IsNullOrWhiteSpace(parsed.Mrn))
            return BadRequest(new { message = "无法从腕带码解析 MRN" });

        var patient = await _db.Patients.FindAsync(parsed.Mrn);
        Visit? visit = null;
        var visitId = parsed.VisitNumber;
        if (!string.IsNullOrWhiteSpace(visitId))
            visit = await _db.Visits.FindAsync(visitId);
        else
            visit = await _db.Visits.Where(v => v.PatientId == parsed.Mrn)
                .OrderByDescending(v => v.AdmitDateTime ?? v.CreatedAt)
                .FirstOrDefaultAsync();

        return Ok(new
        {
            found = patient is not null,
            mrn = parsed.Mrn,
            visitNumber = visit?.VisitId ?? visitId,
            patient,
            visit
        });
    }

    [HttpPost("scan/bed")]
    public async Task<IActionResult> ScanBed([FromBody] ScanRequest request)
    {
        var value = await ResolveBedScanValueAsync(request.RawText);
        if (string.IsNullOrWhiteSpace(value))
            return BadRequest(new { message = "床位/设备码不能为空" });

        var bed = await FindBedByValueAsync(value);
        if (bed is null)
            return NotFound(new { message = "未找到匹配的 Philips 床位映射", code = value });

        var active = await _db.AutoAdtBindings
            .Where(b => b.BedId == bed.Id && b.BindingStatus == "Active")
            .OrderByDescending(b => b.BindTime)
            .FirstOrDefaultAsync();

        return Ok(new { found = true, bed, activeBinding = active });
    }

    [HttpPost("admit")]
    public async Task<IActionResult> Admit([FromBody] AutoAdtAdmitRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Patient.Mrn))
            return BadRequest(new { message = "MRN 必填" });
        if (string.IsNullOrWhiteSpace(request.Patient.VisitNumber))
            return BadRequest(new { message = "Visit Number 必填" });

        var features = await _featureService.GetFeaturesAsync(_db);
        var patientValidation = ValidatePatientForAdmit(request.Patient, features);
        if (patientValidation is not null) return patientValidation;

        var bed = request.BedId.HasValue
            ? await _db.AutoAdtBeds.FindAsync(request.BedId.Value)
            : await FindBedByScanAsync(request.BedCode ?? "");
        if (bed is null || !bed.IsEnabled)
            return BadRequest(new { message = "目标床位不存在或已停用" });
        if (string.IsNullOrWhiteSpace(bed.PhilipsLocationValue))
            return BadRequest(new { message = "目标床位 PhilipsLocationValue 不能为空" });

        var duplicate = await CheckDuplicateSubmitAsync("A01", request.Patient.Mrn.Trim(), request.Patient.VisitNumber.Trim(), bed.Id, features.DuplicateSubmitWindowSeconds);
        if (duplicate is not null) return duplicate;

        var occupied = await FindActiveBindingByBedAsync(bed.Id);
        if (occupied is not null && occupied.VisitId != request.Patient.VisitNumber && !request.Force)
            return BadRequest(new { message = $"目标床位已有活动绑定: {occupied.PatientId}/{occupied.VisitId}" });
        if (occupied is not null && occupied.VisitId != request.Patient.VisitNumber && request.Force)
            CloseBinding(occupied, now: ChinaTime.Now, status: "Overridden");

        var patientResult = await UpsertPatientAndVisitAsync(request.Patient, bed, true);

        var now = ChinaTime.Now;
        var binding = occupied is null || occupied.VisitId != request.Patient.VisitNumber
            ? new AutoAdtBinding
            {
                PatientId = request.Patient.Mrn.Trim(),
                VisitId = request.Patient.VisitNumber.Trim(),
                BedId = bed.Id,
                DeviceCode = bed.DeviceCode,
                BindingStatus = "Active",
                BindTime = now,
                CreatedAt = now,
                UpdatedAt = now
            }
            : occupied;
        if (binding.Id == 0)
            _db.AutoAdtBindings.Add(binding);
        else
        {
            binding.PatientId = request.Patient.Mrn.Trim();
            binding.VisitId = request.Patient.VisitNumber.Trim();
            binding.DeviceCode = bed.DeviceCode;
            binding.BindingStatus = "Active";
            binding.UpdatedAt = now;
        }

        return Ok(await QueueAutoAdtAsync("A01", request.Patient, targetBed: bed, sourceBed: null, binding, patientResult, request.Priority));
    }

    [HttpPost("update")]
    public async Task<IActionResult> UpdatePatient([FromBody] AutoAdtAdmitRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Patient.Mrn))
            return BadRequest(new { message = "MRN 必填" });
        if (string.IsNullOrWhiteSpace(request.Patient.VisitNumber))
            return BadRequest(new { message = "Visit Number 必填" });

        var binding = await FindActiveBindingByVisitAsync(request.Patient.VisitNumber.Trim());
        var bed = binding is not null ? await _db.AutoAdtBeds.FindAsync(binding.BedId) : null;
        if (bed is null)
        {
            bed = request.BedId.HasValue
                ? await _db.AutoAdtBeds.FindAsync(request.BedId.Value)
                : await FindBedByScanAsync(request.BedCode ?? "");
        }
        if (bed is null || !bed.IsEnabled)
            return BadRequest(new { message = "未找到该 Visit 的活动床位，请先入院或指定目标床位" });

        var patientResult = await UpsertPatientAndVisitAsync(request.Patient, bed, true);
        return Ok(await QueueAutoAdtAsync("A08", request.Patient, targetBed: bed, sourceBed: null, binding, patientResult, request.Priority));
    }

    [HttpPost("transfer")]
    public async Task<IActionResult> Transfer([FromBody] AutoAdtAdmitRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Patient.Mrn))
            return BadRequest(new { message = "MRN 必填" });
        if (string.IsNullOrWhiteSpace(request.Patient.VisitNumber))
            return BadRequest(new { message = "Visit Number 必填" });

        var targetBed = request.BedId.HasValue
            ? await _db.AutoAdtBeds.FindAsync(request.BedId.Value)
            : await FindBedByScanAsync(request.BedCode ?? "");
        if (targetBed is null || !targetBed.IsEnabled)
            return BadRequest(new { message = "目标床位不存在或已停用" });

        var features = await _featureService.GetFeaturesAsync(_db);
        var patientValidation = ValidatePatientForAdmit(request.Patient, features);
        if (patientValidation is not null) return patientValidation;
        var duplicate = await CheckDuplicateSubmitAsync("A02", request.Patient.Mrn.Trim(), request.Patient.VisitNumber.Trim(), targetBed.Id, features.DuplicateSubmitWindowSeconds);
        if (duplicate is not null) return duplicate;

        var now = ChinaTime.Now;
        var currentBinding = await FindActiveBindingByVisitAsync(request.Patient.VisitNumber.Trim());
        var sourceBed = currentBinding is not null ? await _db.AutoAdtBeds.FindAsync(currentBinding.BedId) : null;
        var targetOccupied = await FindActiveBindingByBedAsync(targetBed.Id);
        if (targetOccupied is not null && targetOccupied.VisitId != request.Patient.VisitNumber && !request.Force)
            return BadRequest(new { message = $"目标床位已有活动绑定: {targetOccupied.PatientId}/{targetOccupied.VisitId}" });
        if (targetOccupied is not null && targetOccupied.VisitId != request.Patient.VisitNumber && request.Force)
            CloseBinding(targetOccupied, now, "Overridden");

        if (currentBinding is not null && currentBinding.BedId != targetBed.Id)
            CloseBinding(currentBinding, now, "Transferred");

        var patientResult = await UpsertPatientAndVisitAsync(request.Patient, targetBed, true);
        var binding = currentBinding is not null && currentBinding.BedId == targetBed.Id
            ? currentBinding
            : new AutoAdtBinding
            {
                PatientId = request.Patient.Mrn.Trim(),
                VisitId = request.Patient.VisitNumber.Trim(),
                BedId = targetBed.Id,
                DeviceCode = targetBed.DeviceCode,
                BindingStatus = "Active",
                BindTime = now,
                CreatedAt = now,
                UpdatedAt = now
            };
        if (binding.Id == 0)
            _db.AutoAdtBindings.Add(binding);
        else
        {
            binding.DeviceCode = targetBed.DeviceCode;
            binding.UpdatedAt = now;
        }

        return Ok(await QueueAutoAdtAsync("A02", request.Patient, targetBed, sourceBed, binding, patientResult, request.Priority));
    }

    [HttpPost("discharge")]
    public async Task<IActionResult> Discharge([FromBody] AutoAdtAdmitRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Patient.Mrn))
            return BadRequest(new { message = "MRN 必填" });
        if (string.IsNullOrWhiteSpace(request.Patient.VisitNumber))
            return BadRequest(new { message = "Visit Number 必填" });

        var now = ChinaTime.Now;
        var binding = await FindActiveBindingByVisitAsync(request.Patient.VisitNumber.Trim());
        var bed = binding is not null ? await _db.AutoAdtBeds.FindAsync(binding.BedId) : null;
        if (binding is not null)
            CloseBinding(binding, now, "Discharged");

        var patientResult = await UpsertPatientAndVisitAsync(request.Patient, bed, true);
        var visit = await _db.Visits.FindAsync(request.Patient.VisitNumber.Trim());
        if (visit is not null)
        {
            visit.DischargeDateTime = now;
            visit.UpdatedAt = now;
        }

        return Ok(await QueueAutoAdtAsync("A03", request.Patient, targetBed: bed, sourceBed: null, binding, patientResult, request.Priority));
    }

    [HttpPost("messages/{id:long}/resend")]
    public async Task<IActionResult> ResendMessage(long id)
    {
        var original = await _db.AutoAdtMessages.FindAsync(id);
        if (original is null) return NotFound(new { message = "Auto ADT 消息不存在" });
        if (string.IsNullOrWhiteSpace(original.Hl7Raw))
            return BadRequest(new { message = "该消息没有原始 HL7，无法重发" });

        var now = ChinaTime.Now;
        var queueItem = new AdtQueueItem
        {
            AdtMessageType = original.MessageType,
            MessageContent = original.Hl7Raw,
            TargetEndpoint = "Philips HIF bridge",
            Priority = 0,
            Status = 0,
            CreatedAt = now,
            MaxRetries = 3
        };
        _db.AdtQueue.Add(queueItem);
        await _db.SaveChangesAsync();

        original.SendStatus = "Requeued";
        original.RetryCount += 1;
        original.AdtQueueId = queueItem.QueueId;
        original.QueuedAt = now;
        original.ErrorText = null;
        await _db.SaveChangesAsync();

        return Ok(new { message = "Auto ADT 消息已重新入队", autoMessage = original, queueItem });
    }

    [HttpGet("bindings")]
    public async Task<IActionResult> GetBindings([FromQuery] bool activeOnly = true)
    {
        var query = _db.AutoAdtBindings.AsQueryable();
        if (activeOnly)
            query = query.Where(b => b.BindingStatus == "Active");

        var items = await query
            .OrderByDescending(b => b.BindTime)
            .Select(b => new
            {
                b.Id,
                b.PatientId,
                b.VisitId,
                b.BedId,
                Bed = _db.AutoAdtBeds.Where(x => x.Id == b.BedId).FirstOrDefault(),
                b.DeviceCode,
                b.BindingStatus,
                b.BindTime,
                b.UnbindTime,
                b.UpdatedAt
            })
            .ToListAsync();

        return Ok(items);
    }

    [HttpGet("board")]
    public async Task<IActionResult> GetBoard([FromQuery] bool includeDisabled = false, [FromQuery] string? careArea = null)
    {
        var bedsQuery = _db.AutoAdtBeds.AsQueryable();
        if (!includeDisabled)
            bedsQuery = bedsQuery.Where(b => b.IsEnabled);
        if (!string.IsNullOrWhiteSpace(careArea))
            bedsQuery = bedsQuery.Where(b => b.CareArea == careArea);

        var beds = await bedsQuery
            .OrderBy(b => b.CareArea).ThenBy(b => b.Room).ThenBy(b => b.Bed)
            .ToListAsync();

        var activeBindings = await _db.AutoAdtBindings
            .Where(b => b.BindingStatus == "Active")
            .ToListAsync();
        var bindingByBed = activeBindings
            .GroupBy(b => b.BedId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.BindTime).First());

        var patientIds = activeBindings.Select(b => b.PatientId).Distinct().ToList();
        var patientNames = await _db.Patients
            .Where(p => patientIds.Contains(p.PatientId))
            .ToDictionaryAsync(p => p.PatientId, p => p.Name);

        var items = beds.Select(bed =>
        {
            bindingByBed.TryGetValue(bed.Id, out var binding);
            string? patientName = null;
            if (binding is not null)
                patientNames.TryGetValue(binding.PatientId, out patientName);

            return new
            {
                bed.Id,
                bed.CareArea,
                bed.Room,
                bed.Bed,
                bed.BedLabel,
                bed.DeviceCode,
                bed.PhilipsLocationValue,
                bed.IsEnabled,
                Occupied = binding is not null,
                PatientId = binding?.PatientId,
                VisitId = binding?.VisitId,
                PatientName = patientName,
                BindingId = binding?.Id,
                BindTime = binding?.BindTime,
            };
        }).ToList();

        var careAreas = beds
            .Select(b => b.CareArea)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct()
            .OrderBy(c => c)
            .ToList();

        return Ok(new
        {
            total = items.Count,
            occupied = items.Count(i => i.Occupied),
            free = items.Count(i => !i.Occupied),
            careAreas,
            items
        });
    }

    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboard()
    {
        var payload = await _cache.GetOrCreateAsync("autoadt:dashboard", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(15);
            return await BuildDashboardAsync();
        });
        return Ok(payload);
    }

    private async Task<object> BuildDashboardAsync()
    {
        var today = ChinaTime.Now.Date;
        var tomorrow = today.AddDays(1);

        var eventStats = await _db.AutoAdtEvents
            .AsNoTracking()
            .Where(e => e.CreatedAt >= today && e.CreatedAt < tomorrow)
            .GroupBy(e => e.EventType)
            .Select(g => new { EventType = g.Key, Count = g.Count() })
            .ToListAsync();
        int CountEvents(string type) =>
            eventStats.FirstOrDefault(x => x.EventType == type)?.Count ?? 0;

        var queueStats = await _db.AdtQueue
            .AsNoTracking()
            .Where(q => q.CreatedAt >= today)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Queued = g.Count(q => q.Status == 0),
                Failed = g.Count(q => q.Status == 3),
            })
            .FirstOrDefaultAsync();

        return new
        {
            todayAdmit = CountEvents("A01"),
            todayUpdate = CountEvents("A08"),
            todayTransfer = CountEvents("A02"),
            todayDischarge = CountEvents("A03"),
            activeBindings = await _db.AutoAdtBindings.AsNoTracking().CountAsync(b => b.BindingStatus == "Active"),
            enabledBeds = await _db.AutoAdtBeds.AsNoTracking().CountAsync(b => b.IsEnabled),
            queuedMessages = queueStats?.Queued ?? 0,
            failedMessages = queueStats?.Failed ?? 0,
            recentErrors = await _db.AutoAdtMessages
                .AsNoTracking()
                .Where(m => m.ErrorText != null)
                .OrderByDescending(m => m.CreatedAt)
                .Take(5)
                .Select(m => new
                {
                    m.Id,
                    m.MessageType,
                    m.MessageControlId,
                    Error = m.ErrorText,
                    m.CreatedAt
                })
                .ToListAsync()
        };
    }

    [HttpGet("events")]
    public async Task<IActionResult> GetEvents([FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var query = _db.AutoAdtEvents.OrderByDescending(e => e.CreatedAt);
        var total = await query.CountAsync();
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return Ok(new { total, page, pageSize, items });
    }

    [HttpGet("messages")]
    public async Task<IActionResult> GetMessages([FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var total = await _db.AutoAdtMessages.CountAsync();
        var items = await _db.AutoAdtMessages
            .OrderByDescending(m => m.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(m => new
            {
                m.Id,
                m.EventId,
                m.AdtQueueId,
                m.MessageType,
                m.MessageControlId,
                m.SendStatus,
                QueueStatus = m.AdtQueueId == null ? null : _db.AdtQueue.Where(q => q.QueueId == m.AdtQueueId).Select(q => (byte?)q.Status).FirstOrDefault(),
                QueueError = m.AdtQueueId == null ? null : _db.AdtQueue.Where(q => q.QueueId == m.AdtQueueId).Select(q => q.LastError).FirstOrDefault(),
                QueueAck = m.AdtQueueId == null ? null : _db.AdtQueue.Where(q => q.QueueId == m.AdtQueueId).Select(q => q.AckContent).FirstOrDefault(),
                m.ResponseText,
                m.ErrorText,
                m.RetryCount,
                m.Hl7Raw,
                m.CreatedAt,
                m.QueuedAt,
                m.SentAt
            })
            .ToListAsync();
        return Ok(new { total, page, pageSize, items });
    }

    // ---------- 扫码规则配置 ----------

    [HttpGet("scan-rules")]
    public async Task<IActionResult> GetScanRules([FromQuery] string? type)
    {
        var q = _db.AutoAdtScanRules.AsQueryable();
        if (!string.IsNullOrWhiteSpace(type))
            q = q.Where(r => r.RuleType == type);
        var items = await q
            .OrderBy(r => r.RuleType).ThenBy(r => r.Priority).ThenBy(r => r.Id)
            .ToListAsync();
        return Ok(items);
    }

    [HttpPost("scan-rules")]
    public async Task<IActionResult> CreateScanRule([FromBody] AutoAdtScanRule rule)
    {
        var err = ValidateScanRule(rule);
        if (err is not null) return BadRequest(new { message = err });
        rule.Id = 0;
        rule.CreatedAt = ChinaTime.Now;
        rule.UpdatedAt = ChinaTime.Now;
        _db.AutoAdtScanRules.Add(rule);
        await _db.SaveChangesAsync();
        return Ok(rule);
    }

    [HttpPut("scan-rules/{id:long}")]
    public async Task<IActionResult> UpdateScanRule(long id, [FromBody] AutoAdtScanRule rule)
    {
        var existing = await _db.AutoAdtScanRules.FindAsync(id);
        if (existing is null) return NotFound();
        var err = ValidateScanRule(rule);
        if (err is not null) return BadRequest(new { message = err });
        existing.Name = rule.Name;
        existing.RuleType = rule.RuleType;
        existing.Pattern = rule.Pattern;
        existing.StripPrefixes = rule.StripPrefixes;
        existing.Priority = rule.Priority;
        existing.IsEnabled = rule.IsEnabled;
        existing.Sample = rule.Sample;
        existing.Remark = rule.Remark;
        existing.UpdatedAt = ChinaTime.Now;
        await _db.SaveChangesAsync();
        return Ok(existing);
    }

    [HttpDelete("scan-rules/{id:long}")]
    public async Task<IActionResult> DeleteScanRule(long id)
    {
        var existing = await _db.AutoAdtScanRules.FindAsync(id);
        if (existing is null) return NotFound();
        _db.AutoAdtScanRules.Remove(existing);
        await _db.SaveChangesAsync();
        return Ok(new { deleted = true });
    }

    [HttpPost("scan-rules/test")]
    public async Task<IActionResult> TestScanRules([FromBody] ScanRuleTestRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.RawText))
            return BadRequest(new { message = "测试条码不能为空" });

        if (string.Equals(req.RuleType, "Bed", StringComparison.OrdinalIgnoreCase))
        {
            var value = await ResolveBedScanValueAsync(req.RawText);
            var bed = await FindBedByValueAsync(value);
            return Ok(new { ruleType = "Bed", matched = !string.IsNullOrWhiteSpace(value), code = value, matchedBed = bed });
        }

        var parsed = await ResolvePatientScanAsync(req.RawText);
        return Ok(new { ruleType = "Patient", matched = !string.IsNullOrWhiteSpace(parsed.Mrn), mrn = parsed.Mrn, visitNumber = parsed.VisitNumber });
    }

    private static string? ValidateScanRule(AutoAdtScanRule rule)
    {
        if (string.IsNullOrWhiteSpace(rule.Name)) return "规则名称必填";
        if (rule.RuleType != "Patient" && rule.RuleType != "Bed") return "RuleType 必须是 Patient 或 Bed";
        if (!string.IsNullOrWhiteSpace(rule.Pattern))
        {
            try { _ = System.Text.RegularExpressions.Regex.Match(string.Empty, rule.Pattern); }
            catch (Exception ex) { return $"正则表达式无效: {ex.Message}"; }
        }
        if (string.IsNullOrWhiteSpace(rule.Pattern) && string.IsNullOrWhiteSpace(rule.StripPrefixes))
            return "请至少填写正则表达式或去前缀之一";
        return null;
    }

    private async Task<object> UpsertPatientAndVisitAsync(AutoAdtPatientRequest request, AutoAdtBed? bed, bool admit)
    {
        var mrn = request.Mrn.Trim();
        var visitNumber = request.VisitNumber.Trim();
        var now = ChinaTime.Now;
        var patient = await _db.Patients.FindAsync(mrn);
        var patientName = BuildPatientName(request);

        if (patient is null)
        {
            patient = new Patient
            {
                PatientId = mrn,
                PatientIdList = $"{mrn}^^^^MR~{visitNumber}^^^^VN",
                Name = patientName,
                Gender = Clean(request.Gender),
                DateOfBirth = ToDateOnly(request.DateOfBirth),
                CreatedAt = now,
                UpdatedAt = now
            };
            _db.Patients.Add(patient);
        }
        else
        {
            patient.PatientIdList = $"{mrn}^^^^MR~{visitNumber}^^^^VN";
            if (!string.IsNullOrWhiteSpace(patientName)) patient.Name = patientName;
            if (!string.IsNullOrWhiteSpace(request.Gender)) patient.Gender = Clean(request.Gender);
            var dob = ToDateOnly(request.DateOfBirth);
            if (dob.HasValue) patient.DateOfBirth = dob;
            patient.UpdatedAt = now;
        }

        var visit = await _db.Visits.FindAsync(visitNumber);
        if (visit is null)
        {
            visit = new Visit
            {
                VisitId = visitNumber,
                PatientId = mrn,
                PatientClass = "I",
                AdmitDateTime = admit ? now : null,
                CreatedAt = now,
                UpdatedAt = now
            };
            _db.Visits.Add(visit);
        }

        visit.PatientId = mrn;
        visit.PatientClass = "I";
        if (admit && !visit.AdmitDateTime.HasValue) visit.AdmitDateTime = now;
        if (bed is not null)
        {
            visit.Department = Clean(bed.CareArea);
            visit.Room = Clean(bed.Room);
            visit.Bed = Clean(bed.Bed);
            visit.Ward = null;
        }
        visit.UpdatedAt = now;

        await _db.SaveChangesAsync();
        return BuildPatientSnapshot(patient, visit);
    }

    private async Task<AutoAdtBed?> FindBedByScanAsync(string raw)
    {
        var value = await ResolveBedScanValueAsync(raw);
        return await FindBedByValueAsync(value);
    }

    private async Task<AutoAdtBed?> FindBedByValueAsync(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return await _db.AutoAdtBeds
            .Where(b => b.IsEnabled)
            .Where(b =>
                b.DeviceBarcode == value ||
                b.BedBarcode == value ||
                b.DeviceCode == value ||
                b.BedLabel == value ||
                b.PhilipsLocationValue == value)
            .FirstOrDefaultAsync();
    }

    // ---- 扫码规则解析（配了规则按规则，否则回退内置默认逻辑） ----

    private async Task<ParsedPatientScan> ResolvePatientScanAsync(string? raw)
    {
        var input = (raw ?? string.Empty).Trim();
        var rules = await _db.AutoAdtScanRules
            .Where(r => r.IsEnabled && r.RuleType == "Patient")
            .OrderBy(r => r.Priority).ThenBy(r => r.Id)
            .ToListAsync();
        foreach (var rule in rules)
        {
            var (ok, mrn, visit) = TryApplyPatientRule(rule, input);
            if (ok) return new ParsedPatientScan(mrn, visit);
        }
        return ParsePatientScan(raw);
    }

    private async Task<string> ResolveBedScanValueAsync(string? raw)
    {
        var input = (raw ?? string.Empty).Trim();
        var rules = await _db.AutoAdtScanRules
            .Where(r => r.IsEnabled && r.RuleType == "Bed")
            .OrderBy(r => r.Priority).ThenBy(r => r.Id)
            .ToListAsync();
        foreach (var rule in rules)
        {
            var (ok, code) = TryApplyBedRule(rule, input);
            if (ok) return code;
        }
        return CleanBarcode(raw);
    }

    private static (bool Ok, string Mrn, string Visit) TryApplyPatientRule(AutoAdtScanRule rule, string input)
    {
        var value = ApplyStripPrefixes(input, rule.StripPrefixes);
        if (!string.IsNullOrWhiteSpace(rule.Pattern))
        {
            try
            {
                var m = System.Text.RegularExpressions.Regex.Match(value, rule.Pattern);
                if (!m.Success) return (false, "", "");
                var mrn = m.Groups["mrn"].Success ? m.Groups["mrn"].Value.Trim() : "";
                var visit = m.Groups["visit"].Success ? m.Groups["visit"].Value.Trim() : "";
                return string.IsNullOrWhiteSpace(mrn) ? (false, "", "") : (true, mrn, visit);
            }
            catch
            {
                return (false, "", "");
            }
        }
        var v = value.Trim();
        return string.IsNullOrWhiteSpace(v) ? (false, "", "") : (true, v, "");
    }

    private static (bool Ok, string Code) TryApplyBedRule(AutoAdtScanRule rule, string input)
    {
        var value = ApplyStripPrefixes(input, rule.StripPrefixes);
        if (!string.IsNullOrWhiteSpace(rule.Pattern))
        {
            try
            {
                var m = System.Text.RegularExpressions.Regex.Match(value, rule.Pattern);
                if (!m.Success) return (false, "");
                var code = m.Groups["code"].Success ? m.Groups["code"].Value.Trim() : m.Value.Trim();
                return string.IsNullOrWhiteSpace(code) ? (false, "") : (true, code);
            }
            catch
            {
                return (false, "");
            }
        }
        var v = value.Trim();
        return string.IsNullOrWhiteSpace(v) ? (false, "") : (true, v);
    }

    private static string ApplyStripPrefixes(string input, string? prefixes)
    {
        var v = (input ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(prefixes)) return v;
        foreach (var p in prefixes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (v.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                return v[p.Length..].Trim();
        }
        return v;
    }

    private BadRequestObjectResult? ValidateBedRequest(AutoAdtBedRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.PhilipsLocationValue))
            return BadRequest(new { message = "PhilipsLocationValue 必填" });
        if (string.IsNullOrWhiteSpace(request.DeviceBarcode) && string.IsNullOrWhiteSpace(request.BedBarcode) && string.IsNullOrWhiteSpace(request.DeviceCode))
            return BadRequest(new { message = "至少填写 DeviceCode、DeviceBarcode 或 BedBarcode 之一" });
        return null;
    }

    private async Task<object> QueueAutoAdtAsync(
        string eventType,
        AutoAdtPatientRequest patient,
        AutoAdtBed? targetBed,
        AutoAdtBed? sourceBed,
        AutoAdtBinding? binding,
        object patientResult,
        int priority)
    {
        var now = ChinaTime.Now;
        var controlId = $"AADT{now:yyyyMMddHHmmss}{Guid.NewGuid():N}"[..30];
        var hl7 = BuildAdt(eventType, patient, targetBed, sourceBed, controlId, now);
        var messageType = $"ADT^{eventType}";
        var autoEvent = new AutoAdtEvent
        {
            EventType = eventType,
            PatientId = patient.Mrn.Trim(),
            VisitId = patient.VisitNumber.Trim(),
            SourceBedId = sourceBed?.Id,
            TargetBedId = targetBed?.Id,
            BindingId = binding?.Id == 0 ? null : binding?.Id,
            MessageControlId = controlId,
            EventStatus = "Queued",
            OperatorUser = User?.Identity?.Name,
            PatientSnapshotJson = JsonSerializer.Serialize(patientResult, SnapshotJsonOptions),
            BedSnapshotJson = JsonSerializer.Serialize(BuildBedSnapshot(sourceBed, targetBed), SnapshotJsonOptions),
            CreatedAt = now,
            UpdatedAt = now
        };
        _db.AutoAdtEvents.Add(autoEvent);

        var queueItem = new AdtQueueItem
        {
            AdtMessageType = messageType,
            MessageContent = hl7,
            TargetEndpoint = "Philips HIF bridge",
            Priority = priority,
            Status = 0,
            CreatedAt = now,
            MaxRetries = 3
        };
        _db.AdtQueue.Add(queueItem);
        await _db.SaveChangesAsync();

        if (autoEvent.BindingId is null && binding is not null)
            autoEvent.BindingId = binding.Id;

        var message = new AutoAdtMessage
        {
            EventId = autoEvent.Id,
            AdtQueueId = queueItem.QueueId,
            MessageType = messageType,
            MessageControlId = controlId,
            Hl7Raw = hl7,
            SendStatus = "Queued",
            CreatedAt = now,
            QueuedAt = now
        };
        _db.AutoAdtMessages.Add(message);
        await _db.SaveChangesAsync();

        await _trace.AppendAsync(_db, controlId, "AutoAdt.Queued", "AutoAdt", "OK",
            $"Auto ADT {eventType} · 队列 #{queueItem.QueueId}", "adt-queue", null,
            "AutoAdtEvent", autoEvent.Id);
        await _trace.AppendAsync(_db, controlId, "AdtQueue.Pending", "Outbound", "Pending",
            $"等待 Service 发送 · 队列 #{queueItem.QueueId}", "adt-queue", null,
            "AdtQueue", queueItem.QueueId);

        return new
        {
            message = $"Auto ADT {eventType} 已入队",
            patient = patientResult,
            bed = targetBed,
            sourceBed,
            binding,
            autoEvent,
            autoMessage = message,
            queueItem,
            hl7
        };
    }

    private async Task<AutoAdtBinding?> FindActiveBindingByBedAsync(long bedId)
        => await _db.AutoAdtBindings.FirstOrDefaultAsync(b => b.BedId == bedId && b.BindingStatus == "Active");

    private async Task<AutoAdtBinding?> FindActiveBindingByVisitAsync(string visitId)
        => await _db.AutoAdtBindings.FirstOrDefaultAsync(b => b.VisitId == visitId && b.BindingStatus == "Active");

    private static void CloseBinding(AutoAdtBinding binding, DateTime now, string status)
    {
        binding.BindingStatus = status;
        binding.UnbindTime = now;
        binding.UpdatedAt = now;
    }

    private static string BuildAdt(string eventType, AutoAdtPatientRequest patient, AutoAdtBed? targetBed, AutoAdtBed? sourceBed, string controlId, DateTime now)
    {
        var dt = now.ToString("yyyyMMddHHmmss");
        var dob = FormatHl7Date(patient.DateOfBirth);
        var family = EscapeField(patient.FamilyName);
        var given = EscapeField(patient.GivenName);
        var name = string.IsNullOrWhiteSpace(given) ? family : $"{family}^{given}";
        if (string.IsNullOrWhiteSpace(name)) name = EscapeField(patient.PatientName);
        var mrn = EscapeField(patient.Mrn);
        var visit = EscapeField(patient.VisitNumber);
        var loc = EscapeField(targetBed?.PhilipsLocationValue);
        var priorLoc = EscapeField(sourceBed?.PhilipsLocationValue);

        var segments = new List<string>
        {
            $"MSH|^~\\&|HL7Gateway|AutoADT|Philips.HIF|PICIX|{dt}||ADT^{eventType}|{controlId}|P|2.3||||||UNICODE UTF-8",
            $"EVN|{eventType}|{dt}",
            $"PID|||{mrn}^^^^MR~{visit}^^^^VN||{name}||{dob}|{EscapeField(patient.Gender)}",
        };

        segments.Add(eventType == "A02"
            ? $"PV1||I|{loc}|||{priorLoc}|||||||||||||{visit}"
            : $"PV1||I|{loc}||||||||||||||||{visit}");

        return string.Join("\r", segments);
    }

    private static ParsedPatientScan ParsePatientScan(string? raw)
    {
        var value = CleanBarcode(raw);
        var parts = value.Split(['|', ';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var mrn = "";
        var visit = "";

        foreach (var part in parts)
        {
            var p = part.Trim();
            if (p.StartsWith("MRN=", StringComparison.OrdinalIgnoreCase) || p.StartsWith("MRN:", StringComparison.OrdinalIgnoreCase))
                mrn = p[4..];
            else if (p.StartsWith("VN=", StringComparison.OrdinalIgnoreCase) || p.StartsWith("VN:", StringComparison.OrdinalIgnoreCase) || p.StartsWith("VISIT=", StringComparison.OrdinalIgnoreCase))
                visit = p[(p.IndexOfAny(['=', ':']) + 1)..];
        }

        if (string.IsNullOrWhiteSpace(mrn))
        {
            mrn = value;
            foreach (var prefix in new[] { "PAT-", "PAT:", "MRN-", "MRN:" })
            {
                if (mrn.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    mrn = mrn[prefix.Length..];
                    break;
                }
            }
        }

        return new ParsedPatientScan(mrn.Trim(), visit.Trim());
    }

    private static string BuildPatientName(AutoAdtPatientRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.PatientName))
            return request.PatientName.Trim();
        return string.Concat(Clean(request.FamilyName), Clean(request.GivenName));
    }

    private static string Clean(string? value) => string.IsNullOrWhiteSpace(value) ? "" : value.Trim();

    private static string CleanBarcode(string? value)
    {
        var v = Clean(value);
        foreach (var prefix in new[] { "BED-", "BED:", "MON-", "MON:", "DEV-", "DEV:" })
        {
            if (v.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return v[prefix.Length..].Trim();
        }
        return v;
    }

    private static string EscapeField(string? value)
        => (value ?? "")
            .Replace("\\", @"\E\")
            .Replace("|", @"\F\")
            .Replace("^", @"\S\")
            .Replace("~", @"\R\")
            .Replace("&", @"\T\");

    private static string FormatHl7Date(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var v = value.Trim();
        if (v.Length == 10 && v[4] == '-') return v.Replace("-", "");
        if (v.Length == 8 && v.All(char.IsDigit)) return v;
        return "";
    }

    private static DateOnly? ToDateOnly(string? value)
    {
        var v = FormatHl7Date(value);
        return DateOnly.TryParseExact(v, "yyyyMMdd", out var parsed) ? parsed : null;
    }

    private static BadRequestObjectResult? ValidatePatientForAdmit(AutoAdtPatientRequest patient, AutoAdtFeatures features)
    {
        if (!features.StrictAdmitValidation)
            return null;
        if (features.RequirePatientName && string.IsNullOrWhiteSpace(BuildPatientName(patient)))
            return new BadRequestObjectResult(new { message = "严格校验：病人姓名必填" });
        if (features.RequireDateOfBirth && string.IsNullOrWhiteSpace(patient.DateOfBirth))
            return new BadRequestObjectResult(new { message = "严格校验：出生日期必填" });
        return null;
    }

    private async Task<IActionResult?> CheckDuplicateSubmitAsync(string eventType, string mrn, string visitId, long bedId, int windowSeconds)
    {
        if (windowSeconds <= 0) return null;
        var since = ChinaTime.Now.AddSeconds(-windowSeconds);
        var dup = await _db.AutoAdtEvents.AsNoTracking().AnyAsync(e =>
            e.EventType == eventType
            && e.PatientId == mrn
            && e.VisitId == visitId
            && e.TargetBedId == bedId
            && e.CreatedAt >= since
            && e.EventStatus != "Failed");
        if (!dup) return null;
        return BadRequest(new { message = $"最近 {windowSeconds} 秒内已有相同 {eventType} 操作，已阻止重复提交" });
    }

    private static List<(int LineNo, AutoAdtBedRequest Row)> ParseBedImportCsv(string csv)
    {
        var lines = csv.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var result = new List<(int, AutoAdtBedRequest)>();
        var start = 0;
        if (lines.Length > 0 && lines[0].Contains("PhilipsLocationValue", StringComparison.OrdinalIgnoreCase))
            start = 1;

        for (var i = start; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) continue;
            var cols = line.Split(',').Select(c => c.Trim().Trim('"')).ToArray();
            if (cols.Length < 1) continue;
            result.Add((i + 1, new AutoAdtBedRequest
            {
                CareArea = cols.ElementAtOrDefault(0),
                Room = cols.ElementAtOrDefault(1),
                Bed = cols.ElementAtOrDefault(2),
                BedLabel = cols.ElementAtOrDefault(3),
                DeviceCode = cols.ElementAtOrDefault(4),
                DeviceBarcode = cols.ElementAtOrDefault(5),
                BedBarcode = cols.ElementAtOrDefault(6),
                PhilipsLocationValue = cols.ElementAtOrDefault(7) ?? "",
                IsEnabled = ParseBoolOrNull(cols.ElementAtOrDefault(8)) ?? true,
                Remark = cols.ElementAtOrDefault(9)
            }));
        }
        return result;
    }

    private static bool? ParseBoolOrNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (bool.TryParse(value, out var b)) return b;
        if (value is "1" or "是" or "Y" or "y") return true;
        if (value is "0" or "否" or "N" or "n") return false;
        return null;
    }

    private static async Task<bool> IsTcpPortOpenAsync(string host, int port)
    {
        try
        {
            using var client = new TcpClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await client.ConnectAsync(host, port, cts.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<(bool ok, string detail)> ProbeBridgeAsync(string baseUrl)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var url = baseUrl.TrimEnd('/') + "/status";
            var text = await http.GetStringAsync(url);
            var ok = !string.IsNullOrWhiteSpace(text);
            return (ok, ok ? text.Split(';').FirstOrDefault()?.Trim() ?? "在线" : "无响应");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private int ResolveMllpPortFromServiceConfig()
    {
        var path = ResolveServiceConfigPath();
        if (path is null) return _configuration.GetValue<int?>("HL7:Listener:Port") ?? 2575;
        try
        {
            using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
            return doc.RootElement.GetProperty("HL7").GetProperty("Listener").GetProperty("Port").GetInt32();
        }
        catch
        {
            return 2575;
        }
    }

    private string ResolveBridgeUrlFromServiceConfig()
    {
        var path = ResolveServiceConfigPath();
        if (path is null) return _configuration.GetValue<string>("HL7:ADT:HifBridge:BaseUrl") ?? "http://localhost:5080/";
        try
        {
            using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
            return doc.RootElement.GetProperty("HL7").GetProperty("ADT").GetProperty("HifBridge").GetProperty("BaseUrl").GetString()
                ?? "http://localhost:5080/";
        }
        catch
        {
            return "http://localhost:5080/";
        }
    }

    private static string? ResolveServiceConfigPath()
    {
        var baseDir = AppContext.BaseDirectory;
        var prodSibling = Path.GetFullPath(Path.Combine(baseDir, "..", "service", "appsettings.json"));
        if (System.IO.File.Exists(prodSibling)) return prodSibling;
        var prodSame = Path.Combine(baseDir, "appsettings.json");
        if (System.IO.File.Exists(prodSame)) return prodSame;
        var dev = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "HL7Gateway.Service", "appsettings.json"));
        return System.IO.File.Exists(dev) ? dev : null;
    }

    private static object BuildPatientSnapshot(Patient patient, Visit visit) => new
    {
        patientId = patient.PatientId,
        name = patient.Name,
        gender = patient.Gender,
        dateOfBirth = patient.DateOfBirth?.ToString("yyyy-MM-dd"),
        visitId = visit.VisitId,
        patientClass = visit.PatientClass,
        department = visit.Department,
        room = visit.Room,
        bed = visit.Bed,
        admitDateTime = visit.AdmitDateTime
    };

    private static object BuildBedSnapshot(AutoAdtBed? sourceBed, AutoAdtBed? targetBed) => new
    {
        source = sourceBed is null ? null : new
        {
            sourceBed.Id,
            sourceBed.CareArea,
            sourceBed.Room,
            sourceBed.Bed,
            sourceBed.BedLabel,
            sourceBed.DeviceCode,
            sourceBed.PhilipsLocationValue
        },
        target = targetBed is null ? null : new
        {
            targetBed.Id,
            targetBed.CareArea,
            targetBed.Room,
            targetBed.Bed,
            targetBed.BedLabel,
            targetBed.DeviceCode,
            targetBed.PhilipsLocationValue
        }
    };

    private sealed record ParsedPatientScan(string Mrn, string VisitNumber);
}

public class AutoAdtBedRequest
{
    public string? CareArea { get; set; }
    public string? Room { get; set; }
    public string? Bed { get; set; }
    public string? BedLabel { get; set; }
    public string? DeviceCode { get; set; }
    public string? DeviceBarcode { get; set; }
    public string? BedBarcode { get; set; }
    public string PhilipsLocationValue { get; set; } = string.Empty;
    public bool? IsEnabled { get; set; }
    public string? Remark { get; set; }
}

public class AutoAdtPatientRequest
{
    public string Mrn { get; set; } = string.Empty;
    public string VisitNumber { get; set; } = string.Empty;
    public string? PatientName { get; set; }
    public string? FamilyName { get; set; }
    public string? GivenName { get; set; }
    public string? Gender { get; set; }
    public string? DateOfBirth { get; set; }
}

public class AutoAdtAdmitRequest
{
    public AutoAdtPatientRequest Patient { get; set; } = new();
    public long? BedId { get; set; }
    public string? BedCode { get; set; }
    public int Priority { get; set; }
    public bool Force { get; set; }
}

public class ScanRequest
{
    public string? RawText { get; set; }
}

public class ScanRuleTestRequest
{
    public string? RuleType { get; set; }
    public string? RawText { get; set; }
}

public class BedImportRequest
{
    public string Csv { get; set; } = string.Empty;
    public bool UpdateExisting { get; set; }
}
