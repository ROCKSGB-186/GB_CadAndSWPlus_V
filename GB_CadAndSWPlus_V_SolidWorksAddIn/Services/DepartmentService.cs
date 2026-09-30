using GB_CadAndSWPlus_V.SolidWorksAddIn.Configuration;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.Serialization;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.Services
{
    [DataContract]
    public sealed class DepartmentListResponse
    {
        [DataMember(Name = "success")]
        public bool Success { get; set; }

        [DataMember(Name = "message")]
        public string Message { get; set; } = string.Empty;

        [DataMember(Name = "departments")]
        public List<DepartmentDto> Departments { get; set; } = new List<DepartmentDto>();
    }

    [DataContract]
    public sealed class DepartmentDto
    {
        [DataMember(Name = "id")]
        public int Id { get; set; }

        [DataMember(Name = "name")]
        public string Name { get; set; } = string.Empty;

        [DataMember(Name = "displayName")]
        public string DisplayName { get; set; } = string.Empty;

        [DataMember(Name = "description")]
        public string Description { get; set; } = string.Empty;

        [DataMember(Name = "isActive")]
        public bool IsActive { get; set; }
    }

    public sealed class DepartmentService
    {
        public Task<DepartmentListResponse> GetAsync(
            SwApiSettings settings,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return new SwApiClient(settings).GetJsonAsync<DepartmentListResponse>(
                "api/departments", cancellationToken);
        }
    }
}
