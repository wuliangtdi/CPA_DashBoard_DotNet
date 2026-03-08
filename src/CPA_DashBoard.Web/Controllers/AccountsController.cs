using CPA_DashBoard.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace CPA_DashBoard.Web.Controllers;

/// <summary>
/// 负责账户列表和删除接口。
/// </summary>
[ApiController]
[Route("api/accounts")]
public sealed class AccountsController : ControllerBase
{
    /// <summary>
    /// 保存账户业务服务实例。
    /// </summary>
    private readonly AccountApiService _accountApiService;

    /// <summary>
    /// 使用账户业务服务初始化控制器。
    /// </summary>
    public AccountsController(AccountApiService accountApiService)
    {
        // 这里保存服务实例，避免控制器直接承担业务逻辑。
        _accountApiService = accountApiService;
    }

    /// <summary>
    /// 获取账户列表。
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAccountsAsync(CancellationToken cancellationToken)
    {
        // 这里调用服务层获取账户列表与缓存配额。
        var payload = await _accountApiService.GetAccountsAsync(cancellationToken);

        // 这里统一返回 JSON 结果。
        return Ok(payload);
    }

    /// <summary>
    /// 删除指定账户。
    /// </summary>
    [HttpDelete("{accountName}")]
    public async Task<IActionResult> DeleteAccountAsync(string accountName, CancellationToken cancellationToken)
    {
        // 这里调用服务层执行删除逻辑。
        var result = await _accountApiService.DeleteAccountAsync(accountName, cancellationToken);

        // 这里返回删除结果与对应状态码。
        return StatusCode(result.StatusCode, result.Payload);
    }
}
