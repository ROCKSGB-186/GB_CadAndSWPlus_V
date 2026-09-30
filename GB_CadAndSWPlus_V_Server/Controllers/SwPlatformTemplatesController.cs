using GB_CadAndSWPlus_V.UploadApi.Filters;
using GB_CadAndSWPlus_V.UploadApi.Models;
using GB_CadAndSWPlus_V.UploadApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace GB_CadAndSWPlus_V.UploadApi.Controllers;

/// <summary>
/// SolidWorks 平台模板接口。
/// CAD 客户端和 SolidWorks AddIn 都通过本接口获取统一模板信息。
/// </summary>
[ApiController]
[Route("api/sw-platform-templates")]
[ServiceFilter(typeof(OperationLogFilter))]
public sealed class SwPlatformTemplatesController : ControllerBase
{
    private readonly SwPlatformTemplateService _service;
    private readonly ILogger<SwPlatformTemplatesController> _logger;

    /// <summary>
    /// 创建 SolidWorks 平台模板控制器。
    /// </summary>
    public SwPlatformTemplatesController(
        SwPlatformTemplateService service,
        ILogger<SwPlatformTemplatesController> logger)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 查询指定平台代码对应的 SolidWorks 模板。
    /// 请求：GET /api/sw-platform-templates/{platformCode}
    /// </summary>
    [HttpGet("{platformCode}")]
    [ProducesResponseType(typeof(SwPlatformTemplateResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<SwPlatformTemplateResponse>> GetAsync(
        string platformCode,
        CancellationToken cancellationToken)
    {
        try
        {
            // 第一步：调用服务器业务服务查询模板元数据。
            SwPlatformTemplateResponse response = await _service
                .GetAsync(platformCode, cancellationToken)
                .ConfigureAwait(false);

            // 第二步：未配置模板不是服务器故障，仍返回 200，让客户端执行离线回退。
            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "SolidWorks 平台模板请求参数无效。PlatformCode={PlatformCode}", platformCode);
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new EmptyResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SolidWorks 平台模板查询失败。PlatformCode={PlatformCode}", platformCode);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                success = false,
                message = "平台模板查询失败，客户端可以继续尝试本地缓存或 Resources。"
            });
        }
    }

    /// <summary>
    /// 检查指定平台的模板附件是否有效。
    /// 请求：GET /api/sw-platform-templates/{platformCode}/binding-check
    /// </summary>
    [HttpGet("{platformCode}/binding-check")]
    [ProducesResponseType(typeof(SwPlatformTemplateBindingCheckResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SwPlatformTemplateBindingCheckResponse>> CheckBindingAsync(
        string platformCode,
        CancellationToken cancellationToken)
    {
        // 第一步：模板绑定检查属于管理操作，沿用现有管理员请求头机制。
        if (!StandardManagementAuthorization.IsAdministrator(Request, out string operatorName))
            return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = "只有 sa、SYSDBA、admin 可以检查模板绑定。" });

        try
        {
            // 第二步：执行服务器端配置和附件元数据检查，并记录操作者。
            SwPlatformTemplateBindingCheckResponse response = await _service
                .CheckBindingAsync(platformCode, cancellationToken)
                .ConfigureAwait(false);
            _logger.LogInformation("SolidWorks 平台模板绑定检查请求完成。PlatformCode={PlatformCode}, Operator={Operator}, IsUsable={IsUsable}", platformCode, operatorName, response.IsUsable);
            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "SolidWorks 平台模板绑定检查参数无效。PlatformCode={PlatformCode}", platformCode);
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new EmptyResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SolidWorks 平台模板绑定检查失败。PlatformCode={PlatformCode}", platformCode);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { success = false, message = "模板绑定检查失败，请查看服务器日志。" });
        }
    }
}
