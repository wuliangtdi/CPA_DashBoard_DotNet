using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CPA_DashBoard.Web.Helpers;
using CPA_DashBoard.Web.Models;

namespace CPA_DashBoard.Web.Services;

/// <summary>
/// 负责迁移 Python 版本中的配额查询、静态模型展示与 Token 校验逻辑。
/// </summary>
public sealed class QuotaService
{
    /// <summary>
    /// 保存 Codex OAuth Token 接口地址。
    /// </summary>
    private const string CodexTokenUrl = "https://auth.openai.com/oauth/token";

    /// <summary>
    /// 保存 Codex Models 接口地址。
    /// </summary>
    private const string CodexModelsUrl = "https://chatgpt.com/backend-api/codex/models";

    /// <summary>
    /// 保存 Codex 客户端标识。
    /// </summary>
    private const string CodexClientId = "app_EMoamEEZ73f0CkXaXp7hrann";

    /// <summary>
    /// 保存 Codex 客户端版本号。
    /// </summary>
    private const string CodexClientVersion = "0.101.0";

    /// <summary>
    /// 保存 Claude OAuth Token 接口地址。
    /// </summary>
    private const string ClaudeTokenUrl = "https://console.anthropic.com/v1/oauth/token";

    /// <summary>
    /// 保存 Claude 客户端标识。
    /// </summary>
    private const string ClaudeClientId = "9d1c250a-e61b-44d9-88ed-5944d1962f5e";

    /// <summary>
    /// 保存 Qwen OAuth Token 接口地址。
    /// </summary>
    private const string QwenTokenUrl = "https://chat.qwen.ai/api/v1/oauth2/token";

    /// <summary>
    /// 保存 Qwen 客户端标识。
    /// </summary>
    private const string QwenClientId = "f0304373b74a44d2b584a3fb70ca9e56";

    /// <summary>
    /// 保存 iFlow OAuth Token 接口地址。
    /// </summary>
    private const string IflowTokenUrl = "https://iflow.cn/oauth/token";

    /// <summary>
    /// 保存 iFlow 客户端标识。
    /// </summary>
    private readonly string? _iflowClientId;

    /// <summary>
    /// 保存 iFlow 客户端密钥。
    /// </summary>
    private readonly string? _iflowClientSecret;

    /// <summary>
    /// 保存 Gemini CLI 客户端标识。
    /// </summary>
    private readonly string? _geminiCliClientId;

    /// <summary>
    /// 保存 Gemini CLI 客户端密钥。
    /// </summary>
    private readonly string? _geminiCliClientSecret;

    /// <summary>
    /// 保存实时配额接口回退项目 ID。
    /// </summary>
    private const string DefaultCloudProjectId = "bamboo-precept-lgxtn";

    /// <summary>
    /// 保存应用上下文服务实例。
    /// </summary>
    private readonly AppContextService _appContextService;

    /// <summary>
    /// 保存日志实例。
    /// </summary>
    private readonly ILogger<QuotaService> _logger;

    /// <summary>
    /// 保存静态元数据文档。
    /// </summary>
    private readonly QuotaMetadataDocument _metadata;

    /// <summary>
    /// 保存支持实时配额的 Provider 集合。
    /// </summary>
    private readonly HashSet<string> _supportedQuotaProviders;

    /// <summary>
    /// 保存静态模型 Provider 集合。
    /// </summary>
    private readonly HashSet<string> _staticModelsProviders;

    /// <summary>
    /// 保存 Token 校验 Provider 集合。
    /// </summary>
    private readonly HashSet<string> _tokenValidationProviders;

    /// <summary>
    /// 保存无需 Token 校验的 Provider 集合。
    /// </summary>
    private readonly HashSet<string> _noTokenValidationProviders;

