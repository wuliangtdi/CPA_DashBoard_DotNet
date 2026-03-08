using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;

namespace CPA_DashBoard.Web.Services;

/// <summary>
/// 负责查询、启动、停止和重启 CLIProxyAPI 进程。
/// </summary>
public sealed class CliProxyProcessService
{
    /// <summary>
    /// 保存应用上下文服务实例。
    /// </summary>
    private readonly AppContextService _appContextService;

    /// <summary>
    /// 保存日志实例。
    /// </summary>
    private readonly ILogger<CliProxyProcessService> _logger;

    /// <summary>
    /// 使用应用上下文和日志实例初始化进程服务。
    /// </summary>
    public CliProxyProcessService(AppContextService appContextService, ILogger<CliProxyProcessService> logger)
    {
        _appContextService = appContextService;
        _logger = logger;
    }

    /// <summary>
    /// 获取 CLIProxyAPI 服务状态。
    /// </summary>
    public JsonObject GetServiceStatus()
    {
        var settings = _appContextService.Settings;
        var matchedProcesses = FindMatchingProcesses();
        return new JsonObject
        {
            ["running"] = matchedProcesses.Count > 0,
            ["pids"] = new JsonArray(matchedProcesses.Select(item => (JsonNode?)item.Pid.ToString()).ToArray()),
            ["processes"] = new JsonArray(matchedProcesses.Select(item => (JsonNode?)new JsonObject { ["pid"] = item.Pid.ToString(), ["info"] = item.Info }).ToArray()),
            ["count"] = matchedProcesses.Count,
            ["service_dir"] = settings.ServiceDir,
            ["binary_name"] = settings.BinaryName,
            ["log_file"] = settings.LogFile,
            ["configured"] = !string.IsNullOrWhiteSpace(settings.ServiceDir) && Directory.Exists(settings.ServiceDir),
        };
    }

    /// <summary>
    /// 启动 CLIProxyAPI 服务进程。
    /// </summary>
    public async Task<(int StatusCode, JsonObject Payload)> StartAsync(CancellationToken cancellationToken = default)
    {
        var settings = _appContextService.Settings;

        if (string.IsNullOrWhiteSpace(settings.ServiceDir) || !Directory.Exists(settings.ServiceDir))
        {
            return (StatusCodes.Status400BadRequest, new JsonObject { ["error"] = "服务目录未配置或不存在", ["service_dir"] = settings.ServiceDir });
        }

        var binaryPath = ResolveBinaryPath(settings.ServiceDir, settings.BinaryName);

        if (!File.Exists(binaryPath))
        {
            return (StatusCodes.Status400BadRequest, new JsonObject { ["error"] = $"可执行文件不存在: {binaryPath}" });
        }

        var currentStatus = GetServiceStatus();

        if (currentStatus["running"]?.GetValue<bool>() == true)
        {
            return (StatusCodes.Status200OK, new JsonObject { ["success"] = false, ["message"] = "服务已在运行", ["pids"] = currentStatus["pids"]?.DeepClone() });
        }

        var logPath = string.IsNullOrWhiteSpace(settings.LogFile) ? Path.Combine(settings.ServiceDir, "cliproxyapi.log") : settings.LogFile;
        Directory.CreateDirectory(Path.GetDirectoryName(logPath) ?? settings.ServiceDir);

        var startInfo = new ProcessStartInfo(binaryPath)
        {
            WorkingDirectory = settings.ServiceDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.Start();
        _ = PumpOutputToLogAsync(process.StandardOutput, logPath, cancellationToken);
        _ = PumpOutputToLogAsync(process.StandardError, logPath, cancellationToken);
        await Task.Delay(1000, cancellationToken);

        if (!process.HasExited)
        {
            return (StatusCodes.Status200OK, new JsonObject { ["success"] = true, ["message"] = "服务启动成功", ["pids"] = new JsonArray(process.Id.ToString()) });
        }

        var newStatus = GetServiceStatus();

        if (newStatus["running"]?.GetValue<bool>() == true)
        {
            return (StatusCodes.Status200OK, new JsonObject { ["success"] = true, ["message"] = "服务启动成功", ["pids"] = newStatus["pids"]?.DeepClone() });
        }

        return (StatusCodes.Status500InternalServerError, new JsonObject { ["success"] = false, ["message"] = "服务启动失败，请检查日志" });
    }

    /// <summary>
    /// 停止 CLIProxyAPI 服务进程。
    /// </summary>
    public async Task<(int StatusCode, JsonObject Payload)> StopAsync(CancellationToken cancellationToken = default)
    {
        var processes = FindMatchingProcesses();

        if (processes.Count == 0)
        {
            return (StatusCodes.Status200OK, new JsonObject { ["success"] = true, ["message"] = "服务未在运行" });
        }

        foreach (var processSnapshot in processes)
        {
            try
            {
                var process = Process.GetProcessById(processSnapshot.Pid);
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "停止进程失败: {Pid}", processSnapshot.Pid);
            }
        }

        await Task.Delay(300, cancellationToken);
        var finalStatus = GetServiceStatus();
        var success = finalStatus["running"]?.GetValue<bool>() != true;
        return (StatusCodes.Status200OK, new JsonObject
        {
            ["success"] = success,
            ["message"] = success ? "服务已停止" : "停止服务失败",
            ["killed_pids"] = new JsonArray(processes.Select(item => (JsonNode?)item.Pid.ToString()).ToArray()),
            ["remaining_pids"] = finalStatus["pids"]?.DeepClone(),
        });
    }

