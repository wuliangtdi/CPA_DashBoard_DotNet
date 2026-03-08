namespace CPA_DashBoard.Web.Middleware;

/// <summary>
/// 负责为 API 和 HTML 响应统一追加禁用缓存头。
/// </summary>
public sealed class NoCacheHeadersMiddleware
{
    /// <summary>
    /// 保存下一个中间件委托。
    /// </summary>
    private readonly RequestDelegate _next;

    /// <summary>
    /// 使用下一个中间件委托初始化当前中间件。
    /// </summary>
    public NoCacheHeadersMiddleware(RequestDelegate next)
    {
        // 这里保存下一个中间件，确保请求链条可以继续向后执行。
        _next = next;
    }

    /// <summary>
    /// 执行当前中间件逻辑。
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        // 这里注册响应开始前的回调，保证写头时机正确。
        context.Response.OnStarting(() =>
        {
            // 这里判断当前请求是否属于 API 路径。
            var isApiRequest = context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase);

            // 这里判断响应内容类型是否为 HTML。
            var isHtmlResponse = string.Equals(context.Response.ContentType, "text/html", StringComparison.OrdinalIgnoreCase) || (context.Response.ContentType?.Contains("text/html", StringComparison.OrdinalIgnoreCase) ?? false);

            // 这里仅对 API 和 HTML 响应写入禁用缓存头。
            if (isApiRequest || isHtmlResponse)
            {
                // 这里禁止浏览器缓存响应主体。
                context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate, max-age=0";

                // 这里兼容旧式缓存控制头。
                context.Response.Headers.Pragma = "no-cache";

                // 这里显式指定过期时间为 0。
                context.Response.Headers.Expires = "0";
            }

            // 这里结束响应开始前回调。
            return Task.CompletedTask;
        });

        // 这里继续执行后续中间件。
        await _next(context);
    }
}
