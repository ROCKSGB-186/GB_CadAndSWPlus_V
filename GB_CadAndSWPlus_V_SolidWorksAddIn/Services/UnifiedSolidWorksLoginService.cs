using GB_CadAndSWPlus_V.Shared.Models; // 引入统一登录请求、结果和会话模型。
using GB_CadAndSWPlus_V.Shared.Services; // 引入统一登录服务接口和共享会话存储。
using Newtonsoft.Json; // 与 CAD 客户端使用相同的 JSON 处理方式，兼容服务端 DateTime JSON。
using System; // 引入基础异常和日期类型。
using System.Collections.Generic; // 引入只读列表类型。
using System.Globalization; // 引入兼容服务端 ISO 8601 日期的解析类型。
using System.Net.Http; // 引入 HTTP 登录请求类型。
using System.Text; // 引入 UTF-8 编码类型。
using System.Threading; // 引入取消令牌类型。
using System.Threading.Tasks; // 引入异步任务类型。

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.Services // 使用 SolidWorks 插件专用服务命名空间。
{
    /// <summary>为 SolidWorks 统一登录窗口提供服务端登录能力。</summary>
    public sealed class UnifiedSolidWorksLoginService : IUnifiedLoginService // 实现共享登录窗口需要的统一服务接口。
    {
        private static readonly HttpClient Client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) }; // 复用 HTTP 客户端并设置登录超时时间。

        /// <summary>使用登录窗口输入的账号信息调用服务端登录接口。</summary>
        public async Task<UnifiedLoginResult> LoginAsync(UnifiedLoginRequest request, CancellationToken cancellationToken = default(CancellationToken)) // 实现账号密码登录。
        {
            if (request == null) throw new ArgumentNullException(nameof(request)); // 防止调用方传入空登录请求。
            DateTime startedAt = DateTime.Now; // 记录本次登录开始时间，便于追踪每一步耗时。
            string url = BuildUrl(request.ServerHost, request.ApiPort, "api/auth/login"); // 根据登录页面配置构造登录地址。
            SolidWorksFileLogger.Info(string.Format("登录开始；服务器={0}；端口={1}；用户名={2}；接口={3}", request.ServerHost, request.ApiPort, request.Username, url)); // 记录登录开始，但不记录密码。
            var loginRequest = new LoginRequest // 使用强类型请求模型，确保密码中的特殊字符与 CAD 端一致地进行 JSON 转义。
            {
                Username = request.Username?.Trim() ?? string.Empty, // 账号只清理首尾空格，与服务器查询规则保持一致。
                Password = request.Password ?? string.Empty, // 密码不执行 Trim，避免改变用户实际密码。
                ClientPlatform = "SolidWorks" // 标记来源平台，供服务端日志分流，不参与密码校验。
            };
            string json = Serialize(loginRequest); // 使用 .NET 内置序列化器生成合法 JSON，避免手工拼接遗漏控制字符。
            SolidWorksFileLogger.Info("登录请求体已生成；密码字段已脱敏，未写入日志。"); // 记录请求体生成完成，明确敏感字段未写入日志。
            using (var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)) // 创建可携带平台标识的登录请求。
            using (var content = new StringContent(json, Encoding.UTF8, "application/json")) // 设置 JSON 请求内容和 UTF-8 编码。
            {
                httpRequest.Headers.TryAddWithoutValidation("X-Client-Platform", "SOLIDWORKS"); // 标记 SolidWorks 来源，供服务端日志分流。
                httpRequest.Content = content;
                using (HttpResponseMessage response = await Client.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false)) // 异步调用服务端登录接口。
            {
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false); // 读取服务端返回内容。
                SolidWorksFileLogger.Info(string.Format("登录响应已收到；HTTP状态={0}；耗时毫秒={1}", (int)response.StatusCode, (DateTime.Now - startedAt).TotalMilliseconds.ToString("0"))); // 记录响应状态和耗时，不记录响应正文。
                if (!response.IsSuccessStatusCode) throw new InvalidOperationException("服务器登录失败：HTTP " + (int)response.StatusCode + "。" + ExtractMessage(body)); // 将 HTTP 失败转换为中文提示并保留服务器原因。
                LoginPayload payload = Deserialize<LoginPayload>(body); // 使用与 CAD 相同的 JSON 规则反序列化服务端登录结果。
                if (!payload.Success) throw new InvalidOperationException(string.IsNullOrWhiteSpace(payload.Message) ? "用户名或密码错误。" : payload.Message); // 处理业务登录失败。
                SolidWorksFileLogger.Info("服务器认证成功，开始解析用户和会话信息。"); // 记录认证成功步骤。
                return new UnifiedLoginResult // 将服务端结果转换为共享登录结果。
                {
                    Success = true, // 标记登录成功。
                    Message = payload.Message ?? "登录成功。", // 设置登录结果消息。
                    DisplayName = payload.User == null ? (request.Username ?? string.Empty) : (payload.User.DisplayName ?? request.Username ?? string.Empty), // 设置用户显示名称。
                    UserId = payload.User?.Id, // 保存用户编号。
                    DepartmentId = payload.User?.DepartmentId, // 保存部门编号。
                    DepartmentName = payload.User?.DepartmentName ?? string.Empty, // 保存部门名称。
                    AccessToken = payload.AccessToken ?? string.Empty, // 保存访问令牌供共享会话使用。
                    AccessTokenExpiresAtUtc = ParseServerDate(payload.AccessTokenExpiresAtUtc), // 兼容 .NET 8 返回的 ISO 8601 日期。
                    PlatformSession = request.Username ?? string.Empty // 保存 SolidWorks 平台会话标识。
                }; // 返回统一登录结果。
            }
            }
        }

        /// <summary>使用共享会话重新登录 SolidWorks。</summary>
        public Task<UnifiedLoginResult> LoginWithSharedSessionAsync(SharedLoginSession session, CancellationToken cancellationToken = default(CancellationToken)) // 实现共享会话登录接口。
        {
            UnifiedLoginRequest request = new SharedLoginSessionStore().ToLoginRequest(session, UnifiedLoginPlatform.SolidWorks); // 将共享会话转换为 SolidWorks 登录请求。
            return LoginAsync(request, cancellationToken); // 复用账号登录流程。
        }

        private static DateTime? ParseServerDate(string value) // 兼容 .NET 8 ISO 8601 与旧 .NET /Date(...)\/ 日期格式。
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            DateTime parsed;
            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out parsed))
                return parsed;

            string text = value.Trim();
            if (text.StartsWith("/Date(", StringComparison.OrdinalIgnoreCase) && text.Contains(")"))
            {
                string millisecondsText = text.Substring(6, text.IndexOf(')') - 6);
                long milliseconds;
                if (long.TryParse(millisecondsText, NumberStyles.Integer, CultureInfo.InvariantCulture, out milliseconds))
                    return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(milliseconds);
            }

            SolidWorksFileLogger.Warning("服务器返回的会话过期时间无法解析，已按无过期时间处理。"); // 日期异常不应阻断已经成功的登录。
            return null;
        }

        /// <summary>从统一服务器读取部门列表，保证 CAD、SolidWorks 和未来 Revit 使用同一份部门数据。</summary>
        public Task<IReadOnlyList<UnifiedDepartmentOption>> LoadDepartmentsAsync(UnifiedLoginRequest request, CancellationToken cancellationToken = default(CancellationToken)) // 实现部门加载接口。
        {
            return LoadDepartmentsCoreAsync(request, cancellationToken); // 复用统一部门接口，不在 SolidWorks 客户端维护第二套部门数据。
        }

        private async Task<IReadOnlyList<UnifiedDepartmentOption>> LoadDepartmentsCoreAsync(UnifiedLoginRequest request, CancellationToken cancellationToken)
        {
            if (request == null) throw new ArgumentNullException(nameof(request)); // 防止登录窗口传入空请求。

            string url = BuildUrl(request.ServerHost, request.ApiPort, "api/departments"); // 使用登录页面配置的服务器和端口。
            SolidWorksFileLogger.Info("开始读取统一部门列表；平台=SolidWorks；密码和令牌不写入日志。"); // 记录关键操作但不记录敏感信息。
            using (HttpResponseMessage response = await Client.GetAsync(url, cancellationToken).ConfigureAwait(false)) // 调用与 CAD 相同的部门接口。
            {
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false); // 读取统一部门响应。
                if (!response.IsSuccessStatusCode)
                    throw new InvalidOperationException("部门读取失败：HTTP " + (int)response.StatusCode + "。" + ExtractMessage(body)); // 返回可理解的中文错误。

                DepartmentPayload payload = Deserialize<DepartmentPayload>(body); // 使用统一 JSON 规则读取部门响应。
                var departments = new List<UnifiedDepartmentOption>(); // 将服务端 DTO 转换为共享登录窗口模型。
                if (payload?.Departments != null)
                {
                    foreach (DepartmentItem item in payload.Departments)
                    {
                        if (item == null || !item.IsActive) continue; // 登录选择中不显示停用部门。
                        departments.Add(new UnifiedDepartmentOption
                        {
                            Id = item.Id,
                            DisplayName = string.IsNullOrWhiteSpace(item.DisplayName) ? item.Name : item.DisplayName
                        });
                    }
                }

                SolidWorksFileLogger.Info("统一部门列表读取完成；有效部门数量=" + departments.Count + "。"); // 记录数量，不记录部门敏感数据。
                return departments;
            }
        }

        private static string BuildUrl(string host, int port, string path) // 构造服务端 API 地址。
        {
            string normalizedHost = host?.Trim() ?? string.Empty; // 清理主机地址首尾空格。
            if (!normalizedHost.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !normalizedHost.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) normalizedHost = "http://" + normalizedHost; // 没有协议时使用 HTTP 默认协议。
            if (!Uri.TryCreate(normalizedHost, UriKind.Absolute, out Uri baseUri) || (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps)) throw new InvalidOperationException("服务器地址无效，请填写 IP 或主机名。"); // 先验证输入，避免生成错误请求地址。
            var builder = new UriBuilder(baseUri) { Port = port }; // 明确使用登录页面输入的 API 端口，避免重复拼接端口。
            return new Uri(builder.Uri, path.TrimStart('/')).ToString(); // 使用 Uri 合并接口路径，兼容带协议和端口的服务器地址。
        }

        private static string Serialize(LoginRequest request) // 序列化登录请求，避免手工拼接 JSON 导致特殊密码字符失真。
        {
            return JsonConvert.SerializeObject(request); // 与 CAD 端一致处理密码中的引号、反斜杠和控制字符。
        }

        private static string ExtractMessage(string json) // 从服务器错误响应中提取中文原因，不暴露密码等敏感信息。
        {
            try
            {
                LoginPayload payload = Deserialize<LoginPayload>(json); // 尝试读取服务器统一错误结构。
                return string.IsNullOrWhiteSpace(payload.Message) ? string.Empty : payload.Message;
            }
            catch
            {
                return string.Empty; // 错误响应格式异常时保留原 HTTP 错误提示。
            }
        }

        private static T Deserialize<T>(string json) where T : class // 使用 .NET Framework 内置序列化器读取服务端 JSON。
        {
            return JsonConvert.DeserializeObject<T>(json ?? string.Empty) ?? throw new InvalidOperationException("服务器返回空响应。格式不符合统一接口约定。"); // DateTime 字段按服务端 ISO 8601 格式兼容解析。
        }

        private sealed class LoginRequest // 定义与服务器 LoginRequest 对应的请求结构。
        {
            [JsonProperty("username")] public string Username { get; set; } = string.Empty; // 保存登录账号。
            [JsonProperty("password")] public string Password { get; set; } = string.Empty; // 保存登录密码，仅用于本次请求。
            [JsonProperty("clientPlatform")] public string ClientPlatform { get; set; } = "SolidWorks"; // 保存客户端平台标识，供服务端日志分流。
        }

        private sealed class LoginPayload // 定义服务端登录结果结构。
        {
            [JsonProperty("success")] public bool Success { get; set; } // 保存登录成功标志。
            [JsonProperty("message")] public string Message { get; set; } = string.Empty; // 保存服务端提示消息。
            [JsonProperty("accessToken")] public string AccessToken { get; set; } = string.Empty; // 保存访问令牌。
            [JsonProperty("accessTokenExpiresAtUtc")] public string AccessTokenExpiresAtUtc { get; set; } = string.Empty; // 先按字符串读取，统一兼容 ISO 8601 和旧日期格式。
            [JsonProperty("user")] public UserPayload User { get; set; } = new UserPayload(); // 保存用户信息。
        }

        private sealed class UserPayload // 定义服务端用户信息结构。
        {
            [JsonProperty("id")] public int? Id { get; set; } // 保存用户编号。
            [JsonProperty("displayName")] public string DisplayName { get; set; } = string.Empty; // 保存用户显示名称。
            [JsonProperty("departmentId")] public int? DepartmentId { get; set; } // 保存部门编号。
            [JsonProperty("departmentName")] public string DepartmentName { get; set; } = string.Empty; // 保存部门名称。
        }

        private sealed class DepartmentPayload // 定义统一部门接口返回结构。
        {
            [JsonProperty("success")] public bool Success { get; set; } // 保存查询成功标志。
            [JsonProperty("message")] public string Message { get; set; } = string.Empty; // 保存查询提示消息。
            [JsonProperty("departments")] public List<DepartmentItem> Departments { get; set; } = new List<DepartmentItem>(); // 保存部门列表。
        }

        private sealed class DepartmentItem // 定义部门公共字段，未来 CAD/SolidWorks/Revit 共用。
        {
            [JsonProperty("id")] public int Id { get; set; } // 保存部门编号。
            [JsonProperty("name")] public string Name { get; set; } = string.Empty; // 保存部门内部名称。
            [JsonProperty("displayName")] public string DisplayName { get; set; } = string.Empty; // 保存部门显示名称。
            [JsonProperty("isActive")] public bool IsActive { get; set; } // 保存启用状态。
        }
    }
}
