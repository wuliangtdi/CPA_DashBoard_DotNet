using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using CPA_DashBoard.Web.Helpers;
using CPA_DashBoard.Web.Models;

namespace CPA_DashBoard.Web.Services;

/// <summary>
/// 负责账户列表、删除、认证文件读取和配额刷新等业务。
/// </summary>
public sealed class AccountApiService
{
    /// <summary>
    /// 保存应用上下文服务实例。
    /// </summary>
    private readonly AppContextService _appContextService;

    /// <summary>
    /// 保存配额缓存存储实例。
    /// </summary>
    private readonly QuotaCacheStore _quotaCacheStore;

    /// <summary>
    /// 保存配额服务实例。
    /// </summary>
    private readonly QuotaService _quotaService;

    /// <summary>
    /// 保存日志实例。
    /// </summary>
    private readonly ILogger<AccountApiService> _logger;

    /// <summary>
    /// 使用所需依赖初始化账户服务。
    /// </summary>
    public AccountApiService(AppContextService appContextService, QuotaCacheStore quotaCacheStore, QuotaService quotaService, ILogger<AccountApiService> logger)
    {
        // 这里保存应用上下文服务，供后续读取配置和路径。
        _appContextService = appContextService;

        // 这里保存配额缓存服务，供账户列表和刷新结果复用。
        _quotaCacheStore = quotaCacheStore;

        // 这里保存配额查询服务，供单账号和批量刷新时调用。
        _quotaService = quotaService;

        // 这里保存日志实例，供异常回退时记录调试信息。
        _logger = logger;
    }

    /// <summary>
    /// 获取前端所需的配置对象。
    /// </summary>
    public JsonObject GetConfig()
    {
        // 这里先读取统一配置，后续所有返回字段都基于当前运行环境生成。
        var settings = _appContextService.Settings;

        // 这里组装前端初始化面板所需的基础配置字段。
        var result = new JsonObject
        {
            ["management_api_url"] = settings.ManagementApiUrl,
            ["has_api_key"] = !string.IsNullOrWhiteSpace(settings.ManagementApiKey),
            ["auth_dir"] = settings.AuthDir,
            ["mode"] = string.IsNullOrWhiteSpace(settings.ManagementApiKey) ? "local" : "api",
            ["quota_refresh_concurrency"] = settings.QuotaRefreshConcurrency,
        };

        // 这里在本地模式下额外返回认证目录状态，便于前端提示用户。
        if (string.IsNullOrWhiteSpace(settings.ManagementApiKey))
        {
            // 这里定位 AUTH_DIR，后续要统计文件数量与示例文件。
            var authDirectory = new DirectoryInfo(settings.AuthDir);

            // 这里先判断认证目录是否存在，避免枚举文件时报错。
            if (authDirectory.Exists)
            {
                // 这里只截取前 10 个 JSON 文件名作为示例，避免响应过大。
                var sampleFiles = authDirectory
                    .EnumerateFiles()
                    .Where(file => string.Equals(file.Extension, ".json", StringComparison.OrdinalIgnoreCase))
                    .Select(file => file.Name)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .Take(10)
                    .ToArray();

                // 这里把目录存在性、文件数量和样例文件一并返回给前端。
                result["auth_dir_exists"] = true;
                result["auth_file_count"] = authDirectory.EnumerateFiles().Count(file => string.Equals(file.Extension, ".json", StringComparison.OrdinalIgnoreCase));
                result["auth_file_sample"] = new JsonArray(sampleFiles.Select(file => (JsonNode?)file).ToArray());
            }
            else
            {
                // 这里在目录不存在时明确返回空状态。
                result["auth_dir_exists"] = false;
                result["auth_file_count"] = 0;
            }
        }

        // 这里返回最终配置对象。
        return result;
    }

