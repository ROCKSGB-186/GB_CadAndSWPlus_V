using GB_CadAndSWPlus_V.UploadApi.Filters;
using GB_CadAndSWPlus_V.UploadApi.Models;
using GB_CadAndSWPlus_V.UploadApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace GB_CadAndSWPlus_V.UploadApi.Controllers;

/// <summary>
/// CAD 与 SolidWorks 共用构件定义接口。
/// </summary>
[ApiController]
[Route("api/elements")]
[ServiceFilter(typeof(OperationLogFilter))]
public sealed class ElementsController : ControllerBase
{
    private readonly ElementDefinitionService _service;
    private readonly ILogger<ElementsController> _logger;

    public ElementsController(ElementDefinitionService service, ILogger<ElementsController> logger)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [HttpGet]
    [ProducesResponseType(typeof(ElementDefinitionListResponse), StatusCodes.Status200OK)]
    public Task<ElementDefinitionListResponse> ListAsync(
        [FromQuery] int? categoryId,
        [FromQuery] string? platform,
        [FromQuery] string? keyword,
        [FromQuery] string? status,
        CancellationToken cancellationToken)
        => _service.ListAsync(categoryId, platform, keyword, status, cancellationToken);

    [HttpGet("{id:long}")]
    public async Task<ActionResult<ElementDefinitionResponse>> GetAsync(long id, CancellationToken cancellationToken)
    {
        try
        {
            ElementDefinitionResponse response = await _service.GetAsync(id, cancellationToken).ConfigureAwait(false);
            return response.Success ? Ok(response) : NotFound(response);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return BadRequest(new ElementDefinitionResponse { Success = false, Message = ex.Message });
        }
    }

    [HttpPost]
    public async Task<ActionResult<ElementDefinitionResponse>> CreateAsync(
        [FromBody] ElementDefinitionRequest request,
        CancellationToken cancellationToken)
    {
        if (!StandardManagementAuthorization.IsAdministrator(Request, out string operatorName))
            return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = "只有管理员可以新增构件。" });

        try
        {
            return Ok(await _service.CreateAsync(request, operatorName, cancellationToken).ConfigureAwait(false));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ElementDefinitionResponse { Success = false, Message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new ElementDefinitionResponse { Success = false, Message = ex.Message });
        }
    }

    [HttpPut("{id:long}")]
    public async Task<ActionResult<ElementDefinitionResponse>> UpdateAsync(
        long id,
        [FromBody] ElementDefinitionRequest request,
        CancellationToken cancellationToken)
    {
        if (!StandardManagementAuthorization.IsAdministrator(Request, out string operatorName))
            return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = "只有管理员可以修改构件。" });

        try
        {
            ElementDefinitionResponse response = await _service.UpdateAsync(id, request, operatorName, cancellationToken).ConfigureAwait(false);
            return response.Success ? Ok(response) : NotFound(response);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ElementDefinitionResponse { Success = false, Message = ex.Message });
        }
    }

    [HttpDelete("{id:long}")]
    public async Task<ActionResult> DeleteAsync(long id, CancellationToken cancellationToken)
    {
        if (!StandardManagementAuthorization.IsAdministrator(Request, out _))
            return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = "只有管理员可以归档构件。" });

        try
        {
            bool success = await _service.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
            return success ? Ok(new { success = true, message = "构件已归档。" }) : NotFound(new { success = false, message = "构件不存在。" });
        }
        catch (ArgumentOutOfRangeException ex)
        {
            _logger.LogWarning(ex, "归档构件参数无效。ElementId={ElementId}", id);
            return BadRequest(new { success = false, message = ex.Message });
        }
    }
}
