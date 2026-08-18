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
        public void ShowRealTimeMeasurementPage()
        {
            _realTimePage ??= new RealTimeMeasurementPage(_acquisitionService, _modbusClient);
            _realTimePage.RefreshTaskContext();
            SetActiveNavigation(CalibrationTaskButton);
            MainFrame.Navigate(_realTimePage);
            SetStatus(_acquisitionService.IsRunning ? "已返回正在运行的校准作业" : "已进入实时采集与校准");
        }

        public void ShowResultPage()
        {
            SetActiveNavigation(CalibrationTaskButton);
            MainFrame.Navigate(new ResultView());
            SetStatus("已进入结果与报告");
        }

        public void ShowSettingsPage()
        {
            SetActiveNavigation(SettingsButton);
            MainFrame.Navigate(new DeviceView());
            SetStatus("已进入系统设置");
        }

        public void ShowCalibrationJobPage()
        {
            if (_acquisitionService.IsRunning)
            {
                ShowRealTimeMeasurementPage();
                return;
            }

            ShowTaskConfigurationPage();
        }

        public void ShowTaskConfigurationPage()
        {
            SetActiveNavigation(CalibrationTaskButton);
            MainFrame.Navigate(new SchemeView());
            SetStatus("已进入校准作业配置");
        }

        private void ShowHistoryPage()
        {
            SetActiveNavigation(HistoryButton);
            MainFrame.Navigate(new HistoryView());
            SetStatus("已进入历史记录");
        }

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

        private void CalibrationTaskButton_Click(object sender, RoutedEventArgs e) => ShowCalibrationJobPage();
        private void HistoryButton_Click(object sender, RoutedEventArgs e) => ShowHistoryPage();
        private void SettingsButton_Click(object sender, RoutedEventArgs e) => ShowSettingsPage();

        // 保留隐藏入口的处理器，兼容旧 XAML 日志和页面跳转。
        private void HomeButton_Click(object sender, RoutedEventArgs e) => ShowCalibrationJobPage();
        private void RealTimeMeasurementButton_Click(object sender, RoutedEventArgs e) => ShowRealTimeMeasurementPage();
        private void CalibrationResultButton_Click(object sender, RoutedEventArgs e) => ShowResultPage();

        private void SetStatus(string message)
        {
            BottomStatusTextBlock.Text = "状态：" + message;
        }

        protected override void OnClosed(EventArgs e)
        {
            try
            {
                _acquisitionService.Stop();
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
