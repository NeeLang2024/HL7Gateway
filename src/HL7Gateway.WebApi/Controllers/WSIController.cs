using HL7Gateway.Core;
using HL7Gateway.Core.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace HL7Gateway.WebApi.Controllers;

[ApiController]
[Route("api/wsi")]
public class WSIController : ControllerBase
{
    private readonly IWsiService _wsi;
    private readonly ILogger<WSIController> _logger;

    public WSIController(IWsiService wsi, ILogger<WSIController> logger)
    {
        _wsi = wsi;
        _logger = logger;
    }

    /// <summary>
    /// PIC iX 调用此接口订阅患者身份更新通知
    /// </summary>
    [HttpPost("subscribe")]
    public async Task<IActionResult> Subscribe([FromBody] WsiSubscribeRequest request)
    {
        if (string.IsNullOrEmpty(request.NotificationUri))
            return BadRequest(new { error = "NotificationUri is required" });

        var id = await _wsi.SubscribeAsync(
            request.NotificationUri,
            request.ClientId,
            request.PatientIdDomain,
            request.FacilityCode);

        _logger.LogInformation("PIC iX subscribed #{Id} at {Uri}", id, request.NotificationUri);
        return Ok(new { subscriptionId = id, expiresAt = ChinaTime.Now.AddDays(30) });
    }

    /// <summary>
    /// PIC iX 取消订阅
    /// </summary>
    [HttpPost("unsubscribe")]
    public async Task<IActionResult> Unsubscribe([FromBody] WsiUnsubscribeRequest request)
    {
        var ok = await _wsi.UnsubscribeAsync(request.SubscriptionId);
        if (!ok) return NotFound(new { error = "Subscription not found" });
        return Ok(new { message = "Unsubscribed" });
    }

    /// <summary>
    /// 查询活跃订阅列表
    /// </summary>
    [HttpGet("subscriptions")]
    public async Task<IActionResult> GetSubscriptions()
    {
        var subs = await _wsi.GetActiveSubscriptionsAsync();
        return Ok(subs.Select(s => new
        {
            s.SubscriptionId,
            s.NotificationUri,
            s.ClientId,
            s.PatientIdDomain,
            s.FacilityCode,
            s.IsActive,
            s.CreatedAt,
            s.ExpiresAt,
            s.NotifyCount,
            s.LastNotifiedAt,
            s.FailedCount,
            s.LastFailedAt,
        }));
    }
}

public class WsiSubscribeRequest
{
    public string NotificationUri { get; set; } = "";
    public string? ClientId { get; set; }
    public string? PatientIdDomain { get; set; }
    public string? FacilityCode { get; set; }
}

public class WsiUnsubscribeRequest
{
    public int SubscriptionId { get; set; }
}
