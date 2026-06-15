using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HL7Gateway.Core.Entities;

[Table("Visits")]
public class Visit
{
    [Key]
    [MaxLength(100)]
    public string VisitId { get; set; } = string.Empty;

    [MaxLength(100)]
    public string PatientId { get; set; } = string.Empty;

    public DateTime? AdmitDateTime { get; set; }
    public DateTime? DischargeDateTime { get; set; }

    [MaxLength(50)]
    public string? PatientClass { get; set; }

    [MaxLength(500)]
    public string? AdmitDiagnosis { get; set; }

    [MaxLength(100)]
    public string? AttendingDoctor { get; set; }

    [MaxLength(100)]
    public string? ReferringDoctor { get; set; }

    [MaxLength(100)]
    public string? Department { get; set; }

    [MaxLength(100)]
    public string? Ward { get; set; }

    [MaxLength(50)]
    public string? Room { get; set; }

    [MaxLength(50)]
    public string? Bed { get; set; }

    [MaxLength(50)]
    public string? PatientType { get; set; }

    public DateTime CreatedAt { get; set; } = HL7Gateway.Core.ChinaTime.Now;
    public DateTime UpdatedAt { get; set; } = HL7Gateway.Core.ChinaTime.Now;

    [ForeignKey(nameof(PatientId))]
    public Patient Patient { get; set; } = null!;
}
