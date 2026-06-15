using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HL7Gateway.Core.DbContexts;
using HL7Gateway.Core.Entities;

namespace HL7Gateway.WebApi.Controllers;

[ApiController]
[Route("api/fhir")]
public class FhirController : ControllerBase
{
    private readonly Hl7GatewayDbContext _db;

    public FhirController(Hl7GatewayDbContext db) => _db = db;

    // ────────────────────────────── Patient ──────────────────────────────

    /// <summary>GET /api/fhir/Patient/{id} — Read a Patient resource by id.</summary>
    [HttpGet("Patient/{id}")]
    public async Task<IActionResult> GetPatientById(string id)
    {
        var patient = await _db.Patients.FindAsync(id);
        if (patient is null)
            return NotFound(new OperationOutcome("not-found", $"Patient with id '{id}' not found."));

        return Ok(MapPatient(patient));
    }

    /// <summary>GET /api/fhir/Patient — Search Patients.</summary>
    [HttpGet("Patient")]
    public async Task<IActionResult> SearchPatients(
        [FromQuery] string? _id,
        [FromQuery] string? name,
        [FromQuery] string? identifier)
    {
        IQueryable<Patient> query = _db.Patients;

        if (!string.IsNullOrEmpty(_id))
            query = query.Where(p => p.PatientId == _id);

        if (!string.IsNullOrEmpty(name))
            query = query.Where(p =>
                p.Name != null && p.Name.Contains(name));

        if (!string.IsNullOrEmpty(identifier))
            query = query.Where(p =>
                p.PatientId == identifier ||
                (p.PatientIdList != null && p.PatientIdList.Contains(identifier)));

        var patients = await query.OrderBy(p => p.PatientId).ToListAsync();

        var bundle = new Bundle("Patient", patients.Select(MapPatient).ToList());
        return Ok(bundle);
    }

    // ────────────────────────────── Observation ──────────────────────────

    /// <summary>GET /api/fhir/Observation/{id} — Read an Observation resource by id.</summary>
    [HttpGet("Observation/{id:long}")]
    public async Task<IActionResult> GetObservationById(long id)
    {
        var vitalSign = await _db.VitalSigns.FindAsync(id);
        if (vitalSign is null)
            return NotFound(new OperationOutcome("not-found", $"Observation with id '{id}' not found."));

        return Ok(MapObservation(vitalSign));
    }

    /// <summary>GET /api/fhir/Observation — Search Observations.</summary>
    [HttpGet("Observation")]
    public async Task<IActionResult> SearchObservations(
        [FromQuery] string? patient,
        [FromQuery] string? code,
        [FromQuery] string? date)
    {
        IQueryable<VitalSign> query = _db.VitalSigns;

        if (!string.IsNullOrEmpty(patient))
        {
            var patientId = patient.StartsWith("Patient/") ? patient[8..] : patient;
            query = query.Where(v => v.PatientId == patientId);
        }

        if (!string.IsNullOrEmpty(code))
            query = query.Where(v => v.VitalSignType == code || v.OriginalCode == code);

        if (!string.IsNullOrEmpty(date))
        {
            // FHIR date format: yyyy-MM-dd or yyyy-MM-ddThh:mm:ss[Z|+/-offset]
            // Support simple date prefix matching (e.g. 2024-01-01 => on that day)
            if (date.Length >= 10)
            {
                var datePrefix = date[..10];
                if (DateTime.TryParse(datePrefix, out var dateStart))
                {
                    var dateEnd = dateStart.AddDays(1);
                    query = query.Where(v =>
                        v.ObservationDateTime >= dateStart &&
                        v.ObservationDateTime < dateEnd);
                }
            }
        }

        var vitalSigns = await query
            .OrderByDescending(v => v.ObservationDateTime)
            .ToListAsync();

        var bundle = new Bundle("Observation", vitalSigns.Select(MapObservation).ToList());
        return Ok(bundle);
    }

    // ────────────────────────────── CapabilityStatement ──────────────────

    /// <summary>GET /api/fhir/metadata — Return a CapabilityStatement.</summary>
    [HttpGet("metadata")]
    public IActionResult GetMetadata()
    {
        return Ok(new
        {
            resourceType = "CapabilityStatement",
            status = "active",
            date = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            publisher = "HL7Gateway",
            kind = "instance",
            software = new
            {
                name = "HL7Gateway",
                version = "1.0.0"
            },
            implementation = new
            {
                description = "HL7Gateway FHIR R4 API",
                url = $"{Request.Scheme}://{Request.Host}/api/fhir"
            },
            fhirVersion = "4.0.1",
            format = new[] { "json" },
            rest = new[]
            {
                new
                {
                    mode = "server",
                    resource = new[]
                    {
                        new
                        {
                            type = "Patient",
                            profile = new[] { "http://hl7.org/fhir/StructureDefinition/Patient" },
                            interaction = new[]
                            {
                                new { code = "read" },
                                new { code = "search-type" }
                            },
                            searchParam = new[]
                            {
                                new { name = "_id", type = "token", documentation = "Logical id of the resource" },
                                new { name = "name", type = "string", documentation = "A patient name" },
                                new { name = "identifier", type = "token", documentation = "A patient identifier" }
                            }
                        },
                        new
                        {
                            type = "Observation",
                            profile = new[] { "http://hl7.org/fhir/StructureDefinition/Observation" },
                            interaction = new[]
                            {
                                new { code = "read" },
                                new { code = "search-type" }
                            },
                            searchParam = new[]
                            {
                                new { name = "patient", type = "reference", documentation = "The subject that the observation is about" },
                                new { name = "code", type = "token", documentation = "The code of the observation type" },
                                new { name = "date", type = "date", documentation = "The date of the observation" }
                            }
                        }
                    }
                }
            }
        });
    }

