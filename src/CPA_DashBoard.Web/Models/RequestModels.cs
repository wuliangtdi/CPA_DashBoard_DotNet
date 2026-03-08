namespace CPA_DashBoard.Web.Models;

/// <summary>
/// 表示 OAuth 交互输入接口的请求体。
/// </summary>
public sealed class OAuthInputRequest
{
    /// <summary>
    /// 保存目标会话的状态标识。
    /// </summary>
    public string State { get; set; } = string.Empty;

    /// <summary>
    /// 保存需要发送给交互式进程的输入内容。
    /// </summary>
    public string? Input { get; set; }
}

/// <summary>
/// 表示 OAuth 取消接口的请求体。
/// </summary>
public sealed class OAuthCancelRequest
{
    /// <summary>
    /// 保存待取消会话的状态标识。
    /// </summary>
    public string State { get; set; } = string.Empty;
}

/// <summary>
/// 表示日志清理接口的请求体。
/// </summary>
public sealed class LogClearRequest
{
    /// <summary>
    /// 保存是否在清空前先备份日志文件。
    /// </summary>
    public bool Backup { get; set; }
}
