using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HL7Gateway.Core.Entities;

[Table("Patients")]
public class Patient
{
    [Key]
    [MaxLength(100)]
    public string PatientId { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? PatientIdList { get; set; }

    [MaxLength(100)]
    public string? Name { get; set; }

    public DateOnly? DateOfBirth { get; set; }

    [MaxLength(1)]
    public string? Gender { get; set; }

    [MaxLength(500)]
    public string? Address { get; set; }

    [MaxLength(50)]
    public string? PhoneNumber { get; set; }

    [MaxLength(50)]
    public string? Ssn { get; set; }

    [MaxLength(50)]
    public string? Race { get; set; }

    [MaxLength(50)]
    public string? MaritalStatus { get; set; }

    public DateTime CreatedAt { get; set; } = HL7Gateway.Core.ChinaTime.Now;
    public DateTime UpdatedAt { get; set; } = HL7Gateway.Core.ChinaTime.Now;

    public ICollection<Visit> Visits { get; set; } = new List<Visit>();
}
