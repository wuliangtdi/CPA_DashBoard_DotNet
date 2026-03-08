using CPA_DashBoard.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace CPA_DashBoard.Web.Controllers;

/// <summary>
/// 负责返回 API 使用说明与示例代码。
/// </summary>
[ApiController]
[Route("api/usage-guide")]
public sealed class UsageGuideController : ControllerBase
{
    /// <summary>
    /// 保存使用说明服务实例。
    /// </summary>
    private readonly UsageGuideService _usageGuideService;

    /// <summary>
    /// 使用使用说明服务初始化控制器。
    /// </summary>
    public UsageGuideController(UsageGuideService usageGuideService)
    {
        // 这里保存服务实例，后续直接复用示例代码生成逻辑。
        _usageGuideService = usageGuideService;
    }

    /// <summary>
    /// 获取使用说明。
    /// </summary>
    [HttpGet]
    public IActionResult GetUsageGuide()
    {
        // 这里生成前端展示所需的 API 使用说明数据。
        var payload = _usageGuideService.GetUsageGuide();

        // 这里返回 JSON 结果。
        return Ok(payload);
    }
}
