using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HL7Gateway.Core.DbContexts;

namespace HL7Gateway.WebApi.Controllers;

[ApiController]
[Route("api/validation")]
public class ValidationController : ControllerBase
{
    private readonly Hl7GatewayDbContext _db;

    public ValidationController(Hl7GatewayDbContext db) => _db = db;

    [HttpGet("{messageId:long}")]
    public async Task<IActionResult> ValidateMessage(long messageId)
    {
        var msg = await _db.Hl7Messages.FindAsync(messageId);
        if (msg is null) return NotFound();

        var raw = msg.RawContent;
        var lines = raw.Split('\r');
        var issues = new List<ValidationIssue>();

        if (lines.Length == 0)
        {
            issues.Add(new ValidationIssue("error", "MSG-001", "消息内容为空"));
            return Ok(new { issues, valid = false });
        }

        var msh = lines[0].Split('|');

        if (msh.Length < 9 || string.IsNullOrEmpty(msh[8]))
            issues.Add(new ValidationIssue("error", "MSH-008", "MSH-8 (消息类型) 缺失"));
        if (msh.Length < 10 || string.IsNullOrEmpty(msh[9]))
            issues.Add(new ValidationIssue("error", "MSH-009", "MSH-9 (控制ID) 缺失"));
        if (msh.Length < 7 || string.IsNullOrEmpty(msh[6]))
            issues.Add(new ValidationIssue("warning", "MSH-006", "MSH-6 (消息时间) 缺失"));
        if (msh.Length < 3 || string.IsNullOrEmpty(msh[2]))
            issues.Add(new ValidationIssue("info", "MSH-002", "MSH-2 (发送应用) 缺失"));

        if (msh.Length > 11)
        {
            var ver = msh[11].Split('^')[0];
            if (!IsValidVersion(ver))
                issues.Add(new ValidationIssue("warning", "MSH-011", $"未知HL7版本: {ver}"));
        }

        var hasPid = false;
        var hasPv1 = false;

        foreach (var line in lines)
        {
            if (string.IsNullOrEmpty(line)) continue;
            var segType = line.Length >= 3 ? line[..3] : line;
            var fields = line.Split('|');

            switch (segType)
            {
                case "PID":
                    hasPid = true;
                    if (fields.Length < 5 || string.IsNullOrEmpty(fields[3]))
                        issues.Add(new ValidationIssue("warning", "PID-003", "PID-3 (患者ID) 缺失"));
                    if (fields.Length < 6 || string.IsNullOrEmpty(fields[5]))
                        issues.Add(new ValidationIssue("info", "PID-005", "PID-5 (患者姓名) 缺失"));
                    break;
                case "PV1":
                    hasPv1 = true;
                    if (fields.Length < 3 || string.IsNullOrEmpty(fields[2]))
                        issues.Add(new ValidationIssue("warning", "PV1-002", "PV1-2 (患者类型) 缺失"));
                    break;
                case "OBX":
                    if (fields.Length < 5 || string.IsNullOrEmpty(fields[5]))
                        issues.Add(new ValidationIssue("warning", "OBX-005", "OBX-5 (观察值) 缺失"));
                    if (fields.Length < 3 || string.IsNullOrEmpty(fields[3]))
                        issues.Add(new ValidationIssue("warning", "OBX-003", "OBX-3 (标识码) 缺失"));
                    break;
                case "OBR":
                    if (fields.Length < 5 || string.IsNullOrEmpty(fields[4]))
                        issues.Add(new ValidationIssue("info", "OBR-004", "OBR-4 (通用服务ID) 缺失"));
                    break;
            }
        }

        if (!hasPid) issues.Add(new ValidationIssue("error", "SEG-PID", "缺少PID段"));
        if (!hasPv1 && msg.MessageType == "ADT")
            issues.Add(new ValidationIssue("warning", "SEG-PV1", "ADT消息缺少PV1段"));

        return Ok(new
        {
            issues,
            valid = issues.Count == 0,
            totalIssues = issues.Count,
            errors = issues.Count(i => i.Severity == "error"),
            warnings = issues.Count(i => i.Severity == "warning")
        });
    }

    private static bool IsValidVersion(string v) => v is "2.1" or "2.2" or "2.3" or "2.3.1" or "2.4" or "2.5" or "2.5.1" or "2.6" or "2.7" or "2.8";

    private record ValidationIssue(string Severity, string Code, string Message);
}
