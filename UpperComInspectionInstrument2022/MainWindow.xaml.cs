using System;
using System.IO.Ports;
using System.Windows;
using UpperComInspectionInstrument2022.Communication;
using UpperComInspectionInstrument2022.Services;
using UpperComInspectionInstrument2022.Views;

namespace UpperComInspectionInstrument2022
{
    /// <summary>
    /// 工业设备校准系统主窗口
    /// </summary>
    public partial class MainWindow : Window
    {
        /// <summary>
        /// Modbus RTU 通信客户端
        /// </summary>
        private readonly ModbusRtuClient _modbusClient;


        /// <summary>
        /// 巡检仪服务
        /// </summary>
        private readonly InspectionMeterService
            _inspectionMeterService;


        /// <summary>
        /// 自动采集服务
        /// </summary>
        private readonly InspectionDataAcquisitionService
            _acquisitionService;


        public MainWindow()
        {
            InitializeComponent();


            // =====================================================
            // 初始化通信层
            // =====================================================

            _modbusClient =
                new ModbusRtuClient();


            // 巡检仪服务
            _inspectionMeterService =
                new InspectionMeterService(
                    _modbusClient);


            // 自动采集服务
            //
            // 注意：
            // 这里使用你当前项目已经验证成功的
            // InspectionMeterService。
            //
            _acquisitionService =
                new InspectionDataAcquisitionService(
                    _inspectionMeterService);


            // =====================================================
            // 初始化首页
            // =====================================================

            ShowHome();


            // =====================================================
            // 初始化串口
            // =====================================================

            LoadSerialPorts();


            // =====================================================
            // 启动日志
            // =====================================================

            SetStatus(
                "系统启动完成");

        }


        // =========================================================
        // 首页
        // =========================================================

        /// <summary>
        /// 显示首页
        /// </summary>
        private void ShowHome()
        {
            HomeButton.Background =
                new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(
                        55,
                        65,
                        81));

            RealTimeMeasurementButton.Background =
                System.Windows.Media.Brushes.Transparent;

            CalibrationTaskButton.Background =
                System.Windows.Media.Brushes.Transparent;

            CalibrationResultButton.Background =
                System.Windows.Media.Brushes.Transparent;

            HistoryButton.Background =
                System.Windows.Media.Brushes.Transparent;

            SettingsButton.Background =
                System.Windows.Media.Brushes.Transparent;


            // 暂时使用主窗口内部的首页页面。
            //
            // 后续可以继续拆分成：
            //
            // Views/HomePage.xaml
            //

            MainFrame.Content =
                CreateHomeContent();
        }


