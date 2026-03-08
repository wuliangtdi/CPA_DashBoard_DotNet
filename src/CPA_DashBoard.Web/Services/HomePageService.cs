namespace CPA_DashBoard.Web.Services;

/// <summary>
/// 负责解析前端首页模板文件的实际路径。
/// </summary>
public sealed class HomePageService
{
    /// <summary>
    /// 保存宿主环境实例，用于获取项目根目录与内容根目录。
    /// </summary>
    private readonly IWebHostEnvironment _environment;

    /// <summary>
    /// 使用宿主环境初始化首页路径服务。
    /// </summary>
    public HomePageService(IWebHostEnvironment environment)
    {
        // 保存宿主环境，供后续拼接模板文件路径时复用。
        _environment = environment;
    }

    /// <summary>
    /// 获取首页模板文件路径，优先复用 Python 版本模板，其次回退到本项目 wwwroot 下的副本。
    /// </summary>
    public string? ResolveIndexFilePath()
    {
        // 先定位 Python 原项目模板目录，保证前端页面与原版保持一致。
        var primaryIndexPath = Path.GetFullPath(Path.Combine(_environment.ContentRootPath, "..", "..", "..", "CPA_DashBoard_Python", "templates", "index.html"));

        // 再定位当前 .NET 项目中的静态首页副本，作为兜底方案。
        var fallbackIndexPath = Path.Combine(_environment.ContentRootPath, "wwwroot", "index.html");

        // 如果 Python 模板存在，则优先返回 Python 模板路径。
        if (File.Exists(primaryIndexPath))
        {
            return primaryIndexPath;
        }

        // 如果兜底首页存在，则返回兜底首页路径。
        if (File.Exists(fallbackIndexPath))
        {
            return fallbackIndexPath;
        }

        // 如果两个候选文件都不存在，则返回空值给上层决定如何处理。
        return null;
    }
}
