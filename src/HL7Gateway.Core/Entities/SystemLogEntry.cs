using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HL7Gateway.Core.Entities;

[Table("SystemLogs")]
public class SystemLogEntry
{
    [Key]
    public long LogId { get; set; }

    public byte Level { get; set; }

    [MaxLength(100)]
    public string? Category { get; set; }

    [MaxLength(2000)]
    public string Message { get; set; } = string.Empty;

    public string? StackTrace { get; set; }

    public DateTime CreatedAt { get; set; } = HL7Gateway.Core.ChinaTime.Now;
}
