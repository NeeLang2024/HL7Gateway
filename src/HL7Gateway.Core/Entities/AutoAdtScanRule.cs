using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HL7Gateway.Core.Entities;

/// <summary>
/// 扫码解析规则：让不同医院的腕带 / 床位条码格式可在前端配置，而不必改代码。
/// 解析时按 Priority 升序逐条尝试，命中即止；没有任何规则命中时回退到内置默认解析。
/// </summary>
[Table("AutoAdtScanRules")]
public class AutoAdtScanRule
{
    [Key]
    public long Id { get; set; }

    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>规则适用对象：Patient（腕带）或 Bed（床位/设备码）。</summary>
    [MaxLength(20)]
    public string RuleType { get; set; } = "Patient";

    /// <summary>
    /// 正则表达式。
    /// Patient 规则使用命名组 (?&lt;mrn&gt;...) 与可选 (?&lt;visit&gt;...)；
    /// Bed 规则使用命名组 (?&lt;code&gt;...) 提取用于匹配床位映射的编码。
    /// </summary>
    [MaxLength(500)]
    public string? Pattern { get; set; }

    /// <summary>可选：逗号分隔的前缀，命中则从原始串去掉（在正则之前应用）。</summary>
    [MaxLength(300)]
    public string? StripPrefixes { get; set; }

    public int Priority { get; set; } = 100;

    public bool IsEnabled { get; set; } = true;

    /// <summary>示例条码，便于在配置页测试。</summary>
    [MaxLength(200)]
    public string? Sample { get; set; }

    [MaxLength(300)]
    public string? Remark { get; set; }

    public DateTime CreatedAt { get; set; } = ChinaTime.Now;
    public DateTime UpdatedAt { get; set; } = ChinaTime.Now;
}
