using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HL7Gateway.Core.Entities;

[Table("AutoAdtEvents")]
public class AutoAdtEvent
{
    [Key]
    public long Id { get; set; }

    [MaxLength(10)]
    public string EventType { get; set; } = string.Empty;

    [MaxLength(100)]
    public string PatientId { get; set; } = string.Empty;

    [MaxLength(100)]
    public string VisitId { get; set; } = string.Empty;

    public long? SourceBedId { get; set; }
    public long? TargetBedId { get; set; }
    public long? BindingId { get; set; }

    [MaxLength(100)]
    public string MessageControlId { get; set; } = string.Empty;

    [MaxLength(30)]
    public string EventStatus { get; set; } = "Created";

    /// <summary>操作审计：执行该 Auto ADT 动作的登录用户名。</summary>
    [MaxLength(100)]
    public string? OperatorUser { get; set; }

    public string? PatientSnapshotJson { get; set; }
    public string? BedSnapshotJson { get; set; }

    public DateTime CreatedAt { get; set; } = ChinaTime.Now;
    public DateTime UpdatedAt { get; set; } = ChinaTime.Now;
}
