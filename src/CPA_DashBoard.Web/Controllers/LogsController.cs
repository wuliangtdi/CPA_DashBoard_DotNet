using CPA_DashBoard.Web.Models.Requests;
using CPA_DashBoard.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace CPA_DashBoard.Web.Controllers;

/// <summary>
/// 负责日志读取、尾部轮询和清空接口。
/// </summary>
[ApiController]
[Route("api/logs")]
public sealed class LogsController : ControllerBase
{
    /// <summary>
    /// 保存日志服务实例。
    /// </summary>
    private readonly LogService _logService;

    /// <summary>
    /// 使用日志服务初始化控制器。
    /// </summary>
    public LogsController(LogService logService)
    {
        // 这里保存日志服务实例，后续所有日志读取都通过服务层完成。
        _logService = logService;
    }

    /// <summary>
    /// 获取日志内容。
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetLogsAsync([FromQuery] int? lines, [FromQuery] int? offset, CancellationToken cancellationToken)
    {
        // 这里把前端可选参数转换成服务层需要的实际值。
        var result = await _logService.GetLogsAsync(lines ?? 200, offset ?? 0, cancellationToken);

        // 这里返回日志内容结果。
        return StatusCode(result.StatusCode, result.Payload);
    }

    /// <summary>
    /// 获取日志尾部内容。
    /// </summary>
    [HttpGet("tail")]
    public async Task<IActionResult> GetTailAsync([FromQuery] int? lines, CancellationToken cancellationToken)
    {
        // 这里读取日志尾部，供前端轮询刷新使用。
        var payload = await _logService.GetTailAsync(lines ?? 50, cancellationToken);

        // 这里返回尾部内容。
        return Ok(payload);
    }

    /// <summary>
    /// 清空日志，或在清空前先备份。
    /// </summary>
    [HttpPost("clear")]
    public async Task<IActionResult> ClearAsync([FromBody] ClearLogsRequest? request, CancellationToken cancellationToken)
    {
        // 这里读取是否需要先备份日志文件。
        var backup = request?.Backup == true;

        // 这里调用日志服务执行清空逻辑。
        var result = await _logService.ClearAsync(backup, cancellationToken);

        // 这里返回清空结果。
        return StatusCode(result.StatusCode, result.Payload);
    }
}
