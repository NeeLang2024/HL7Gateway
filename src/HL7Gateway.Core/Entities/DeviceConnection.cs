using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HL7Gateway.Core.Entities;

[Table("DeviceConnections")]
public class DeviceConnection
{
    [Key]
    public long ConnectionId { get; set; }

    [MaxLength(50)]
    public string SourceIp { get; set; } = string.Empty;

    public int SourcePort { get; set; }

    [MaxLength(200)]
    public string? DeviceName { get; set; }

    public int MessageCount { get; set; }

    public bool IsConnected { get; set; }

    public DateTime FirstConnected { get; set; } = HL7Gateway.Core.ChinaTime.Now;
    public DateTime LastActivity { get; set; } = HL7Gateway.Core.ChinaTime.Now;
    public DateTime? DisconnectedAt { get; set; }
}
