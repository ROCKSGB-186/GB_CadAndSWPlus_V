using GB_CadAndSWPlus_V.Shared.Models;
using System;
using System.IO;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GB_CadAndSWPlus_V.Shared.Services
{
    /// <summary>CAD 与 SolidWorks 共用的部门/人员 API 服务，统一服务器地址、端口和认证令牌处理。</summary>
    public sealed class UnifiedDepartmentUserApiService
    {
        private static readonly HttpClient Client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        private readonly SharedLoginSession _session;

        public UnifiedDepartmentUserApiService(SharedLoginSession session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
        }

        public Task<UnifiedDepartmentListResponse> GetDepartmentsAsync(CancellationToken cancellationToken = default(CancellationToken))
            => SendAsync<UnifiedDepartmentListResponse>(HttpMethod.Get, "api/departments", null, cancellationToken);

        public Task<UnifiedUserListResponse> GetUsersAsync(int departmentId, CancellationToken cancellationToken = default(CancellationToken))
            => SendAsync<UnifiedUserListResponse>(HttpMethod.Get, "api/users?departmentId=" + departmentId, null, cancellationToken);

        public Task<UnifiedDepartmentMutationResponse> AddDepartmentAsync(string name, string displayName, string description, int sortOrder, CancellationToken cancellationToken = default(CancellationToken))
            => SendAsync<UnifiedDepartmentMutationResponse>(HttpMethod.Post, "api/departments", new DepartmentRequest
            {
                Name = name ?? string.Empty,
                DisplayName = displayName ?? string.Empty,
                Description = description ?? string.Empty,
                SortOrder = sortOrder,
                IsActive = true
            }, cancellationToken);

        public Task<UnifiedDepartmentMutationResponse> UpdateDepartmentAsync(int id, string name, string displayName, string description, int sortOrder, int? managerUserId, bool isActive, CancellationToken cancellationToken = default(CancellationToken))
            => SendAsync<UnifiedDepartmentMutationResponse>(HttpMethod.Put, "api/departments/" + id, new DepartmentRequest
            {
                Name = name ?? string.Empty,
                DisplayName = displayName ?? string.Empty,
                Description = description ?? string.Empty,
                SortOrder = sortOrder,
                ManagerUserId = managerUserId,
                IsActive = isActive
            }, cancellationToken);

        public Task<UnifiedDepartmentMutationResponse> DeleteDepartmentAsync(int id, CancellationToken cancellationToken = default(CancellationToken))
            => SendAsync<UnifiedDepartmentMutationResponse>(HttpMethod.Delete, "api/departments/" + id, null, cancellationToken);

        public Task<UnifiedDepartmentMutationResponse> SyncDepartmentsFromCategoriesAsync(CancellationToken cancellationToken = default(CancellationToken))
            => SendAsync<UnifiedDepartmentMutationResponse>(HttpMethod.Post, "api/departments/sync-from-categories", null, cancellationToken);

        public Task<UnifiedUserMutationResponse> AddUserAsync(string username, string password, int departmentId, string departmentName, string role, bool isActive, string realName, string gender, string phone, string email, CancellationToken cancellationToken = default(CancellationToken))
            => SendAsync<UnifiedUserMutationResponse>(HttpMethod.Post, "api/users", new UserRequest
            {
                Username = username ?? string.Empty, Password = password ?? string.Empty, DepartmentId = departmentId,
                DepartmentName = departmentName ?? string.Empty, Role = role ?? "user", IsActive = isActive,
                RealName = realName ?? string.Empty, Gender = gender ?? string.Empty, Phone = phone ?? string.Empty, Email = email ?? string.Empty
            }, cancellationToken);

        public Task<UnifiedUserMutationResponse> UpdateUserAsync(int id, string username, string role, bool isActive, int? departmentId, string departmentName, string password, string realName, string gender, string phone, string email, CancellationToken cancellationToken = default(CancellationToken))
            => SendAsync<UnifiedUserMutationResponse>(HttpMethod.Put, "api/users/" + id, new UserRequest
            {
                Username = username ?? string.Empty, Password = password ?? string.Empty, DepartmentId = departmentId,
                DepartmentName = departmentName ?? string.Empty, Role = role ?? "user", IsActive = isActive,
                RealName = realName ?? string.Empty, Gender = gender ?? string.Empty, Phone = phone ?? string.Empty, Email = email ?? string.Empty
            }, cancellationToken);

        public Task<UnifiedUserMutationResponse> DeleteUserAsync(int id, CancellationToken cancellationToken = default(CancellationToken))
            => SendAsync<UnifiedUserMutationResponse>(HttpMethod.Delete, "api/users/" + id, null, cancellationToken);

        public Task<UnifiedUserMutationResponse> AssignUserToDepartmentAsync(string username, int departmentId, CancellationToken cancellationToken = default(CancellationToken))
            => SendAsync<UnifiedUserMutationResponse>(HttpMethod.Post, "api/users/assign", new UserRequest
            {
                Username = username ?? string.Empty, DepartmentId = departmentId
            }, cancellationToken);

        private async Task<T> SendAsync<T>(HttpMethod method, string path, object body, CancellationToken cancellationToken)
        {
            string host = (_session.ServerHost ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(host)) throw new InvalidOperationException("统一登录会话中没有服务器地址。");
            string schemeHost = host.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || host.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? host : "http://" + host;
            string url = schemeHost.TrimEnd('/') + ":" + (_session.ApiPort > 0 ? _session.ApiPort : 10010) + "/" + path.TrimStart('/');

            using (var request = new HttpRequestMessage(method, url))
            {
                request.Headers.TryAddWithoutValidation("X-Client-Platform", "UNIFIED");
                if (!string.IsNullOrWhiteSpace(_session.AccessToken))
                    request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + _session.AccessToken);
                if (body != null)
                    request.Content = new StringContent(Serialize(body), Encoding.UTF8, "application/json");

                using (HttpResponseMessage response = await Client.SendAsync(request, cancellationToken).ConfigureAwait(false))
                {
                    string content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                        throw new InvalidOperationException("统一部门/人员接口请求失败，HTTP " + (int)response.StatusCode + "。");
                    T result = Deserialize<T>(content);
                    if (result == null) throw new InvalidOperationException("统一部门/人员接口返回为空。");
                    return result;
                }
            }
        }

        private static string Serialize(object value)
        {
            var serializer = new DataContractJsonSerializer(value.GetType());
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, value);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        private static T Deserialize<T>(string value)
        {
            var serializer = new DataContractJsonSerializer(typeof(T));
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(value ?? string.Empty)))
                return (T)serializer.ReadObject(stream);
        }

        [DataContract]
        private sealed class DepartmentRequest
        {
            [DataMember(Name = "name")] public string Name { get; set; } = string.Empty;
            [DataMember(Name = "displayName")] public string DisplayName { get; set; } = string.Empty;
            [DataMember(Name = "description")] public string Description { get; set; } = string.Empty;
            [DataMember(Name = "sortOrder")] public int SortOrder { get; set; }
            [DataMember(Name = "managerUserId")] public int? ManagerUserId { get; set; }
            [DataMember(Name = "isActive")] public bool IsActive { get; set; }
        }

        [DataContract]
        private sealed class UserRequest
        {
            [DataMember(Name = "username")] public string Username { get; set; } = string.Empty;
            [DataMember(Name = "password")] public string Password { get; set; } = string.Empty;
            [DataMember(Name = "departmentId")] public int? DepartmentId { get; set; }
            [DataMember(Name = "departmentName")] public string DepartmentName { get; set; } = string.Empty;
            [DataMember(Name = "realName")] public string RealName { get; set; } = string.Empty;
            [DataMember(Name = "gender")] public string Gender { get; set; } = string.Empty;
            [DataMember(Name = "phone")] public string Phone { get; set; } = string.Empty;
            [DataMember(Name = "email")] public string Email { get; set; } = string.Empty;
            [DataMember(Name = "role")] public string Role { get; set; } = "user";
            [DataMember(Name = "isActive")] public bool IsActive { get; set; }
        }
    }
}
