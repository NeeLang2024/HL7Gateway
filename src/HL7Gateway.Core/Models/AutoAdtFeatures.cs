namespace HL7Gateway.Core.Models;

/// <summary>
/// Auto ADT 增强功能开关。所有新能力默认关闭，避免影响现有部署。
/// </summary>
public class AutoAdtFeatures
{
    /// <summary>双码齐备后自动发起入院（带倒计时确认）。</summary>
    public bool AutoAdmitEnabled { get; set; }

    /// <summary>自动入院倒计时秒数（0=立即，建议 3）。</summary>
    public int AutoAdmitConfirmSeconds { get; set; } = 3;

    /// <summary>入院/转床前校验必填项。</summary>
    public bool StrictAdmitValidation { get; set; } = true;

    /// <summary>要求填写病人姓名（姓+名或 patientName）。</summary>
    public bool RequirePatientName { get; set; }

    /// <summary>要求填写出生日期。</summary>
    public bool RequireDateOfBirth { get; set; }

    /// <summary>相同病人+床位+事件类型在此秒数内禁止重复提交。</summary>
    public int DuplicateSubmitWindowSeconds { get; set; } = 60;

    /// <summary>HIS 经 MLLP 发来 ADT^A01/A02/A03 时自动维护床位绑定。</summary>
    public bool HisAutoBindingEnabled { get; set; }

    /// <summary>显示环境自检入口（端口/数据库/桥接）。</summary>
    public bool PreflightCheckEnabled { get; set; } = true;
}