    /// <summary>
    /// 使用应用上下文、配置和日志服务初始化配额服务。
    /// </summary>
    public QuotaService(AppContextService appContextService, IConfiguration configuration, ILogger<QuotaService> logger)
    {
        _appContextService = appContextService;
        _logger = logger;
        _iflowClientId = GetConfiguredValue(configuration, "OAuthClients:Iflow:ClientId", "IFLOW_CLIENT_ID");
        _iflowClientSecret = GetConfiguredValue(configuration, "OAuthClients:Iflow:ClientSecret", "IFLOW_CLIENT_SECRET");
        _geminiCliClientId = GetConfiguredValue(configuration, "OAuthClients:GeminiCli:ClientId", "GEMINI_CLI_CLIENT_ID");
        _geminiCliClientSecret = GetConfiguredValue(configuration, "OAuthClients:GeminiCli:ClientSecret", "GEMINI_CLI_CLIENT_SECRET");

        var metadataPath = Path.Combine(Path.GetDirectoryName(_appContextService.Settings.QuotaCacheFilePath) ?? AppContext.BaseDirectory, "Data", "QuotaMetadata.json");
        var metadataText = File.ReadAllText(metadataPath);
        _metadata = JsonSerializer.Deserialize<QuotaMetadataDocument>(metadataText) ?? new QuotaMetadataDocument();
        _supportedQuotaProviders = new HashSet<string>(_metadata.SupportedQuotaProviders, StringComparer.OrdinalIgnoreCase);
        _staticModelsProviders = new HashSet<string>(_metadata.StaticModelsProviders, StringComparer.OrdinalIgnoreCase);
        _tokenValidationProviders = new HashSet<string>(_metadata.TokenValidationProviders, StringComparer.OrdinalIgnoreCase);
        _noTokenValidationProviders = new HashSet<string>(_metadata.NoTokenValidationProviders, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 从应用配置或环境变量中读取 OAuth 客户端配置。
    /// </summary>
    private static string? GetConfiguredValue(IConfiguration configuration, string configurationKey, string environmentKey)
    {
        var value = configuration[configurationKey];
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        value = configuration[environmentKey];
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>
    /// 判断指定 Provider 是否受当前系统支持。
    /// </summary>
    public bool IsSupportedProvider(string provider)
    {
        return _supportedQuotaProviders.Contains(provider) || _staticModelsProviders.Contains(provider);
    }

    /// <summary>
    /// 判断指定 Provider 是否属于静态模型类账号。
    /// </summary>
    public bool IsStaticProvider(string provider)
    {
        return _staticModelsProviders.Contains(provider);
    }

    /// <summary>
    /// 获取指定账号的配额数据。
    /// </summary>
    public async Task<JsonObject> GetQuotaForAccountAsync(JsonObject authData, CancellationToken cancellationToken = default)
    {
        // 这里先从认证文件读取 provider 类型，并统一转成小写。
        var provider = authData.GetString("type").ToLowerInvariant();

        // 这里先判断 provider 是否支持实时配额查询。
        if (!_supportedQuotaProviders.Contains(provider))
        {
            // 这里对静态模型 provider 先尝试走静态列表分支。
            var staticResult = await GetStaticModelsForProviderAsync(provider, authData, cancellationToken);

            if (staticResult is not null)
            {
                return staticResult;
            }

            return CreateQuotaErrorResult(provider, "配额查询暂不支持该类型账号。", "error");
        }

        // 这里提取 access_token、refresh_token 和 project_id。
        var (accessToken, refreshToken, projectId) = ExtractTokensFromAuthData(authData, provider);

        // 这里在 access_token 和 refresh_token 都缺失时直接返回 missing。
        if (string.IsNullOrWhiteSpace(accessToken) && string.IsNullOrWhiteSpace(refreshToken))
        {
            return CreateQuotaErrorResult(provider, "缺少 access_token 和 refresh_token。", "missing");
        }

        // 这里记录 Token 是否发生刷新，供最终写入 token_status。
        var tokenRefreshed = false;
        var originalAccessToken = accessToken;

        // 这里在存在 refresh_token 时先尝试刷新 access_token。
        if (!string.IsNullOrWhiteSpace(refreshToken))
        {
            var refreshedToken = await RefreshAccessTokenAsync(refreshToken, provider, cancellationToken);

            if (!string.IsNullOrWhiteSpace(refreshedToken?.GetString("access_token")))
            {
                accessToken = refreshedToken!.GetString("access_token");
                tokenRefreshed = !string.Equals(originalAccessToken, accessToken, StringComparison.Ordinal);
            }
        }

        // 这里拿不到有效 access_token 时立即结束，避免无效请求。
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return CreateQuotaErrorResult(provider, "无法获取有效的 access_token。", "refresh_failed");
        }

        // 这里使用当前 access_token 实际拉取实时配额。
        var (quota, success) = await FetchQuotaWithTokenAsync(accessToken, projectId, provider, cancellationToken);

        // 这里在首次查询失败后，再用 refresh_token 做一次刷新后重试。
        if (!success && !string.IsNullOrWhiteSpace(refreshToken))
        {
            var refreshedToken = await RefreshAccessTokenAsync(refreshToken, provider, cancellationToken);

            if (!string.IsNullOrWhiteSpace(refreshedToken?.GetString("access_token")))
            {
                var retryToken = refreshedToken!.GetString("access_token");
                var retryResult = await FetchQuotaWithTokenAsync(retryToken, projectId, provider, cancellationToken);
                quota = retryResult.Quota;
                success = retryResult.Success;

                if (success)
                {
                    tokenRefreshed = true;
                }
            }
        }

        // 这里根据刷新结果和查询结果统一设置 token_status。
        if (tokenRefreshed)
        {
            quota["token_status"] = "refreshed";
            quota["token_refreshed"] = true;
        }
        else if (success)
        {
            quota["token_status"] = "valid";
        }
        else
        {
            quota["token_status"] = "error";
        }

        // 这里返回最终配额结果。
        return quota;
    }

    /// <summary>
    /// 获取静态模型类账号的模型列表，并按需校验 Token。
    /// </summary>
    public async Task<JsonObject?> GetStaticModelsForProviderAsync(string provider, JsonObject? authData, CancellationToken cancellationToken = default)
    {
        // 这里先判断当前 provider 是否存在预置静态模型定义。
        if (!_metadata.StaticModelLists.TryGetValue(provider, out var modelDefinitions))
        {
            return null;
        }

        // 这里创建静态模型列表容器。
        var models = new JsonArray();

        // 这里把静态元数据转换成前端需要的模型结构。
        foreach (var modelDefinition in modelDefinitions)
        {
            models.Add(new JsonObject
            {
                ["name"] = modelDefinition.Name,
                ["display_name"] = modelDefinition.DisplayName,
                ["description"] = modelDefinition.Description,
            });
        }

        // 这里组装静态模型 provider 的统一返回结构。
        var result = new JsonObject
        {
            ["models"] = models,
            ["last_updated"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ["is_forbidden"] = false,
            ["subscription_tier"] = null,
            ["static_list"] = true,
            ["note"] = "静态模型列表",
        };

        // 这里在有认证数据时顺手补一次 Token 状态校验。
        if (authData is not null)
        {
            var tokenValidation = await ValidateTokenForProviderAsync(authData, provider, cancellationToken);
            result["token_status"] = tokenValidation.Status;
        }

        // 这里返回静态模型结果。
        return result;
    }

    /// <summary>
    /// 刷新 access_token，目前与 Python 版本保持一致，仅用于 Antigravity 实时配额查询。
    /// </summary>
    public async Task<JsonObject?> RefreshAccessTokenAsync(string refreshToken, string provider, CancellationToken cancellationToken = default)
    {
        // 这里仅对 Antigravity 开放 access_token 刷新逻辑。
        if (!string.Equals(provider, "antigravity", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // 这里缺少客户端凭据时无法刷新 access_token。
        if (string.IsNullOrWhiteSpace(_appContextService.Settings.AntigravityClientId) || string.IsNullOrWhiteSpace(_appContextService.Settings.AntigravityClientSecret))
        {
            return null;
        }

        // 这里调用 Google Token 接口刷新 Antigravity 的 access_token。
        using var client = CreateProxyAwareClient();
        using var response = await client.PostAsync(
            _appContextService.Settings.GoogleTokenUrl,
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = _appContextService.Settings.AntigravityClientId,
                ["client_secret"] = _appContextService.Settings.AntigravityClientSecret,
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
            }),
            cancellationToken);

        // 这里刷新失败时返回 null，让调用方决定是否继续。
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        // 这里把刷新响应解析成 JsonObject 返回。
        return JsonHelpers.ParseObject(await response.Content.ReadAsStringAsync(cancellationToken));
    }

    /// <summary>
    /// 校验指定 Provider 的 Token 是否有效。
    /// </summary>
    public async Task<(bool IsValid, string Status)> ValidateTokenForProviderAsync(JsonObject authData, string provider, CancellationToken cancellationToken = default)
    {
        // 这里统一归一化 provider 名称，避免大小写分支重复。
        var normalizedProvider = provider.ToLowerInvariant();

        // 这里对无需校验或未实现校验的 provider 直接返回 not_applicable。
        if (_noTokenValidationProviders.Contains(normalizedProvider) || !_tokenValidationProviders.Contains(normalizedProvider))
        {
            return (true, "not_applicable");
        }

        // 这里准备读取后续校验所需的 refresh_token。
        string? refreshToken;

        // 这里兼容 Gemini 把 refresh_token 放在 token 子对象中的结构。
        if (string.Equals(normalizedProvider, "gemini", StringComparison.OrdinalIgnoreCase))
        {
            refreshToken = authData["token"] is JsonObject tokenObject ? tokenObject.GetString("refresh_token") : string.Empty;
        }
        else
        {
            refreshToken = authData.GetString("refresh_token");
        }

        // 这里兼容 Codex 只有 access_token 没有 refresh_token 的认证场景。
        if (string.Equals(normalizedProvider, "codex", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(refreshToken))
        {
            var accessToken = authData.GetString("access_token");

            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return (false, "missing");
            }

            return IsCodexAccessTokenExpired(authData) ? (false, "expired") : (true, "valid");
        }

        // 这里对必须依赖 refresh_token 的 provider 做缺失校验。
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return (false, "missing");
        }

        // 这里按 provider 分发到对应的 Token 校验实现。
        return normalizedProvider switch
        {
            "gemini" => await ValidateGeminiTokenAsync(refreshToken, cancellationToken),
            "codex" => await ValidateCodexAccountAsync(authData, cancellationToken),
            "claude" => await ValidateClaudeTokenAsync(refreshToken, cancellationToken),
            "qwen" => await ValidateQwenTokenAsync(refreshToken, cancellationToken),
            "iflow" => await ValidateIflowTokenAsync(refreshToken, cancellationToken),
            _ => (true, "not_applicable"),
        };
    }

    /// <summary>
    /// 调用 Cloud Code 接口获取项目 ID 与订阅等级。
    /// </summary>
    public async Task<(string? ProjectId, string? SubscriptionTier)> FetchProjectAndTierAsync(string accessToken, string provider, CancellationToken cancellationToken = default)
    {
        // 这里创建客户端并准备调用 loadCodeAssist 接口。
        using var client = CreateProxyAwareClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_appContextService.Settings.CloudCodeApiUrl}/v1internal:loadCodeAssist");

        // 这里根据 provider 区分 IDE 类型，保持与 Python 版请求体一致。
        request.Content = JsonContent.Create(new JsonObject { ["metadata"] = string.Equals(provider, "gemini", StringComparison.OrdinalIgnoreCase) ? new JsonObject { ["ideType"] = "IDE_UNSPECIFIED" } : new JsonObject { ["ideType"] = "ANTIGRAVITY" } });

        // 这里补充 Cloud Code 所需的认证头和客户端标识。
        foreach (var header in CreateCloudCodeHeaders(accessToken, provider))
        {
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        // 这里发送 loadCodeAssist 请求。
        using var response = await client.SendAsync(request, cancellationToken);

        // 这里在接口失败时返回空结果，让上层继续使用默认值。
        if (!response.IsSuccessStatusCode)
        {
            return (null, null);
        }

        // 这里解析 Cloud Code 返回的项目和订阅等级信息。
        var responseObject = JsonHelpers.ParseObject(await response.Content.ReadAsStringAsync(cancellationToken));
        var projectId = responseObject.GetString("cloudaicompanionProject");
        var paidTierId = responseObject["paidTier"] is JsonObject paidTier ? paidTier.GetString("id") : string.Empty;
        var currentTierId = responseObject["currentTier"] is JsonObject currentTier ? currentTier.GetString("id") : string.Empty;

        // 这里优先取付费层级，没有时退回当前层级。
        var subscriptionTier = !string.IsNullOrWhiteSpace(paidTierId) ? paidTierId : currentTierId;
        return (string.IsNullOrWhiteSpace(projectId) ? null : projectId, string.IsNullOrWhiteSpace(subscriptionTier) ? null : subscriptionTier);
    }

    /// <summary>
    /// 使用 access_token 查询实时配额。
    /// </summary>
    private async Task<(JsonObject Quota, bool Success)> FetchQuotaWithTokenAsync(string accessToken, string? projectId, string provider, CancellationToken cancellationToken)
    {
        // 这里先初始化一个带默认字段的配额结果对象。
        var result = new JsonObject
        {
            ["models"] = new JsonArray(),
            ["last_updated"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ["is_forbidden"] = false,
            ["subscription_tier"] = null,
        };

        // 这里先补查项目 ID 和订阅等级，供后续模型接口共用。
        var (fetchedProjectId, subscriptionTier) = await FetchProjectAndTierAsync(accessToken, provider, cancellationToken);
        result["subscription_tier"] = subscriptionTier;

        // 这里确定最终 project_id：优先使用认证文件中的值，再退回接口返回值和默认值。
        var finalProjectId = string.IsNullOrWhiteSpace(projectId) ? (string.IsNullOrWhiteSpace(fetchedProjectId) ? DefaultCloudProjectId : fetchedProjectId) : projectId;

        // 这里创建实时模型查询请求。
        using var client = CreateProxyAwareClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_appContextService.Settings.CloudCodeApiUrl}/v1internal:fetchAvailableModels");
        request.Content = JsonContent.Create(new JsonObject { ["project"] = finalProjectId });

        // 这里附加 Cloud Code 请求头，保证 provider 身份与客户端元数据正确。
        foreach (var header in CreateCloudCodeHeaders(accessToken, provider))
        {
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        // 这里发送 fetchAvailableModels 请求。
        using var response = await client.SendAsync(request, cancellationToken);

        // 这里把 403 单独标记成权限受限，但仍视为成功返回。
        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            result["is_forbidden"] = true;
            return (result, true);
        }

        // 这里把 401 视作 access_token 不可用，通知上层尝试刷新后重试。
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            return (result, false);
        }

        // 这里把其他非成功状态统一视作失败。
        if (!response.IsSuccessStatusCode)
        {
            return (result, false);
        }

        // 这里解析 fetchAvailableModels 接口响应体。
        var responseObject = JsonHelpers.ParseObject(await response.Content.ReadAsStringAsync(cancellationToken));

        // 这里在接口未返回 models 对象时，直接返回空模型列表。
        if (responseObject["models"] is not JsonObject modelsObject)
        {
            return (result, true);
        }

        // 这里取出结果对象中的 models 数组，准备填充前端模型项。
        var models = result["models"]!.AsArray();

        // 这里逐个展开 Cloud Code 返回的模型列表。
        foreach (var modelEntry in modelsObject)
        {
            var modelName = modelEntry.Key;

            // 这里仅保留前端面板需要展示的 Gemini 和 Claude 模型。
            if (!modelName.Contains("gemini", StringComparison.OrdinalIgnoreCase) && !modelName.Contains("claude", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // 这里把 Antigravity 原始模型名映射成前端展示别名。
            var aliasName = MapAntigravityModelName(modelName);

            if (aliasName is null)
            {
                continue;
            }

            // 这里确保当前模型节点确实是对象结构。
            if (modelEntry.Value is not JsonObject modelObject)
            {
                continue;
            }

            // 这里提取 quotaInfo 中的剩余比例和重置时间。
            var quotaInfo = modelObject["quotaInfo"] as JsonObject;
            var remainingFraction = quotaInfo?.GetDouble("remainingFraction") ?? 0;
            var resetTime = quotaInfo?.GetString("resetTime") ?? string.Empty;
            models!.Add(new JsonObject
            {
                ["name"] = aliasName,
                ["original_name"] = modelName,
                ["percentage"] = (int)(remainingFraction * 100),
                ["reset_time"] = string.IsNullOrWhiteSpace(resetTime) ? null : resetTime,
            });
        }

        // 这里按模型名排序，保证前端展示顺序稳定。
        var sortedModels = models!.Select(node => node).OrderBy(node => node?["name"]?.GetValue<string?>(), StringComparer.OrdinalIgnoreCase).ToList();
        result["models"] = new JsonArray(sortedModels.ToArray());

        // 这里返回实时配额查询结果。
        return (result, true);
    }

    /// <summary>
    /// 校验 Gemini Token 是否仍可刷新。
    /// </summary>
    private async Task<(bool IsValid, string Status)> ValidateGeminiTokenAsync(string refreshToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_geminiCliClientId) || string.IsNullOrWhiteSpace(_geminiCliClientSecret))
        {
            _logger.LogWarning("Gemini CLI OAuth client configuration is missing.");
            return (false, "missing_client_config");
        }

        using var client = CreateProxyAwareClient();
        using var response = await client.PostAsync(
            _appContextService.Settings.GoogleTokenUrl,
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = _geminiCliClientId,
                ["client_secret"] = _geminiCliClientSecret,
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
            }),
            cancellationToken);

