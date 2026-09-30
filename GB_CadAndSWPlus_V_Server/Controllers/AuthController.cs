using GB_CadAndSWPlus_V.UploadApi.Filters;
using GB_CadAndSWPlus_V.UploadApi.Models;
using GB_CadAndSWPlus_V.UploadApi.Services;
using Dm;
using Microsoft.AspNetCore.Mvc;

namespace GB_CadAndSWPlus_V.UploadApi.Controllers;

[ApiController]
[Route("api/auth")]
[ServiceFilter(typeof(OperationLogFilter))]
public sealed class AuthController : ControllerBase
{
    private readonly AuthUserDepartmentService _service;
    private readonly ILogger<AuthController> _logger;

    public AuthController(AuthUserDepartmentService service, ILogger<AuthController> logger)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> LoginAsync([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _service.LoginAsync(request, cancellationToken).ConfigureAwait(false));
        }
        catch (DmException ex)
        {
            _logger.LogError(ex, "登录时连接达梦数据库失败。DatabaseType=DM");
            return StatusCode(503, new
            {
                success = false,
                message = "数据库服务暂时不可用，请稍后重试。"
            });
        }
        catch (Exception ex)
        {
            // MySQL 和其他数据库异常也统一转换为安全的 503 响应，避免客户端收到无上下文的 500。
            _logger.LogError(ex, "登录接口执行失败。用户名={Username}", request?.Username);
            return StatusCode(503, new
            {
                success = false,
                message = "认证服务或数据库暂时不可用，请检查服务器配置和数据库状态。"
            });
        }
    }

    [HttpPost("register")]
    public async Task<ActionResult<MutationResponse>> RegisterAsync([FromBody] RegisterUserRequest request, CancellationToken cancellationToken)
        => Ok(await _service.RegisterAsync(request, cancellationToken).ConfigureAwait(false));

    [HttpPost("reset-password")]
    public async Task<ActionResult<MutationResponse>> ResetPasswordAsync([FromBody] ResetPasswordRequest request, CancellationToken cancellationToken)
        => Ok(await _service.ResetPasswordAsync(request, cancellationToken).ConfigureAwait(false));

    [HttpPost("validate-session")]
    public async Task<ActionResult<SessionResponse>> ValidateSessionAsync([FromBody] ValidateSessionRequest request, CancellationToken cancellationToken)
        => Ok(await _service.ValidateSessionAsync(request.AccessToken, cancellationToken).ConfigureAwait(false));

    [HttpPost("logout")]
    public async Task<IActionResult> LogoutAsync([FromBody] ValidateSessionRequest request, CancellationToken cancellationToken)
        => Ok(new { success = await _service.RevokeSessionAsync(request.AccessToken, cancellationToken).ConfigureAwait(false) });
}