    /// <summary>
    /// 重启 CLIProxyAPI 服务进程。
    /// </summary>
    public async Task<(int StatusCode, JsonObject Payload)> RestartAsync(CancellationToken cancellationToken = default)
    {
        var stopResult = await StopAsync(cancellationToken);
        await Task.Delay(500, cancellationToken);
        var startResult = await StartAsync(cancellationToken);
        return (StatusCodes.Status200OK, new JsonObject { ["stop"] = stopResult.Payload, ["start"] = startResult.Payload, ["success"] = startResult.Payload["success"]?.GetValue<bool>() == true });
    }

    /// <summary>
    /// 解析跨平台可执行文件路径。
    /// </summary>
    public string ResolveBinaryPath(string serviceDirectory, string binaryName)
    {
        var basePath = Path.Combine(serviceDirectory, binaryName);

        if (File.Exists(basePath))
        {
            return basePath;
        }

        if (OperatingSystem.IsWindows() && !binaryName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            var exePath = $"{basePath}.exe";

            if (File.Exists(exePath))
            {
                return exePath;
            }
        }

        return basePath;
    }

    /// <summary>
    /// 查找与 CLIProxyAPI 二进制匹配的系统进程。
    /// </summary>
    private List<(int Pid, string Info)> FindMatchingProcesses()
    {
        var result = new List<(int Pid, string Info)>();
        var settings = _appContextService.Settings;
        var expectedBinaryPath = string.IsNullOrWhiteSpace(settings.ServiceDir) ? string.Empty : ResolveBinaryPath(settings.ServiceDir, settings.BinaryName);
        var expectedBinaryName = Path.GetFileNameWithoutExtension(settings.BinaryName);

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.Id == Environment.ProcessId)
                {
                    continue;
                }

                var processName = process.ProcessName;
                string? processPath = null;

                try
                {
                    processPath = process.MainModule?.FileName;
                }
                catch
                {
                    processPath = null;
                }

                var fileName = string.IsNullOrWhiteSpace(processPath) ? string.Empty : Path.GetFileNameWithoutExtension(processPath);
                var nameMatch = processName.Contains(expectedBinaryName, StringComparison.OrdinalIgnoreCase) || fileName.Contains(expectedBinaryName, StringComparison.OrdinalIgnoreCase);
                var pathMatch = !string.IsNullOrWhiteSpace(expectedBinaryPath) && !string.IsNullOrWhiteSpace(processPath) && string.Equals(Path.GetFullPath(processPath), Path.GetFullPath(expectedBinaryPath), StringComparison.OrdinalIgnoreCase);

                if (!nameMatch && !pathMatch)
                {
                    continue;
                }

                var uptime = string.Empty;

                try
                {
                    var elapsed = DateTime.Now - process.StartTime;
                    uptime = $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
                }
                catch
                {
                    uptime = string.Empty;
                }

                var memoryMb = process.WorkingSet64 / 1024d / 1024d;
                var info = $"pid={process.Id} mem={memoryMb:0.0}MB uptime={uptime} cmd={processPath ?? processName}";
                result.Add((process.Id, info));
            }
            catch
            {
                // 这里忽略单个进程读取失败，继续处理剩余进程。
            }
            finally
            {
                process.Dispose();
            }
        }

        return result;
    }

    /// <summary>
    /// 将标准输出或标准错误持续写入日志文件。
    /// </summary>
    private static async Task PumpOutputToLogAsync(StreamReader reader, string logPath, CancellationToken cancellationToken)
    {
        await using var fileStream = new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        await using var writer = new StreamWriter(fileStream, Encoding.UTF8) { AutoFlush = true };

        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);

            if (line is null)
            {
                break;
            }

            await writer.WriteLineAsync(line);
        }
    }
}
