using CPA_DashBoard.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace CPA_DashBoard.Web.Controllers;

/// <summary>
/// 负责返回单页应用首页。
/// </summary>
[ApiController]
public sealed class HomeController : ControllerBase
{
    /// <summary>
    /// 保存首页解析服务实例。
    /// </summary>
    private readonly IndexPageService _indexPageService;

    /// <summary>
    /// 使用首页解析服务初始化控制器。
    /// </summary>
    public HomeController(IndexPageService indexPageService)
    {
        // 这里保存首页解析服务，后续首页和回退路由都会复用它。
        _indexPageService = indexPageService;
    }

    /// <summary>
    /// 返回首页 HTML。
    /// </summary>
    [HttpGet("/")]
    public IActionResult Index()
    {
        // 这里解析最终应返回的首页文件路径。
        var indexFilePath = _indexPageService.ResolveIndexFilePath();

        // 这里判断首页文件是否真实存在。
        if (!System.IO.File.Exists(indexFilePath))
        {
            // 这里在首页文件缺失时返回 404，避免前端得到空响应。
            return NotFound();
        }

        // 这里直接返回物理 HTML 文件，以保证前端页面与 Python 版本保持一致。
        return PhysicalFile(indexFilePath, "text/html; charset=utf-8");
    }
}
