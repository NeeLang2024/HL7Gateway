using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HL7Gateway.Core.Entities;

[Table("AutoAdtBindings")]
public class AutoAdtBinding
{
    [Key]
    public long Id { get; set; }

    [MaxLength(100)]
    public string PatientId { get; set; } = string.Empty;

    [MaxLength(100)]
    public string VisitId { get; set; } = string.Empty;

    public long BedId { get; set; }

    [MaxLength(100)]
    public string? DeviceCode { get; set; }

    [MaxLength(30)]
    public string BindingStatus { get; set; } = "Active";

    public DateTime BindTime { get; set; } = ChinaTime.Now;
    public DateTime? UnbindTime { get; set; }

    public DateTime CreatedAt { get; set; } = ChinaTime.Now;
    public DateTime UpdatedAt { get; set; } = ChinaTime.Now;

    [ForeignKey(nameof(BedId))]
    public AutoAdtBed? Bed { get; set; }
}
