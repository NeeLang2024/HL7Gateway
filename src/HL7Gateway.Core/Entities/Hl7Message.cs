using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HL7Gateway.Core.Entities;

[Table("HL7Messages")]
public class Hl7Message
{
    [Key]
    public long MessageId { get; set; }

    [MaxLength(100)]
    public string MessageControlId { get; set; } = string.Empty;

    [MaxLength(20)]
    public string MessageType { get; set; } = string.Empty;

    [MaxLength(20)]
    public string? TriggerEvent { get; set; }

    [MaxLength(10)]
    public string? VersionId { get; set; }

    [MaxLength(100)]
    public string? SendingApp { get; set; }

    [MaxLength(100)]
    public string? SendingFacility { get; set; }

    [MaxLength(100)]
    public string? ReceivingApp { get; set; }

    [MaxLength(100)]
    public string? ReceivingFacility { get; set; }

    public DateTime? MessageDateTime { get; set; }

    [MaxLength(50)]
    public string SourceIp { get; set; } = string.Empty;

    public int SourcePort { get; set; }

    public string RawContent { get; set; } = string.Empty;

    public byte ParseStatus { get; set; }

    [MaxLength(100)]
    public string? PatientId { get; set; }

    [MaxLength(100)]
    public string? VisitId { get; set; }

    /// <summary>
    /// PV1-3 assigned patient location (Department^Ward^Bed^Room ...), captured even when
    /// the message has no patient, so monitor data from an empty bed can still be identified.
    /// </summary>
    [MaxLength(200)]
    public string? PatientLocation { get; set; }

    public DateTime ReceivedAt { get; set; } = HL7Gateway.Core.ChinaTime.Now;
    public DateTime? ProcessedAt { get; set; }

    [MaxLength(2000)]
    public string? ErrorMessage { get; set; }

    public ICollection<ParsedSegment> ParsedSegments { get; set; } = new List<ParsedSegment>();
    public ICollection<Observation> Observations { get; set; } = new List<Observation>();
}
