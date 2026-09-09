using System.Windows;
using System.Windows.Media;
using UpperComInspectionInstrument2022.Models;
using UpperComInspectionInstrument2022.Services;

namespace UpperComInspectionInstrument2022.Views
{
    /// <summary>
    /// 程序入口的登录与注册窗口。
    /// 首次运行时创建本机账户；忘记密码时允许直接重新注册并替换原账户。
    /// </summary>
    public partial class LoginWindow : Window
    {
        private readonly LocalAccountService _accountService;
        private readonly RememberedCredentialService _rememberedCredentialService;
        private bool _isReRegistration;

        public LoginWindow() : this(
            LocalAccountService.Default,
            RememberedCredentialService.Default)
        {
        }

        /// <summary>允许自动测试注入临时账户目录，不接触真实用户账户。</summary>
        internal LoginWindow(
            LocalAccountService accountService,
            RememberedCredentialService rememberedCredentialService)
        {
            _accountService = accountService;
            _rememberedCredentialService = rememberedCredentialService;
            InitializeComponent();
            Loaded += LoginWindow_Loaded;
        }

        /// <summary>根据账户文件状态决定显示登录还是首次注册。</summary>
        private void LoginWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (!_accountService.TryHasAccounts(out bool hasAccounts, out string error))
            {
                ShowLogin();
                SetLoginMessage(error, true);
                return;
            }

            if (hasAccounts)
            {
                ShowLogin();
                LoadRememberedCredential();
            }
            else
            {
                _rememberedCredentialService.TryClear(out _);
                ShowRegister(false);
            }
        }

        /// <summary>验证本机账户，成功后建立只存在于当前进程中的用户会话。</summary>
        private void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            SetLoginMessage(string.Empty, true);
            string password = LoginPasswordBox.Password;
            if (!_accountService.TryAuthenticate(
                    LoginUserNameTextBox.Text,
                    password,
                    out LocalUserIdentity? identity,
                    out string error) || identity == null)
            {
                LocalTraceService.Default.TryWriteOperation(
                    "用户登录", "失败", LoginUserNameTextBox.Text.Trim(),
                    "用户名或密码验证未通过", _accountService.AccountFilePath, out _);
                LoginPasswordBox.Clear();
                SetLoginMessage(error, true);
                LoginPasswordBox.Focus();
                return;
            }

