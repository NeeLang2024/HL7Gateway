using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HL7Gateway.Core.Entities;

[Table("AutoAdtBeds")]
public class AutoAdtBed
{
    [Key]
    public long Id { get; set; }

    [MaxLength(100)]
    public string? CareArea { get; set; }

    [MaxLength(100)]
    public string? Room { get; set; }

    [MaxLength(50)]
    public string? Bed { get; set; }

    [MaxLength(100)]
    public string? BedLabel { get; set; }

    [MaxLength(100)]
    public string? DeviceCode { get; set; }

    [MaxLength(200)]
    public string? DeviceBarcode { get; set; }

    [MaxLength(200)]
    public string? BedBarcode { get; set; }

    [MaxLength(300)]
    public string PhilipsLocationValue { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;

    [MaxLength(500)]
    public string? Remark { get; set; }

    public DateTime CreatedAt { get; set; } = ChinaTime.Now;
    public DateTime UpdatedAt { get; set; } = ChinaTime.Now;
}
