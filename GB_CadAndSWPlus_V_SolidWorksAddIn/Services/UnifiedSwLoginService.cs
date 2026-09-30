using GB_CadAndSWPlus_V.Shared.Models;
using GB_CadAndSWPlus_V.Shared.Services;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Configuration;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.Services
{
    /// <summary>
    /// SolidWorks 对共享登录窗口的适配器。
    /// </summary>
    public sealed class UnifiedSwLoginService : IUnifiedLoginService
    {
        private readonly AuthService _authService;
        private readonly SwUserSession _userSession;

        public UnifiedSwLoginService(AuthService authService, SwUserSession userSession)
        {
            _authService = authService ?? throw new ArgumentNullException(nameof(authService));
            _userSession = userSession ?? throw new ArgumentNullException(nameof(userSession));
        }

        public async Task<UnifiedLoginResult> LoginAsync(
            UnifiedLoginRequest request,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            var settings = new SwApiSettings
            {
                ServerHost = request.ServerHost,
                ApiPort = request.ApiPort
            };
            SwUserSession session = await _authService.LoginAsync(
                settings,
                request.Username,
                request.Password,
                cancellationToken).ConfigureAwait(false);

            _userSession.Settings = session.Settings;
            _userSession.User = session.User;
            _userSession.Save();

            return new UnifiedLoginResult
            {
                Success = true,
                DisplayName = session.User.DisplayName,
                UserId = session.User.Id,
                DepartmentId = session.User.DepartmentId,
                DepartmentName = session.User.DepartmentName,
                AccessToken = session.AccessToken,
                AccessTokenExpiresAtUtc = session.AccessTokenExpiresAtUtc,
                PlatformSession = session
            };
        }

        public Task<UnifiedLoginResult> LoginWithSharedSessionAsync(
            SharedLoginSession session,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var request = new SharedLoginSessionStore().ToLoginRequest(session, UnifiedLoginPlatform.SolidWorks);
            return LoginAsync(request, cancellationToken);
        }

        public Task<IReadOnlyList<UnifiedDepartmentOption>> LoadDepartmentsAsync(
            UnifiedLoginRequest request,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return Task.FromResult<IReadOnlyList<UnifiedDepartmentOption>>(
                new List<UnifiedDepartmentOption>());
        }
    }
}
