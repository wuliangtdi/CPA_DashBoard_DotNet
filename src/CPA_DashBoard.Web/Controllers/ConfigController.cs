using CPA_DashBoard.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace CPA_DashBoard.Web.Controllers;

/// <summary>
/// 负责返回前端初始化所需的运行配置。
/// </summary>
[ApiController]
[Route("api/config")]
public sealed class ConfigController : ControllerBase
{
    /// <summary>
    /// 保存配置服务实例。
    /// </summary>
    private readonly ConfigService _configService;

    /// <summary>
    /// 使用配置服务初始化控制器。
    /// </summary>
    public ConfigController(ConfigService configService)
    {
        // 这里保存配置服务实例，避免控制器直接拼装配置对象。
        _configService = configService;
    }

    /// <summary>
    /// 获取当前运行配置。
    /// </summary>
    [HttpGet]
    public IActionResult GetConfig()
    {
        // 这里读取前端初始化需要的配置对象。
        var payload = _configService.GetConfig();

        // 这里返回 JSON 配置结果。
        return Ok(payload);
    }
}