    /// <summary>
    /// 获取账户列表，并合并缓存中的配额信息。
    /// </summary>
    public async Task<JsonObject> GetAccountsAsync(CancellationToken cancellationToken = default)
    {
        // 这里先拉取所有认证文件描述。
        var authFiles = await FetchAuthFilesAsync(cancellationToken);

        // 这里读取配额缓存快照，避免组装列表时反复访问磁盘。
        var quotaCache = _quotaCacheStore.Snapshot();
        var accounts = new JsonArray();

        // 这里逐个把认证文件转换成前端账户卡片结构。
        foreach (var authFile in authFiles)
        {
            // 这里保留与 Python 版一致的基础账户字段。
            var account = new JsonObject
            {
                ["id"] = authFile.Id,
                ["name"] = authFile.Name,
                ["email"] = authFile.Email,
                ["type"] = authFile.Type,
                ["provider"] = authFile.Provider,
                ["status"] = authFile.Status,
                ["status_message"] = authFile.StatusMessage,
                ["disabled"] = authFile.Disabled,
                ["account_type"] = authFile.AccountType,
                ["account"] = authFile.Account,
                ["created_at"] = authFile.CreatedAt,
                ["modtime"] = authFile.ModTime,
                ["last_refresh"] = authFile.LastRefresh,
                ["runtime_only"] = authFile.RuntimeOnly,
                ["source"] = authFile.Source,
            };

            // 这里把缓存里的配额信息合并到账户列表中，减少首页等待。
            if (quotaCache[authFile.Id] is JsonObject cachedEntry)
            {
                // 这里克隆 quota 对象，避免多个节点共享引用。
                if (cachedEntry["quota"] is JsonObject quotaObject)
                {
                    account["quota"] = JsonHelpers.CloneObject(quotaObject);
                }

                // 这里同步缓存中的订阅等级和 Token 状态。
                account["subscription_tier"] = cachedEntry["subscription_tier"]?.ToString();
                var tokenStatus = cachedEntry["quota"] is JsonObject cachedQuota ? cachedQuota.GetString("token_status") : string.Empty;

                // 这里显式标记需要重新登录的账号，便于前端直接提示。
                if (tokenStatus is "missing" or "expired" or "invalid")
                {
                    account["needs_relogin"] = true;
                }
            }

            // 这里把当前账号加入返回数组。
            accounts.Add(account);
        }

        // 这里返回首页账户列表接口约定的完整响应。
        return new JsonObject
        {
            ["accounts"] = accounts,
            ["auth_dir"] = _appContextService.Settings.AuthDir,
            ["mode"] = string.IsNullOrWhiteSpace(_appContextService.Settings.ManagementApiKey) ? "local" : "api",
        };
    }

    /// <summary>
    /// 刷新单个账户的配额信息。
    /// </summary>
    public async Task<(int StatusCode, JsonObject Payload)> RefreshAccountQuotaAsync(string accountId, CancellationToken cancellationToken = default)
    {
        // 这里先加载账号列表，再按 id 或文件名定位目标账号。
        var authFiles = await FetchAuthFilesAsync(cancellationToken);
        var authFile = authFiles.FirstOrDefault(file => string.Equals(file.Id, accountId, StringComparison.OrdinalIgnoreCase) || string.Equals(file.Name, accountId, StringComparison.OrdinalIgnoreCase));

        // 这里找不到目标账号时直接返回 404。
        if (authFile is null)
        {
            return (StatusCodes.Status404NotFound, new JsonObject { ["error"] = "账号不存在" });
        }

        // 这里统一规范 provider 名称，便于后续能力判断。
        var provider = authFile.Type.ToLowerInvariant();

        // 这里先拦截不支持的 provider，避免进入错误的配额查询流程。
        if (!_quotaService.IsSupportedProvider(provider))
        {
            return (StatusCodes.Status400BadRequest, new JsonObject { ["error"] = $"暂不支持 {provider} 类型账号的配额查询", ["account_id"] = accountId });
        }

        // 这里优先复用已加载的认证数据，缺失时再回源下载。
        var authData = authFile.RawData ?? await DownloadAuthFileAsync(authFile.Name, cancellationToken);

        // 这里校验认证数据是否可用，避免把空对象传给配额服务。
        if (authData is null || authData.Count == 0)
        {
            return (StatusCodes.Status500InternalServerError, new JsonObject { ["error"] = "无法获取认证数据" });
        }

        // 这里调用配额服务查询最新配额结果。
        var quota = await _quotaService.GetQuotaForAccountAsync(authData, cancellationToken);
        var subscriptionTier = quota.GetString("subscription_tier");

        // 这里把最新配额写回缓存，并同步订阅等级。
        _quotaCacheStore.Upsert(accountId, quota, subscriptionTier);

        // 这里立即持久化缓存，保证后续列表请求可见最新结果。
        await _quotaCacheStore.SaveAsync(cancellationToken);

        // 这里返回单账号刷新后的标准响应结构。
        return (StatusCodes.Status200OK, new JsonObject
        {
            ["account_id"] = accountId,
            ["quota"] = JsonHelpers.CloneObject(quota),
            ["subscription_tier"] = string.IsNullOrWhiteSpace(subscriptionTier) ? null : subscriptionTier,
            ["tier_display"] = GetTierDisplay(subscriptionTier),
        });
    }

