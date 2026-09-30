using GB_CadAndSWPlus_V.Shared.Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GB_CadAndSWPlus_V.Shared.Services
{
    public interface IUnifiedLoginService
    {
        Task<UnifiedLoginResult> LoginAsync(UnifiedLoginRequest request, CancellationToken cancellationToken = default(CancellationToken));
        Task<UnifiedLoginResult> LoginWithSharedSessionAsync(SharedLoginSession session, CancellationToken cancellationToken = default(CancellationToken));
        Task<IReadOnlyList<UnifiedDepartmentOption>> LoadDepartmentsAsync(UnifiedLoginRequest request, CancellationToken cancellationToken = default(CancellationToken));
    }
}