using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HL7Gateway.Core.Entities;

[Table("ParsedSegments")]
public class ParsedSegment
{
    [Key]
    public long SegmentId { get; set; }

    public long MessageId { get; set; }

    [MaxLength(10)]
    public string SegmentType { get; set; } = string.Empty;

    public int SegmentIndex { get; set; }

    [MaxLength(4000)]
    public string? SegmentRaw { get; set; }

    public string? JsonContent { get; set; }

    [ForeignKey(nameof(MessageId))]
    public Hl7Message Message { get; set; } = null!;
}