        /// <summary>
        /// 创建首页内容
        /// </summary>
        private FrameworkElement CreateHomeContent()
        {
            System.Windows.Controls.Grid grid =
                new System.Windows.Controls.Grid();

            grid.Margin =
                new Thickness(35);


            grid.RowDefinitions.Add(
                new System.Windows.Controls.RowDefinition
                {
                    Height =
                        new GridLength(
                            90)
                });


            grid.RowDefinitions.Add(
                new System.Windows.Controls.RowDefinition
                {
                    Height =
                        new GridLength(
                            1,
                            GridUnitType.Star)
                });


            // =====================================================
            // 首页标题
            // =====================================================

            System.Windows.Controls.StackPanel titlePanel =
                new System.Windows.Controls.StackPanel();


            System.Windows.Controls.TextBlock title =
                new System.Windows.Controls.TextBlock
                {
                    Text =
                        "欢迎使用工业设备校准系统",

                    FontSize =
                        30,

                    FontWeight =
                        FontWeights.Bold,

                    Foreground =
                        new System.Windows.Media.SolidColorBrush(
                            System.Windows.Media.Color.FromRgb(
                                31,
                                41,
                                55))
                };


            titlePanel.Children.Add(title);


            System.Windows.Controls.TextBlock subtitle =
                new System.Windows.Controls.TextBlock
                {
                    Text =
                        "通过标准器巡检仪采集数据，并按照校准规范完成数据处理与结果评价",

                    FontSize =
                        15,

                    Foreground =
                        new System.Windows.Media.SolidColorBrush(
                            System.Windows.Media.Color.FromRgb(
                                107,
                                114,
                                128)),

                    Margin =
                        new Thickness(
                            0,
                            8,
                            0,
                            0)
                };


            titlePanel.Children.Add(subtitle);


            grid.Children.Add(titlePanel);


            // =====================================================
            // 首页功能区域
            // =====================================================

            System.Windows.Controls.Grid functionGrid =
                new System.Windows.Controls.Grid();


            System.Windows.Controls.Grid.SetRow(
                functionGrid,
                1);


            functionGrid.ColumnDefinitions.Add(
                new System.Windows.Controls.ColumnDefinition());


            functionGrid.ColumnDefinitions.Add(
                new System.Windows.Controls.ColumnDefinition());


            functionGrid.ColumnDefinitions.Add(
                new System.Windows.Controls.ColumnDefinition());


            functionGrid.RowDefinitions.Add(
                new System.Windows.Controls.RowDefinition());


            functionGrid.RowDefinitions.Add(
                new System.Windows.Controls.RowDefinition());


            // =====================================================
            // 实时测量
            // =====================================================

            System.Windows.Controls.Button realTimeButton =
                CreateHomeFunctionButton(
                    "实时测量",
                    "查看巡检仪实时采集数据");


            System.Windows.Controls.Grid.SetColumn(
                realTimeButton,
                0);

            System.Windows.Controls.Grid.SetRow(
                realTimeButton,
                0);


            realTimeButton.Click +=
                RealTimeMeasurementButton_Click;


            functionGrid.Children.Add(
                realTimeButton);


            // =====================================================
            // 校准任务
            // =====================================================

            System.Windows.Controls.Button calibrationButton =
                CreateHomeFunctionButton(
                    "校准任务",
                    "创建和管理校准任务");


            System.Windows.Controls.Grid.SetColumn(
                calibrationButton,
                1);

            System.Windows.Controls.Grid.SetRow(
                calibrationButton,
                0);


            calibrationButton.Click +=
                CalibrationTaskButton_Click;


            functionGrid.Children.Add(
                calibrationButton);


            // =====================================================
            // 校准结果
            // =====================================================

            System.Windows.Controls.Button resultButton =
                CreateHomeFunctionButton(
                    "校准结果",
                    "查看校准计算结果");


            System.Windows.Controls.Grid.SetColumn(
                resultButton,
                2);

            System.Windows.Controls.Grid.SetRow(
                resultButton,
                0);


            resultButton.Click +=
                CalibrationResultButton_Click;


            functionGrid.Children.Add(
                resultButton);


            // =====================================================
            // 历史记录
            // =====================================================

            System.Windows.Controls.Button historyButton =
                CreateHomeFunctionButton(
                    "历史记录",
                    "查看历史采集与校准记录");


            System.Windows.Controls.Grid.SetColumn(
                historyButton,
                0);

            System.Windows.Controls.Grid.SetRow(
                historyButton,
                1);


            historyButton.Click +=
                HistoryButton_Click;


            functionGrid.Children.Add(
                historyButton);


            // =====================================================
            // 系统设置
            // =====================================================

            System.Windows.Controls.Button settingsButton =
                CreateHomeFunctionButton(
                    "系统设置",
                    "串口、设备及系统参数设置");


            System.Windows.Controls.Grid.SetColumn(
                settingsButton,
                1);

            System.Windows.Controls.Grid.SetRow(
                settingsButton,
                1);


            settingsButton.Click +=
                SettingsButton_Click;


            functionGrid.Children.Add(
                settingsButton);


            grid.Children.Add(
                functionGrid);


            return grid;
        }


