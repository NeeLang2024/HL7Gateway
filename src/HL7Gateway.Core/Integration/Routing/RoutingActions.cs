namespace HL7Gateway.Core.Integration.Routing;

public static class RoutingActions
{
    /// <summary>与升级前一致：ADT 自动入队 + HIS 绑定同步。</summary>
    public const string LegacyDefault = "LegacyDefault";

    public const string ForwardAdt = "ForwardAdt";
    public const string SkipForward = "SkipForward";
    public const string Webhook = "Webhook";
    public const string ForwardAdtWebhook = "ForwardAdtWebhook";

    public static bool ForwardsAdt(string action) =>
        action is ForwardAdt or ForwardAdtWebhook or LegacyDefault;

    public static bool CallsWebhook(string action) =>
        action is Webhook or ForwardAdtWebhook;
}
