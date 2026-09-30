using GB_CadAndSWPlus_V.SolidWorksAddIn.Configuration;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Services;
using System;
using System.Windows;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.DisplayPages
{
    public partial class SW_LoginWindow : Window
    {
        private readonly AuthService _authService = new AuthService();

        public SW_LoginWindow(SwUserSession? session, Window? owner = null)
        {
            InitializeComponent();
            Owner = owner;

            SwApiSettings settings = session?.Settings ?? new SwApiSettings();
            ServerHostTextBox.Text = settings.ServerHost;
            ApiPortTextBox.Text = settings.ApiPort.ToString();
            UsernameTextBox.Text = session?.User?.Username ?? string.Empty;
        }

        public SwUserSession? Session { get; private set; }

        private void TogglePassword_Click(object sender, RoutedEventArgs e)
        {
            bool isPasswordVisible = PasswordVisibleTextBox.Visibility == Visibility.Visible;

            if (isPasswordVisible)
            {
                PasswordBox.Password = PasswordVisibleTextBox.Text;
                PasswordVisibleTextBox.Visibility = Visibility.Collapsed;
                PasswordBox.Visibility = Visibility.Visible;
                TogglePasswordButton.Content = "👁";
                TogglePasswordButton.ToolTip = "显示密码";
                PasswordBox.Focus();
                return;
            }

            PasswordVisibleTextBox.Text = PasswordBox.Password;
            PasswordBox.Visibility = Visibility.Collapsed;
            PasswordVisibleTextBox.Visibility = Visibility.Visible;
            TogglePasswordButton.Content = "🙈";
            TogglePasswordButton.ToolTip = "隐藏密码";
            PasswordVisibleTextBox.Focus();
            PasswordVisibleTextBox.CaretIndex = PasswordVisibleTextBox.Text.Length;
        }

        private async void Login_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(ServerHostTextBox.Text))
            {
                StatusTextBlock.Text = "请输入服务器地址或 IP。";
                ServerHostTextBox.Focus();
                return;
            }

            if (!int.TryParse(ApiPortTextBox.Text.Trim(), out int port) || port < 1 || port > 65535)
            {
                StatusTextBlock.Text = "API 端口必须是 1 到 65535 之间的数字。";
                return;
            }

            var settings = new SwApiSettings
            {
                ServerHost = ServerHostTextBox.Text.Trim(),
                ApiPort = port
            };

            try
            {
                IsEnabled = false;
                StatusTextBlock.Text = "正在登录服务器...";
                string password = PasswordVisibleTextBox.Visibility == Visibility.Visible
                    ? PasswordVisibleTextBox.Text
                    : PasswordBox.Password;
                Session = await _authService.LoginAsync(
                    settings,
                    UsernameTextBox.Text,
                    password);
                // 登录成功后保存服务器地址、端口和用户会话，部门/分类页面复用同一配置。
                Session.Save();
                DialogResult = true;
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = ex.Message;
            }
            finally
            {
                IsEnabled = true;
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
