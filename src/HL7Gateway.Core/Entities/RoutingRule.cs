using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HL7Gateway.Core.Entities;

/// <summary>
/// 入站 HL7 路由规则（模块化；未启用路由或无规则时不参与处理）。
/// </summary>
[Table("RoutingRules")]
public class RoutingRule
{
    [Key]
    public long Id { get; set; }

    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>越小越优先。</summary>
    public int Priority { get; set; } = 100;

    public bool IsEnabled { get; set; } = true;

    /// <summary>如 ADT、ORU；空=任意。</summary>
    [MaxLength(20)]
    public string? MessageType { get; set; }

    /// <summary>如 A01；空=任意。</summary>
    [MaxLength(20)]
    public string? TriggerEvent { get; set; }

    /// <summary>来源 IP，支持 * 通配。</summary>
    [MaxLength(100)]
    public string? SourceIpPattern { get; set; }

    [MaxLength(100)]
    public string? SendingApp { get; set; }

    [MaxLength(100)]
    public string? SendingFacility { get; set; }

    /// <summary>
    /// LegacyDefault | ForwardAdt | SkipForward | Webhook | ForwardAdtWebhook
    /// </summary>
    [MaxLength(40)]
    public string Action { get; set; } = "ForwardAdt";

    [MaxLength(500)]
    public string? WebhookUrl { get; set; }

    /// <summary>可选 HL7 字段改写 JSON，见 Hl7FieldTransform。</summary>
    public string? TransformJson { get; set; }

    [MaxLength(300)]
    public string? Remark { get; set; }

    public DateTime CreatedAt { get; set; } = ChinaTime.Now;
    public DateTime UpdatedAt { get; set; } = ChinaTime.Now;
}
