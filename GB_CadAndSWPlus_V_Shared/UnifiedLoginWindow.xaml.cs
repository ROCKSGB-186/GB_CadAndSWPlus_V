using GB_CadAndSWPlus_V.Shared.Models;
using GB_CadAndSWPlus_V.Shared.Services;
using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;

namespace GB_CadAndSWPlus_V.Shared
{
    /// <summary>
    /// CAD 与 SolidWorks 共用的登录页面。
    /// </summary>
    public partial class UnifiedLoginWindow : Window
    {
        /// <summary>
        /// 用于执行统一登录操作的服务实例。
        /// </summary>
        private readonly IUnifiedLoginService _loginService;

        /// <summary>
        /// 用于存储和加载共享登录会话的实例，支持在不同平台之间共享登录信息。
        /// </summary>
        private readonly SharedLoginSessionStore _sessionStore;

        /// <summary>
        /// 指示当前登录平台（CAD 或 SolidWorks）的枚举值。
        /// </summary>
        private readonly UnifiedLoginPlatform _platform;

        /// <summary>
        /// 指示密码是否可见的标志，true 表示密码可见，false 表示密码隐藏。
        /// </summary>
        private bool _passwordVisible;

        /// <summary>
        /// 用于测试 API 和数据库连接的 HttpClient 实例，设置了 10 秒的超时时间。
        /// </summary>
        private static readonly HttpClient HealthClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        /// <summary>
        /// 获取登录结果，如果登录成功则包含会话信息，否则为 null。
        /// </summary>
        public UnifiedLoginResult LoginResult { get; private set; }

        /// <summary>
        /// 初始化一个新的 <see cref="UnifiedLoginWindow"/> 实例。
        /// </summary>
        /// <param name="platform">登录平台（CAD 或 SolidWorks）</param>
        /// <param name="loginService">统一登录服务实例</param>
        /// <param name="sessionStore">共享登录会话存储实例</param>
        /// <exception cref="ArgumentNullException">当 <paramref name="loginService"/> 为 null 时引发</exception>
        public UnifiedLoginWindow(
            UnifiedLoginPlatform platform,
            IUnifiedLoginService loginService,
            SharedLoginSessionStore sessionStore = null)
        {
            InitializeComponent();
            _platform = platform;
            _loginService = loginService ?? throw new ArgumentNullException(nameof(loginService));
            _sessionStore = sessionStore ?? new SharedLoginSessionStore();

            CadSettingsGroupBox.Visibility = platform == UnifiedLoginPlatform.Cad
                ? Visibility.Visible
                : Visibility.Collapsed;
            ApiPortTextBox.Text = "10010";
            DatabasePortTextBox.Text = "5236";
            DatabaseTypeComboBox.SelectionChanged += DatabaseTypeComboBox_SelectionChanged;
            LoadSharedSession();
        }

        /// <summary>
        /// 处理测试连接按钮点击事件，测试 API 和数据库连接是否正常。
        /// </summary>
        /// <param name="sender">事件源</param>
        /// <param name="e">事件参数</param>
        private async void TestConnection_Click(object sender, RoutedEventArgs e)
        {
            TestConnectionButton.IsEnabled = false;
            StatusTextBlock.Text = "正在测试 API 和数据库连接...";
            try
            {
                if (!int.TryParse(ApiPortTextBox.Text.Trim(), out int apiPort) || apiPort < 1 || apiPort > 65535)
                    throw new InvalidOperationException("API 端口必须在 1 到 65535 之间。");

                string host = ServerHostTextBox.Text.Trim();
                if (string.IsNullOrWhiteSpace(host))
                    throw new InvalidOperationException("请先填写服务器地址。");

                string url = BuildApiUrl(host, apiPort, "api/health");
                using (HttpResponseMessage response = await HealthClient.GetAsync(url).ConfigureAwait(true))
                {
                    string body = await response.Content.ReadAsStringAsync().ConfigureAwait(true);
                    bool databaseOk = body.IndexOf("\"database\":\"ok\"", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!response.IsSuccessStatusCode || !databaseOk)
                        throw new InvalidOperationException($"服务器可访问，但数据库状态异常。HTTP {(int)response.StatusCode}。");

                    StatusTextBlock.Text = "连接成功：API 和数据库均正常。";
                }
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = "连接失败：" + ex.Message;
            }
            finally
            {
                TestConnectionButton.IsEnabled = true;
            }
        }

        /// <summary>
        /// 处理切换账号按钮点击事件，清除当前共享账号信息并重置登录表单。
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SwitchAccount_Click(object sender, RoutedEventArgs e)
        {
            _sessionStore.Clear();
            UsernameTextBox.Clear();
            PasswordBox.Clear();
            PasswordVisibleTextBox.Clear();
            RememberPasswordCheckBox.IsChecked = false;
            StatusTextBlock.Text = "已清除当前共享账号，请输入新账号登录。";
            UsernameTextBox.Focus();
        }

