using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using UpperComInspectionInstrument2022.Communication;
using UpperComInspectionInstrument2022.Models;
using UpperComInspectionInstrument2022.Services;
using UpperComInspectionInstrument2022.Views;

namespace UpperComInspectionInstrument2022
{
    /// <summary>
    /// 应用外壳只负责一级导航和共享服务生命周期。
    /// 校准作业内部按任务配置、实时采集、结果与报告顺序推进。
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly ModbusRtuClient _modbusClient;
        private readonly InspectionDataAcquisitionService _acquisitionService;
        private RealTimeMeasurementPage? _realTimePage;

        /// <summary>
        /// 初始化全局设置、任务上下文、通信服务，并将用户带到任务配置页。
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();
            SystemSettingsContext.Load();
            CalibrationTaskContext.Load();

            _modbusClient = new ModbusRtuClient();
            InspectionMeterService inspectionMeterService = new InspectionMeterService(_modbusClient);
            _acquisitionService = new InspectionDataAcquisitionService(inspectionMeterService);

            ShowTaskConfigurationPage();
            SetStatus("系统启动完成");
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
        /// 显示设备、标准器和环境条件等系统级设置。
        /// </summary>
        public void ShowSettingsPage()
        {
            SetActiveNavigation(SettingsButton);
            MainFrame.Navigate(new DeviceView());
            SetStatus("已进入系统设置");
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
        /// 窗口关闭时停止采集、释放串口，并把尚未结束的正式校准标记为“已中断”。
        /// </summary>
        protected override void OnClosed(EventArgs e)
        {
            try
            {
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

            base.OnClosed(e);
        }
    }
}
