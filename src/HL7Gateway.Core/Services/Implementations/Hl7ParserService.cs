using HL7Gateway.Core.Entities;
using HL7Gateway.Core.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace HL7Gateway.Core.Services.Implementations;

public class Hl7ParserService : IHl7ParserService
{
    private readonly ILogger<Hl7ParserService> _logger;

    public Hl7ParserService(ILogger<Hl7ParserService> logger)
    {
        _logger = logger;
    }

    public (bool Success, string? Error) ParseMessage(
        Hl7Message message,
        out List<SegmentParseResult> segments,
        out List<Observation> observations,
        out List<VitalSign> vitalSigns,
        out List<Patient> patients,
        out List<Visit> visits,
        List<IdentifierMapping> mappings)
    {
        segments = [];
        observations = [];
        vitalSigns = [];
        patients = [];
        visits = [];
        var visitDict = new Dictionary<string, Visit>();

        try
        {
            var lines = message.RawContent.Split('\r');
            var segIndex = 0;
            var currentPatientId = "";
            var currentVisitId = "";
            var patientDict = new Dictionary<string, Patient>();

            foreach (var line in lines)
            {
                if (string.IsNullOrEmpty(line)) continue;

                var segType = line.Length >= 3 ? line[..3] : line;

                segments.Add(new SegmentParseResult
                {
                    SegmentType = segType,
                    SegmentIndex = segIndex++,
                    Raw = line
                });

                if (segType == "PID")
                {
                    ParsePid(line, patientDict, out currentPatientId);
                    message.PatientId ??= currentPatientId;
                }
                else if (segType == "PV1")
                {
                    ParsePv1(line, currentPatientId, visitDict, out currentVisitId);
                    message.VisitId ??= currentVisitId;
                }
                else if (segType == "OBX")
                {
                    var obs = ParseObxToObservation(line, message, currentPatientId, currentVisitId);
                    if (obs is not null)
                    {
                        observations.Add(obs);
                    }
                }
            }

            patients = [.. patientDict.Values];
            visits = [.. visitDict.Values];

            if (observations.Count > 0)
            {
                vitalSigns = ExtractVitalSigns(observations, message, mappings);
            }

            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse HL7 message {ControlId}", message.MessageControlId);
            return (false, ex.Message);
        }
    }