        /// <summary>
        /// 构建 API URL，确保主机地址以 http:// 或 https:// 开头，并拼接端口和路径。
        /// </summary>
        /// <param name="host">主机地址</param>
        /// <param name="port">端口号</param>
        /// <param name="path">API 路径</param>
        /// <returns>构建后的完整 API URL</returns>
        private static string BuildApiUrl(string host, int port, string path)
        {
            string normalizedHost = host;
            if (!normalizedHost.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !normalizedHost.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                normalizedHost = "http://" + normalizedHost;
            return normalizedHost.TrimEnd('/') + ":" + port + "/" + path.TrimStart('/');
        }

        /// <summary>
        /// 处理数据库类型下拉框的选择更改事件，根据选择的数据库类型自动设置默认端口号。
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void DatabaseTypeComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (DatabaseTypeComboBox.SelectedItem is System.Windows.Controls.ComboBoxItem item &&
                string.Equals(item.Content as string, "MYSQL", StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrWhiteSpace(DatabasePortTextBox.Text))
            {
                DatabasePortTextBox.Text = "3308";
            }
            else if (DatabaseTypeComboBox.SelectedItem is System.Windows.Controls.ComboBoxItem dmItem &&
                     string.Equals(dmItem.Content as string, "DM", StringComparison.OrdinalIgnoreCase) &&
                     string.IsNullOrWhiteSpace(DatabasePortTextBox.Text))
            {
                DatabasePortTextBox.Text = "5236";
            }
        }

        /// <summary>
        /// 加载共享登录会话，如果存在已保存的会话信息，则填充登录表单。
        /// </summary>
        private void LoadSharedSession()
        {
            SharedLoginSession session = _sessionStore.Load();
            if (string.IsNullOrWhiteSpace(session.ServerHost))
                return;

            ServerHostTextBox.Text = session.ServerHost;
            ApiPortTextBox.Text = session.ApiPort.ToString();
            UsernameTextBox.Text = session.Username;
            RememberPasswordCheckBox.IsChecked = !string.IsNullOrWhiteSpace(session.EncryptedPassword);
            if (RememberPasswordCheckBox.IsChecked == true)
            {
                string password = _sessionStore.GetPassword(session);
                PasswordBox.Password = password;
                PasswordVisibleTextBox.Text = password;
            }
            DatabasePortTextBox.Text = session.DatabasePort > 0
                ? session.DatabasePort.ToString()
                : DatabasePortTextBox.Text;
            SelectDatabaseType(session.DatabaseType);
        }

        /// <summary>
        /// 处理切换密码可见性的按钮点击事件，切换密码输入框的显示状态。
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void TogglePassword_Click(object sender, RoutedEventArgs e)
        {
            _passwordVisible = !_passwordVisible;
            if (_passwordVisible)
            {
                PasswordVisibleTextBox.Text = PasswordBox.Password;
                PasswordBox.Visibility = Visibility.Collapsed;
                PasswordVisibleTextBox.Visibility = Visibility.Visible;
                TogglePasswordButton.Content = "隐藏";
            }
            else
            {
                PasswordBox.Password = PasswordVisibleTextBox.Text;
                PasswordBox.Visibility = Visibility.Visible;
                PasswordVisibleTextBox.Visibility = Visibility.Collapsed;
                TogglePasswordButton.Content = "显示";
            }
        }

        /// <summary>
        /// 处理登录按钮点击事件，执行登录操作。
        /// </summary>
        /// <param name="sender">事件源</param>
        /// <param name="e">事件数据</param>
        private async void Login_Click(object sender, RoutedEventArgs e)
        {
            StatusTextBlock.Text = string.Empty;
            LoginButton.IsEnabled = false;

            try
            {
                int apiPort;
                if (!int.TryParse(ApiPortTextBox.Text.Trim(), out apiPort) || apiPort < 1 || apiPort > 65535)
                    throw new InvalidOperationException("API 端口必须在 1 到 65535 之间。");

                string password = _passwordVisible ? PasswordVisibleTextBox.Text : PasswordBox.Password;
                int? databasePort = null;
                string databaseType = "DM";
                if (_platform == UnifiedLoginPlatform.Cad)
                {
                    databaseType = (DatabaseTypeComboBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content as string ?? "DM";
                    if (!int.TryParse(DatabasePortTextBox.Text.Trim(), out int parsedDatabasePort) || parsedDatabasePort < 1 || parsedDatabasePort > 65535)
                        throw new InvalidOperationException("数据库端口必须在 1 到 65535 之间。");
                    databasePort = parsedDatabasePort;
                }
                var request = new UnifiedLoginRequest
                {
                    Platform = _platform,
                    ServerHost = ServerHostTextBox.Text.Trim(),
                    ApiPort = apiPort,
                    Username = UsernameTextBox.Text.Trim(),
                    Password = password,
                    DatabaseType = databaseType,
                    DatabasePort = databasePort
                };

                UnifiedLoginResult result = await _loginService.LoginAsync(request).ConfigureAwait(true);
                if (result == null || !result.Success)
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(result?.Message) ? "登录失败。" : result.Message);

                LoginResult = result;
                _sessionStore.Save(request, result, RememberPasswordCheckBox.IsChecked == true);
                // 登录成功后启动独立托盘；托盘启动失败不影响 CAD/SolidWorks 登录。
                TrayLauncher.EnsureStarted();
                DialogResult = true;
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = ex.Message;
            }
            finally
            {
                LoginButton.IsEnabled = true;
            }
        }
        /// <summary>
        /// 根据数据库类型选择下拉框中的对应项。
        /// </summary>
        /// <param name="databaseType">数据库类型</param>
        private void SelectDatabaseType(string databaseType)
        {
            if (string.IsNullOrWhiteSpace(databaseType))
                return;
            // 遍历 ComboBox 的所有项，找到匹配的项并设置为选中状态
            foreach (object item in DatabaseTypeComboBox.Items)
            {
                if (item is System.Windows.Controls.ComboBoxItem comboItem &&
                    string.Equals(comboItem.Content as string, databaseType, StringComparison.OrdinalIgnoreCase))
                {
                    DatabaseTypeComboBox.SelectedItem = comboItem;
                    return;
                }
            }
        }
    }
}
