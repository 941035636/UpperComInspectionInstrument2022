using System;
using System.Windows;
using UpperComInspectionInstrument2022.Models;
using UpperComInspectionInstrument2022.Views;

namespace UpperComInspectionInstrument2022
{
    /// <summary>
    /// WPF 应用程序入口。
    /// 负责登录窗口与业务主窗口之间的生命周期切换。
    /// 只有本地账户验证成功后才创建 <see cref="MainWindow"/>。
    /// </summary>
    public partial class App : Application
    {
        private bool _returningToLogin;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            ShowLoginWindow();
        }

        /// <summary>显示登录窗口；用户取消或关闭时退出整个应用。</summary>
        private void ShowLoginWindow()
        {
            LoginWindow loginWindow = new();
            MainWindow = loginWindow;
            bool? loginResult = loginWindow.ShowDialog();
            if (loginResult == true && UserSessionContext.IsAuthenticated)
            {
                ShowBusinessWindow();
                return;
            }

            UserSessionContext.SignOut();
            Shutdown();
        }

        /// <summary>登录成功后创建业务主窗口，并监听关闭和注销事件。</summary>
        private void ShowBusinessWindow()
        {
            MainWindow businessWindow = new();
            MainWindow = businessWindow;
            businessWindow.LogoutRequested += BusinessWindow_LogoutRequested;
            businessWindow.Closed += BusinessWindow_Closed;
            businessWindow.Show();
        }

        /// <summary>注销时等待旧主窗口关闭完成，再重新打开登录窗口。</summary>
        private void BusinessWindow_LogoutRequested(object? sender, EventArgs e)
        {
            _returningToLogin = true;
            UserSessionContext.SignOut();
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _returningToLogin = false;
                ShowLoginWindow();
            }));
        }

        /// <summary>普通关闭主窗口时退出应用；注销关闭则由登录窗口接管。</summary>
        private void BusinessWindow_Closed(object? sender, EventArgs e)
        {
            if (sender is MainWindow businessWindow)
            {
                businessWindow.LogoutRequested -= BusinessWindow_LogoutRequested;
                businessWindow.Closed -= BusinessWindow_Closed;
            }

            if (_returningToLogin) return;
            UserSessionContext.SignOut();
            Shutdown();
        }
    }
}
