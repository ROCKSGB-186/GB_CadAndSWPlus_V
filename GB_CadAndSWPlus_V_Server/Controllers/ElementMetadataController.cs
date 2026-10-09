using GB_CadAndSWPlus_V.UploadApi.Filters;
using GB_CadAndSWPlus_V.UploadApi.Models;
using GB_CadAndSWPlus_V.UploadApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace GB_CadAndSWPlus_V.UploadApi.Controllers;

/// <summary>统一构件属性查询和保存接口。</summary>
[ApiController]
[Route("api/elements/{elementId:long}/properties")]
[ServiceFilter(typeof(OperationLogFilter))]
public sealed class ElementMetadataController : ControllerBase
{
    private readonly ElementMetadataService _service;

    public ElementMetadataController(ElementMetadataService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
    }

    [HttpGet]
    public async Task<ActionResult<ElementMetadataResponse<IReadOnlyList<ElementPropertyValueDto>>>> ListAsync(long elementId, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _service.ListAsync(elementId, cancellationToken).ConfigureAwait(false));
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return BadRequest(new ElementMetadataResponse<IReadOnlyList<ElementPropertyValueDto>> { Success = false, Message = ex.Message });
        }
    }

    [HttpPut]
    public async Task<ActionResult<ElementMetadataResponse<object>>> SaveAsync(long elementId, [FromBody] IReadOnlyList<ElementPropertyValueRequest> values, CancellationToken cancellationToken)
    {
        if (!StandardManagementAuthorization.IsAdministrator(Request, out string operatorName))
            return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = "只有管理员可以保存构件属性。" });
        try
        {
            return Ok(await _service.SaveAsync(elementId, values ?? Array.Empty<ElementPropertyValueRequest>(), operatorName, cancellationToken).ConfigureAwait(false));
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return BadRequest(new ElementMetadataResponse<object> { Success = false, Message = ex.Message });
        }
    }
}
