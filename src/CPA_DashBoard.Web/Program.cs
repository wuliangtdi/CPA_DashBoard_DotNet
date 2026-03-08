using CPA_DashBoard.Web.Middleware;
using CPA_DashBoard.Web.Services;

// 这里创建 ASP.NET Core 应用构建器。
var builder = WebApplication.CreateBuilder(args);

// 这里注册 MVC 控制器支持，让业务路由统一由 Controller 承载。
builder.Services.AddControllers();

// 这里注册首页路径解析服务，用于返回与 Python 版本一致的前端首页。
builder.Services.AddSingleton<IndexPageService>();

// 这里注册应用上下文服务，统一负责读取配置、路径和环境信息。
builder.Services.AddSingleton<AppContextService>();

// 这里注册配额缓存存储服务，用于读写 quota_cache.json。
builder.Services.AddSingleton<QuotaCacheStore>();

// 这里注册配额服务，用于查询实时配额、静态模型和 Token 状态。
builder.Services.AddSingleton<QuotaService>();

// 这里注册账户业务服务，用于账户列表、删除、认证文件读取和配额刷新。
builder.Services.AddSingleton<AccountApiService>();

// 这里注册配置服务，用于给前端提供初始化配置数据。
builder.Services.AddSingleton<ConfigService>();

// 这里注册使用说明服务，用于生成示例代码和接入说明。
builder.Services.AddSingleton<UsageGuideService>();

// 这里注册日志服务，用于读取、尾随和清理日志文件。
builder.Services.AddSingleton<LogService>();

// 这里注册进程服务，用于控制 CLIProxyAPI 的启停与状态查询。
builder.Services.AddSingleton<CliProxyProcessService>();

// 这里注册 OAuth 会话管理器，用于维护交互式认证会话。
builder.Services.AddSingleton<OAuthSessionManager>();

// 这里构建 WebApplication 实例。
var app = builder.Build();

// 这里统一追加禁用缓存响应头，保证前端行为与 Python 版本一致。
app.UseMiddleware<NoCacheHeadersMiddleware>();

// 这里启用默认首页文件查找能力。
app.UseDefaultFiles();

// 这里启用静态文件中间件，供首页和附带资源访问。
app.UseStaticFiles();

// 这里映射所有控制器路由到终结点系统。
app.MapControllers();

// 这里为所有未命中静态资源和 API 的请求回退到首页，保持单页应用行为。
app.MapFallback((IndexPageService indexPageService) =>
{
    // 这里解析最终应该返回的首页文件路径。
    var indexFilePath = indexPageService.ResolveIndexFilePath();

    // 这里在首页文件存在时直接返回 HTML 内容。
    if (File.Exists(indexFilePath))
    {
        return Results.File(indexFilePath, "text/html; charset=utf-8");
    }

    // 这里在首页文件缺失时明确返回 404。
    return Results.NotFound();
});

// 这里启动 ASP.NET Core 应用。
app.Run();
