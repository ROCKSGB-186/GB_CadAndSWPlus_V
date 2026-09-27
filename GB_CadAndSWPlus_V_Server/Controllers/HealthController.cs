using GB_CadAndSWPlus_V.UploadApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace GB_CadAndSWPlus_V.UploadApi.Controllers;

/// <summary>
/// 服务健康检查接口。
/// </summary>
[ApiController]
[Route("api/health")]
public sealed class HealthController : ControllerBase
{
    private readonly SystemConfigService _systemConfigService;
    private readonly ILogger<HealthController> _logger;

    public HealthController(SystemConfigService systemConfigService, ILogger<HealthController> logger)
    {
        _systemConfigService = systemConfigService ?? throw new ArgumentNullException(nameof(systemConfigService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 检查 API 进程和服务器端 DM 连接。
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAsync(CancellationToken cancellationToken)
    {
        bool databaseAvailable = await _systemConfigService.CheckDatabaseAsync(cancellationToken).ConfigureAwait(false);
        if (!databaseAvailable)
        {
            _logger.LogWarning("健康检查发现 DM 数据库不可用。");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                success = false,
                service = "ok",
                database = "unavailable"
            });
        }

        return Ok(new
        {
            success = true,
            service = "ok",
            database = "ok"
        });
    }
}