    // ────────────────────────────── Mapping helpers ──────────────────────

    private static object MapPatient(Patient patient)
    {
        return new
        {
            resourceType = "Patient",
            id = patient.PatientId,
            identifier = new[]
            {
                new
                {
                    system = "urn:oid:1.2.3.4",
                    value = patient.PatientId
                }
            },
            name = string.IsNullOrEmpty(patient.Name)
                ? null
                : new[]
                {
                    new
                    {
                        family = patient.Name.Contains(' ')
                            ? patient.Name[..patient.Name.LastIndexOf(' ')]
                            : patient.Name,
                        given = patient.Name.Contains(' ')
                            ? new[] { patient.Name[(patient.Name.LastIndexOf(' ') + 1)..] }
                            : Array.Empty<string>()
                    }
                },
            gender = patient.Gender switch
            {
                "M" => "male",
                "F" => "female",
                "O" => "other",
                _ => null
            },
            birthDate = patient.DateOfBirth?.ToString("yyyy-MM-dd")
        };
    }

    private static object MapObservation(VitalSign vitalSign)
    {
        // Build coding array from available fields
        var coding = new List<object>();
        if (!string.IsNullOrEmpty(vitalSign.OriginalCode))
        {
            coding.Add(new
            {
                system = string.IsNullOrEmpty(vitalSign.OriginalSystem)
                    ? "http://loinc.org"
                    : vitalSign.OriginalSystem,
                code = vitalSign.OriginalCode,
                display = vitalSign.OriginalText ?? vitalSign.VitalSignName
            });
        }
        // Always include a basic coding using VitalSignType
        if (!string.IsNullOrEmpty(vitalSign.VitalSignType) &&
            (string.IsNullOrEmpty(vitalSign.OriginalCode) || vitalSign.OriginalCode != vitalSign.VitalSignType))
        {
            coding.Add(new
            {
                system = "http://loinc.org",
                code = vitalSign.VitalSignType,
                display = vitalSign.VitalSignName
            });
        }

        if (coding.Count == 0)
        {
            coding.Add(new
            {
                system = "http://loinc.org",
                code = "unknown",
                display = vitalSign.VitalSignName ?? "Unknown"
            });
        }

        return new
        {
            resourceType = "Observation",
            id = vitalSign.VitalSignId.ToString(),
            status = string.IsNullOrEmpty(vitalSign.ObserveStatus)
                ? "final"
                : vitalSign.ObserveStatus,
            code = new
            {
                coding = coding,
                text = vitalSign.VitalSignName ?? vitalSign.OriginalText
            },
            subject = new
            {
                reference = $"Patient/{vitalSign.PatientId}"
            },
            valueQuantity = vitalSign.ValueNumeric.HasValue
                ? new
                {
                    value = vitalSign.ValueNumeric.Value,
                    unit = vitalSign.Units ?? string.Empty
                }
                : null,
            effectiveDateTime = vitalSign.ObservationDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ")
        };
    }
}

// ────────────────────────────── Internal DTOs ───────────────────────────

/// <summary>Minimal FHIR R4 Bundle for search results.</summary>
internal sealed class Bundle
{
    public string resourceType => "Bundle";
    public string type => "searchset";
    public int total { get; }
    public List<object> entry { get; }

    public Bundle(string resourceType, List<object> resources)
    {
        total = resources.Count;
        entry = resources.Select(r => new { resource = r, fullUrl = "" }).ToList<object>();
    }
}

/// <summary>Minimal FHIR R4 OperationOutcome for error responses.</summary>
internal sealed class OperationOutcome
{
    public string resourceType => "OperationOutcome";
    public List<OperationOutcomeIssue> issue { get; }

    public OperationOutcome(string code, string diagnostics)
    {
        issue = new List<OperationOutcomeIssue>
        {
            new OperationOutcomeIssue
            {
                severity = "error",
                code = code,
                diagnostics = diagnostics
            }
        };
    }

    internal sealed class OperationOutcomeIssue
    {
        public string severity { get; set; } = "error";
        public string code { get; set; } = "not-found";
        public string diagnostics { get; set; } = string.Empty;
    }
}
