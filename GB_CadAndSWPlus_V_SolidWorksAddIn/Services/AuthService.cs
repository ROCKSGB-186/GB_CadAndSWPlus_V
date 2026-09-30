using GB_CadAndSWPlus_V.SolidWorksAddIn.Configuration;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.Services
{
    public sealed class AuthService
    {
        public async Task<SwUserSession> LoginAsync(
            SwApiSettings settings,
            string username,
            string password,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                throw new InvalidOperationException("用户名和密码不能为空。");

            var client = new SwApiClient(settings);
            LoginResponse response = await client.PostJsonAsync<LoginRequest, LoginResponse>(
                "api/auth/login",
                new LoginRequest
                {
                    Username = username.Trim(),
                    Password = password
                },
                cancellationToken).ConfigureAwait(false);

            if (response == null || !response.Success || response.User == null)
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(response?.Message)
                    ? "登录失败。"
                    : response!.Message);

            var session = new SwUserSession
            {
                Settings = settings,
                User = response.User
                ,AccessToken = response.AccessToken
                ,AccessTokenExpiresAtUtc = response.AccessTokenExpiresAtUtc
            };
            session.Save();
            return session;
        }
    }
}
