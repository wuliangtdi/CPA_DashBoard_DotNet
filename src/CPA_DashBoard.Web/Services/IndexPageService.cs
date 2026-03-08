namespace CPA_DashBoard.Web.Services;

/// <summary>
/// 负责解析单页应用首页文件路径。
/// </summary>
public sealed class IndexPageService
{
    /// <summary>
    /// 保存宿主环境实例。
    /// </summary>
    private readonly IWebHostEnvironment _environment;

    /// <summary>
    /// 使用宿主环境初始化首页解析服务。
    /// </summary>
    public IndexPageService(IWebHostEnvironment environment)
    {
        // 这里保存宿主环境，后续需要基于内容根目录拼接路径。
        _environment = environment;
    }

    /// <summary>
    /// 解析最终应返回的首页文件路径。
    /// </summary>
    public string ResolveIndexFilePath()
    {
        // 这里优先定位原 Python 项目的模板文件，以保证前端完全一致。
        var primaryIndexPath = Path.GetFullPath(Path.Combine(_environment.ContentRootPath, "..", "..", "..", "CPA_DashBoard_Python", "templates", "index.html"));

        // 这里准备本项目内的静态首页兜底路径。
        var fallbackIndexPath = Path.Combine(_environment.ContentRootPath, "wwwroot", "index.html");

        // 这里在原模板存在时优先返回原模板。
        if (File.Exists(primaryIndexPath))
        {
            return primaryIndexPath;
        }

        // 这里在原模板不存在时回退到本项目的静态首页。
        return fallbackIndexPath;
    }
}
