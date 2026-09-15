using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using UpperComInspectionInstrument2022.Communication;
using UpperComInspectionInstrument2022.Models;
using UpperComInspectionInstrument2022.Services;
using UpperComInspectionInstrument2022.Views;

namespace UpperComInspectionInstrument2022
{
    /// <summary>主窗口顶部全局运行状态的视觉语义。</summary>
    public enum GlobalRunStatusTone
    {
        Ready,
        Connected,
        Active,
        Warning,
        Error,
        Completed
    }

    /// <summary>
    /// 应用外壳只负责一级导航和共享服务生命周期。
    /// 校准作业内部按任务配置、实时采集、结果与报告顺序推进。
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly ModbusRtuClient _modbusClient;
        private readonly InspectionDataAcquisitionService _acquisitionService;
        private RealTimeMeasurementPage? _realTimePage;
        private bool _globalStatusReturnsToWorkbench;
        private bool _logoutRequested;

        /// <summary>供系统设置页判断当前任务快照是否已经进入不可变的正式采样阶段。</summary>
        public bool IsFormalCalibrationRunning => _realTimePage?.IsFormalCalibrationRunning == true;

        /// <summary>用户点击注销并且主窗口已完成资源释放时触发。</summary>
        public event EventHandler? LogoutRequested;

        /// <summary>
        /// 初始化全局设置、任务上下文、通信服务，并将用户带到任务配置页。
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();
            CurrentUserNameTextBlock.Text = UserSessionContext.Current?.DisplayName ?? "未登录";
            SystemSettingsContext.Load();
            CalibrationTaskContext.Load();

            _modbusClient = new ModbusRtuClient();
            InspectionMeterService inspectionMeterService = new InspectionMeterService(_modbusClient);
            _acquisitionService = new InspectionDataAcquisitionService(inspectionMeterService);

            int recoveredJobs = CalibrationFileStorageService.Default.RecoverAbandonedJobs(out string jobRecoveryError);
            int recoveredRealtimeSessions = RealtimeMeasurementFileStorageService.Default.RecoverAbandonedSessions(out string realtimeRecoveryError);
            List<string> recoveryErrors = new();
            if (!string.IsNullOrWhiteSpace(jobRecoveryError)) recoveryErrors.Add(jobRecoveryError);
            if (!string.IsNullOrWhiteSpace(realtimeRecoveryError)) recoveryErrors.Add(realtimeRecoveryError);
            string startupDetails = $"恢复正式作业 {recoveredJobs} 个，恢复实时会话 {recoveredRealtimeSessions} 个";
            if (recoveryErrors.Count > 0) startupDetails += "；" + string.Join("；", recoveryErrors);
            LocalTraceService.Default.TryWriteRuntime(
                recoveryErrors.Count == 0 ? "信息" : "警告",
                "应用",
                "程序启动",
                startupDetails,
                string.Empty,
                CalibrationFileStorageService.Default.DataRootPath,
                out _);
            if (recoveredJobs + recoveredRealtimeSessions > 0)
            {
                LocalTraceService.Default.TryWriteOperation(
                    "恢复异常退出记录",
                    "成功",
                    string.Empty,
                    startupDetails,
                    CalibrationFileStorageService.Default.DataRootPath,
                    out _);
            }

            ShowTaskConfigurationPage();
            SetStatus(recoveredJobs + recoveredRealtimeSessions > 0
                ? $"系统启动完成；已标记 {recoveredJobs + recoveredRealtimeSessions} 个上次中断记录"
                : "系统启动完成");
            SetGlobalRunStatus(
                recoveredJobs + recoveredRealtimeSessions > 0 ? "已恢复上次中断记录" : "系统就绪",
                recoveredJobs + recoveredRealtimeSessions > 0 ? GlobalRunStatusTone.Warning : GlobalRunStatusTone.Ready,
                false);