    /// <summary>
    /// 顺序刷新所有账户的配额信息，与 Python 版本逻辑保持一致。
    /// </summary>
    public async Task<JsonObject> RefreshAllQuotasAsync(CancellationToken cancellationToken = default)
    {
        // 这里先拉取全部账号，并初始化批量刷新统计。
        var authFiles = await FetchAuthFilesAsync(cancellationToken);
        var results = new JsonArray();
        var successCount = 0;
        var failedCount = 0;
        var skippedCount = 0;
        var staticCount = 0;

        // 这里按账号顺序逐个处理，保持与 Python 版本一致。
        foreach (var authFile in authFiles)
        {
            var accountId = authFile.Id;
            var provider = authFile.Type.ToLowerInvariant();

            // 这里跳过不支持的 provider，并记录跳过原因。
            if (!_quotaService.IsSupportedProvider(provider))
            {
                skippedCount++;
                results.Add(new JsonObject
                {
                    ["account_id"] = accountId,
                    ["email"] = authFile.Email,
                    ["status"] = "skipped",
                    ["message"] = $"不支持 {provider} 类型",
                });
                continue;
            }


            // 这里对静态模型 provider 直接生成静态配额结果，不走实时接口。
            if (_quotaService.IsStaticProvider(provider))
            {
                staticCount++;

                // 这里优先复用原始认证数据，没有时再下载文件内容。
                var staticAuthData = authFile.RawData ?? await DownloadAuthFileAsync(authFile.Name, cancellationToken) ?? new JsonObject { ["type"] = provider };
                var staticQuota = await _quotaService.GetQuotaForAccountAsync(staticAuthData, cancellationToken);
                _quotaCacheStore.Upsert(accountId, staticQuota, staticQuota.GetString("subscription_tier"));
                results.Add(new JsonObject
                {
                    ["account_id"] = accountId,
                    ["email"] = authFile.Email,
                    ["status"] = "static",
                    ["message"] = "静态模型列表",
                    ["models_count"] = CountModels(staticQuota),
                });
                continue;
            }

            // 这里对支持实时查询的账号进入实际刷新流程。
            try
            {
                // 这里先拿到当前账号的认证数据。
                var authData = authFile.RawData ?? await DownloadAuthFileAsync(authFile.Name, cancellationToken);

                // 这里在认证数据缺失时记录失败结果，并继续后续账号。
                if (authData is null || authData.Count == 0)
                {
                    failedCount++;
                    results.Add(new JsonObject
                    {
                        ["account_id"] = accountId,
                        ["email"] = authFile.Email,
                        ["status"] = "error",
                        ["message"] = "无法获取认证数据",
                    });
                    continue;
                }

                // 这里查询实时配额，并把成功结果写入缓存。
                var quota = await _quotaService.GetQuotaForAccountAsync(authData, cancellationToken);
                _quotaCacheStore.Upsert(accountId, quota, quota.GetString("subscription_tier"));
                successCount++;
                results.Add(new JsonObject
                {
                    ["account_id"] = accountId,
                    ["email"] = authFile.Email,
                    ["status"] = "success",
                    ["subscription_tier"] = string.IsNullOrWhiteSpace(quota.GetString("subscription_tier")) ? null : quota.GetString("subscription_tier"),
                    ["models_count"] = CountModels(quota),
                });
            }
            // 这里把单账号异常收敛成结果项，避免整个批量任务中断。
            catch (Exception exception)
            {
                failedCount++;
                results.Add(new JsonObject
                {
                    ["account_id"] = accountId,
                    ["email"] = authFile.Email,
                    ["status"] = "error",
                    ["message"] = exception.Message,
                });
            }
        }


        // 这里在批量处理结束后统一落盘缓存，减少磁盘写入次数。
        await _quotaCacheStore.SaveAsync(cancellationToken);

        // 这里返回汇总统计和逐账号刷新结果。
        return new JsonObject
        {
            ["total"] = authFiles.Count,
            ["success"] = successCount,
            ["static"] = staticCount,
            ["failed"] = failedCount,
            ["skipped"] = skippedCount,
            ["results"] = results,
        };
    }


