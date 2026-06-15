using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HL7Gateway.Core.Entities;

[Table("IdentifierMappings")]
public class IdentifierMapping
{
    [Key]
    public int MappingId { get; set; }

    [MaxLength(100)]
    public string SourceSystem { get; set; } = string.Empty;

    [MaxLength(100)]
    public string SourceCode { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? SourceText { get; set; }

    [MaxLength(50)]
    public string VitalSignType { get; set; } = string.Empty;

    [MaxLength(100)]
    public string VitalSignName { get; set; } = string.Empty;

    [MaxLength(20)]
    public string? LoincCode { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = HL7Gateway.Core.ChinaTime.Now;
    public DateTime UpdatedAt { get; set; } = HL7Gateway.Core.ChinaTime.Now;
}
