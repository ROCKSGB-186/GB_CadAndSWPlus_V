using GB_CadAndSWPlus_V.UploadApi.Filters;
using GB_CadAndSWPlus_V.UploadApi.Models;
using GB_CadAndSWPlus_V.UploadApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace GB_CadAndSWPlus_V.UploadApi.Controllers;

/// <summary>
/// 图层字典基础设施接口。
/// </summary>
[ApiController]
[Route("api/layer-dictionary")]
[ServiceFilter(typeof(OperationLogFilter))]
public sealed class LayerDictionaryController : ControllerBase
{
    private readonly LayerDictionaryInitializationService _initializationService;
    private readonly LayerDictionaryService _layerDictionaryService;
    private readonly ILogger<LayerDictionaryController> _logger;

    public LayerDictionaryController(
        LayerDictionaryInitializationService initializationService,
        LayerDictionaryService layerDictionaryService,
        ILogger<LayerDictionaryController> logger)
    {
        _initializationService = initializationService ?? throw new ArgumentNullException(nameof(initializationService));
        _layerDictionaryService = layerDictionaryService ?? throw new ArgumentNullException(nameof(layerDictionaryService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 确保服务器端图层字典表已创建。
    /// 请求：POST /api/layer-dictionary/initialize
    /// </summary>
    [HttpPost("initialize")]
    [ProducesResponseType(typeof(LayerDictionaryInitializationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<LayerDictionaryInitializationResponse>> InitializeAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            LayerDictionaryInitializationResponse response = await _initializationService
                .EnsureTableAsync(cancellationToken)
                .ConfigureAwait(false);

            return response.Success ? Ok(response) : StatusCode(StatusCodes.Status500InternalServerError, response);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new EmptyResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "图层字典初始化接口执行失败。");
            return StatusCode(StatusCodes.Status500InternalServerError, new LayerDictionaryInitializationResponse
            {
                Success = false,
                Message = "图层字典初始化失败，请查看服务器日志。"
            });
        }
    }

    /// <summary>
    /// 获取指定用户的个人图层字典。
    /// </summary>
    [HttpGet("personal")]
    [ProducesResponseType(typeof(List<LayerDictionaryEntryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<LayerDictionaryEntryDto>>> GetPersonalAsync(
        [FromQuery] string username,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _layerDictionaryService.GetPersonalAsync(username, cancellationToken).ConfigureAwait(false));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "查询个人图层字典失败。Username={Username}", username);
            return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "查询个人图层字典失败。" });
        }
    }

    /// <summary>
    /// 获取管理员发布的标准图层字典，可按专业过滤。
    /// </summary>
    [HttpGet("standard")]
    [ProducesResponseType(typeof(List<LayerDictionaryEntryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<LayerDictionaryEntryDto>>> GetStandardAsync(
        [FromQuery] string? major,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _layerDictionaryService.GetStandardAsync(major, cancellationToken).ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "查询标准图层字典失败。Major={Major}", major);
            return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "查询标准图层字典失败。" });
        }
    }

    /// <summary>
    /// 覆盖保存指定用户的个人图层字典。
    /// </summary>
    [HttpPost("personal")]
    [ProducesResponseType(typeof(LayerDictionaryMutationResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<LayerDictionaryMutationResponse>> SavePersonalAsync(
        [FromBody] SaveLayerDictionaryRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _layerDictionaryService.SavePersonalAsync(request, cancellationToken).ConfigureAwait(false));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new LayerDictionaryMutationResponse { Success = false, Message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存个人图层字典失败。Username={Username}", request?.Username);
            return StatusCode(StatusCodes.Status500InternalServerError, new LayerDictionaryMutationResponse
            {
                Success = false,
                Message = "保存个人图层字典失败。"
            });
        }
    }

    /// <summary>
    /// 按 ID 批量删除图层字典记录。
    /// </summary>
    [HttpDelete("entries")]
    [ProducesResponseType(typeof(LayerDictionaryMutationResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<LayerDictionaryMutationResponse>> DeleteAsync(
        [FromBody] DeleteLayerDictionaryRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _layerDictionaryService.DeleteAsync(request?.Ids ?? new List<int>(), cancellationToken).ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "删除图层字典失败。");
            return StatusCode(StatusCodes.Status500InternalServerError, new LayerDictionaryMutationResponse
            {
                Success = false,
                Message = "删除图层字典失败。"
            });
        }
    }
}
