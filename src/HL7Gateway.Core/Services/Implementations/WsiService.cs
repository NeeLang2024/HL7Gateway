using HL7Gateway.Core;
using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;
using HL7Gateway.Core.DbContexts;
using HL7Gateway.Core.Entities;
using HL7Gateway.Core.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HL7Gateway.Core.Services.Implementations;

public class WsiService : IWsiService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly HttpClient _http;
    private readonly ILogger<WsiService> _logger;

    public WsiService(IServiceScopeFactory scopeFactory, HttpClient http, ILogger<WsiService> logger)
    {
        _scopeFactory = scopeFactory;
        _http = http;
        _logger = logger;
    }

    // ─── Subscription Management ───────────────────────────────────────

    public async Task<int> SubscribeAsync(string notificationUri, string? clientId, string? patientIdDomain, string? facilityCode, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Hl7GatewayDbContext>();

        var sub = new WsiSubscription
        {
            NotificationUri = notificationUri,
            ClientId = clientId,
            PatientIdDomain = patientIdDomain,
            FacilityCode = facilityCode,
            IsActive = true,
            CreatedAt = ChinaTime.Now,
            ExpiresAt = ChinaTime.Now.AddDays(30),
        };

        db.WsiSubscriptions.Add(sub);
        await db.SaveChangesAsync(ct);
        _logger.LogInformation("WSI subscription #{Id} created for {Uri}", sub.SubscriptionId, notificationUri);
        return sub.SubscriptionId;
    }

    public async Task<bool> UnsubscribeAsync(int subscriptionId, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Hl7GatewayDbContext>();

        var sub = await db.WsiSubscriptions.FindAsync([subscriptionId], ct);
        if (sub is null) return false;

        sub.IsActive = false;
        await db.SaveChangesAsync(ct);
        _logger.LogInformation("WSI subscription #{Id} deactivated", subscriptionId);
        return true;
    }

    public async Task<List<WsiSubscription>> GetActiveSubscriptionsAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Hl7GatewayDbContext>();
        return await db.WsiSubscriptions
            .Where(s => s.IsActive && s.ExpiresAt > ChinaTime.Now)
            .ToListAsync(ct);
    }

    // ─── Philips ADT XML Generation ────────────────────────────────────

    public async Task<string> BuildAdtXmlAsync(string adtType, Hl7Message message, Patient? patient, Visit? visit, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Hl7GatewayDbContext>();

        var xns = XNamespace.Get("urn:hl7-org:v2xml");
        var triggerEvent = NormalizeAdtTrigger(adtType);
        var rootName = $"ADT_{triggerEvent}";

        var doc = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(xns + rootName,
                BuildMsh(xns, triggerEvent, message),
                BuildEvn(xns, triggerEvent, message),
                BuildPid(xns, patient, message),
                visit is not null ? BuildPv1(xns, visit) : BuildPv1FromMessage(xns, message),
                BuildPv2IfDischarge(xns, triggerEvent, visit)
            )
        );

        return await Task.FromResult(doc.Declaration + Environment.NewLine + doc.ToString());
    }

    private static XElement BuildMsh(XNamespace xns, string adtType, Hl7Message message)
    {
        var now = ChinaTime.Now;
        var dt = now.ToString("yyyyMMddHHmmss");
        var controlId = $"{now:yyyyMMddHHmmss}{Guid.NewGuid():N}"[..20];

        return new XElement(xns + "MSH",
            Field(xns, "MSH.1", "|"),
            Field(xns, "MSH.2", "^~\\&"),
            HdField(xns, "MSH.3", message.SendingApp ?? "HL7Gateway"),
            HdField(xns, "MSH.4", message.SendingFacility ?? ""),
            HdField(xns, "MSH.5", message.ReceivingApp ?? "PICiX"),
            HdField(xns, "MSH.6", message.ReceivingFacility ?? ""),
            Field(xns, "MSH.7", dt),
            Field(xns, "MSH.8", ""),
            new XElement(xns + "MSH.9",
                new XElement(xns + "MSG.1", "ADT"),
                new XElement(xns + "MSG.2", adtType)),
            Field(xns, "MSH.10", controlId),
            Field(xns, "MSH.11", "P"),
            Field(xns, "MSH.12", "2.4")
        );
    }

    private static XElement BuildEvn(XNamespace xns, string adtType, Hl7Message message)
    {
        var dt = ChinaTime.Now.ToString("yyyyMMddHHmmss");
        return new XElement(xns + "EVN",
            Field(xns, "EVN.1", adtType),
            Field(xns, "EVN.2", dt),
            Field(xns, "EVN.6", dt)
        );
    }

    private static XElement BuildPid(XNamespace xns, Patient? patient, Hl7Message message)
    {
        var pid = patient?.PatientId ?? message.PatientId ?? "UNKNOWN";
        var name = patient?.Name ?? "";
        var dob = patient?.DateOfBirth?.ToString("yyyyMMdd") ?? "";
        var gender = patient?.Gender ?? "";

        // Parse name into family/given
        var family = "";
        var given = "";
        if (!string.IsNullOrEmpty(name))
        {
            var parts = name.Split('^');
            family = parts.Length > 0 ? parts[0] : name;
            given = parts.Length > 1 ? parts[1] : "";
        }

        return new XElement(xns + "PID",
            Field(xns, "PID.1", "1"),
            // PID.2 is omitted in HL7 v2 XML
            new XElement(xns + "PID.3",
                new XElement(xns + "CX.1", pid),
                new XElement(xns + "CX.5", "MR")),
            // PID.4 omitted
            new XElement(xns + "PID.5",
                new XElement(xns + "XPN.1",
                    new XElement(xns + "FN.1", family)),
                new XElement(xns + "XPN.2", given)),
            Field(xns, "PID.7", dob),
            Field(xns, "PID.8", gender)
        );
    }

    private static XElement BuildPv1(XNamespace xns, Visit visit)
    {
        var doc = visit.AttendingDoctor ?? "";

        return new XElement(xns + "PV1",
            Field(xns, "PV1.1", "1"),
            Field(xns, "PV1.2", visit.PatientClass ?? "I"),
            new XElement(xns + "PV1.3",
                new XElement(xns + "PL.1", visit.Department ?? ""),
                new XElement(xns + "PL.2", visit.Room ?? ""),
                new XElement(xns + "PL.3", visit.Bed ?? ""),
                new XElement(xns + "PL.4", visit.Ward ?? "")),
            // Skip PV1.4-7 (admit source, etc.)
            new XElement(xns + "PV1.7",
                new XElement(xns + "XCN.1",
                    new XElement(xns + "ID.1", doc))),
            Field(xns, "PV1.8", ""),
            Field(xns, "PV1.9", visit.AdmitDiagnosis ?? "")
        );
    }

    private static XElement BuildPv1FromMessage(XNamespace xns, Hl7Message message)
    {
        return new XElement(xns + "PV1",
            Field(xns, "PV1.1", "1"),
            Field(xns, "PV1.2", "I")
        );
    }

    private static XElement? BuildPv2IfDischarge(XNamespace xns, string adtType, Visit? visit)
    {
        if (adtType != "A03" || visit?.DischargeDateTime is null) return null;

        return new XElement(xns + "PV2",
            new XElement(xns + "PV2.1",
                new XElement(xns + "PL.1", ""))
        );
    }

    // ─── Notification Dispatch ─────────────────────────────────────────

    public async Task NotifySubscribersAsync(string adtType, Hl7Message message, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Hl7GatewayDbContext>();

        var now = ChinaTime.Now;
        var subscribers = await db.WsiSubscriptions
            .Where(s => s.IsActive && s.ExpiresAt > now)
            .ToListAsync(ct);

        // Filter in-memory: include only due-for-retry failed subs
        subscribers = subscribers
            .Where(s => s.FailedCount == 0
                || (s.LastFailedAt != null && now - s.LastFailedAt.Value > Backoff(s.FailedCount)))
            .ToList();

        if (subscribers.Count == 0) return;

        Patient? patient = null;
        Visit? visit = null;
        if (!string.IsNullOrEmpty(message.PatientId))
        {
            patient = await db.Patients.FindAsync([message.PatientId], ct);
            visit = !string.IsNullOrEmpty(message.VisitId)
                ? await db.Visits.FindAsync([message.VisitId], ct)
                : null;
        }

        var xml = await BuildAdtXmlAsync(adtType, message, patient, visit, ct);

        foreach (var sub in subscribers)
        {
            try
            {
                using var content = new StringContent(xml, Encoding.UTF8);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/xml");
                var response = await _http.PostAsync(sub.NotificationUri, content, ct);
                sub.NotifyCount++;
                sub.LastNotifiedAt = now;
                sub.FailedCount = 0;
                sub.LastFailedAt = null;

                _logger.LogInformation("WSI notify #{SubId} → {Uri}: {Status}",
                    sub.SubscriptionId, sub.NotificationUri, response.StatusCode);
            }
            catch (Exception ex)
            {
                sub.FailedCount++;
                sub.LastFailedAt = now;
                _logger.LogWarning(ex, "WSI notify #{SubId} failed ({FailCount}/3): {Msg}",
                    sub.SubscriptionId, sub.FailedCount, ex.Message);

                if (sub.FailedCount >= 3)
                {
                    sub.IsActive = false;
                    _logger.LogWarning("WSI subscription #{SubId} deactivated after {Count} failures",
                        sub.SubscriptionId, sub.FailedCount);
                }
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private static TimeSpan Backoff(int failedCount) => failedCount switch
    {
        1 => TimeSpan.FromSeconds(10),
        2 => TimeSpan.FromSeconds(30),
        _ => TimeSpan.FromMinutes(2),
    };

    // ─── XML Helpers ──────────────────────────────────────────────────

    private static XElement Field(XNamespace xns, string name, string value) =>
        new(xns + name, value);

    private static XElement HdField(XNamespace xns, string name, string value) =>
        string.IsNullOrEmpty(value)
            ? new XElement(xns + name)
            : new XElement(xns + name, new XElement(xns + "HD.1", value));

    private static string NormalizeAdtTrigger(string? adtType)
    {
        var value = (adtType ?? "A01").Trim();
        if (value.StartsWith("ADT^", StringComparison.OrdinalIgnoreCase))
            value = value[4..];
        if (value.StartsWith("ADT_", StringComparison.OrdinalIgnoreCase))
            value = value[4..];
        if (value.Length <= 2 && value.All(char.IsDigit))
            value = $"A{value.PadLeft(2, '0')}";
        return value.StartsWith('A') ? value : $"A{value.PadLeft(2, '0')}";
    }
}
