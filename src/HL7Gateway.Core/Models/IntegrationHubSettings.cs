namespace HL7Gateway.Core.Models;

/// <summary>
/// 集成中枢模块开关。默认全部关闭，不影响现有 MLLP//ADT 行为。
/// </summary>
public class IntegrationHubSettings
{
    /// <summary>启用入站消息路由引擎（须同时配置至少一条已启用规则才生效）。</summary>
    public bool RoutingEnabled { get; set; }

    /// <summary>路由匹配时写入 Integration Trace。</summary>
    public bool RoutingTraceEnabled { get; set; } = true;
}
