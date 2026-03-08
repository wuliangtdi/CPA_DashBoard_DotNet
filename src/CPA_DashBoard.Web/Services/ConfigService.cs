using System.Text.Json.Nodes;

namespace CPA_DashBoard.Web.Services;

/// <summary>
/// 负责返回前端初始化所需的运行配置。
/// </summary>
public sealed class ConfigService
{
    /// <summary>
    /// 保存应用上下文服务实例。
    /// </summary>
    private readonly AppContextService _appContextService;

    /// <summary>
    /// 使用应用上下文服务初始化配置服务。
    /// </summary>
    public ConfigService(AppContextService appContextService)
    {
        // 这里保存应用上下文服务，供后续统一读取运行配置。
        _appContextService = appContextService;
    }

    /// <summary>
    /// 获取前端初始化所需的配置对象。
    /// </summary>
    public JsonObject GetConfig()
    {
        // 这里先读取统一配置，后续所有返回字段都从这里派生。
        var settings = _appContextService.Settings;

        // 这里先构造前端固定会读取的基础字段。
        var result = new JsonObject
        {
            // 这里返回 Management API 地址，供前端展示当前后端来源。
            ["management_api_url"] = settings.ManagementApiUrl,

            // 这里告诉前端当前是否已经配置 Management API Key。
            ["has_api_key"] = !string.IsNullOrWhiteSpace(settings.ManagementApiKey),

            // 这里返回本地认证目录路径，供页面展示与排障使用。
            ["auth_dir"] = settings.AuthDir,

            // 这里根据是否存在 API Key 推导当前运行模式。
            ["mode"] = string.IsNullOrWhiteSpace(settings.ManagementApiKey) ? "local" : "api",

            // 这里返回批量刷新配额时的并发配置。
            ["quota_refresh_concurrency"] = settings.QuotaRefreshConcurrency,
        };

        // 这里只有在本地模式下才需要补充认证目录的探测信息。
        if (string.IsNullOrWhiteSpace(settings.ManagementApiKey))
        {
            // 这里把 AUTH_DIR 转成目录对象，便于后续统一判断存在性和统计文件。
            var authDirectory = new DirectoryInfo(settings.AuthDir);

            // 这里在目录存在时返回样本文件名和数量，帮助前端快速确认本地账号来源。
            if (authDirectory.Exists)
            {
                // 这里挑选少量样本文件名返回给前端，避免一次性传输太多列表内容。
                var sampleFiles = authDirectory
                    .EnumerateFiles()
                    .Where(file => string.Equals(file.Extension, ".json", StringComparison.OrdinalIgnoreCase))
                    .Select(file => file.Name)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .Take(10)
                    .ToArray();

                // 这里明确标记认证目录存在。
                result["auth_dir_exists"] = true;

                // 这里统计本地 JSON 认证文件总数。
                result["auth_file_count"] = authDirectory.EnumerateFiles().Count(file => string.Equals(file.Extension, ".json", StringComparison.OrdinalIgnoreCase));

                // 这里返回认证文件名样本，供前端面板展示。
                result["auth_file_sample"] = new JsonArray(sampleFiles.Select(file => (JsonNode?)file).ToArray());
            }
            else
            {
                // 这里明确告知前端认证目录不存在。
                result["auth_dir_exists"] = false;

                // 这里在目录缺失时把文件数归零，避免前端误判。
                result["auth_file_count"] = 0;
            }
        }

        // 这里返回最终配置对象。
        return result;
    }
}
