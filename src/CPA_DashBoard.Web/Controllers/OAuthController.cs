using CPA_DashBoard.Web.Models.Requests;
using CPA_DashBoard.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace CPA_DashBoard.Web.Controllers;

/// <summary>
/// 负责 OAuth 启动、轮询、输入和取消相关接口。
/// </summary>
[ApiController]
[Route("api/accounts/auth")]
public sealed class OAuthController : ControllerBase
{
    /// <summary>
    /// 保存 OAuth 会话管理器实例。
    /// </summary>
    private readonly OAuthSessionManager _oauthSessionManager;

    /// <summary>
    /// 使用 OAuth 会话管理器初始化控制器。
    /// </summary>
    public OAuthController(OAuthSessionManager oauthSessionManager)
    {
        // 这里保存会话管理器，所有认证状态都统一由它维护。
        _oauthSessionManager = oauthSessionManager;
    }

    /// <summary>
    /// 启动指定 Provider 的 OAuth 流程。
    /// </summary>
    [HttpPost("{provider}")]
    public async Task<IActionResult> StartAsync(string provider, CancellationToken cancellationToken)
    {
        // 这里调用会话管理器启动交互式认证进程。
        var result = await _oauthSessionManager.StartAsync(provider, cancellationToken);

        // 这里输出认证启动结果。
        return StatusCode(result.StatusCode, result.Payload);
    }

    /// <summary>
    /// 查询指定 OAuth 会话状态。
    /// </summary>
    [HttpGet("status")]
    public async Task<IActionResult> GetStatusAsync([FromQuery] string state, CancellationToken cancellationToken)
    {
        // 这里调用会话管理器读取当前会话状态。
        var result = await _oauthSessionManager.GetStatusAsync(state, cancellationToken);

        // 这里返回状态查询结果。
        return StatusCode(result.StatusCode, result.Payload);
    }

    /// <summary>
    /// 获取指定会话的完整输出。
    /// </summary>
    [HttpGet("output")]
    public async Task<IActionResult> GetOutputAsync([FromQuery] string state, CancellationToken cancellationToken)
    {
        // 这里调用会话管理器读取完整输出文本。
        var result = await _oauthSessionManager.GetOutputAsync(state, cancellationToken);

        // 这里返回输出结果。
        return StatusCode(result.StatusCode, result.Payload);
    }

    /// <summary>
    /// 向交互式 OAuth 进程发送输入。
    /// </summary>
    [HttpPost("input")]
    public async Task<IActionResult> SendInputAsync([FromBody] OAuthInputRequest request, CancellationToken cancellationToken)
    {
        // 这里调用会话管理器转发用户输入。
        var result = await _oauthSessionManager.SendInputAsync(request.State, request.Input, cancellationToken);

        // 这里返回输入发送结果。
        return StatusCode(result.StatusCode, result.Payload);
    }

    /// <summary>
    /// 取消指定 OAuth 会话。
    /// </summary>
    [HttpPost("cancel")]
    public async Task<IActionResult> CancelAsync([FromBody] OAuthCancelRequest? request, [FromQuery] string? state, CancellationToken cancellationToken)
    {
        // 这里优先使用查询字符串中的 state。
        var finalState = state;

        // 这里在查询字符串为空时回退到请求体中的 state。
        if (string.IsNullOrWhiteSpace(finalState))
        {
            finalState = request?.State ?? string.Empty;
        }

        // 这里调用会话管理器取消当前会话。
        var result = await _oauthSessionManager.CancelAsync(finalState, cancellationToken);

        // 这里返回取消结果。
        return StatusCode(result.StatusCode, result.Payload);
    }
}
