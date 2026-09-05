using GB_CadAndSWPlus_V.UploadApi.Filters;
using GB_CadAndSWPlus_V.UploadApi.Models;
using GB_CadAndSWPlus_V.UploadApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace GB_CadAndSWPlus_V.UploadApi.Controllers;

[ApiController]
[Route("api/departments")]
[ServiceFilter(typeof(OperationLogFilter))]
public sealed class DepartmentCommandController : ControllerBase
{
    private readonly AuthUserDepartmentService _service;
    public DepartmentCommandController(AuthUserDepartmentService service) => _service = service;

    [HttpPost]
    public async Task<ActionResult<MutationResponse>> AddAsync([FromBody] DepartmentMutationRequest request, CancellationToken cancellationToken)
        => Ok(await _service.AddDepartmentAsync(request, cancellationToken).ConfigureAwait(false));

    [HttpPut("{id:int}")]
    public async Task<ActionResult<MutationResponse>> UpdateAsync(int id, [FromBody] DepartmentMutationRequest request, CancellationToken cancellationToken)
        => Ok(await _service.UpdateDepartmentAsync(id, request, cancellationToken).ConfigureAwait(false));

    [HttpDelete("{id:int}")]
    public async Task<ActionResult<MutationResponse>> DeleteAsync(int id, CancellationToken cancellationToken)
        => Ok(await _service.DeleteDepartmentAsync(id, cancellationToken).ConfigureAwait(false));

    [HttpPost("sync-from-categories")]
    public async Task<ActionResult<MutationResponse>> SyncFromCategoriesAsync(CancellationToken cancellationToken)
        => Ok(await _service.SyncDepartmentsFromCategoriesAsync(cancellationToken).ConfigureAwait(false));
}