        return response.IsSuccessStatusCode ? (true, "refreshed") : (false, "refresh_failed");
    }

    /// <summary>
    /// 校验 Claude Token 是否仍可刷新。
    /// </summary>
    private async Task<(bool IsValid, string Status)> ValidateClaudeTokenAsync(string refreshToken, CancellationToken cancellationToken)
    {
        using var client = CreateProxyAwareClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, ClaudeTokenUrl);
        request.Content = JsonContent.Create(new JsonObject
        {
            ["client_id"] = ClaudeClientId,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
        });
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await client.SendAsync(request, cancellationToken);
        return response.IsSuccessStatusCode ? (true, "refreshed") : (false, "refresh_failed");
    }

    /// <summary>
    /// 校验 Qwen Token 是否仍可刷新。
    /// </summary>
    private async Task<(bool IsValid, string Status)> ValidateQwenTokenAsync(string refreshToken, CancellationToken cancellationToken)
    {
        using var client = CreateProxyAwareClient();
        using var response = await client.PostAsync(
            QwenTokenUrl,
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = QwenClientId,
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
            }),
            cancellationToken);

        return response.IsSuccessStatusCode ? (true, "refreshed") : (false, "refresh_failed");
    }

    /// <summary>
    /// 校验 iFlow Token 是否仍可刷新。
    /// </summary>
    private async Task<(bool IsValid, string Status)> ValidateIflowTokenAsync(string refreshToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_iflowClientId) || string.IsNullOrWhiteSpace(_iflowClientSecret))
        {
            _logger.LogWarning("iFlow OAuth client configuration is missing.");
            return (false, "missing_client_config");
        }

        using var client = CreateProxyAwareClient();
        using var response = await client.PostAsync(
            IflowTokenUrl,
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["client_id"] = _iflowClientId,
                ["client_secret"] = _iflowClientSecret,
            }),
            cancellationToken);

        return response.IsSuccessStatusCode ? (true, "refreshed") : (false, "refresh_failed");
    }

    /// <summary>
    /// 对 Codex 账号做“刷新 + Models API”双重校验。
    /// </summary>
    private async Task<(bool IsValid, string Status)> ValidateCodexAccountAsync(JsonObject authData, CancellationToken cancellationToken)
    {
        // 这里先执行 Codex 刷新流程，拿到用于校验的 access_token。
        var (accessToken, success, status) = await RefreshCodexAndGetAccessTokenAsync(authData, cancellationToken);

        // 这里刷新失败或没有 access_token 时直接判定校验失败。
        if (!success || string.IsNullOrWhiteSpace(accessToken))
        {
            return (false, status);
        }

        // 这里读取账号 ID，并继续用 Models API 做可用性确认。
        var accountId = authData.GetString("account_id");
        var apiCheck = await CheckCodexModelsApiAsync(accessToken, accountId, cancellationToken);

        // 这里把刷新结果与 Models API 校验结果合并成最终状态。
        return apiCheck.IsValid ? (true, string.IsNullOrWhiteSpace(apiCheck.Status) ? status : apiCheck.Status) : (false, "invalid");
    }

    /// <summary>
    /// 通过 refresh_token 刷新 Codex 的 access_token。
    /// </summary>
    private async Task<(string? AccessToken, bool Success, string Status)> RefreshCodexAndGetAccessTokenAsync(JsonObject authData, CancellationToken cancellationToken)
    {
        // 这里先读取 Codex 的 refresh_token。
        var refreshToken = authData.GetString("refresh_token");

        // 这里兼容只有 access_token 没有 refresh_token 的旧认证文件。
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            var existingAccessToken = authData.GetString("access_token");
            return string.IsNullOrWhiteSpace(existingAccessToken) ? (null, false, "missing") : (existingAccessToken, true, "valid");
        }

        // 这里调用 Codex Token 接口，用 refresh_token 换取新的 access_token。
        using var client = CreateProxyAwareClient();
        using var response = await client.PostAsync(
            CodexTokenUrl,
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = CodexClientId,
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["scope"] = "openid profile email",
            }),
            cancellationToken);

        // 这里在刷新接口失败时直接返回 refresh_failed。
        if (!response.IsSuccessStatusCode)
        {
            return (null, false, "refresh_failed");
        }

        // 这里解析刷新响应里的新 access_token。
        var responseObject = JsonHelpers.ParseObject(await response.Content.ReadAsStringAsync(cancellationToken));
        var accessToken = responseObject.GetString("access_token");

        // 这里返回 Codex 刷新结果。
        return string.IsNullOrWhiteSpace(accessToken) ? (null, false, "refresh_failed") : (accessToken, true, "refreshed");
    }

    /// <summary>
    /// 用 Codex Models API 二次确认 Token 是否可用。
    /// </summary>
    private async Task<(bool IsValid, string Status)> CheckCodexModelsApiAsync(string accessToken, string accountId, CancellationToken cancellationToken)
    {
        // 这里创建客户端并准备访问 Codex Models API。
        using var client = CreateProxyAwareClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{CodexModelsUrl}?client_version={CodexClientVersion}");

        // 这里补齐 Codex CLI 常用请求头，尽量模拟官方客户端行为。
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {accessToken}");
        request.Headers.TryAddWithoutValidation("Content-Type", "application/json");
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        request.Headers.TryAddWithoutValidation("Version", CodexClientVersion);
        request.Headers.TryAddWithoutValidation("Session_id", Guid.NewGuid().ToString());
        request.Headers.TryAddWithoutValidation("Originator", "codex_cli_rs");
        request.Headers.TryAddWithoutValidation("User-Agent", $"codex_cli_rs/{CodexClientVersion}");
        request.Headers.TryAddWithoutValidation("Connection", "Keep-Alive");

        // 这里在存在账号 ID 时补充到账户级请求头中。
        if (!string.IsNullOrWhiteSpace(accountId))
        {
            request.Headers.TryAddWithoutValidation("Chatgpt-Account-Id", accountId);
        }

        // 这里真正发起 Models API 请求做二次校验。
        try
        {
            using var response = await client.SendAsync(request, cancellationToken);

            // 这里把 200 明确视为 Token 可用。
            if (response.StatusCode == HttpStatusCode.OK)
            {
                return (true, "refreshed");
            }

            // 这里把 401 明确识别成 Token 无效。
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return (false, "invalid");
            }

            // 这里对除 401 外的状态保持宽松，避免偶发接口异常误伤账号状态。
            return (true, "refreshed");
        }
        // 这里在网络异常时也保持宽松处理，避免把瞬时故障误判成账号失效。
        catch
        {
            return (true, "refreshed");
        }
    }

    /// <summary>
    /// 判断 Codex access_token 是否已过期。
    /// </summary>
    private static bool IsCodexAccessTokenExpired(JsonObject authData)
    {
        // 这里统一取当前 UTC 时间戳，后续所有过期判断都基于它。
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var expiredText = authData.GetString("expired");

        // 这里兼容不同来源文件对过期字段命名不一致的情况。
        if (string.IsNullOrWhiteSpace(expiredText))
        {
            expiredText = authData.GetString("expire");
        }

        // 这里优先解析字符串格式的过期时间字段。
        if (!string.IsNullOrWhiteSpace(expiredText) && DateTimeOffset.TryParse(expiredText.Replace("Z", "+00:00", StringComparison.OrdinalIgnoreCase), out var expiredTime))
        {
            return expiredTime.ToUnixTimeSeconds() <= now;
        }

        // 这里兼容部分 auth 文件把过期时间写成数字时间戳。
        var expValue = authData["exp"];

        if (expValue is not null && long.TryParse(expValue.ToString(), out var expUnixTime))
        {
            return expUnixTime <= now;
        }

        // 这里显式过期字段缺失时，再尝试从 access_token 中反推。
        var accessToken = authData.GetString("access_token");

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return false;
        }

        // 这里按 JWT 三段式拆分 access_token。
        var parts = accessToken.Split('.');

        if (parts.Length < 2)
        {
            return false;
        }

        // 这里解析 JWT 载荷，读取 exp 字段。
        try
        {
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            var bytes = Convert.FromBase64String(payload);
            var payloadObject = JsonHelpers.ParseObject(Encoding.UTF8.GetString(bytes));
            var jwtExp = payloadObject.GetInt64("exp");

            // 这里用 exp 与当前时间比较 Token 是否过期。
            return jwtExp > 0 && jwtExp <= now;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 提取 auth_data 中的访问令牌、刷新令牌和项目 ID。
    /// </summary>
    private static (string? AccessToken, string? RefreshToken, string? ProjectId) ExtractTokensFromAuthData(JsonObject authData, string provider)
    {
        // 这里先读取 project_id，后续实时配额查询会优先使用它。
        var projectId = authData.GetString("project_id");

        // 这里兼容 Gemini 把 token 嵌套在 token 节点中的存储结构。
        if (string.Equals(provider, "gemini", StringComparison.OrdinalIgnoreCase) && authData["token"] is JsonObject tokenObject)
        {
            return (tokenObject.GetString("access_token"), tokenObject.GetString("refresh_token"), projectId);
        }

        // 这里返回默认结构下的 access_token、refresh_token 和 project_id。
        return (authData.GetString("access_token"), authData.GetString("refresh_token"), projectId);
    }

    /// <summary>
    /// 生成 Cloud Code 调用所需的请求头。
    /// </summary>
    private Dictionary<string, string> CreateCloudCodeHeaders(string accessToken, string provider)
    {
        // 这里为 Gemini 单独生成更贴近官方客户端的请求头。
        if (string.Equals(provider, "gemini", StringComparison.OrdinalIgnoreCase))
        {
            return new Dictionary<string, string>
            {
                ["Authorization"] = $"Bearer {accessToken}",
                ["Content-Type"] = "application/json",
                ["User-Agent"] = _appContextService.Settings.GeminiCliUserAgent,
                ["X-Goog-Api-Client"] = "gl-node/22.17.0",
                ["Client-Metadata"] = "ideType=IDE_UNSPECIFIED,platform=PLATFORM_UNSPECIFIED,pluginType=GEMINI",
            };
        }

        // 这里为其他 Cloud Code provider 生成通用请求头。
        return new Dictionary<string, string>
        {
            ["Authorization"] = $"Bearer {accessToken}",
            ["Content-Type"] = "application/json",
            ["User-Agent"] = _appContextService.Settings.AntigravityUserAgent,
        };
    }

    /// <summary>
    /// 将 Antigravity 原始模型名映射成前端展示使用的别名。
    /// </summary>
    private string? MapAntigravityModelName(string modelName)
    {
        // 这里先过滤掉前端不希望展示的模型。
        if (_metadata.AntigravitySkipModels.Contains(modelName, StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        // 这里优先返回别名，没有映射时保留原始模型名。
        return _metadata.AntigravityModelNameToAlias.TryGetValue(modelName, out var aliasName) ? aliasName : modelName;
    }

    /// <summary>
    /// 创建配额错误结果对象。
    /// </summary>
    private static JsonObject CreateQuotaErrorResult(string provider, string errorMessage, string tokenStatus)
    {
        // 这里统一构造配额查询失败时的返回结构。
        return new JsonObject
        {
            ["models"] = new JsonArray(),
            ["last_updated"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ["is_forbidden"] = false,
            ["subscription_tier"] = null,
            ["token_status"] = tokenStatus,
            ["error"] = string.IsNullOrWhiteSpace(provider) ? errorMessage : $"{errorMessage}",
        };
    }

    /// <summary>
    /// 创建走代理的 HttpClient。
    /// </summary>
    private HttpClient CreateProxyAwareClient()
    {
        // 这里在未配置代理时直接使用默认客户端。
        if (string.IsNullOrWhiteSpace(_appContextService.Settings.ProxyUrl))
        {
            return new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        }

        // 这里在配置了代理地址时显式创建带代理的处理器。
        var handler = new HttpClientHandler
        {
            Proxy = new WebProxy(_appContextService.Settings.ProxyUrl),
            UseProxy = true,
        };

        // 这里根据是否配置代理返回最终的 HttpClient 实例。
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
    }

}