            if (recoveryErrors.Count > 0)
            {
                MessageBox.Show(
                    string.Join("\n", recoveryErrors),
                    "历史记录恢复不完整",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }


            else if (recoveredJobs + recoveredRealtimeSessions > 0)
            {
                MessageBox.Show(
                    $"检测到上次未正常结束的记录：正式作业 {recoveredJobs} 个、实时测量 {recoveredRealtimeSessions} 个。\n\n系统已标记为“已中断”，已保存数据没有删除，可在历史目录中复核。",
                    "已恢复上次中断记录",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        /// <summary>
        /// 显示校准工作台。工作台实例会被复用，因此切换页面不会中断正在进行的采集。
        /// </summary>
        public void ShowRealTimeMeasurementPage()
        {
            _realTimePage ??= new RealTimeMeasurementPage(_acquisitionService, _modbusClient);
            _realTimePage.RefreshTaskContext();
            SetActiveNavigation(CalibrationTaskButton);
            MainFrame.Navigate(_realTimePage);
            SetStatus(_acquisitionService.IsRunning ? "已返回正在运行的校准作业" : "已进入实时采集与校准");
        }

        /// <summary>
        /// 显示本次正式校准的计算结果。
        /// </summary>
        public void ShowResultPage()
        {
            SetActiveNavigation(CalibrationTaskButton);
            MainFrame.Navigate(new ResultView());
            SetStatus("已进入结果与报告");
        }

        /// <summary>
        /// 显示实验室与标准器等系统级设置。
        /// 优先使用任务配置页当前选项，否则使用已保存任务规范；设置页不再重复选择规范。
        /// </summary>
        public void ShowSettingsPage(int? preferredStandardIndex = null)
        {
            int standardIndex = preferredStandardIndex
                ?? (MainFrame.Content as SchemeView)?.SelectedStandardIndex
                ?? CalibrationTaskContext.StandardIndex;
            SetActiveNavigation(SettingsButton);
            MainFrame.Navigate(new DeviceView(standardIndex));
            SetStatus("已进入系统设置");
        }

        /// <summary>系统资料保存后通知复用中的工作台，使下一组普通实时数据使用最新通道修正。</summary>
        public void NotifySystemSettingsSaved()
        {
            _realTimePage?.NotifySystemSettingsSaved();
        }

        /// <summary>
        /// 进入校准作业：本次会话已经进入过工作台且任务仍有效时，始终返回同一工作台实例；
        /// 只有尚未建立工作台或任务不存在时才进入任务配置。这样即使停止测量后切到历史/设置，
        /// 再返回也不会让实时矩阵和趋势看起来“消失”。
        /// </summary>
        public void ShowCalibrationJobPage()
        {
            if (_realTimePage != null && CalibrationTaskContext.IsConfigured)
            {
                ShowRealTimeMeasurementPage();
                return;
            }

            ShowTaskConfigurationPage();
        }

        /// <summary>
        /// 打开新建或修改校准任务所使用的配置页面。
        /// </summary>
        public void ShowTaskConfigurationPage()
        {
            SetActiveNavigation(CalibrationTaskButton);
            MainFrame.Navigate(new SchemeView());
            SetStatus("已进入校准作业配置");
        }

        /// <summary>
        /// 显示本地文件归档形成的历史任务列表。
        /// </summary>
        private void ShowHistoryPage()
        {
            SetActiveNavigation(HistoryButton);
            MainFrame.Navigate(new HistoryView());
            SetStatus("已进入历史记录");
        }

        /// <summary>
        /// 统一更新左侧导航按钮的选中背景，确保任意时刻只有一个入口高亮。
        /// </summary>
        private void SetActiveNavigation(Button activeButton)
        {
            Brush transparent = Brushes.Transparent;
            CalibrationTaskButton.Background = transparent;
            HistoryButton.Background = transparent;
            SettingsButton.Background = transparent;
            HomeButton.Background = transparent;
            RealTimeMeasurementButton.Background = transparent;
            CalibrationResultButton.Background = transparent;
            activeButton.Background = new SolidColorBrush(Color.FromRgb(51, 65, 85));
        }

        /// <summary>响应“校准作业”导航按钮。</summary>
        private void CalibrationTaskButton_Click(object sender, RoutedEventArgs e) => ShowCalibrationJobPage();
        /// <summary>响应“历史记录”导航按钮。</summary>
        private void HistoryButton_Click(object sender, RoutedEventArgs e) => ShowHistoryPage();
        /// <summary>响应“系统设置”导航按钮。</summary>
        private void SettingsButton_Click(object sender, RoutedEventArgs e) => ShowSettingsPage();

        /// <summary>注销会关闭当前业务窗口，因此会先停止采集、释放串口并保存中断状态。</summary>
        private void LogoutButton_Click(object sender, RoutedEventArgs e)
        {
            string message = _acquisitionService.IsRunning
                ? "当前正在采集。注销将停止采集，并把未完成的正式校准标记为中断。是否继续？"
                : "确定要注销当前账户并返回登录页面吗？";
            if (MessageBox.Show(message, "注销账户", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            string userName = UserSessionContext.Current?.UserName ?? string.Empty;
            LocalTraceService.Default.TryWriteOperation(
                "用户注销", "成功", userName,
                "当前账户退出系统", CalibrationFileStorageService.Default.DataRootPath, out _);
            _logoutRequested = true;
            Close();
        }

        // 保留隐藏入口的处理器，兼容旧 XAML 日志和页面跳转。
        /// <summary>兼容旧版首页入口，实际转到校准作业。</summary>
        private void HomeButton_Click(object sender, RoutedEventArgs e) => ShowCalibrationJobPage();
        /// <summary>兼容旧版实时测量入口，实际转到校准工作台。</summary>
        private void RealTimeMeasurementButton_Click(object sender, RoutedEventArgs e) => ShowRealTimeMeasurementPage();
        /// <summary>兼容旧版结果入口，实际转到结果页。</summary>
        private void CalibrationResultButton_Click(object sender, RoutedEventArgs e) => ShowResultPage();

        /// <summary>
        /// 在窗口底部状态栏显示一条简短的操作结果。
        /// </summary>
        private void SetStatus(string message)
        {
            BottomStatusTextBlock.Text = "状态：" + message;
        }

        /// <summary>
        /// 更新始终可见的运行状态。采集或正式校准在后台继续时，用户可点击状态返回工作台。
        /// </summary>
        public void SetGlobalRunStatus(string message, GlobalRunStatusTone tone, bool returnsToWorkbench)
        {
            GlobalRunStatusTextBlock.Text = message;
            _globalStatusReturnsToWorkbench = returnsToWorkbench;
            GlobalRunStatusBorder.Cursor = returnsToWorkbench ? Cursors.Hand : Cursors.Arrow;
            GlobalRunStatusBorder.ToolTip = returnsToWorkbench ? "点击返回正在运行的校准工作台" : message;

            (Color background, Color indicator) = tone switch
            {
                GlobalRunStatusTone.Connected => (Color.FromRgb(30, 64, 175), Color.FromRgb(147, 197, 253)),
                GlobalRunStatusTone.Active => (Color.FromRgb(7, 89, 133), Color.FromRgb(34, 211, 238)),
                GlobalRunStatusTone.Warning => (Color.FromRgb(120, 53, 15), Color.FromRgb(251, 191, 36)),
                GlobalRunStatusTone.Error => (Color.FromRgb(127, 29, 29), Color.FromRgb(248, 113, 113)),
                GlobalRunStatusTone.Completed => (Color.FromRgb(20, 83, 45), Color.FromRgb(74, 222, 128)),
                _ => (Color.FromRgb(15, 23, 42), Color.FromRgb(34, 197, 94))
            };
            GlobalRunStatusBorder.Background = new SolidColorBrush(background);
            GlobalRunStatusEllipse.Fill = new SolidColorBrush(indicator);
        }

        /// <summary>后台采集状态可点击时返回复用的工作台页面。</summary>
        private void GlobalRunStatusBorder_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_globalStatusReturnsToWorkbench)
                ShowRealTimeMeasurementPage();
        }

        /// <summary>
        /// 窗口关闭时停止采集、释放串口，并把尚未结束的正式校准标记为“已中断”。
        /// </summary>
        protected override void OnClosed(EventArgs e)
        {
            try
            {
                LocalTraceService.Default.TryWriteRuntime(
                    "信息", "应用", "程序关闭", "操作人员关闭应用", CalibrationFileStorageService.Default.CurrentJobId,
                    CalibrationFileStorageService.Default.CurrentJobDirectory ?? string.Empty, out _);
                CalibrationFileStorageService.Default.TryMarkInterrupted("程序关闭，正式校准自动中断", out _);
                RealtimeMeasurementFileStorageService.Default.TryEndSession("程序关闭", "应用关闭，实时测量记录已结束", out _);
                // 最多等待一个串口超时周期外加余量，避免窗口关闭时仍有旧请求访问已释放的串口。
                _acquisitionService.StopAndWait(TimeSpan.FromSeconds(4));
                _modbusClient.Close();
                _modbusClient.Dispose();
            }
            catch
            {
                // 应用关闭阶段不再向操作人员弹出异常。
            }

            if (_logoutRequested) LogoutRequested?.Invoke(this, EventArgs.Empty);
            base.OnClosed(e);
        }

    }
}
