using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HL7Gateway.Core.Entities;

[Table("VitalSigns")]
public class VitalSign
{
    [Key]
    public long VitalSignId { get; set; }

    public long MessageId { get; set; }
    public long? ObservationId { get; set; }

    [MaxLength(100)]
    public string PatientId { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? VisitId { get; set; }

    [MaxLength(50)]
    public string VitalSignType { get; set; } = string.Empty;

    [MaxLength(100)]
    public string VitalSignName { get; set; } = string.Empty;

    [Column(TypeName = "decimal(18,4)")]
    public decimal? ValueNumeric { get; set; }

    [MaxLength(200)]
    public string? ValueString { get; set; }

    [MaxLength(50)]
    public string? Units { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal? Systolic { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal? Diastolic { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal? MeanPressure { get; set; }

    [MaxLength(100)]
    public string OriginalCode { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? OriginalText { get; set; }

    [MaxLength(100)]
    public string? OriginalSystem { get; set; }

    [MaxLength(20)]
    public string? AbnormalFlags { get; set; }

    [MaxLength(200)]
    public string? ReferenceRange { get; set; }

    [MaxLength(20)]
    public string? ObserveStatus { get; set; }

    public DateTime ObservationDateTime { get; set; }
    public DateTime ReceivedAt { get; set; } = HL7Gateway.Core.ChinaTime.Now;

    [MaxLength(100)]
    public string? DeviceId { get; set; }

    [ForeignKey(nameof(MessageId))]
    public Hl7Message Message { get; set; } = null!;
}