    private static void ParsePid(string line, Dictionary<string, Patient> patientDict, out string currentPatientId)
    {
        var fields = SplitFields(line);
        currentPatientId = "";
        if (fields.Count <= 3) return;

        var pidField = fields[3];
        var pidParts = pidField.Split('^');
        var rawId = pidParts.Length > 0 ? pidParts[0] : "";
        var patientId = rawId.Trim('"');
        if (string.IsNullOrEmpty(patientId)) return;

        currentPatientId = patientId;

        if (!patientDict.ContainsKey(patientId))
        {
            var patient = new Patient
            {
                PatientId = patientId,
                PatientIdList = pidField,
                Name = ParsePatientName(fields.Count > 5 ? fields[5] : null),
                DateOfBirth = fields.Count > 7 && DateOnly.TryParseExact(fields[7], "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var dob) ? dob : null,
                Gender = fields.Count > 8 ? fields[8] : null,
                Address = fields.Count > 11 ? fields[11] : null,
                PhoneNumber = fields.Count > 13 ? fields[13] : null,
                Ssn = fields.Count > 19 ? fields[19] : null,
                Race = fields.Count > 10 ? fields[10] : null,
                MaritalStatus = fields.Count > 16 ? fields[16] : null,
            };
            patientDict[patientId] = patient;
        }
    }

    private static void ParsePv1(string line, string currentPatientId, Dictionary<string, Visit> visitDict, out string currentVisitId)
    {
        var fields = SplitFields(line);
        currentVisitId = "";
        if (string.IsNullOrEmpty(currentPatientId)) return;

        // PV1-19: Visit Number (may be absent in ORU messages)
        var visitId = "";
        if (fields.Count > 19)
        {
            var visitField = fields[19];
            var visitParts = visitField.Split('^');
            visitId = visitParts.Length > 0 ? visitParts[0] : visitField;
        }

        // Generate a stable key: use VisitId if present, otherwise patient-based key
        var key = !string.IsNullOrEmpty(visitId) ? visitId : $"VISIT_{currentPatientId}";
        currentVisitId = key;

        // PV1-3: Assigned Patient Location → Department^Ward^Room^Bed
        var locParts = fields.Count > 3 && !string.IsNullOrEmpty(fields[3])
            ? fields[3].Split('^')
            : [];

        if (!visitDict.TryGetValue(key, out var visit))
        {
            visit = new Visit
            {
                VisitId = key,
                PatientId = currentPatientId,
                PatientClass = fields.Count > 2 ? fields[2] : null,
                AttendingDoctor = fields.Count > 7 ? fields[7] : null,
                ReferringDoctor = fields.Count > 8 ? fields[8] : null,
                AdmitDiagnosis = fields.Count > 6 ? fields[6] : null,
                PatientType = fields.Count > 18 ? fields[18] : null,
                Department = locParts.Length > 0 ? locParts[0] : null,
                Ward = locParts.Length > 1 ? locParts[1] : null,
                Room = locParts.Length > 3 ? locParts[3] : null,
                Bed = locParts.Length > 2 ? locParts[2].Split('&')[0] : null,
            };
            if (fields.Count > 5 && DateTime.TryParseExact(fields[5], "yyyyMMddHHmmss", null, System.Globalization.DateTimeStyles.None, out var admitDt))
                visit.AdmitDateTime = admitDt;
            visitDict[key] = visit;
        }
        else if (locParts.Length > 0)
        {
            // Update location fields if the visit already exists
            visit.Department ??= locParts[0];
            if (locParts.Length > 1) visit.Ward ??= locParts[1];
            if (locParts.Length > 2) visit.Bed ??= locParts[2].Split('&')[0];
            if (locParts.Length > 3) visit.Room ??= locParts[3];
        }
    }

    private static Observation? ParseObxToObservation(string obxText, Hl7Message message, string currentPatientId, string currentVisitId)
    {
        var fields = SplitFields(obxText);
        if (fields.Count < 5) return null;

        var obs = new Observation
        {
            MessageId = message.MessageId,
            PatientId = currentPatientId,
            SetId = GetField(fields, 1),
            ValueType = GetField(fields, 2),
            ObservationValue = GetField(fields, 5),
            AbnormalFlags = GetField(fields, 8),
            ObserveStatus = GetField(fields, 11),
            ProducerId = fields.Count > 14 ? GetField(fields, 14) : null,
        };

        var obsIdField = GetField(fields, 3);
        var obsIdParts = obsIdField?.Split('^');
        if (obsIdParts is { Length: > 0 })
        {
            obs.IdentifierCode = obsIdParts[0] ?? "";
            obs.IdentifierText = obsIdParts.Length > 1 ? obsIdParts[1] : null;
            obs.IdentifierSystem = obsIdParts.Length > 2 ? obsIdParts[2] : null;
        }

        var unitsField = GetField(fields, 6);
        if (!string.IsNullOrEmpty(unitsField))
        {
            var unitParts = unitsField.Split('^');
            obs.Units = unitParts.Length > 1 ? unitParts[1] : unitParts[0];
        }

        var rangeField = GetField(fields, 7);
        obs.ReferenceRange = rangeField;

        if (DateTime.TryParseExact(GetField(fields, 14), "yyyyMMddHHmmss", null,
                System.Globalization.DateTimeStyles.None, out var obsDt))
        {
            obs.ObservationDateTime = obsDt;
        }

        return obs;
    }

    private static List<VitalSign> ExtractVitalSigns(
        List<Observation> observations,
        Hl7Message message,
        List<IdentifierMapping> mappings)
    {
        var vitalSigns = new List<VitalSign>();
        var mappingLookup = mappings
            .Where(m => m.IsActive)
            .ToLookup(m => m.SourceCode);

        foreach (var obs in observations)
        {
            foreach (var mapping in mappingLookup[obs.IdentifierCode])
            {
                var vs = new VitalSign
                {
                    MessageId = message.MessageId,
                    ObservationId = obs.ObservationId,
                    PatientId = obs.PatientId,
                    VisitId = message.VisitId,
                    VitalSignType = mapping.VitalSignType,
                    VitalSignName = mapping.VitalSignName,
                    Units = obs.Units,
                    OriginalCode = obs.IdentifierCode,
                    OriginalText = obs.IdentifierText,
                    OriginalSystem = obs.IdentifierSystem,
                    AbnormalFlags = obs.AbnormalFlags,
                    ReferenceRange = obs.ReferenceRange,
                    ObserveStatus = obs.ObserveStatus,
                    ObservationDateTime = obs.ObservationDateTime ?? message.ReceivedAt,
                    DeviceId = obs.ProducerId,
                };

                if (decimal.TryParse(obs.ObservationValue,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var numVal))
                {
                    vs.ValueNumeric = numVal;

                    if (mapping.VitalSignType is "NIBP_SYS" or "IBP_SYS")
                        vs.Systolic = numVal;
                    else if (mapping.VitalSignType is "NIBP_DIA" or "IBP_DIA")
                        vs.Diastolic = numVal;
                    else if (mapping.VitalSignType is "NIBP_MEAN" or "IBP_MEAN")
                        vs.MeanPressure = numVal;
                }
                else
                {
                    vs.ValueString = obs.ObservationValue;
                }

                vitalSigns.Add(vs);
            }
        }

        return vitalSigns;
    }

    private static List<string> SplitFields(string segment) =>
        [.. segment.Split('|')];

    private static string? GetField(List<string> fields, int index) =>
        index < fields.Count ? fields[index] : null;

    private static string? ParsePatientName(string? pid5)
    {
        if (string.IsNullOrEmpty(pid5)) return null;
        var parts = pid5.Split('^');
        var family = CleanNamePart(parts.Length > 0 ? parts[0] : "");
        var given = CleanNamePart(parts.Length > 1 ? parts[1] : "");
        return string.IsNullOrEmpty(given) ? family : $"{family}{given}";
    }

    private static string CleanNamePart(string? value)
    {
        var cleaned = (value ?? "").Trim();
        if (cleaned == "\"\"" || cleaned == "''") return "";
        return cleaned.Trim('"', '\'').Trim();
    }
}
