using GB_CadAndSWPlus_V.FunctionalMethod;
using GB_CadAndSWPlus_V.UniFiedStandards;
using GB_CadAndSWPlus_V.Shared.Models;
using GB_CadAndSWPlus_V.Shared.Services;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GB_CadAndSWPlus_V.Helpers
{
    /// <summary>
    /// CAD 对共享登录窗口的适配器。
    /// </summary>
    public sealed class UnifiedCadLoginService : IUnifiedLoginService
    {
        private readonly AuthUserDepartmentApiService _authService;
        private readonly DepartmentApiService _departmentService;

        public UnifiedCadLoginService(
            AuthUserDepartmentApiService authService = null,
            DepartmentApiService departmentService = null)
        {
            _authService = authService ?? new AuthUserDepartmentApiService();
            _departmentService = departmentService ?? new DepartmentApiService();
        }

        public async Task<UnifiedLoginResult> LoginAsync(
            UnifiedLoginRequest request,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.ServerHost))
                throw new InvalidOperationException("请填写服务器地址。");

            // 统一更新 CAD 全局配置，后续所有 API 请求都使用登录页面输入的端口。
            VariableDictionary._serverIP = request.ServerHost.Trim();
            VariableDictionary._apiPort = request.ApiPort;
            VariableDictionary._userName = request.Username?.Trim();
            VariableDictionary._passWord = request.Password ?? string.Empty;
            VariableDictionary._databaseType = string.IsNullOrWhiteSpace(request.DatabaseType)
                ? "DM"
                : request.DatabaseType.Trim().ToUpperInvariant();
            VariableDictionary._dataBaseServerPort = request.DatabasePort.GetValueOrDefault(
                string.Equals(VariableDictionary._databaseType, "MYSQL", StringComparison.OrdinalIgnoreCase)
                    ? 3308
                    : 5236);

            AuthUserDepartmentApiService.LoginResponse authResult = await _authService.AuthenticateWithSessionAsync(
                VariableDictionary._userName,
                VariableDictionary._passWord,
                cancellationToken).ConfigureAwait(false);

            return new UnifiedLoginResult
            {
                Success = authResult.Success,
                Message = authResult.Success ? "登录成功。" : authResult.Message,
                DisplayName = authResult.User?.DisplayName ?? VariableDictionary._userName ?? string.Empty,
                UserId = authResult.User?.Id,
                DepartmentId = authResult.User?.DepartmentId,
                DepartmentName = authResult.User?.DepartmentName ?? string.Empty,
                AccessToken = authResult.AccessToken ?? string.Empty,
                AccessTokenExpiresAtUtc = authResult.AccessTokenExpiresAtUtc,
                PlatformSession = authResult.Success ? VariableDictionary._userName : null
            };
        }

        public Task<UnifiedLoginResult> LoginWithSharedSessionAsync(
            SharedLoginSession session,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var request = new SharedLoginSessionStore().ToLoginRequest(session, UnifiedLoginPlatform.Cad);
            return LoginAsync(request, cancellationToken);
        }

        public async Task<IReadOnlyList<UnifiedDepartmentOption>> LoadDepartmentsAsync(
            UnifiedLoginRequest request,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            VariableDictionary._serverIP = request.ServerHost?.Trim();
            VariableDictionary._apiPort = request.ApiPort;
            List<DepartmentModel> departments = await _departmentService
                .GetDepartmentsWithCountsAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = new List<UnifiedDepartmentOption>();
            foreach (DepartmentModel department in departments)
            {
                result.Add(new UnifiedDepartmentOption
                {
                    Id = department.Id,
                    DisplayName = string.IsNullOrWhiteSpace(department.DisplayName)
                        ? department.Name
                        : department.DisplayName
                });
            }

            return result;
        }
    }
}
