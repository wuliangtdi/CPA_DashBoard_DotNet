using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace CPA_DashBoard.Web.Models;

/// <summary>
/// 保存从 Python 版本提取出的配额元数据。
/// </summary>
public sealed class QuotaMetadataDocument
{
    /// <summary>
    /// 保存各个 Provider 的静态模型列表。
    /// </summary>
    [JsonPropertyName("STATIC_MODEL_LISTS")]
    public Dictionary<string, List<StaticModelDefinition>> StaticModelLists { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 保存 Antigravity 返回模型名到展示名的映射关系。
    /// </summary>
    [JsonPropertyName("ANTIGRAVITY_MODEL_NAME_TO_ALIAS")]
    public Dictionary<string, string> AntigravityModelNameToAlias { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 保存需要跳过的 Antigravity 模型名集合。
    /// </summary>
    [JsonPropertyName("ANTIGRAVITY_SKIP_MODELS")]
    public List<string> AntigravitySkipModels { get; set; } = [];

    /// <summary>
    /// 保存支持实时配额查询的 Provider 列表。
    /// </summary>
    [JsonPropertyName("SUPPORTED_QUOTA_PROVIDERS")]
    public List<string> SupportedQuotaProviders { get; set; } = [];

    /// <summary>
    /// 保存支持静态模型展示的 Provider 列表。
    /// </summary>
    [JsonPropertyName("STATIC_MODELS_PROVIDERS")]
    public List<string> StaticModelsProviders { get; set; } = [];

    /// <summary>
    /// 保存支持 Token 校验的 Provider 列表。
    /// </summary>
    [JsonPropertyName("TOKEN_VALIDATION_PROVIDERS")]
    public List<string> TokenValidationProviders { get; set; } = [];

    /// <summary>
    /// 保存不支持 Token 校验的 Provider 列表。
    /// </summary>
    [JsonPropertyName("NO_TOKEN_VALIDATION_PROVIDERS")]
    public List<string> NoTokenValidationProviders { get; set; } = [];
}

/// <summary>
/// 表示单个静态模型定义。
/// </summary>
public sealed class StaticModelDefinition
{
    /// <summary>
    /// 保存模型内部名称。
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 保存模型显示名称。
    /// </summary>
    [JsonPropertyName("display_name")]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// 保存模型描述信息。
    /// </summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// 保存解析后的应用配置。
/// </summary>
public sealed record ResolvedAppSettings(
    string? ConfigPath,
    string ManagementApiUrl,
    string ManagementApiKey,
    string AuthDir,
    string WebUiHost,
    int WebUiPort,
    bool WebUiDebug,
    string ServiceDir,
    string BinaryName,
    string LogFile,
    string ProxyUrl,
    string CloudCodeApiUrl,
    string AntigravityUserAgent,
    string GeminiCliUserAgent,
    string GoogleTokenUrl,
    string AntigravityClientId,
    string AntigravityClientSecret,
    string ApiHost,
    int ApiPort,
    int QuotaRefreshConcurrency,
    IReadOnlyList<string> ApiKeys,
    string QuotaCacheFilePath);

/// <summary>
/// 表示读取到的认证文件信息。
/// </summary>
public sealed class AuthFileDescriptor
{
    /// <summary>
    /// 保存账号唯一标识。
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// 保存认证文件名。
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// 保存邮箱地址。
    /// </summary>
    public string Email { get; init; } = string.Empty;

    /// <summary>
    /// 保存账号类型。
    /// </summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>
    /// 保存 Provider 名称。
    /// </summary>
    public string Provider { get; init; } = string.Empty;

    /// <summary>
    /// 保存账号状态。
    /// </summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>
    /// 保存状态说明。
    /// </summary>
    public string StatusMessage { get; init; } = string.Empty;

    /// <summary>
    /// 保存是否禁用。
    /// </summary>
    public bool Disabled { get; init; }

    /// <summary>
    /// 保存账号类别。
    /// </summary>
    public string AccountType { get; init; } = string.Empty;

    /// <summary>
    /// 保存账号名字段。
    /// </summary>
    public string Account { get; init; } = string.Empty;

    /// <summary>
    /// 保存创建时间原始值。
    /// </summary>
    public string CreatedAt { get; init; } = string.Empty;

    /// <summary>
    /// 保存最后修改时间戳。
    /// </summary>
    public double ModTime { get; init; }

    /// <summary>
    /// 保存最近刷新时间。
    /// </summary>
    public string LastRefresh { get; init; } = string.Empty;

    /// <summary>
    /// 保存是否仅在运行时存在。
    /// </summary>
    public bool RuntimeOnly { get; init; }

    /// <summary>
    /// 保存数据来源。
    /// </summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>
    /// 保存原始认证数据，供后端内部继续使用。
    /// </summary>
    public JsonObject? RawData { get; init; }
}

/// <summary>
/// 保存 OAuth Provider 的命令参数与回调端口。
/// </summary>
public sealed record OAuthProviderDefinition(string Flag, int Port);
