using GB_CadAndSWPlus_V.UploadApi.Filters;
using GB_CadAndSWPlus_V.UploadApi.Models;
using GB_CadAndSWPlus_V.UploadApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace GB_CadAndSWPlus_V.UploadApi.Controllers;

[ApiController]
[Route("api/graphics")]
[ServiceFilter(typeof(OperationLogFilter))]
public sealed class GraphicsQueryController : ControllerBase
{
    private readonly GraphicQueryService _graphicQueryService;
    private readonly GraphicDetailsService _graphicDetailsService;
    private readonly ILogger<GraphicsQueryController> _logger;

    public GraphicsQueryController(
        GraphicQueryService graphicQueryService,
        GraphicDetailsService graphicDetailsService,
        ILogger<GraphicsQueryController> logger)
    {
        _graphicQueryService = graphicQueryService ?? throw new ArgumentNullException(nameof(graphicQueryService));
        _graphicDetailsService = graphicDetailsService ?? throw new ArgumentNullException(nameof(graphicDetailsService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [HttpGet("{id:int}/details")]
    [ProducesResponseType(typeof(GraphicDetailsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GraphicDetailsResponse>> GetDetailsByIdAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        try
        {
            GraphicDetailsResponse? result = await _graphicDetailsService
                .GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
            return result == null
                ? NotFound(new { success = false, message = "未找到图元记录。" })
                : Ok(result);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "按 ID 查询图元详情失败。Id={Id}", id);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { success = false, message = "图元详情查询失败，请查看服务器日志。" });
        }
    }

    [HttpGet("hash/{fileHash}/details")]
    [ProducesResponseType(typeof(GraphicDetailsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GraphicDetailsResponse>> GetDetailsByHashAsync(
        string fileHash,
        CancellationToken cancellationToken = default)
    {
        try
        {
            GraphicDetailsResponse? result = await _graphicDetailsService
                .GetByHashAsync(fileHash, cancellationToken).ConfigureAwait(false);
            return result == null
                ? NotFound(new { success = false, message = "未找到图元记录。" })
                : Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "按哈希查询图元详情失败。FileHash={FileHash}", fileHash);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { success = false, message = "图元详情查询失败，请查看服务器日志。" });
        }
    }

    [HttpGet("category/{categoryId:int}")]
    [ProducesResponseType(typeof(GraphicListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<GraphicListResponse>> GetByCategoryAsync(
        int categoryId,
        [FromQuery] string categoryType = "sub",
        CancellationToken cancellationToken = default)
    {
        try
        {
            return Ok(await _graphicQueryService.GetByCategoryAsync(categoryId, categoryType, cancellationToken).ConfigureAwait(false));
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new EmptyResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "按分类查询文件接口执行失败。CategoryId={CategoryId}, CategoryType={CategoryType}", categoryId, categoryType);
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                success = false,
                message = "文件查询失败，请查看服务器日志。"
            });
        }
    }

    [HttpGet("active")]
    [ProducesResponseType(typeof(GraphicListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<GraphicListResponse>> GetAllActiveAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return Ok(await _graphicQueryService.GetAllActiveAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new EmptyResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "查询全部有效图元失败。");
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                success = false,
                message = "文件查询失败，请查看服务器日志。"
            });
        }
    }
}
