// 托盘程序使用的命名空间引用
using GB_CadAndSWPlus_V.Shared.Models;
using GB_CadAndSWPlus_V.Shared.Services;
using GB_CadAndSWPlus_V.Shared;
using System;
using System.Drawing;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using WpfApplication = System.Windows.Application; // 为 WPF 的 Application 起别名，避免与 WinForms 的 Application 冲突
using Newtonsoft.Json;

namespace GB_CadAndSWPlus_V.Tray
{
    /// <summary>
    /// 托盘应用程序上下文，管理托盘图标、菜单和登录窗口的显示与交互。
    /// </summary>
    internal sealed class TrayApplicationContext : ApplicationContext
    {
        private readonly NotifyIcon _notifyIcon; // 托盘图标
        private readonly SharedLoginSessionStore _sessionStore = new SharedLoginSessionStore(); // 共享登录会话存储
        private readonly TrayLoginService _loginService = new TrayLoginService(); // 托盘登录服务
        private readonly ToolStripMenuItem _accountItem; // 显示当前账号或登录窗口的菜单项
        private UnifiedLoginWindowHost _loginWindowHost; // WPF 登录窗口宿主

        /// <summary>
        /// 初始化托盘上下文，创建菜单项、托盘图标，并刷新账号显示。
        /// </summary>
        public TrayApplicationContext()
        {
            // “显示登录窗口”菜单项，点击时打开登录窗口且不清除当前会话
            _accountItem = new ToolStripMenuItem("显示登录窗口");
            _accountItem.Click += (sender, args) => ShowLoginWindow(false);

            // “切换账号”菜单项，点击时清除当前会话后打开登录窗口
            var switchItem = new ToolStripMenuItem("切换账号");
            switchItem.Click += (sender, args) => ShowLoginWindow(true);

            // “退出登录”清除认证信息并关闭托盘程序；连接配置仍保留。
            var logoutItem = new ToolStripMenuItem("退出登录");
            logoutItem.Click += (sender, args) => LogoutAndExit();

            // “退出托盘程序”菜单项
            var exitItem = new ToolStripMenuItem("退出托盘程序");
            // CAD 或 SolidWorks 运行期间，所有退出入口都必须经过统一检查。
            exitItem.Click += (sender, args) => RequestExit();

            // 构建托盘右键菜单
            var menu = new ContextMenuStrip();
            menu.Items.Add(_accountItem);
            menu.Items.Add(switchItem);
            menu.Items.Add(logoutItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(exitItem);

            // 创建并显示托盘图标
            _notifyIcon = new NotifyIcon
            {
                Icon = SystemIcons.Application,
                Text = "GB CAD/SolidWorks 统一登录",
                ContextMenuStrip = menu,
                Visible = true
            };

            // 双击托盘图标时显示登录窗口
            _notifyIcon.DoubleClick += (sender, args) => ShowLoginWindow(false);

            // 根据本地会话刷新菜单中的账号文本
            UpdateAccountText();
        }

        private void LogoutAndExit()
        {
            _sessionStore.ClearAuthentication();

            // 退出登录后仍通过统一退出检查，确保 CAD/SolidWorks 运行时托盘继续驻留。
            RequestExit(true);
        }

        /// <summary>
        /// 请求退出托盘程序；CAD 或 SolidWorks 运行期间禁止退出。
        /// </summary>
        /// <param name="authenticationCleared">是否已经清除了登录认证信息。</param>
        private void RequestExit(bool authenticationCleared = false)
        {
            // 客户端运行期间必须保留托盘，避免共享会话宿主和登录状态被意外终止。
            if (TrayLauncher.IsCadOrSolidWorksRunning())
            {
                UpdateAccountText();
                string message = authenticationCleared
                    ? "已退出登录，但 CAD 或 SolidWorks 当前仍在运行，托盘程序不能退出。请先关闭 CAD 和 SolidWorks 后再退出托盘。"
                    : "CAD 或 SolidWorks 当前仍在运行，托盘程序不能退出。请先关闭 CAD 和 SolidWorks 后再退出托盘。";
                MessageBox.Show(
                    message,
                    "无法退出托盘",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            // 只有确认所有客户端均已关闭后，才真正结束托盘消息循环。
            ExitThread();
        }

        /// <summary>
        /// 显示统一登录窗口。
        /// </summary>
        /// <param name="switchAccount">是否切换账号；为 true 时会清除本地保存的会话。</param>
        private void ShowLoginWindow(bool switchAccount)
        {
            // 切换账号时清除已有登录会话
            if (switchAccount) _sessionStore.Clear();

            // 刷新账号显示
            UpdateAccountText();

            // 如果登录窗口宿主不存在或线程已结束，则重新创建
            if (_loginWindowHost == null || !_loginWindowHost.IsAlive)
                _loginWindowHost = new UnifiedLoginWindowHost(_loginService, _sessionStore);

            // 托盘程序默认使用 CAD 平台打开统一登录窗口
            _loginWindowHost.Show(UnifiedLoginPlatform.Cad);

            // 登录窗口显示后再次刷新账号显示
            UpdateAccountText();
        }

        /// <summary>
        /// 根据本地会话更新托盘菜单中的账号显示文本。
        /// </summary>
        private void UpdateAccountText()
        {
            SharedLoginSession session = _sessionStore.Load();
            _accountItem.Text = string.IsNullOrWhiteSpace(session.Username)
                ? "显示登录窗口"
                : "当前账号：" + session.Username;
        }

        /// <summary>
        /// 退出托盘程序时释放托盘图标并关闭登录窗口。
        /// </summary>
        protected override void ExitThreadCore()
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _loginWindowHost?.Close();
            base.ExitThreadCore();
        }
    }

    /// <summary>
    /// 在独立的 STA 线程中承载 WPF 统一登录窗口，供 WinForms 托盘程序调用。
    /// </summary>
    internal sealed class UnifiedLoginWindowHost
    {
        private readonly IUnifiedLoginService _loginService; // 登录服务
        private readonly SharedLoginSessionStore _sessionStore; // 登录会话存储
        private Thread _thread; // 承载 WPF 窗口的线程
        private UnifiedLoginWindow _window; // WPF 登录窗口实例

        /// <summary>
        /// 获取承载窗口的线程是否仍然存活。
        /// </summary>
        public bool IsAlive => _thread != null && _thread.IsAlive;

        /// <summary>
        /// 初始化登录窗口宿主。
        /// </summary>
        /// <param name="loginService">登录服务。</param>
        /// <param name="sessionStore">登录会话存储。</param>
        public UnifiedLoginWindowHost(IUnifiedLoginService loginService, SharedLoginSessionStore sessionStore)
        {
            _loginService = loginService;
            _sessionStore = sessionStore;
        }

        /// <summary>
        /// 显示指定平台的统一登录窗口；若窗口已存在则将其激活。
        /// </summary>
        /// <param name="platform">统一登录平台。</param>
        public void Show(UnifiedLoginPlatform platform)
        {
            if (IsAlive)
            {
                // 窗口线程仍在运行：通过 WPF Dispatcher 激活已有窗口
                _window?.Dispatcher.Invoke(new Action(() =>
                {
                    _window.Activate();
                    _window.Topmost = true;
                    _window.Topmost = false;
                }));
                return;
            }

            // 新建 STA 线程运行 WPF 窗口和消息循环
            _thread = new Thread(() =>
            {
                WpfApplication application = new WpfApplication();
                _window = new UnifiedLoginWindow(platform, _loginService, _sessionStore);

                // 窗口关闭时结束 WPF 应用消息循环
                _window.Closed += (sender, args) => application.Shutdown();

                _window.Show();
                application.Run();
            });

            // WPF 窗口需要运行在 STA 线程中
            _thread.SetApartmentState(ApartmentState.STA);

            // 后台线程，不阻止进程退出
            _thread.IsBackground = true;

            _thread.Start();
        }

        /// <summary>
        /// 请求关闭登录窗口。
        /// </summary>
        public void Close()
        {
            // 通过 Dispatcher 在 WPF UI 线程上关闭窗口
            if (_window != null && !_window.Dispatcher.HasShutdownStarted)
                _window.Dispatcher.BeginInvoke(new Action(() => _window.Close()));
        }
    }

    /// <summary>
    /// 托盘程序使用的统一登录服务实现，通过 HTTP 调用服务端登录接口。
    /// </summary>
    internal sealed class TrayLoginService : IUnifiedLoginService
    {
        /// <summary>
        /// 共享 HttpClient，超时时间为 30 秒。
        /// </summary>
        private static readonly HttpClient Client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        /// <summary>
        /// 使用用户名和密码异步登录。
        /// </summary>
        /// <param name="request">统一登录请求。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>统一登录结果。</returns>
        public async Task<UnifiedLoginResult> LoginAsync(UnifiedLoginRequest request, CancellationToken cancellationToken = default(CancellationToken))
        {
            // 构建登录接口地址
            string url = BuildUrl(request.ServerHost, request.ApiPort, "api/auth/login");

            // 手动拼接 JSON 请求体，并对用户名和密码进行 JSON 字符串转义
            string json = "{\"username\":\"" + Escape(request.Username) + "\",\"password\":\"" + Escape(request.Password) + "\",\"clientPlatform\":\"Tray\"}";

            using (var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"))
            using (HttpResponseMessage response = await Client.PostAsync(url, content, cancellationToken).ConfigureAwait(false))
            {
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                // HTTP 状态码不是成功状态时抛出异常
                if (!response.IsSuccessStatusCode)
                    throw new InvalidOperationException("服务器登录失败：HTTP " + (int)response.StatusCode);

                // 反序列化服务端返回的登录结果
                LoginPayload payload = JsonConvert.DeserializeObject<LoginPayload>(body);

                // 业务失败时抛出带服务端消息的异常
                if (payload == null || !payload.Success)
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(payload?.Message) ? "用户名或密码错误。" : payload.Message);

                // 将服务端返回结果转换为统一登录结果
                return new UnifiedLoginResult
                {
                    Success = true,
                    Message = payload.Message ?? "登录成功。",
                    DisplayName = payload.User?.DisplayName ?? request.Username,
                    UserId = payload.User?.Id,
                    DepartmentId = payload.User?.DepartmentId,
                    DepartmentName = payload.User?.DepartmentName ?? string.Empty,
                    AccessToken = payload.AccessToken ?? string.Empty,
                    AccessTokenExpiresAtUtc = payload.AccessTokenExpiresAtUtc,
                    PlatformSession = request.Username
                };
            }
        }

        /// <summary>
        /// 使用共享登录会话发起登录。
        /// </summary>
        /// <param name="session">共享登录会话。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>统一登录结果。</returns>
        public Task<UnifiedLoginResult> LoginWithSharedSessionAsync(SharedLoginSession session, CancellationToken cancellationToken = default(CancellationToken))
            => LoginAsync(new SharedLoginSessionStore().ToLoginRequest(session, UnifiedLoginPlatform.Cad), cancellationToken);

        /// <summary>
        /// 加载部门列表。托盘程序当前不需要部门选择，因此返回空列表。
        /// </summary>
        /// <param name="request">统一登录请求。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>部门选项列表。</returns>
        public Task<IReadOnlyList<UnifiedDepartmentOption>> LoadDepartmentsAsync(UnifiedLoginRequest request, CancellationToken cancellationToken = default(CancellationToken))
            => Task.FromResult<IReadOnlyList<UnifiedDepartmentOption>>(new UnifiedDepartmentOption[0]);

        /// <summary>
        /// 构建完整的服务端接口地址。
        /// </summary>
        /// <param name="host">服务器主机。</param>
        /// <param name="port">接口端口。</param>
        /// <param name="path">接口路径。</param>
        /// <returns>完整 URL。</returns>
        private static string BuildUrl(string host, int port, string path)
        {
            // 如果 host 未包含协议，则默认补上 http://
            string normalized = host.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || host.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? host : "http://" + host;

            return normalized.TrimEnd('/') + ":" + port + "/" + path;
        }

        /// <summary>
        /// 对字符串进行 JSON 值转义。
        /// </summary>
        /// <param name="value">原始字符串。</param>
        /// <returns>转义后的字符串。</returns>
        private static string Escape(string value) => (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");

        /// <summary>
        /// 登录接口返回的数据结构。
        /// </summary>
        private sealed class LoginPayload
        {
            [JsonProperty("success")] public bool Success { get; set; } // 是否登录成功
            [JsonProperty("message")] public string Message { get; set; } // 服务端消息
            [JsonProperty("accessToken")] public string AccessToken { get; set; } // 访问令牌
            [JsonProperty("accessTokenExpiresAtUtc")] public DateTime? AccessTokenExpiresAtUtc { get; set; } // 访问令牌过期时间（UTC）
            [JsonProperty("user")] public UserPayload User { get; set; } // 用户信息
        }

        /// <summary>
        /// 登录接口返回的用户信息结构。
        /// </summary>
        private sealed class UserPayload
        {
            [JsonProperty("id")] public int Id { get; set; } // 用户 ID
            [JsonProperty("displayName")] public string DisplayName { get; set; } // 显示名称
            [JsonProperty("departmentId")] public int? DepartmentId { get; set; } // 部门 ID
            [JsonProperty("departmentName")] public string DepartmentName { get; set; } // 部门名称
        }
    }
}