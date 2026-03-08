using System.Text;
using System.Text.Json.Nodes;

namespace CPA_DashBoard.Web.Services;

/// <summary>
/// 负责读取、截取和清理 CLIProxyAPI 日志文件。
/// </summary>
public sealed class LogService
{
    /// <summary>
    /// 保存应用上下文服务实例。
    /// </summary>
    private readonly AppContextService _appContextService;

    /// <summary>
    /// 使用应用上下文服务初始化日志服务。
    /// </summary>
    public LogService(AppContextService appContextService)
    {
        _appContextService = appContextService;
    }

    /// <summary>
    /// 获取日志内容，并支持 offset 与 lines 参数。
    /// </summary>
    public async Task<(int StatusCode, JsonObject Payload)> GetLogsAsync(int lines, int offset, CancellationToken cancellationToken = default)
    {
        var logFilePath = _appContextService.Settings.LogFile;

        if (string.IsNullOrWhiteSpace(logFilePath))
        {
            return (StatusCodes.Status400BadRequest, new JsonObject { ["error"] = "日志文件未配置" });
        }

        if (!File.Exists(logFilePath))
        {
            return (StatusCodes.Status200OK, new JsonObject
            {
                ["content"] = string.Empty,
                ["lines"] = 0,
                ["size"] = 0,
                ["exists"] = false,
                ["path"] = logFilePath,
            });
        }

        var allLines = await File.ReadAllLinesAsync(logFilePath, Encoding.UTF8, cancellationToken);
        var totalLines = allLines.Length;
        var selectedLines = offset == 0 ? allLines.TakeLast(Math.Max(1, lines)).ToArray() : allLines.Skip(offset).Take(Math.Max(1, lines)).ToArray();
        var fileInfo = new FileInfo(logFilePath);
        return (StatusCodes.Status200OK, new JsonObject
        {
            ["content"] = string.Join(Environment.NewLine, selectedLines),
            ["lines"] = selectedLines.Length,
            ["total_lines"] = totalLines,
            ["size"] = fileInfo.Length,
            ["size_human"] = FormatFileSize(fileInfo.Length),
            ["exists"] = true,
            ["path"] = logFilePath,
        });
    }

    /// <summary>
    /// 获取日志尾部内容，供前端实时轮询使用。
    /// </summary>
    public async Task<JsonObject> GetTailAsync(int lines, CancellationToken cancellationToken = default)
    {
        var logFilePath = _appContextService.Settings.LogFile;

        if (string.IsNullOrWhiteSpace(logFilePath) || !File.Exists(logFilePath))
        {
            return new JsonObject { ["content"] = string.Empty, ["lines"] = 0 };
        }

        var tailLines = (await File.ReadAllLinesAsync(logFilePath, Encoding.UTF8, cancellationToken)).TakeLast(Math.Max(1, lines)).ToArray();
        return new JsonObject { ["content"] = string.Join(Environment.NewLine, tailLines), ["lines"] = tailLines.Length };
    }

    /// <summary>
    /// 清空日志文件，或在清空前先执行备份。
    /// </summary>
    public Task<(int StatusCode, JsonObject Payload)> ClearAsync(bool backup, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var logFilePath = _appContextService.Settings.LogFile;

        if (string.IsNullOrWhiteSpace(logFilePath))
        {
            return Task.FromResult((StatusCodes.Status400BadRequest, new JsonObject { ["error"] = "日志文件未配置" }));
        }

        if (!File.Exists(logFilePath))
        {
            return Task.FromResult((StatusCodes.Status200OK, new JsonObject { ["success"] = true, ["message"] = "日志文件不存在" }));
        }

        if (backup)
        {
            var backupPath = $"{logFilePath}.{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}.bak";
            File.Move(logFilePath, backupPath, overwrite: true);
            File.WriteAllText(logFilePath, string.Empty, Encoding.UTF8);
            return Task.FromResult((StatusCodes.Status200OK, new JsonObject { ["success"] = true, ["message"] = $"日志已备份至 {backupPath}", ["backup_path"] = backupPath }));
        }

        File.WriteAllText(logFilePath, string.Empty, Encoding.UTF8);
        return Task.FromResult((StatusCodes.Status200OK, new JsonObject { ["success"] = true, ["message"] = "日志已清除" }));
    }

    /// <summary>
    /// 将字节大小格式化成可读文本。
    /// </summary>
    private static string FormatFileSize(long sizeInBytes)
    {
        var units = new[] { "B", "KB", "MB", "GB", "TB" };
        double size = sizeInBytes;
        var unitIndex = 0;

        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return $"{size:0.0} {units[unitIndex]}";
    }
}
