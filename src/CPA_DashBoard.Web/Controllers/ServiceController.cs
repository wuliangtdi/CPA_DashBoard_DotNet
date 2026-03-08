using CPA_DashBoard.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace CPA_DashBoard.Web.Controllers;

/// <summary>
/// 负责 CLIProxyAPI 服务状态、启动、停止和重启接口。
/// </summary>
[ApiController]
[Route("api/service")]
public sealed class ServiceController : ControllerBase
{
    /// <summary>
    /// 保存进程服务实例。
    /// </summary>
    private readonly CliProxyProcessService _cliProxyProcessService;

    /// <summary>
    /// 使用进程服务初始化控制器。
    /// </summary>
    public ServiceController(CliProxyProcessService cliProxyProcessService)
    {
        // 这里保存进程服务实例，避免控制器直接处理系统进程。
        _cliProxyProcessService = cliProxyProcessService;
    }

    /// <summary>
    /// 获取服务状态。
    /// </summary>
    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        // 这里读取当前 CLIProxyAPI 的运行状态。
        var payload = _cliProxyProcessService.GetServiceStatus();

        // 这里返回状态对象。
        return Ok(payload);
    }

    /// <summary>
    /// 启动服务。
    /// </summary>
    [HttpPost("start")]
    public async Task<IActionResult> StartAsync(CancellationToken cancellationToken)
    {
        // 这里调用服务层执行启动逻辑。
        var result = await _cliProxyProcessService.StartAsync(cancellationToken);

        // 这里输出启动结果。
        return StatusCode(result.StatusCode, result.Payload);
    }

    /// <summary>
    /// 停止服务。
    /// </summary>
    [HttpPost("stop")]
    public async Task<IActionResult> StopAsync(CancellationToken cancellationToken)
    {
        // 这里调用服务层执行停止逻辑。
        var result = await _cliProxyProcessService.StopAsync(cancellationToken);

        // 这里输出停止结果。
        return StatusCode(result.StatusCode, result.Payload);
    }

    /// <summary>
    /// 重启服务。
    /// </summary>
    [HttpPost("restart")]
    public async Task<IActionResult> RestartAsync(CancellationToken cancellationToken)
    {
        // 这里调用服务层执行重启逻辑。
        var result = await _cliProxyProcessService.RestartAsync(cancellationToken);

        // 这里输出重启结果。
        return StatusCode(result.StatusCode, result.Payload);
    }
}
