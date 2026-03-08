using CPA_DashBoard.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace CPA_DashBoard.Web.Controllers;

/// <summary>
/// 负责账户配额刷新相关接口。
/// </summary>
[ApiController]
[Route("api/accounts")]
public sealed class AccountQuotasController : ControllerBase
{
    /// <summary>
    /// 保存账户业务服务实例。
    /// </summary>
    private readonly AccountApiService _accountApiService;

    /// <summary>
    /// 使用账户业务服务初始化控制器。
    /// </summary>
    public AccountQuotasController(AccountApiService accountApiService)
    {
        // 这里保存服务实例，供配额刷新接口复用统一业务逻辑。
        _accountApiService = accountApiService;
    }

    /// <summary>
    /// 刷新单个账户配额。
    /// </summary>
    [HttpPost("{accountId}/quota")]
    public async Task<IActionResult> RefreshAccountQuotaAsync(string accountId, CancellationToken cancellationToken)
    {
        // 这里调用服务层刷新单个账户配额。
        var result = await _accountApiService.RefreshAccountQuotaAsync(accountId, cancellationToken);

        // 这里按照服务层返回的状态码原样输出结果。
        return StatusCode(result.StatusCode, result.Payload);
    }

    /// <summary>
    /// 刷新全部账户配额。
    /// </summary>
    [HttpPost("quota/refresh-all")]
    public async Task<IActionResult> RefreshAllQuotasAsync(CancellationToken cancellationToken)
    {
        // 这里调用服务层顺序刷新全部账户配额。
        var payload = await _accountApiService.RefreshAllQuotasAsync(cancellationToken);

        // 这里返回批量刷新结果。
        return Ok(payload);
    }
}
