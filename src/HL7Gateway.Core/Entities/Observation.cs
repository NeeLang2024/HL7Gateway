using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HL7Gateway.Core.Entities;

[Table("Observations")]
public class Observation
{
    [Key]
    public long ObservationId { get; set; }

    public long MessageId { get; set; }

    [MaxLength(100)]
    public string PatientId { get; set; } = string.Empty;

    [MaxLength(10)]
    public string? SetId { get; set; }

    [MaxLength(10)]
    public string? ValueType { get; set; }

    [MaxLength(100)]
    public string IdentifierCode { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? IdentifierText { get; set; }

    [MaxLength(100)]
    public string? IdentifierSystem { get; set; }

    [MaxLength(2000)]
    public string? ObservationValue { get; set; }

    [MaxLength(100)]
    public string? Units { get; set; }

    [MaxLength(200)]
    public string? ReferenceRange { get; set; }

    [MaxLength(20)]
    public string? AbnormalFlags { get; set; }

    public DateTime? ObservationDateTime { get; set; }

    [MaxLength(100)]
    public string? ProducerId { get; set; }

    [MaxLength(20)]
    public string? ObserveStatus { get; set; }

    public DateTime CreatedAt { get; set; } = HL7Gateway.Core.ChinaTime.Now;

    [ForeignKey(nameof(MessageId))]
    public Hl7Message Message { get; set; } = null!;
}
