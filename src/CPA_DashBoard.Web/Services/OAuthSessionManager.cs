using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CPA_DashBoard.Web.Models;

namespace CPA_DashBoard.Web.Services;

/// <summary>
/// 负责管理交互式 OAuth 登录会话。
/// </summary>
public sealed class OAuthSessionManager
{
    /// <summary>
    /// 保存支持的 OAuth Provider 映射关系。
    /// </summary>
    private static readonly IReadOnlyDictionary<string, OAuthProviderDefinition> OAuthProviders = new Dictionary<string, OAuthProviderDefinition>(StringComparer.OrdinalIgnoreCase)
    {
        ["antigravity"] = new("-antigravity-login", 51121),
        ["gemini"] = new("-login", 8085),
        ["codex"] = new("-codex-login", 1455),
        ["claude"] = new("-claude-login", 54545),
        ["qwen"] = new("-qwen-login", 0),
        ["iflow"] = new("-iflow-login", 55998),
        ["kimi"] = new("-kimi-login", 0),
    };

    /// <summary>
    /// 保存当前所有活动会话。
    /// </summary>
    private readonly ConcurrentDictionary<string, InteractiveOAuthSession> _sessions = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 保存 CLIProxyAPI 进程服务实例。
    /// </summary>
    private readonly CliProxyProcessService _cliProxyProcessService;

    /// <summary>
    /// 保存应用上下文服务实例。
    /// </summary>
    private readonly AppContextService _appContextService;

    /// <summary>
    /// 保存日志实例。
    /// </summary>
    private readonly ILogger<OAuthSessionManager> _logger;

    /// <summary>
    /// 使用必要依赖初始化 OAuth 会话管理器。
    /// </summary>
    public OAuthSessionManager(CliProxyProcessService cliProxyProcessService, AppContextService appContextService, ILogger<OAuthSessionManager> logger)
    {
        _cliProxyProcessService = cliProxyProcessService;
        _appContextService = appContextService;
        _logger = logger;
    }

    /// <summary>
    /// 启动指定 Provider 的 OAuth 会话。
    /// </summary>
    public async Task<(int StatusCode, JsonObject Payload)> StartAsync(string provider, CancellationToken cancellationToken = default)
    {
        // 这里统一把 provider 归一化成小写，便于后续字典查找。
        var normalizedProvider = provider.ToLowerInvariant();

        // 这里先校验 provider 是否受支持，避免创建无效会话。
        if (!OAuthProviders.TryGetValue(normalizedProvider, out var providerDefinition))
        {
            return (StatusCodes.Status400BadRequest, new JsonObject { ["error"] = $"不支持的 Provider: {normalizedProvider}", ["supported"] = new JsonArray(OAuthProviders.Keys.Select(item => (JsonNode?)item).ToArray()) });
        }

        // 这里读取运行配置，并解析 CLIProxyAPI 的可执行文件路径。
        var settings = _appContextService.Settings;
        var binaryPath = _cliProxyProcessService.ResolveBinaryPath(settings.ServiceDir, settings.BinaryName);

        // 这里先检查 CLIProxyAPI 可执行文件是否存在，避免启动时才报错。
        if (!File.Exists(binaryPath))
        {
            return (StatusCodes.Status400BadRequest, new JsonObject { ["error"] = $"CLIProxyAPI 可执行文件不存在: {binaryPath}" });
        }

        // 这里生成短 state，并准备启动命令与会话对象。
        var state = Guid.NewGuid().ToString("N")[..8];
        var command = new[] { binaryPath, providerDefinition.Flag, "-no-browser" };
        var session = new InteractiveOAuthSession(state, normalizedProvider, command, settings.ServiceDir, _logger);

        // 这里真正启动 OAuth 子进程，失败时直接返回错误。
        if (!session.Start())
        {
            return (StatusCodes.Status500InternalServerError, new JsonObject { ["error"] = $"启动 OAuth 流程失败: {session.Error}", ["state"] = state });
        }

        // 这里把会话缓存起来，后续状态轮询、输入和取消都依赖这个 state。
        _sessions[state] = session;

        // 这里给 CLI 一点启动时间，尽量在首个响应里就带上授权链接。
        await Task.Delay(2000, cancellationToken);
        var statusSnapshot = session.GetStatusSnapshot();
        var authUrl = statusSnapshot["url"]?.ToString();

        // 这里在第一次轮询还没拿到授权链接时，再短暂等待一次。
        if (string.IsNullOrWhiteSpace(authUrl))
        {
            await Task.Delay(1000, cancellationToken);
            statusSnapshot = session.GetStatusSnapshot();
            authUrl = statusSnapshot["url"]?.ToString();
        }

        // 这里组装启动成功后的首次响应，前端会立刻用它展示授权信息。
        var payload = new JsonObject
        {
            ["success"] = true,
            ["url"] = string.IsNullOrWhiteSpace(authUrl) ? null : authUrl,
            ["state"] = state,
            ["provider"] = normalizedProvider,
            ["callback_port"] = providerDefinition.Port,
            ["interactive"] = true,
            ["output"] = statusSnapshot["output"]?.ToString() ?? string.Empty,
            ["needs_input"] = statusSnapshot["needs_input"]?.GetValue<bool>() == true,
            ["input_prompt"] = statusSnapshot["input_prompt"]?.ToString() ?? string.Empty,
        };

        // 这里根据是否存在回调端口返回不同的操作提示。
        if (providerDefinition.Port > 0)
        {
            payload["hint"] = $"请在浏览器中打开上述链接完成认证。如果是远程服务器，请确保端口 {providerDefinition.Port} 可访问（可使用 SSH 端口转发: ssh -L {providerDefinition.Port}:localhost:{providerDefinition.Port} user@server）";
        }
        else
        {
            payload["hint"] = "请查看下方输出，按提示继续完成设备码认证或交互输入。";
        }

        return (StatusCodes.Status200OK, payload);
    }