    /// <summary>
    /// 删除指定账户，优先尝试 Management API，再回退到本地文件删除。
    /// </summary>
    public async Task<(int StatusCode, JsonObject Payload)> DeleteAccountAsync(string accountName, CancellationToken cancellationToken = default)
    {
        // 这里先校验删除参数，避免空值参与删除流程。
        if (string.IsNullOrWhiteSpace(accountName))
        {
            return (StatusCodes.Status400BadRequest, new JsonObject { ["error"] = "账号名称不能为空" });
        }

        // 这里优先尝试通过 Management API 删除账号。
        try
        {
            using var client = CreateNoProxyClient();
            using var request = new HttpRequestMessage(HttpMethod.Delete, $"{_appContextService.Settings.ManagementApiUrl}/v0/management/auth-files?name={Uri.EscapeDataString(accountName)}");
            ApplyManagementHeaders(request.Headers);
            using var response = await client.SendAsync(request, cancellationToken);

            // 这里远程删除成功后直接返回，不再继续本地兜底。
            if (response.StatusCode == HttpStatusCode.OK)
            {
                return (StatusCodes.Status200OK, new JsonObject { ["success"] = true, ["message"] = "账号已删除" });
            }

            // 这里只有未找到或未授权时才回退到本地删除，其他错误直接透传。
            if (response.StatusCode != HttpStatusCode.NotFound && response.StatusCode != HttpStatusCode.Unauthorized)
            {
                var errorText = await response.Content.ReadAsStringAsync(cancellationToken);
                return ((int)response.StatusCode, new JsonObject { ["error"] = $"删除失败: {errorText}" });
            }
        }
        // 这里记录远程删除失败信息，然后进入本地兜底删除。
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "通过 Management API 删除账号失败，将回退到本地文件删除。");
        }

        // 这里构造本地可能存在的认证文件路径。
        var authDirectory = new DirectoryInfo(_appContextService.Settings.AuthDir);
        var candidatePaths = new[]
        {
            Path.Combine(authDirectory.FullName, accountName),
            Path.Combine(authDirectory.FullName, $"{accountName}.json"),
        };
        var targetPath = candidatePaths.FirstOrDefault(File.Exists);

        // 这里本地也不存在目标文件时返回 404。
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            return (StatusCodes.Status404NotFound, new JsonObject { ["error"] = "账号不存在" });
        }

        // 这里执行本地文件删除并返回成功结果。
        File.Delete(targetPath);
        return (StatusCodes.Status200OK, new JsonObject { ["success"] = true, ["message"] = "账号已删除" });
    }

    /// <summary>
    /// 获取认证文件列表，优先走 Management API，失败后回退本地目录。
    /// </summary>
    private async Task<List<AuthFileDescriptor>> FetchAuthFilesAsync(CancellationToken cancellationToken)
    {
        // 这里在配置了 Management API Key 时优先走远程模式。
        if (!string.IsNullOrWhiteSpace(_appContextService.Settings.ManagementApiKey))
        {
            // 这里尝试从管理接口读取认证文件列表。
            var apiFiles = await FetchAuthFilesFromApiAsync(cancellationToken);

            if (apiFiles is not null)
            {
                return apiFiles;
            }
        }

        // 这里在远程不可用时回退到本地 AUTH_DIR。
        return await FetchAuthFilesFromDiskAsync(cancellationToken);
    }

    /// <summary>
    /// 从 Management API 读取认证文件列表。
    /// </summary>
    private async Task<List<AuthFileDescriptor>?> FetchAuthFilesFromApiAsync(CancellationToken cancellationToken)
    {
        // 这里捕获网络异常，把远程失败统一交给上层回退逻辑处理。
        try
        {
            // 这里创建不走系统代理的客户端，避免本地管理接口受代理影响。
            using var client = CreateNoProxyClient();

            // 这里请求管理接口中的认证文件列表。
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{_appContextService.Settings.ManagementApiUrl}/v0/management/auth-files");
            ApplyManagementHeaders(request.Headers);
            using var response = await client.SendAsync(request, cancellationToken);

            // 这里远程接口返回非成功状态时返回 null，让调用方决定是否回退。
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            // 这里解析返回的 files 数组并转换成内部描述对象。
            var responseObject = JsonHelpers.ParseObject(await response.Content.ReadAsStringAsync(cancellationToken));
            return responseObject["files"] is JsonArray filesArray ? filesArray.OfType<JsonObject>().Select(ParseAuthFileDescriptor).ToList() : [];
        }
        // 这里吞掉异常并返回 null，保持“远程失败即回退本地”的策略。
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 从本地 AUTH_DIR 中读取认证文件列表。
    /// </summary>
    private Task<List<AuthFileDescriptor>> FetchAuthFilesFromDiskAsync(CancellationToken cancellationToken)
    {
        // 这里先响应取消请求，避免磁盘扫描继续执行。
        cancellationToken.ThrowIfCancellationRequested();

        // 这里初始化结果集合，并定位本地认证目录。
        var result = new List<AuthFileDescriptor>();
        var authDirectory = new DirectoryInfo(_appContextService.Settings.AuthDir);

        // 这里目录不存在时直接返回空列表。
        if (!authDirectory.Exists)
        {
            return Task.FromResult(result);
        }

        // 这里遍历认证目录中的所有文件。
        foreach (var file in authDirectory.EnumerateFiles())
        {
            // 这里仅处理 JSON 认证文件，忽略其他杂项文件。
            if (!string.Equals(file.Extension, ".json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // 这里单文件解析失败时只跳过当前文件，不影响其他账号加载。
            try
            {
                // 这里读取认证 JSON，并补齐前端所需的描述字段。
                var rawData = JsonHelpers.ParseObject(File.ReadAllText(file.FullName, Encoding.UTF8));
                result.Add(new AuthFileDescriptor
                {
                    Id = file.Name[..^file.Extension.Length],
                    Name = file.Name,
                    Email = rawData.GetString("email"),
                    Type = rawData.GetString("type", "unknown"),
                    Provider = rawData.GetString("type", "unknown"),
                    Status = "active",
                    StatusMessage = string.Empty,
                    Disabled = false,
                    AccountType = rawData.GetString("account_type"),
                    Account = rawData.GetString("account"),
                    CreatedAt = rawData.GetString("created_at"),
                    ModTime = new DateTimeOffset(file.LastWriteTimeUtc).ToUnixTimeSeconds(),
                    LastRefresh = rawData.GetString("last_refresh"),
                    RuntimeOnly = false,
                    Source = "file",
                    RawData = rawData,
                });
            }
            // 这里记录坏文件信息，便于排查本地认证数据问题。
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "读取认证文件失败: {Path}", file.FullName);
            }
        }

        // 这里返回本地扫描得到的账号列表。
        return Task.FromResult(result);
    }

    /// <summary>
    /// 下载指定名称的认证文件内容。
    /// </summary>
    private async Task<JsonObject?> DownloadAuthFileAsync(string name, CancellationToken cancellationToken)
    {
        // 这里在远程模式下优先下载管理接口中的认证文件内容。
        if (!string.IsNullOrWhiteSpace(_appContextService.Settings.ManagementApiKey))
        {
            var apiData = await DownloadAuthFileFromApiAsync(name, cancellationToken);

            if (apiData is not null && apiData.Count > 0)
            {
                return apiData;
            }
        }

        // 这里远程无结果时回退到本地文件读取。
        return await DownloadAuthFileFromDiskAsync(name, cancellationToken);
    }

    /// <summary>
    /// 从 Management API 下载指定认证文件。
    /// </summary>
    private async Task<JsonObject?> DownloadAuthFileFromApiAsync(string name, CancellationToken cancellationToken)
    {
        // 这里捕获下载异常，保持与列表读取一致的回退策略。
        try
        {
            using var client = CreateNoProxyClient();

            // 这里创建请求并附带管理接口鉴权头。
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{_appContextService.Settings.ManagementApiUrl}/v0/management/auth-files/download?name={Uri.EscapeDataString(name)}");
            ApplyManagementHeaders(request.Headers);
            using var response = await client.SendAsync(request, cancellationToken);

            // 这里下载成功时解析 JSON，失败时返回 null。
            return response.IsSuccessStatusCode ? JsonHelpers.ParseObject(await response.Content.ReadAsStringAsync(cancellationToken)) : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 从本地目录下载指定认证文件。
    /// </summary>
    private Task<JsonObject?> DownloadAuthFileFromDiskAsync(string name, CancellationToken cancellationToken)
    {
        // 这里先响应取消请求。
        cancellationToken.ThrowIfCancellationRequested();

        var authDirectory = _appContextService.Settings.AuthDir;

        // 这里同时兼容传入完整文件名和不带扩展名两种情况。
        var candidatePaths = new[]
        {
            Path.Combine(authDirectory, name),
            Path.Combine(authDirectory, $"{name}.json"),
        };

        // 这里选取第一个实际存在的认证文件路径。
        var targetPath = candidatePaths.FirstOrDefault(File.Exists);

        // 这里找不到文件时直接返回 null。
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            return Task.FromResult<JsonObject?>(null);
        }

        // 这里读取并解析本地认证文件内容。
        return Task.FromResult<JsonObject?>(JsonHelpers.ParseObject(File.ReadAllText(targetPath, Encoding.UTF8)));
    }

    private static AuthFileDescriptor ParseAuthFileDescriptor(JsonObject jsonObject)
    {
        return new AuthFileDescriptor
        {
            Id = jsonObject.GetString("id", jsonObject.GetString("name")),
            Name = jsonObject.GetString("name"),
            Email = jsonObject.GetString("email"),
            Type = jsonObject.GetString("type", "unknown"),
            Provider = jsonObject.GetString("provider", jsonObject.GetString("type", "unknown")),
            Status = jsonObject.GetString("status", "unknown"),
            StatusMessage = jsonObject.GetString("status_message"),
            Disabled = jsonObject.GetBoolean("disabled"),
            AccountType = jsonObject.GetString("account_type"),
            Account = jsonObject.GetString("account"),
            CreatedAt = jsonObject.GetString("created_at"),
            ModTime = ReadDouble(jsonObject, "modtime"),
            LastRefresh = jsonObject.GetString("last_refresh"),
            RuntimeOnly = jsonObject.GetBoolean("runtime_only"),
            Source = jsonObject.GetString("source", "file"),
        };
    }

    private static double ReadDouble(JsonObject jsonObject, string propertyName)
    {
        var node = jsonObject[propertyName];
        return node is null ? 0 : double.TryParse(node.ToString(), out var value) ? value : 0;
    }

    private static int CountModels(JsonObject quota)
    {
        return quota["models"] is JsonArray models ? models.Count : 0;
    }

    private static JsonObject GetTierDisplay(string tier)
    {
        var normalizedTier = (tier ?? string.Empty).ToLowerInvariant();

        if (normalizedTier.Contains("ultra", StringComparison.OrdinalIgnoreCase))
        {
            return new JsonObject { ["name"] = "ULTRA", ["color"] = "purple", ["badge_class"] = "tier-ultra" };
        }

        if (normalizedTier.Contains("pro", StringComparison.OrdinalIgnoreCase))
        {
            return new JsonObject { ["name"] = "PRO", ["color"] = "blue", ["badge_class"] = "tier-pro" };
        }

        if (!string.IsNullOrWhiteSpace(tier))
        {
            return new JsonObject { ["name"] = tier.ToUpperInvariant(), ["color"] = "gray", ["badge_class"] = "tier-free" };
        }

        return new JsonObject { ["name"] = "未知", ["color"] = "gray", ["badge_class"] = "tier-unknown" };
    }

    private static HttpClient CreateNoProxyClient()
    {
        return new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(10) };
    }

    private void ApplyManagementHeaders(HttpRequestHeaders headers)
    {
        headers.TryAddWithoutValidation("Content-Type", "application/json");

        if (!string.IsNullOrWhiteSpace(_appContextService.Settings.ManagementApiKey))
        {
            headers.Authorization = new AuthenticationHeaderValue("Bearer", _appContextService.Settings.ManagementApiKey);
        }
    }
}
