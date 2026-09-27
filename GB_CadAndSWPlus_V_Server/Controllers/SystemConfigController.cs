using GB_CadAndSWPlus_V.UploadApi.Filters;
using GB_CadAndSWPlus_V.UploadApi.Models;
using GB_CadAndSWPlus_V.UploadApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace GB_CadAndSWPlus_V.UploadApi.Controllers;

/// <summary>
/// 系统配置查询和写入接口。
/// </summary>
[ApiController]
[Route("api/system-config")]
[ServiceFilter(typeof(OperationLogFilter))]
public sealed class SystemConfigController : ControllerBase
{
    private readonly SystemConfigService _service;
    private readonly ILogger<SystemConfigController> _logger;

    public SystemConfigController(SystemConfigService service, ILogger<SystemConfigController> logger)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 读取系统配置。
    /// </summary>
    [HttpGet("{key}")]
    [ProducesResponseType(typeof(SystemConfigResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<SystemConfigResponse>> GetAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _service.GetAsync(key, cancellationToken).ConfigureAwait(false));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new SystemConfigResponse { Success = false, Key = key ?? string.Empty, Message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "读取系统配置失败。Key={Key}", key);
            return StatusCode(StatusCodes.Status500InternalServerError, new SystemConfigResponse
            {
                Success = false,
                Key = key ?? string.Empty,
                Message = "系统配置读取失败，请查看服务器日志。"
            });
        }
    }

    /// <summary>
    /// 写入系统配置。
    /// </summary>
    [HttpPut("{key}")]
    [ProducesResponseType(typeof(SystemConfigMutationResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<SystemConfigMutationResponse>> SetAsync(
        string key,
        [FromBody] SetSystemConfigRequest? request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _service.SetAsync(key, request?.Value, cancellationToken).ConfigureAwait(false));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new SystemConfigMutationResponse { Success = false, Message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "写入系统配置失败。Key={Key}", key);
            return StatusCode(StatusCodes.Status500InternalServerError, new SystemConfigMutationResponse
            {
                Success = false,
                Message = "系统配置写入失败，请查看服务器日志。"
            });
        }
    }
}
