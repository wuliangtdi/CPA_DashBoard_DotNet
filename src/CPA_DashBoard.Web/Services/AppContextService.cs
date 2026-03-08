using System.Text.Json;
using System.Text.Json.Nodes;
using CPA_DashBoard.Web.Helpers;
using CPA_DashBoard.Web.Models;
using YamlDotNet.Serialization;

namespace CPA_DashBoard.Web.Services;

/// <summary>
/// 负责加载项目配置并解析出 .NET 版本运行所需的上下文信息。
/// </summary>
public sealed class AppContextService
{
    /// <summary>
    /// 保存当前服务的日志实例。
    /// </summary>
    private readonly ILogger<AppContextService> _logger;

    /// <summary>
    /// 保存已经解析完成的设置对象。
    /// </summary>
    public ResolvedAppSettings Settings { get; }

    /// <summary>
    /// 使用宿主环境和日志服务初始化应用上下文。
    /// </summary>
    public AppContextService(IWebHostEnvironment environment, ILogger<AppContextService> logger)
    {
        _logger = logger;

        var configPath = FindConfigYaml(environment.ContentRootPath);
        var projectConfig = LoadProjectConfig(configPath);
        var apiPort = GetIntValue(projectConfig, "port", 8317);
        var apiHost = string.IsNullOrWhiteSpace(GetStringValue(projectConfig, "host", string.Empty)) ? "127.0.0.1" : GetStringValue(projectConfig, "host", string.Empty);
        var authDirValue = Environment.GetEnvironmentVariable("CPA_AUTH_DIR") ?? GetStringValue(projectConfig, "auth-dir", "~/.cli-proxy-api");
        var authDir = Path.GetFullPath(Environment.ExpandEnvironmentVariables(authDirValue.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))));
        var serviceDir = ResolveServiceDirectory(configPath);
        var binaryName = Environment.GetEnvironmentVariable("CPA_BINARY_NAME") ?? "CLIProxyAPI";
        var logFile = Environment.GetEnvironmentVariable("CPA_LOG_FILE") ?? (string.IsNullOrWhiteSpace(serviceDir) ? string.Empty : Path.Combine(serviceDir, "cliproxyapi.log"));
        var rawConcurrency = Environment.GetEnvironmentVariable("CPA_QUOTA_REFRESH_CONCURRENCY") ?? GetStringValue(projectConfig, "quota-refresh-concurrency", "4");
        var quotaRefreshConcurrency = Math.Clamp(JsonHelpers.ToInt32(rawConcurrency, 4), 1, 32);

        Settings = new ResolvedAppSettings(
            ConfigPath: configPath,
            ManagementApiUrl: Environment.GetEnvironmentVariable("CPA_MANAGEMENT_URL") ?? $"http://127.0.0.1:{apiPort}",
            ManagementApiKey: Environment.GetEnvironmentVariable("CPA_MANAGEMENT_KEY") ?? string.Empty,
            AuthDir: authDir,
            WebUiHost: Environment.GetEnvironmentVariable("WEBUI_HOST") ?? "127.0.0.1",
            WebUiPort: JsonHelpers.ToInt32(Environment.GetEnvironmentVariable("WEBUI_PORT"), 5000),
            WebUiDebug: string.Equals(Environment.GetEnvironmentVariable("WEBUI_DEBUG"), "true", StringComparison.OrdinalIgnoreCase),
            ServiceDir: serviceDir,
            BinaryName: binaryName,
            LogFile: logFile,
            ProxyUrl: Environment.GetEnvironmentVariable("CPA_PROXY_URL") ?? GetStringValue(projectConfig, "proxy-url", string.Empty),
            CloudCodeApiUrl: "https://cloudcode-pa.googleapis.com",
            AntigravityUserAgent: "antigravity/1.11.3 Darwin/arm64",
            GeminiCliUserAgent: "google-api-nodejs-client/9.15.1",
            GoogleTokenUrl: "https://oauth2.googleapis.com/token",
            AntigravityClientId: Environment.GetEnvironmentVariable("CPA_ANTIGRAVITY_CLIENT_ID") ?? string.Empty,
            AntigravityClientSecret: Environment.GetEnvironmentVariable("CPA_ANTIGRAVITY_CLIENT_SECRET") ?? string.Empty,
            ApiHost: apiHost,
            ApiPort: apiPort,
            QuotaRefreshConcurrency: quotaRefreshConcurrency,
            ApiKeys: GetStringList(projectConfig, "api-keys"),
            QuotaCacheFilePath: Path.Combine(environment.ContentRootPath, "quota_cache.json"));
    }

    /// <summary>
    /// 查找项目根目录附近的 config.yaml 文件。
    /// </summary>
    private string? FindConfigYaml(string contentRootPath)
    {
        var environmentPath = Environment.GetEnvironmentVariable("CPA_CONFIG_PATH");

        if (!string.IsNullOrWhiteSpace(environmentPath))
        {
            var resolvedEnvironmentPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(environmentPath));

            if (File.Exists(resolvedEnvironmentPath))
            {
                return resolvedEnvironmentPath;
            }

            _logger.LogWarning("环境变量 CPA_CONFIG_PATH 指定的文件不存在: {Path}", resolvedEnvironmentPath);
        }

        var currentDirectory = new DirectoryInfo(contentRootPath);

        for (var index = 0; index < 5 && currentDirectory is not null; index++)
        {
            var candidatePath = Path.Combine(currentDirectory.FullName, "config.yaml");

            if (File.Exists(candidatePath))
            {
                return candidatePath;
            }

            currentDirectory = currentDirectory.Parent;
        }

        return null;
    }

    /// <summary>
    /// 加载 YAML 配置并标准化为字符串键的字典。
    /// </summary>
    private Dictionary<string, object?> LoadProjectConfig(string? configPath)
    {
        if (string.IsNullOrWhiteSpace(configPath) || !File.Exists(configPath))
        {
            _logger.LogInformation("未找到 config.yaml，将使用默认配置。");
            return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var yamlText = File.ReadAllText(configPath);
            var deserializer = new DeserializerBuilder().Build();
            var rawObject = deserializer.Deserialize<object?>(yamlText);
            return NormalizeObject(rawObject);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "读取 config.yaml 失败，将回退到默认配置。");
            return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// 解析最终的服务目录。
    /// </summary>
    private string ResolveServiceDirectory(string? configPath)
    {
        var environmentServiceDirectory = Environment.GetEnvironmentVariable("CPA_SERVICE_DIR");

        if (!string.IsNullOrWhiteSpace(environmentServiceDirectory))
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(environmentServiceDirectory));
        }

        if (!string.IsNullOrWhiteSpace(configPath))
        {
            return Path.GetDirectoryName(configPath) ?? string.Empty;
        }

        return string.Empty;
    }

    /// <summary>
    /// 将 YAML 反序列化结果转换为普通字典。
    /// </summary>
    private static Dictionary<string, object?> NormalizeObject(object? value)
    {
        if (value is IDictionary<object, object?> rawDictionary)
        {
            var dictionary = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

            foreach (var pair in rawDictionary)
            {
                dictionary[Convert.ToString(pair.Key) ?? string.Empty] = NormalizeValue(pair.Value);
            }

            return dictionary;
        }

        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 递归标准化 YAML 节点类型。
    /// </summary>
    private static object? NormalizeValue(object? value)
    {
        if (value is IDictionary<object, object?> dictionary)
        {
            return NormalizeObject(dictionary);
        }

        if (value is IEnumerable<object?> sequence && value is not string)
        {
            return sequence.Select(NormalizeValue).ToList();
        }

        return value;
    }

    /// <summary>
    /// 读取字符串配置值。
    /// </summary>
    private static string GetStringValue(IReadOnlyDictionary<string, object?> dictionary, string key, string defaultValue)
    {
        if (!dictionary.TryGetValue(key, out var value) || value is null)
        {
            return defaultValue;
        }

        return Convert.ToString(value) ?? defaultValue;
    }

    /// <summary>
    /// 读取整数配置值。
    /// </summary>
    private static int GetIntValue(IReadOnlyDictionary<string, object?> dictionary, string key, int defaultValue)
    {
        if (!dictionary.TryGetValue(key, out var value) || value is null)
        {
            return defaultValue;
        }

        return value switch
        {
            int intValue => intValue,
            long longValue => (int)longValue,
            _ => JsonHelpers.ToInt32(Convert.ToString(value), defaultValue),
        };
    }

    /// <summary>
    /// 读取字符串数组配置值。
    /// </summary>
    private static IReadOnlyList<string> GetStringList(IReadOnlyDictionary<string, object?> dictionary, string key)
    {
        if (!dictionary.TryGetValue(key, out var value) || value is null)
        {
            return [];
        }

        if (value is IEnumerable<object?> sequence && value is not string)
        {
            return sequence.Select(item => Convert.ToString(item) ?? string.Empty).Where(item => !string.IsNullOrWhiteSpace(item)).ToList();
        }

        var singleValue = Convert.ToString(value);

        return string.IsNullOrWhiteSpace(singleValue) ? [] : [singleValue];
    }
}

