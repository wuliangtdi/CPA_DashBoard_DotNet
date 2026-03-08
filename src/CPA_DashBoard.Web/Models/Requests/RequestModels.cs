namespace CPA_DashBoard.Web.Models.Requests;

/// <summary>
/// 表示 OAuth 输入接口的请求体。
/// </summary>
public sealed class OAuthInputRequest
{
    /// <summary>
    /// 保存 OAuth 会话状态标识。
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
    /// 保存需要取消的会话状态标识。
    /// </summary>
    public string State { get; set; } = string.Empty;
}

/// <summary>
/// 表示日志清空接口的请求体。
/// </summary>
public sealed class ClearLogsRequest
{
    /// <summary>
    /// 保存是否需要先备份日志文件。
    /// </summary>
    public bool Backup { get; set; }
}