    /// <summary>
    /// 查询指定 OAuth 会话的状态。
    /// </summary>
    public Task<(int StatusCode, JsonObject Payload)> GetStatusAsync(string state, CancellationToken cancellationToken = default)
    {
        // 这里先响应取消请求。
        cancellationToken.ThrowIfCancellationRequested();

        // 这里在缺少 state 时直接返回参数错误。
        if (string.IsNullOrWhiteSpace(state))
        {
            return Task.FromResult((StatusCodes.Status400BadRequest, new JsonObject { ["error"] = "缺少 state 参数" }));
        }

        // 这里在找不到会话时返回 unknown，前端可据此停止轮询。
        if (!_sessions.TryGetValue(state, out var session))
        {
            return Task.FromResult((StatusCodes.Status404NotFound, new JsonObject { ["status"] = "unknown", ["error"] = "会话不存在" }));
        }

        // 这里读取一次状态快照，后续所有判断都基于同一份会话状态。
        var snapshot = session.GetStatusSnapshot();
        var status = snapshot["status"]?.ToString() ?? "unknown";
        var output = snapshot["output"]?.ToString() ?? string.Empty;
        var error = snapshot["error"]?.ToString() ?? string.Empty;
        var needsInput = snapshot["needs_input"]?.GetValue<bool>() == true;
        var inputPrompt = snapshot["input_prompt"]?.ToString() ?? string.Empty;
        var url = snapshot["url"]?.ToString();

        // 这里在认证成功后立即移除并释放会话，避免状态字典长期堆积。
        if (string.Equals(status, "ok", StringComparison.OrdinalIgnoreCase))
        {
            _sessions.TryRemove(state, out _);
            session.Dispose();
            return Task.FromResult((StatusCodes.Status200OK, new JsonObject { ["status"] = "ok", ["output"] = output.Length > 500 ? output[^500..] : output }));
        }

        // 这里把会话错误信息直接透传给前端，便于用户排查失败原因。
        if (string.Equals(status, "error", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult((StatusCodes.Status200OK, new JsonObject { ["status"] = "error", ["error"] = string.IsNullOrWhiteSpace(error) ? "认证失败" : error, ["output"] = output.Length > 500 ? output[^500..] : output }));
        }

        // 这里在 CLI 需要用户补充输入时，明确返回 needs_input 状态。
        if (string.Equals(status, "needs_input", StringComparison.OrdinalIgnoreCase) || needsInput)
        {
            return Task.FromResult((StatusCodes.Status200OK, new JsonObject { ["status"] = "needs_input", ["needs_input"] = true, ["input_prompt"] = inputPrompt, ["output"] = output.Length > 1000 ? output[^1000..] : output, ["url"] = string.IsNullOrWhiteSpace(url) ? null : url }));
        }

        // 这里在认证尚未完成时返回等待态，并附带最近输出和授权链接。
        return Task.FromResult((StatusCodes.Status200OK, new JsonObject { ["status"] = "wait", ["detail"] = status, ["output"] = output.Length > 500 ? output[^500..] : output, ["url"] = string.IsNullOrWhiteSpace(url) ? null : url, ["needs_input"] = needsInput, ["input_prompt"] = inputPrompt }));
    }

    /// <summary>
    /// 获取指定会话的完整输出内容。
    /// </summary>
    public Task<(int StatusCode, JsonObject Payload)> GetOutputAsync(string state, CancellationToken cancellationToken = default)
    {
        // 这里先响应取消请求。
        cancellationToken.ThrowIfCancellationRequested();

        // 这里在缺少 state 时直接返回参数错误。
        if (string.IsNullOrWhiteSpace(state))
        {
            return Task.FromResult((StatusCodes.Status400BadRequest, new JsonObject { ["error"] = "缺少 state 参数" }));
        }

        // 这里先确认状态字典中仍然存在该会话。
        if (!_sessions.TryGetValue(state, out var session))
        {
            return Task.FromResult((StatusCodes.Status404NotFound, new JsonObject { ["error"] = "会话不存在" }));
        }

        // 这里返回当前会话已收集到的完整输出，供前端调试查看。
        return Task.FromResult((StatusCodes.Status200OK, new JsonObject { ["output"] = session.GetFullOutput(), ["state"] = state }));
    }

    /// <summary>
    /// 向会话中的交互式命令发送输入。
    /// </summary>
    public Task<(int StatusCode, JsonObject Payload)> SendInputAsync(string state, string? userInput, CancellationToken cancellationToken = default)
    {
        // 这里先响应取消请求，避免已取消请求继续操作子进程。
        cancellationToken.ThrowIfCancellationRequested();

        // 这里在缺少 state 时直接返回参数错误。
        if (string.IsNullOrWhiteSpace(state))
        {
            return Task.FromResult((StatusCodes.Status400BadRequest, new JsonObject { ["error"] = "缺少 state 参数" }));
        }

        // 这里先确认 state 对应的会话仍然存在。
        if (!_sessions.TryGetValue(state, out var session))
        {
            return Task.FromResult((StatusCodes.Status404NotFound, new JsonObject { ["error"] = "会话不存在" }));
        }

        // 这里把用户输入转发给底层 CLI 会话。
        if (session.SendInput(userInput ?? string.Empty))
        {
            return Task.FromResult((StatusCodes.Status200OK, new JsonObject { ["success"] = true, ["message"] = "输入已发送", ["state"] = state }));
        }

        return Task.FromResult((StatusCodes.Status500InternalServerError, new JsonObject { ["error"] = $"发送输入失败: {session.Error ?? "未知错误"}", ["state"] = state }));
    }

    /// <summary>
    /// 取消指定的 OAuth 会话。
    /// </summary>
    public Task<(int StatusCode, JsonObject Payload)> CancelAsync(string state, CancellationToken cancellationToken = default)
    {
        // 这里先响应取消请求，避免无效操作继续执行。
        cancellationToken.ThrowIfCancellationRequested();

        // 这里在缺少 state 时直接返回参数错误。
        if (string.IsNullOrWhiteSpace(state))
        {
            return Task.FromResult((StatusCodes.Status400BadRequest, new JsonObject { ["error"] = "缺少 state 参数" }));
        }

        // 这里把会话从字典中移除并释放进程资源。
        if (_sessions.TryRemove(state, out var session))
        {
            session.Dispose();
        }

        return Task.FromResult((StatusCodes.Status200OK, new JsonObject { ["success"] = true, ["message"] = "会话已取消" }));
    }

    /// <summary>
    /// 表示单个 OAuth 交互会话。
    /// </summary>
    private sealed class InteractiveOAuthSession : IDisposable
    {
        /// <summary>
        /// 保存 URL 匹配正则。
        /// </summary>
        private static readonly Regex UrlPattern = new("(https?://[^\\s\\x00-\\x1f<>\"'`]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// 保存 ANSI 控制字符清理正则。
        /// </summary>
        private static readonly Regex AnsiPattern = new("\\x1b\\[[0-9;]*[a-zA-Z]|\\x1b\\][^\\x07]*\\x07|\\x1b[()][AB012]", RegexOptions.Compiled);

        /// <summary>
        /// 保存输入提示关键字列表。
        /// </summary>
        private static readonly string[] InputPrompts =
        {
            "Paste the antigravity callback URL",
            "paste the callback URL",
            "callback URL",
            "press Enter to keep waiting",
            "Enter project ID",
            "or ALL:",
            "Available Google Cloud projects",
            "Type 'ALL' to onboard",
            "Enter choice [1]:",
            "Which project ID would you like",
            "[1] Backend (recommended)",
            "[2] Frontend:",
            "Enter 1 or 2",
            "Enter choice",
            "Enter your choice",
            "Please paste",
            "paste the URL",
            "输入项目",
            "选择",
        };

        /// <summary>
        /// 保存认证成功关键字列表。
        /// </summary>
        private static readonly string[] SuccessKeywords =
        {
            "Authentication saved",
            "Gemini authentication successful!",
            "Codex authentication successful!",
            "Claude authentication successful!",
            "Qwen authentication successful!",
            "iFlow authentication successful!",
            "Kimi authentication successful!",
            "Antigravity authentication successful!",
            "saved to",
        };

        /// <summary>
        /// 保存认证 URL 允许的域名关键字。
        /// </summary>
        private static readonly string[] OAuthDomains =
        {
            "accounts.google.com",
            "console.anthropic.com",
            "auth.openai.com",
            "qwen.ai",
            "kimi.com",
            "oauth",
            "login",
            "auth0.com",
        };

        /// <summary>
        /// 保存会话状态锁。
        /// </summary>
        private readonly object _syncRoot = new();

        /// <summary>
        /// 保存日志实例。
        /// </summary>
        private readonly ILogger _logger;

        /// <summary>
        /// 保存待执行命令。
        /// </summary>
        private readonly string[] _command;

        /// <summary>
        /// 保存工作目录。
        /// </summary>
        private readonly string _workingDirectory;

        /// <summary>
        /// 保存输出缓冲区。
        /// </summary>
        private readonly StringBuilder _output = new();

        /// <summary>
        /// 保存当前进程实例。
        /// </summary>
        private Process? _process;

        /// <summary>
        /// 保存当前状态值。
        /// </summary>
        private string _status = "starting";

        /// <summary>
        /// 保存提取到的认证链接。
        /// </summary>
        private string? _url;

        /// <summary>
        /// 保存错误信息。
        /// </summary>
        public string? Error { get; private set; }

        /// <summary>
        /// 保存是否需要用户输入。
        /// </summary>
        private bool _needsInput;

        /// <summary>
        /// 保存当前输入提示文本。
        /// </summary>
        private string _inputPrompt = string.Empty;

        /// <summary>
        /// 保存是否已经完成。
        /// </summary>
        private bool _completed;

        /// <summary>
        /// 使用会话参数初始化交互式 OAuth 会话。
        /// </summary>
        public InteractiveOAuthSession(string state, string provider, string[] command, string workingDirectory, ILogger logger)
        {
            State = state;
            Provider = provider;
            _command = command;
            _workingDirectory = workingDirectory;
            _logger = logger;
        }

        /// <summary>
        /// 保存会话唯一标识。
        /// </summary>
        public string State { get; }

        /// <summary>
        /// 保存 Provider 名称。
        /// </summary>
        public string Provider { get; }

        /// <summary>
        /// 启动进程并开始监听输出。
        /// </summary>
        public bool Start()
        {
            try
            {
                // 这里配置一个可交互但不可见的子进程，用来承载 CLI OAuth 流程。
                var startInfo = new ProcessStartInfo(_command[0])
                {
                    WorkingDirectory = _workingDirectory,
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };

                // 这里把命令参数逐个加入 ArgumentList，避免手写拼接命令行。
                foreach (var argument in _command.Skip(1))
                {
                    startInfo.ArgumentList.Add(argument);
                }

                // 这里创建进程对象并立即启动 OAuth 命令。
                _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
                _process.Start();
                _status = "running";

                // 这里启动后台任务分别监听标准输出、标准错误和进程退出。
                _ = ReadOutputAsync(_process.StandardOutput);
                _ = ReadOutputAsync(_process.StandardError);
                _ = MonitorExitAsync(_process);
                return true;
            }
            // 这里把启动异常记录到 Error，供上层接口直接返回。
            catch (Exception exception)
            {
                _status = "error";
                Error = exception.Message;
                return false;
            }
        }

        /// <summary>
        /// 获取供接口返回使用的状态快照。
        /// </summary>
        public JsonObject GetStatusSnapshot()
        {
            // 这里在锁内读取所有状态字段，确保快照彼此一致。
            lock (_syncRoot)
            {
                // 这里截取最近 2000 字输出，避免状态接口返回过大的日志。
                var output = _output.ToString();
                return new JsonObject
                {
                    ["status"] = _status,
                    ["url"] = string.IsNullOrWhiteSpace(_url) ? null : _url,
                    ["error"] = string.IsNullOrWhiteSpace(Error) ? null : Error,
                    ["output"] = output.Length > 2000 ? output[^2000..] : output,
                    ["needs_input"] = _needsInput,
                    ["input_prompt"] = _inputPrompt,
                    ["completed"] = _completed,
                };
            }
        }

        /// <summary>
        /// 获取完整输出内容。
        /// </summary>
        public string GetFullOutput()
        {
            lock (_syncRoot)
            {
                return _output.ToString();
            }
        }

        /// <summary>
        /// 向子进程发送输入内容。
        /// </summary>
        public bool SendInput(string text)
        {
            // 这里先确保进程仍然存在且会话尚未结束。
            if (_process is null || _completed)
            {
                return false;
            }

            try
            {
                // 这里自动补换行，模拟用户在终端按下回车。
                if (!text.EndsWith(Environment.NewLine, StringComparison.Ordinal))
                {
                    text += Environment.NewLine;
                }

                // 这里立即写入并刷新标准输入，让 CLI 立刻收到用户输入。
                _process.StandardInput.Write(text);
                _process.StandardInput.Flush();

                // 这里在成功发送输入后清空等待输入状态，恢复 running。
                lock (_syncRoot)
                {
                    _needsInput = false;
                    _inputPrompt = string.Empty;
                    _status = "running";
                }

                return true;
            }
            // 这里在写入失败时把错误信息保存到会话上。
            catch (Exception exception)
            {
                Error = exception.Message;
                return false;
            }
        }

        /// <summary>
        /// 释放会话占用的进程资源。
        /// </summary>
        public void Dispose()
        {
            // 这里加锁把会话标记为已完成，阻止后续继续读取输出。
            lock (_syncRoot)
            {
                _completed = true;
            }

            if (_process is null)
            {
                return;
            }

            try
            {
                // 这里在进程仍存活时主动终止整个进程树，避免残留子进程。
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "终止 OAuth 进程失败。");
            }

            _process.Dispose();
            _process = null;
        }

        /// <summary>
        /// 监控进程退出状态并同步会话结果。
        /// </summary>
        private async Task MonitorExitAsync(Process process)
        {
            try
            {
                // 这里等待 OAuth 子进程自然退出。
                await process.WaitForExitAsync();

                // 这里在进程结束后加锁更新完成状态。
                lock (_syncRoot)
                {
                    _completed = true;

                    // 这里根据退出码修正最终状态，补齐只靠输出关键字无法覆盖的场景。
                    if (process.ExitCode == 0)
                    {
                        if (!string.Equals(_status, "ok", StringComparison.OrdinalIgnoreCase))
                        {
                            _status = "ok";
                        }
                    }
                    else if (!string.Equals(_status, "ok", StringComparison.OrdinalIgnoreCase) && !string.Equals(_status, "error", StringComparison.OrdinalIgnoreCase))
                    {
                        _status = "error";
                        Error = $"进程退出码: {process.ExitCode}";
                    }
                }
            }
            // 这里在等待进程退出出错时，把会话标记成 error。
            catch (Exception exception)
            {
                lock (_syncRoot)
                {
                    _status = "error";
                    Error = exception.Message;
                    _completed = true;
                }
            }
        }

        /// <summary>
        /// 持续读取命令输出并驱动状态机。
        /// </summary>
        private async Task ReadOutputAsync(StreamReader reader)
        {
            // 这里使用单字符缓冲区，尽量贴近交互式命令的实时输出节奏。
            var buffer = new char[1];

            // 这里在会话未结束前持续监听输出。
            while (!_completed)
            {
                try
                {
                    // 这里按字符持续读取输出，尽量实时捕获授权链接和交互提示。
                    var count = await reader.ReadAsync(buffer, 0, 1);

                    // 这里在暂时读不到字符时判断进程是否已经结束。
                    if (count <= 0)
                    {
                        if (_process?.HasExited == true)
                        {
                            break;
                        }

                        await Task.Delay(50);
                        continue;
                    }

                    // 这里先去掉 ANSI 控制字符，再把纯文本交给状态机解析。
                    var chunk = AnsiPattern.Replace(new string(buffer, 0, count), string.Empty);
                    ProcessOutput(chunk);
                }
                // 这里在读取异常时结束当前读取循环，交给退出监控处理最终状态。
                catch
                {
                    break;
                }
            }
        }

        /// <summary>
        /// 解析单段输出文本并更新会话状态。
        /// </summary>
        private void ProcessOutput(string chunk)
        {
            // 这里忽略空输出片段，避免无意义状态刷新。
            if (string.IsNullOrEmpty(chunk))
            {
                return;
            }

            // 这里在加锁后追加输出文本，避免多个读取任务并发改写状态。
            lock (_syncRoot)
            {
                _output.Append(chunk);
                var outputText = _output.ToString();

                // 这里先检查是否出现认证成功关键词，一旦命中就结束会话。
                foreach (var successKeyword in SuccessKeywords)
                {
                    if (outputText.Contains(successKeyword, StringComparison.OrdinalIgnoreCase))
                    {
                        _status = "ok";
                        _completed = true;
                        _needsInput = false;
                        _inputPrompt = string.Empty;
                        return;
                    }
                }

                // 这里识别 CLI 输出里的交互提示，通知前端展示输入框。
                foreach (var inputPrompt in InputPrompts)
                {
                    if (outputText.Contains(inputPrompt, StringComparison.OrdinalIgnoreCase))
                    {
                        _needsInput = true;
                        _inputPrompt = inputPrompt;
                        _status = "needs_input";
                    }
                }

                // 这里从完整输出中持续提取最新的授权链接。
                var matches = UrlPattern.Matches(outputText);

                if (matches.Count > 0)
                {
                    var latestUrl = matches[^1].Value.TrimEnd(')');

                    // 这里仅接受真正的 OAuth 域名链接，避免把普通日志中的 URL 误识别成授权地址。
                    if (OAuthDomains.Any(domain => latestUrl.Contains(domain, StringComparison.OrdinalIgnoreCase)))
                    {
                        _url = latestUrl;

                        // 这里在已经拿到授权链接但还未完成回调时，保持 waiting_callback 状态。
                        if (!_needsInput)
                        {
                            _status = "waiting_callback";
                        }
                    }
                }
            }
        }
    }
}