/// <summary>
/// 负责维护配额缓存的内存副本与持久化文件。
/// </summary>
public sealed class QuotaCacheStore
{
    /// <summary>
    /// 保存缓存文件完整路径。
    /// </summary>
    private readonly string _cacheFilePath;

    /// <summary>
    /// 保存线程同步锁，避免并发写入缓存文件。
    /// </summary>
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>
    /// 保存当前内存中的缓存对象。
    /// </summary>
    private JsonObject _cache;

    /// <summary>
    /// 根据应用上下文初始化缓存存储。
    /// </summary>
    public QuotaCacheStore(AppContextService appContextService)
    {
        _cacheFilePath = appContextService.Settings.QuotaCacheFilePath;
        _cache = LoadCacheFromDisk();
    }

    /// <summary>
    /// 获取缓存对象的安全副本。
    /// </summary>
    public JsonObject Snapshot()
    {
        return JsonHelpers.CloneObject(_cache);
    }

    /// <summary>
    /// 获取指定账号的缓存记录。
    /// </summary>
    public JsonObject? GetEntry(string accountId)
    {
        return _cache[accountId] as JsonObject;
    }

    /// <summary>
    /// 更新指定账号的缓存记录。
    /// </summary>
    public void Upsert(string accountId, JsonObject quota, string? subscriptionTier)
    {
        _cache[accountId] = new JsonObject
        {
            ["quota"] = JsonHelpers.CloneObject(quota),
            ["subscription_tier"] = subscriptionTier,
            ["fetched_at"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        };
    }

    /// <summary>
    /// 将内存缓存写回磁盘。
    /// </summary>
    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);

        try
        {
            var directory = Path.GetDirectoryName(_cacheFilePath);

            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(_cacheFilePath, _cache.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// 从磁盘读取现有缓存文件。
    /// </summary>
    private JsonObject LoadCacheFromDisk()
    {
        if (!File.Exists(_cacheFilePath))
        {
            return new JsonObject();
        }

        try
        {
            return JsonHelpers.ParseObject(File.ReadAllText(_cacheFilePath));
        }
        catch
        {
            return new JsonObject();
        }
    }
}