            string rememberError = string.Empty;
            bool rememberSaved = RememberPasswordCheckBox.IsChecked == true
                ? _rememberedCredentialService.TrySave(identity.UserName, password, out rememberError)
                : _rememberedCredentialService.TryClear(out rememberError);
            LoginPasswordBox.Clear();
            if (!rememberSaved)
            {
                MessageBox.Show(
                    $"登录成功，但记住密码状态保存失败。\n{rememberError}",
                    "记住密码",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            UserSessionContext.SignIn(identity.UserName, identity.DisplayName);
            LocalTraceService.Default.TryWriteOperation(
                "用户登录", "成功", identity.UserName,
                $"本地账户 {identity.DisplayName} 登录系统",
                _accountService.AccountFilePath, out _);
            DialogResult = true;
        }

        /// <summary>创建首个账户，或在忘记密码时替换本机原账户。</summary>
        private void RegisterButton_Click(object sender, RoutedEventArgs e)
        {
            SetRegisterMessage(string.Empty, true);
            if (RegisterPasswordBox.Password != RegisterConfirmPasswordBox.Password)
            {
                SetRegisterMessage("两次输入的密码不一致。", true);
                RegisterConfirmPasswordBox.Clear();
                RegisterConfirmPasswordBox.Focus();
                return;
            }

            if (_isReRegistration &&
                MessageBox.Show(
                    "重新注册将使当前本机账户立即失效，但不会删除校准数据、系统设置和历史报告。是否继续？",
                    "确认重新注册",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            bool saved = _isReRegistration
                ? _accountService.TryReRegister(
                    RegisterUserNameTextBox.Text,
                    RegisterDisplayNameTextBox.Text,
                    RegisterPasswordBox.Password,
                    out LocalUserIdentity? identity,
                    out string error)
                : _accountService.TryRegister(
                    RegisterUserNameTextBox.Text,
                    RegisterDisplayNameTextBox.Text,
                    RegisterPasswordBox.Password,
                    out identity,
                    out error);
            if (!saved || identity == null)
            {
                SetRegisterMessage(error, true);
                return;
            }

            string action = _isReRegistration ? "重新注册本地账户" : "注册本地账户";
            string detail = _isReRegistration
                ? $"使用新账户 {identity.DisplayName} 替换原本机账户"
                : $"创建本地账户 {identity.DisplayName}";
            LocalTraceService.Default.TryWriteOperation(
                action, "成功", identity.UserName, detail,
                _accountService.AccountFilePath, out _);

            LoginUserNameTextBox.Text = identity.UserName;
            LoginPasswordBox.Clear();
            RememberPasswordCheckBox.IsChecked = false;
            RegisterPasswordBox.Clear();
            RegisterConfirmPasswordBox.Clear();
            bool wasReRegistration = _isReRegistration;
            string rememberClearError = string.Empty;
            bool rememberedCredentialCleared = !wasReRegistration ||
                _rememberedCredentialService.TryClear(out rememberClearError);
            ShowLogin();
            string successMessage = wasReRegistration
                    ? "重新注册成功，原账户已失效，请使用新账户登录。"
                    : "账户创建成功，请输入密码登录。";
            if (!rememberedCredentialCleared)
                successMessage += $" 旧的记住密码记录清除失败：{rememberClearError}";
            SetLoginMessage(successMessage, !rememberedCredentialCleared);
            LoginPasswordBox.Focus();
        }

        /// <summary>忘记密码时直接进入重新注册，不要求恢复码或原密码。</summary>
        private void ForgotPasswordButton_Click(object sender, RoutedEventArgs e) => ShowRegister(true);

        /// <summary>取消重新注册并返回原登录页面。</summary>
        private void BackToLoginButton_Click(object sender, RoutedEventArgs e)
        {
            RegisterPasswordBox.Clear();
            RegisterConfirmPasswordBox.Clear();
            SetLoginMessage(string.Empty, true);
            ShowLogin();
        }

        private void ShowLogin()
        {
            _isReRegistration = false;
            RegisterPanel.Visibility = Visibility.Collapsed;
            LoginPanel.Visibility = Visibility.Visible;
            Title = "登录 - 温湿度巡检仪校准系统";
            LoginUserNameTextBox.Focus();
        }

        private void ShowRegister(bool isReRegistration)
        {
            _isReRegistration = isReRegistration;
            LoginPanel.Visibility = Visibility.Collapsed;
            RegisterPanel.Visibility = Visibility.Visible;
            Title = isReRegistration
                ? "重新注册 - 温湿度巡检仪校准系统"
                : "注册 - 温湿度巡检仪校准系统";
            RegisterInfoTextBlock.Text = isReRegistration
                ? "无需原密码；新账户将替换原账户，校准数据不受影响"
                : "首次使用，请先创建本机登录账户";
            RegisterButton.Content = isReRegistration ? "重新注册账户" : "创建账户";
            BackToLoginButton.Visibility = isReRegistration ? Visibility.Visible : Visibility.Collapsed;
            RegisterUserNameTextBox.Text = isReRegistration ? LoginUserNameTextBox.Text.Trim() : string.Empty;
            RegisterDisplayNameTextBox.Clear();
            RegisterPasswordBox.Clear();
            RegisterConfirmPasswordBox.Clear();
            SetRegisterMessage(string.Empty, true);
            RegisterUserNameTextBox.Focus();
        }

        /// <summary>启动登录页时读取由当前 Windows 用户加密保存的凭据。</summary>
        private void LoadRememberedCredential()
        {
            if (!_rememberedCredentialService.TryLoad(
                    out bool hasCredential,
                    out string userName,
                    out string password,
                    out string error))
            {
                RememberPasswordCheckBox.IsChecked = false;
                SetLoginMessage(error, true);
                return;
            }
            if (!hasCredential) return;

            LoginUserNameTextBox.Text = userName;
            LoginPasswordBox.Password = password;
            RememberPasswordCheckBox.IsChecked = true;
            LoginPasswordBox.Focus();
        }

        private void SetLoginMessage(string message, bool isError)
        {
            LoginMessageTextBlock.Text = message;
            LoginMessageTextBlock.Foreground = new SolidColorBrush(
                isError ? Color.FromRgb(185, 28, 28) : Color.FromRgb(21, 128, 61));
        }

        private void SetRegisterMessage(string message, bool isError)
        {
            RegisterMessageTextBlock.Text = message;
            RegisterMessageTextBlock.Foreground = new SolidColorBrush(
                isError ? Color.FromRgb(185, 28, 28) : Color.FromRgb(21, 128, 61));
        }
    }
}
