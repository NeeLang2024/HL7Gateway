using HL7Gateway.Core.Entities;

namespace HL7Gateway.Core.Services.Interfaces;

public interface IHl7ParserService
{
    (bool Success, string? Error) ParseMessage(Hl7Message message, out List<SegmentParseResult> segments, out List<Observation> observations, out List<VitalSign> vitalSigns, out List<Patient> patients, out List<Visit> visits, List<IdentifierMapping> mappings);
}

public class SegmentParseResult
{
    public string SegmentType { get; set; } = string.Empty;
    public int SegmentIndex { get; set; }
    public string Raw { get; set; } = string.Empty;
    public string JsonContent { get; set; } = string.Empty;
}