        /// <summary>
        /// 创建首页功能按钮
        /// </summary>
        private System.Windows.Controls.Button
            CreateHomeFunctionButton(
                string title,
                string description)
        {
            System.Windows.Controls.Button button =
                new System.Windows.Controls.Button
                {
                    Margin =
                        new Thickness(
                            10),

                    Background =
                        System.Windows.Media.Brushes.White,

                    BorderBrush =
                        new System.Windows.Media.SolidColorBrush(
                            System.Windows.Media.Color.FromRgb(
                                229,
                                231,
                                235)),

                    BorderThickness =
                        new Thickness(
                            1),

                    HorizontalContentAlignment =
                        HorizontalAlignment.Left,

                    VerticalContentAlignment =
                        VerticalAlignment.Center
                };


            System.Windows.Controls.StackPanel panel =
                new System.Windows.Controls.StackPanel
                {
                    Margin =
                        new Thickness(
                            25)
                };


            System.Windows.Controls.TextBlock titleText =
                new System.Windows.Controls.TextBlock
                {
                    Text =
                        title,

                    FontSize =
                        20,

                    FontWeight =
                        FontWeights.Bold,

                    Foreground =
                        new System.Windows.Media.SolidColorBrush(
                            System.Windows.Media.Color.FromRgb(
                                31,
                                41,
                                55))
                };


            System.Windows.Controls.TextBlock descriptionText =
                new System.Windows.Controls.TextBlock
                {
                    Text =
                        description,

                    FontSize =
                        13,

                    Margin =
                        new Thickness(
                            0,
                            8,
                            0,
                            0),

                    Foreground =
                        new System.Windows.Media.SolidColorBrush(
                            System.Windows.Media.Color.FromRgb(
                                107,
                                114,
                                128))
                };


            panel.Children.Add(titleText);

            panel.Children.Add(descriptionText);

            button.Content = panel;


            return button;
        }


        // =========================================================
        // 实时测量
        // =========================================================

        /// <summary>
        /// 打开实时测量页面
        /// </summary>
        private void RealTimeMeasurementButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                RealTimeMeasurementButton.Background =
                    new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(
                            55,
                            65,
                            81));


                HomeButton.Background =
                    System.Windows.Media.Brushes.Transparent;


                RealTimeMeasurementPage page =
                    new RealTimeMeasurementPage(
                        _acquisitionService);


                MainFrame.Navigate(page);


                SetStatus(
                    "已进入实时测量页面");
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "打开实时测量页面失败",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }


        // =========================================================
        // 校准任务
        // =========================================================

        private void CalibrationTaskButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            MessageBox.Show(
                "校准任务模块将在下一阶段开发。",
                "提示",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            SetStatus(
                "校准任务模块尚未开发");
        }


        // =========================================================
        // 校准结果
        // =========================================================

        private void CalibrationResultButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            MessageBox.Show(
                "校准结果模块将在下一阶段开发。",
                "提示",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            SetStatus(
                "校准结果模块尚未开发");
        }


        // =========================================================
        // 历史记录
        // =========================================================

        private void HistoryButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            MessageBox.Show(
                "历史记录模块将在下一阶段开发。",
                "提示",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            SetStatus(
                "历史记录模块尚未开发");
        }


        // =========================================================
        // 系统设置
        // =========================================================

        private void SettingsButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            MessageBox.Show(
                "系统设置模块将在下一阶段开发。",
                "提示",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            SetStatus(
                "系统设置模块尚未开发");
        }


        // =========================================================
        // 首页按钮
        // =========================================================

        private void HomeButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            ShowHome();

            SetStatus(
                "已返回首页");
        }


        // =========================================================
        // 串口
        // =========================================================

        /// <summary>
        /// 获取电脑当前串口
        /// </summary>
        private void LoadSerialPorts()
        {
            string[] ports =
                SerialPort.GetPortNames();

            Array.Sort(ports);
        }


        // =========================================================
        // 状态栏
        // =========================================================

        private void SetStatus(
            string message)
        {
            if (BottomStatusTextBlock != null)
            {
                BottomStatusTextBlock.Text =
                    "状态：" + message;
            }
        }


        // =========================================================
        // 窗口关闭
        // =========================================================

        protected override void OnClosed(
            EventArgs e)
        {
            try
            {
                // 停止自动采集

                if (_acquisitionService != null)
                {
                    _acquisitionService.Stop();
                }


                // 关闭Modbus串口

                if (_modbusClient != null)
                {
                    _modbusClient.Close();

                    _modbusClient.Dispose();
                }
            }
            catch
            {
                // 程序关闭阶段不再向用户弹出异常
            }


            base.OnClosed(e);
        }
    }
}