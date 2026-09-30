using GB_CadAndSWPlus_V.UploadApi.Filters;
using GB_CadAndSWPlus_V.UploadApi.Models;
using GB_CadAndSWPlus_V.UploadApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace GB_CadAndSWPlus_V.UploadApi.Controllers;

[ApiController]
[Route("api/sw-instances")]
[ServiceFilter(typeof(OperationLogFilter))]
public sealed class SwInstancesController : ControllerBase
{
    private readonly SwInstanceService _service;
    public SwInstancesController(SwInstanceService service) => _service = service;

    [HttpPost("register")]
    public async Task<ActionResult<SwInstanceResponse>> RegisterAsync([FromBody] SwInstanceRegisterRequest request, CancellationToken cancellationToken)
    {
        SwInstanceResponse response = await _service.RegisterAsync(request, cancellationToken).ConfigureAwait(false);
        return response.Conflict ? Conflict(response) : Ok(response);
    }

    [HttpGet("{swInstanceId}")]
    public Task<SwInstanceResponse> GetAsync(string swInstanceId, CancellationToken cancellationToken)
        => _service.GetAsync(swInstanceId, cancellationToken);
}
