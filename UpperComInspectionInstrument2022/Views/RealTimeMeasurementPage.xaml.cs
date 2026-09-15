using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using UpperComInspectionInstrument2022.Communication;
using UpperComInspectionInstrument2022.Models;
using UpperComInspectionInstrument2022.Services;
using UpperComInspectionInstrument2022.ViewModels;

namespace UpperComInspectionInstrument2022.Views
{
    /// <summary>
    /// 校准工作台，将设备连接、连续实时测量、趋势观察、稳定确认和正式校准采样合并在同一页面。
    /// 实时快照用于观察且只保留近期数据；正式样本按任务间隔另行留存并进入规范计算与文件归档。
    /// </summary>
    public partial class RealTimeMeasurementPage : Page
    {
        private readonly RealTimeMeasurementViewModel _viewModel;
        private readonly InspectionDataAcquisitionService _acquisitionService;
        private readonly ModbusRtuClient _modbusClient;
        private readonly RealtimeMeasurementFileStorageService _realtimeStorageService;
        private bool _deviceResponding;
        private bool _requiredChannelsValid;
        private bool _trendLooksStable;
        private bool _calibrationRunning;
        private int _calibrationSampleCount;
        private DateTime? _setPointReachedAt;
        private DateTime _nextCalibrationSampleAt;
        private int? _realtimeIntervalBeforeFormalCalibration;
        private DataTable _measurementTable = new DataTable();
        private string _appliedTaskSignature = string.Empty;
        private readonly List<TrendSample> _visibleTrendSamples = new List<TrendSample>();
        private readonly ToolTip _measurementChartToolTip = new ToolTip
        {
            Placement = PlacementMode.Mouse,
            StaysOpen = true,
            Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
            Foreground = Brushes.White,
            Padding = new Thickness(9, 6, 9, 6)
        };
        private Rect _chartPlotArea = Rect.Empty;
        private Line? _chartCursorLine;

        /// <summary>当前是否正在执行正式校准采样；系统设置据此决定是否允许刷新任务快照。</summary>
        public bool IsFormalCalibrationRunning => _calibrationRunning;

        /// <summary>
        /// 初始化工作台，订阅共享采集服务事件，并建立点数联动、串口列表和任务状态。
        /// </summary>
        public RealTimeMeasurementPage(InspectionDataAcquisitionService acquisitionService, ModbusRtuClient modbusClient)
        {
            InitializeComponent();
            _acquisitionService = acquisitionService ?? throw new ArgumentNullException(nameof(acquisitionService));
            _modbusClient = modbusClient ?? throw new ArgumentNullException(nameof(modbusClient));
            _realtimeStorageService = RealtimeMeasurementFileStorageService.Default;
            _viewModel = new RealTimeMeasurementViewModel();
            DataContext = _viewModel;
            _measurementChartToolTip.PlacementTarget = MeasurementChartCanvas;

            _acquisitionService.DataAcquired += OnDataAcquired;
            _acquisitionService.AcquisitionError += OnAcquisitionError;

            BaudRateComboBox.SelectedIndex = 0;
            CalibrationTypeComboBox.SelectedIndex = 0;
            SensorTypeComboBox.ItemsSource = TemperatureSensorCatalog.DisplayNames;
            SensorTypeComboBox.SelectedIndex = -1;
            for (int i = 0; i <= 50; i++) TemperaturePointCountComboBox.Items.Add(i.ToString());
            for (int i = 0; i <= 10; i++) HumidityPointCountComboBox.Items.Add(i.ToString());
            for (int i = 0; i <= 30; i++) CalibrationCountComboBox.Items.Add(i.ToString());
            TemperaturePointCountComboBox.SelectedItem = "0";
            HumidityPointCountComboBox.SelectedItem = "0";
            CalibrationCountComboBox.SelectedItem = "0";

            TemperaturePointCountComboBox.SelectionChanged += PointCountComboBox_SelectionChanged;
            HumidityPointCountComboBox.SelectionChanged += PointCountComboBox_SelectionChanged;
            CalibrationTypeComboBox.SelectionChanged += CalibrationTypeComboBox_SelectionChanged;
            ApplyTaskContext();
            UpdateCenterPointOptions();
            UpdateParameterVisibility();
            RefreshPorts();
            UpdateConnectionStatus();
            ViewResultButton.IsEnabled = CalibrationTaskContext.HasCompletedCalibration;
            UpdateFormalConditionControls();
            UpdateRealtimeRecordStatus();
            _appliedTaskSignature = BuildTaskSignature();


        }

        /// <summary>把已保存任务映射到工作台参数和规范提示；未配置任务时保留设备联调能力。</summary>
        private void ApplyTaskContext()
        {
            if (!CalibrationTaskContext.IsConfigured)
            {
                TaskSummaryTextBlock.Text = "尚未配置任务，任务参数可在下方填写。";
                TaskParameterPanel.Visibility = Visibility.Visible;
                //FormalRuleTextBlock.Text = "正式校准前必须先建立任务，实时测量可用于设备联调。";
                return;
            }

            CalibrationTypeComboBox.SelectedIndex = CalibrationTaskContext.IncludesHumidity ? 2 : 0;
            SensorTypeComboBox.SelectedIndex = CalibrationTaskContext.SensorTypeIndex;
            TemperaturePointCountComboBox.SelectedItem = CalibrationTaskContext.TemperaturePointCount.ToString();
            HumidityPointCountComboBox.SelectedItem = CalibrationTaskContext.HumidityPointCount.ToString();
            CalibrationCountComboBox.SelectedItem = CalibrationTaskContext.PlannedCount.ToString();
            CenterPointComboBox.SelectedItem = CalibrationTaskContext.TemperatureCenterPoint.ToString();
            CalibrationIntervalTextBox.Text = CalibrationTaskContext.SamplingIntervalSeconds.ToString();
            CenterIntervalTextBox.Text = (CalibrationTaskContext.StableWaitMinutes * 60).ToString();

            string type = CalibrationTaskContext.StandardIndex == 1 ? "炉温参数" : CalibrationTaskContext.IncludesHumidity ? "温湿度参数" : "温度参数";
            string standard = CalibrationTaskContext.StandardIndex == 1 ? "JJF 1376-2012" : "JJF 1101-2019";
            string setPoint = CalibrationTaskContext.IncludesHumidity
                ? $"设定 {CalibrationTaskContext.SetTemperature:0.###} ℃ / {CalibrationTaskContext.SetHumidity:0.###} %RH"
                : $"设定 {CalibrationTaskContext.SetTemperature:0.###} ℃";
            string pressure = CalibrationTaskContext.AmbientPressure.HasValue ? $" / {CalibrationTaskContext.AmbientPressure:0.###} kPa" : string.Empty;
            List<string> equipmentParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(CalibrationTaskContext.EquipmentName)) equipmentParts.Add(CalibrationTaskContext.EquipmentName);
            if (!string.IsNullOrWhiteSpace(CalibrationTaskContext.ModelSpecification)) equipmentParts.Add(CalibrationTaskContext.ModelSpecification);
            if (!string.IsNullOrWhiteSpace(CalibrationTaskContext.EquipmentSerialNumber)) equipmentParts.Add(CalibrationTaskContext.EquipmentSerialNumber);
            string equipmentSummary = equipmentParts.Count == 0 ? "设备档案未填写（可选）" : string.Join(" · ", equipmentParts);
            TaskSummaryTextBlock.Text = $"{standard} · {type}\n{equipmentSummary}\n{setPoint} · 温度 {CalibrationTaskContext.TemperaturePointCount} 点 · 湿度 {CalibrationTaskContext.HumidityPointCount} 点\n环境 {CalibrationTaskContext.AmbientTemperature:0.###} ℃ / {CalibrationTaskContext.AmbientHumidity:0.###} %RH{pressure} · 正式样本 {CalibrationTaskContext.PlannedCount} 组 × {CalibrationTaskContext.SamplingIntervalSeconds} s";
            CalibrationStandardRule rule = CalibrationStandardRuleService.GetRule(CalibrationTaskContext.StandardIndex, CalibrationTaskContext.VolumeIndex, CalibrationTaskContext.IncludesHumidity);
            //FormalRuleTextBlock.Text = $"{rule.StabilityRuleText}\n正式采样：{rule.SamplingRuleText}";
            DutDisplayTemperatureTextBox.Text = CalibrationTaskContext.DutDisplayTemperature?.ToString("0.###") ?? string.Empty;
            DutDisplayHumidityTextBox.Text = CalibrationTaskContext.DutDisplayHumidity?.ToString("0.###") ?? string.Empty;
            FormalSampleProgressTextBlock.Text = $"正式样本 0 / {CalibrationTaskContext.PlannedCount}";
            TaskParameterPanel.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// 页面再次显示时同步任务配置。采集运行中不覆盖当前参数，保证切换页面不会打断测量。
        /// </summary>
        public void RefreshTaskContext()
        {
            if (_acquisitionService.IsRunning)
            {
                SaveRealtimeRecordCheckBox.IsEnabled = false;
                UpdateRealtimeRecordStatus();
                return;
            }

            string newSignature = BuildTaskSignature();
            bool taskChanged = !string.Equals(_appliedTaskSignature, newSignature, StringComparison.Ordinal);
            ApplyTaskContext();
            UpdateCenterPointOptions();
            UpdateParameterVisibility();
            UpdateFormalConditionControls();
            ViewResultButton.IsEnabled = CalibrationTaskContext.HasCompletedCalibration;

            if (taskChanged)
            {
                ResetMeasurementDisplay("任务参数已更新，请按新测点数重新开始实时测量", true);
            }
            _appliedTaskSignature = newSignature;
            SaveRealtimeRecordCheckBox.IsEnabled = true;
            UpdateRealtimeRecordStatus();
        }

        /// <summary>
        /// 系统设置保存后使普通实时测量立即改用最新修正值，并撤销旧数据下的稳定确认。
        /// 正式校准运行中只保存系统设置，不改变已经冻结的本轮校准依据。
        /// </summary>
        public void NotifySystemSettingsSaved()
        {
            if (_calibrationRunning)
            {
                StatusTextBlock.Text = "系统设置已保存；当前正式校准继续使用启动时冻结的修正值";
                return;
            }

            _trendLooksStable = false;
            _setPointReachedAt = null;
            SetPointReachedCheckBox.IsChecked = false;
            ConfirmStableCheckBox.IsChecked = false;
            StabilityTextBlock.Text = "修正值已更新，等待新数据重新判断稳定性";
            StabilityTextBlock.Foreground = Brushes.DarkOrange;
            StatusTextBlock.Text = "通道修正已更新，将从下一组实时数据生效";
            EvaluateFormalReadiness();
        }

        /// <summary>生成影响工作台矩阵和采集解析的任务签名，用于判断是否必须清空旧实时数据。</summary>
        private static string BuildTaskSignature()
        {
            if (!CalibrationTaskContext.IsConfigured) return "UNCONFIGURED";
            return string.Join("|",
                CalibrationTaskContext.StandardIndex,
                CalibrationTaskContext.VolumeIndex,
                CalibrationTaskContext.CalibrationTypeIndex,
                CalibrationTaskContext.TemperaturePointCount,
                CalibrationTaskContext.HumidityPointCount,
                CalibrationTaskContext.TemperatureCenterPoint,
                CalibrationTaskContext.HumidityCenterPoint,
                CalibrationTaskContext.SensorTypeCode,
                CalibrationTaskContext.SetTemperature,
                CalibrationTaskContext.SetHumidity);
        }

        /// <summary>按规范稳定依据显示计时确认或人工稳定确认控件。</summary>
        private void UpdateFormalConditionControls()
        {
            bool configured = CalibrationTaskContext.IsConfigured;
            bool timedWait = configured && CalibrationTaskContext.StandardIndex == 0 && CalibrationTaskContext.StabilityBasisIndex == 1;
            SetPointReachedCheckBox.Visibility = timedWait ? Visibility.Visible : Visibility.Collapsed;
            ConfirmStableCheckBox.Visibility = configured && !timedWait ? Visibility.Visible : Visibility.Collapsed;
            DutDisplayHumidityLabel.Visibility = configured && CalibrationTaskContext.IncludesHumidity ? Visibility.Visible : Visibility.Collapsed;
            DutDisplayHumidityTextBox.Visibility = configured && CalibrationTaskContext.IncludesHumidity ? Visibility.Visible : Visibility.Collapsed;
            FormalReadinessPanel.Visibility = configured ? Visibility.Visible : Visibility.Collapsed;
            EvaluateFormalReadiness();
        }

        /// <summary>记录“达到设定值”的起始时刻，或响应人工稳定确认变化。</summary>
        private void StabilityConfirmation_Changed(object sender, RoutedEventArgs e)
        {
            if (SetPointReachedCheckBox.IsChecked == true)
                _setPointReachedAt ??= DateTime.Now;
            else
                _setPointReachedAt = null;
            EvaluateFormalReadiness();
        }

        /// <summary>重新评估正式采样条件，并同步按钮文字、可用状态和面向用户的原因说明。</summary>
        private bool EvaluateFormalReadiness()
        {
            bool ready = TryGetFormalReadiness(out string reason);
            FormalReadinessTextBlock.Text = reason;
            FormalReadinessTextBlock.Foreground = ready ? Brushes.DarkGreen : Brushes.DarkOrange;
            bool standardReferenceInvalid = HasInvalidTaskStandardReference();
            RefreshStandardReferenceButton.Visibility = CalibrationTaskContext.IsConfigured && standardReferenceInvalid
                ? Visibility.Visible
                : Visibility.Collapsed;
            if (!_calibrationRunning)
            {
                StartCalibrationButton.Content = ready
                    ? "启动校准采样"
                    : standardReferenceInvalid
                        ? "请先完善标准器资料"
                        : reason.Contains("稳定", StringComparison.Ordinal)
                            ? "请先确认设备稳定"
                            : reason.Contains("实时数据", StringComparison.Ordinal) || reason.Contains("通道", StringComparison.Ordinal)
                                ? "等待有效实时数据"
                                : "暂不能启动校准";
                StartCalibrationButton.ToolTip = reason;
            }
            StartCalibrationButton.IsEnabled = ready && !_calibrationRunning;
            return ready;
        }

        /// <summary>
        /// 汇总任务、设备响应、必需通道、外观检查、标准器证书和稳定确认等启动条件。
        /// 趋势稳定只作为参考，不替代规范要求的操作人员确认。
        /// </summary>
        private bool TryGetFormalReadiness(out string reason)
        {
            if (!CalibrationTaskContext.IsConfigured)
            {
                reason = "请先建立并保存校准任务。";
                return false;
            }
            List<string> blockers = new List<string>();
            if (!_viewModel.IsAcquiring || !_deviceResponding)
                blockers.Add("等待巡检仪返回有效实时数据");
            else if (!_requiredChannelsValid)
                blockers.Add("任务要求的温湿度测点尚未全部有效，请检查接线、点数和通道原始值");
            if (CalibrationTaskContext.StandardIndex == 1 && CalibrationTaskContext.AppearanceCheckIndex != 1)
                blockers.Add("箱式炉外观与运行检查尚未确认“符合”");
            if (!CalibrationTaskContext.TryValidateReferencedStandardSettings(out string standardReferenceError))
                blockers.Add(standardReferenceError);

            bool timedWait = CalibrationTaskContext.StandardIndex == 0 && CalibrationTaskContext.StabilityBasisIndex == 1;
            if (timedWait)
            {
                if (_setPointReachedAt == null)
                    blockers.Add("设备达到设定值后勾选确认，系统再执行稳定等待计时");
                else
                {
                    TimeSpan remaining = TimeSpan.FromMinutes(CalibrationTaskContext.StableWaitMinutes) - (DateTime.Now - _setPointReachedAt.Value);
                    if (remaining > TimeSpan.Zero)
                        blockers.Add($"规范稳定等待中，剩余 {Math.Ceiling(remaining.TotalMinutes):0} min");
                }
            }
            else if (ConfirmStableCheckBox.IsChecked != true)
            {
                blockers.Add(CalibrationTaskContext.StandardIndex == 1
                    ? "请确认箱式炉已达到校准温度并处于热稳定状态。"
                    : "请依据设备说明书或现场状态确认设备已经稳定");
            }


            if (blockers.Count > 0)
            {
                reason = "尚需完成：\n• " + string.Join("\n• ", blockers);
                if (_trendLooksStable)
                    reason += "\n趋势参考：最近 5 组波动已稳定，但仍需完成上述规范确认。";
                return false;
            }

            reason = _trendLooksStable
                ? "正式校准条件已确认，可以开始采样。"
                : "正式条件已确认；最近 5 组趋势仍有波动，请核实后再启动。";

            return true;
        }

        /// <summary>检查任务固化的标准器身份、证书有效期和规范能力字段是否完整有效。</summary>
        private static bool HasInvalidTaskStandardReference() =>
            !CalibrationTaskContext.TryValidateReferencedStandardSettings(out _);

        /// <summary>不中断实时测量，将最新系统标准器资料重新固化到当前任务。</summary>
        private void RefreshStandardReferenceButton_Click(object sender, RoutedEventArgs e)
        {
            if (CalibrationTaskContext.TrySnapshotCurrentStandardSettings(
                    CalibrationTaskContext.StandardIndex, CalibrationTaskContext.IncludesHumidity, out string error))
            {
                CalibrationTaskContext.Save();
                EvaluateFormalReadiness();
                StatusTextBlock.Text = "已将系统设置中的标准器资料同步到当前任务";
                MessageBox.Show("标准器身份、证书、能力和不确定度资料已同步到当前任务。实时测量数据未中断。", "标准器资料已同步", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            MessageBoxResult result = MessageBox.Show(
                $"{error}\n\n是否现在前往系统设置维护？实时测量会继续运行，保存后从左侧返回“校准作业”，再点击同步按钮。",
                "标准器资料未完成", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes && Application.Current.MainWindow is MainWindow mainWindow)
                mainWindow.ShowSettingsPage();




        }

        /// <summary>响应“刷新串口”按钮。</summary>
        private void RefreshPortsButton_Click(object sender, RoutedEventArgs e) => RefreshPorts();

        /// <summary>枚举当前可用串口，并保留已经由本程序打开的端口。</summary>
        private void RefreshPorts()
        {
            string? current = PortComboBox.SelectedItem as string;
            PortComboBox.Items.Clear();
            string[] ports = SerialPort.GetPortNames();
            Array.Sort(ports);
            foreach (string port in ports)
            {
                // 本程序已经打开的串口无法再次探测，但仍应保留在下拉列表中。
                if ((_modbusClient.IsOpen && string.Equals(_modbusClient.PortName, port, StringComparison.OrdinalIgnoreCase)) || CanOpenPort(port))
                    PortComboBox.Items.Add(port);
            }

            if (current != null && PortComboBox.Items.Contains(current)) PortComboBox.SelectedItem = current;
            else if (PortComboBox.Items.Count > 0) PortComboBox.SelectedIndex = 0;
            PortTextBlock.Text = PortComboBox.SelectedItem as string ?? "串口：未发现可用端口";
        }

        /// <summary>短暂打开候选串口，排除已被其他程序独占的端口。</summary>
        private static bool CanOpenPort(string portName)
        {
            try
            {
                using SerialPort probe = new SerialPort(portName, 115200, Parity.None, 8, StopBits.One)
                {
                    Handshake = Handshake.None,
                    DtrEnable = false,
                    RtsEnable = false,
                    ReadTimeout = 200,
                    WriteTimeout = 200
                };
                probe.Open();
                probe.Close();
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>测点数变化时联动中心点选项和实时数据矩阵列。</summary>
        private void PointCountComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CenterPointComboBox != null) UpdateCenterPointOptions();
            if (MeasurementMatrixDataGrid != null) UpdateMeasurementMatrixColumns();
        }

        /// <summary>按当前温湿度最大点数重建中心点下拉选项，并尽量保留原选择。</summary>
        private void UpdateCenterPointOptions()
        {
            string selected = CenterPointComboBox.SelectedItem as string ?? "1";
            CenterPointComboBox.Items.Clear();
            int temperatureCount = int.TryParse(TemperaturePointCountComboBox.SelectedItem as string, out int temperatureValue) ? temperatureValue : 0;
            int humidityCount = int.TryParse(HumidityPointCountComboBox.SelectedItem as string, out int humidityValue) ? humidityValue : 0;
            int count = Math.Max(1, Math.Max(temperatureCount, humidityCount));
            for (int i = 1; i <= count; i++) CenterPointComboBox.Items.Add(i.ToString());
            CenterPointComboBox.SelectedItem = CenterPointComboBox.Items.Contains(selected) ? selected : "1";
        }

        /// <summary>校准类型变化时更新温度、湿度参数及矩阵显示。</summary>
        private void CalibrationTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CalibrationTypeComboBox != null) UpdateParameterVisibility();
        }

        /// <summary>
        /// 根据温度/湿度模式、任务锁定和采集状态控制参数可见性与可编辑性。
        /// </summary>
        private void UpdateParameterVisibility()
        {
            string type = (CalibrationTypeComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "温度";
            bool hasTemperature = type.Contains("温度");
            bool hasHumidity = type.Contains("湿度");
            bool taskLocked = CalibrationTaskContext.IsConfigured;
            bool editable = !_viewModel.IsAcquiring && !_calibrationRunning && !taskLocked;
            TemperaturePointCountComboBox.IsEnabled = hasTemperature && editable;
            HumidityPointCountComboBox.IsEnabled = hasHumidity && editable;
            SensorTypeComboBox.IsEnabled = hasTemperature && editable;
            CalibrationTypeComboBox.IsEnabled = editable;
            TemperaturePointCountLabel.Visibility = hasTemperature ? Visibility.Visible : Visibility.Collapsed;
            HumidityPointCountLabel.Visibility = hasHumidity ? Visibility.Visible : Visibility.Collapsed;
            SensorTypeLabel.Visibility = hasTemperature ? Visibility.Visible : Visibility.Collapsed;
            CenterPointComboBox.IsEnabled = editable && (hasTemperature || hasHumidity);
            CenterTemperatureLabel.Visibility = hasTemperature ? Visibility.Visible : Visibility.Collapsed;
            CenterTemperatureTextBlock.Visibility = hasTemperature ? Visibility.Visible : Visibility.Collapsed;
            CenterHumidityLabel.Visibility = hasHumidity ? Visibility.Visible : Visibility.Collapsed;
            CenterHumidityTextBlock.Visibility = hasHumidity ? Visibility.Visible : Visibility.Collapsed;
            UpdateMeasurementMatrixColumns();
        }

        /// <summary>
        /// 按当前任务测点数动态重建 DataTable 列，使“选择几个点就显示几个通道”。
        /// 重建列会清空旧矩阵，避免不同点数的数据发生列错位。
        /// </summary>
        private void UpdateMeasurementMatrixColumns()
        {
            if (MeasurementMatrixDataGrid == null || CalibrationTypeComboBox == null) return;
            _measurementTable = new DataTable();
            _measurementTable.Columns.Add("采集序号", typeof(long));
            _measurementTable.Columns.Add("采集时间", typeof(string));
            if (HasTemperatureMode())
            {
                int count = GetPointCount(TemperaturePointCountComboBox);
                for (int i = 1; i <= count; i++) _measurementTable.Columns.Add($"温度{i}", typeof(string));
            }
            if (HasHumidityMode())
            {
                int count = GetPointCount(HumidityPointCountComboBox);
                for (int i = 1; i <= count; i++) _measurementTable.Columns.Add($"湿度{i}", typeof(string));
            }
            MeasurementMatrixDataGrid.ItemsSource = _measurementTable.DefaultView;
        }

        /// <summary>读取点数下拉框，无法解析时安全返回 0。</summary>
        private static int GetPointCount(ComboBox comboBox)
        {
            return int.TryParse(comboBox.SelectedItem as string, out int count) ? Math.Max(0, count) : 0;
        }

        /// <summary>为自动生成的矩阵列补充单位，并明确标出中心点或湿度 O 点。</summary>
        private void MeasurementMatrixDataGrid_AutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
        {
            if (e.PropertyName == "采集序号")
            {
                e.Column.Width = new DataGridLength(74);
                return;
            }
            if (e.PropertyName == "采集时间")
            {
                e.Column.Width = new DataGridLength(106);
                return;
            }

            int temperatureCenter = CalibrationTaskContext.IsConfigured
                ? CalibrationTaskContext.TemperatureCenterPoint
                : int.TryParse(CenterPointComboBox.SelectedItem as string, out int center) ? center : 1;
            int humidityCenter = CalibrationTaskContext.IsConfigured
                ? Math.Max(1, CalibrationTaskContext.HumidityCenterPoint)
                : temperatureCenter;
            if (TryReadChannelNumber(e.PropertyName, "温度", out int temperatureChannel))
            {
                e.Column.Header = temperatureChannel == temperatureCenter
                    ? $"T{temperatureChannel} 中心 (℃)"
                    : $"T{temperatureChannel} (℃)";
                e.Column.Width = new DataGridLength(temperatureChannel == temperatureCenter ? 98 : 76);
            }
            else if (TryReadChannelNumber(e.PropertyName, "湿度", out int humidityChannel))
            {
                e.Column.Header = humidityChannel == humidityCenter
                    ? $"H{humidityChannel} / O点 (%RH)"
                    : $"H{humidityChannel} (%RH)";
                e.Column.Width = new DataGridLength(humidityChannel == humidityCenter ? 116 : 92);
            }
        }

        /// <summary>从“温度5”或“湿度3”等内部列名中读取通道号。</summary>
        private static bool TryReadChannelNumber(string propertyName, string prefix, out int channel)
        {
            channel = 0;
            return propertyName.StartsWith(prefix, StringComparison.Ordinal) &&
                   int.TryParse(propertyName[prefix.Length..], out channel);
        }


        /// <summary>把最新快照按温度点、湿度点顺序插入矩阵首行，最多保留 200 行。</summary>
        private void AppendMeasurementMatrixRow(MeasurementSnapshot snapshot)
        {
            if (_measurementTable.Columns.Count < 3) return;
            DataRow row = _measurementTable.NewRow();
            row["采集序号"] = snapshot.Sequence;
            row["采集时间"] = snapshot.Timestamp.ToString("HH:mm:ss");
            foreach (DataColumn column in _measurementTable.Columns)
            {
                if (column.ColumnName == "采集序号" || column.ColumnName == "采集时间") continue;
                row[column.ColumnName] = "-";
            }
            int temperaturePointCount = HasTemperatureMode() ? GetPointCount(TemperaturePointCountComboBox) : 0;
            int humidityPointCount = HasHumidityMode() ? GetPointCount(HumidityPointCountComboBox) : 0;
            foreach (InspectionChannelData channel in MeasurementChannelSelectionService.SelectRequired(
                         snapshot.Channels, temperaturePointCount, humidityPointCount))
            {
                string prefix = channel.Type == ChannelType.Temperature ? "温度" : "湿度";
                string columnName = prefix + channel.Channel;
                if (_measurementTable.Columns.Contains(columnName))
                    row[columnName] = FormatMeasurementMatrixValue(channel);
            }
            _measurementTable.Rows.InsertAt(row, 0);
            while (_measurementTable.Rows.Count > 200) _measurementTable.Rows.RemoveAt(_measurementTable.Rows.Count - 1);
        }

        /// <summary>
        /// 格式化实时矩阵数值。湿度超出有效范围时仍保留设备实际解析值和正负号，
        /// 但明确标注为异常；不可解析的通道继续显示通用异常提示。
        /// </summary>
        private static string FormatMeasurementMatrixValue(InspectionChannelData channel)
        {
            if (channel.IsValid) return channel.Value.ToString("F2");
            if (channel.Role == ChannelRole.Humidity && double.IsFinite(channel.Value))
                return $"{channel.Value:F2}（异常）";

            return "异常";
        }

        /// <summary>连接或断开巡检仪串口；连接成功只表示端口已打开，收到有效响应后才显示设备已响应。</summary>
        private void ConnectDeviceButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_modbusClient.IsOpen)
                {
                    string disconnectedPort = _modbusClient.PortName ?? "未知串口";
                    _modbusClient.Close();
                    _deviceResponding = false;
                    _requiredChannelsValid = false;
                    StartAcquisitionButton.IsEnabled = false;
                    PortComboBox.IsEnabled = true;
                    BaudRateComboBox.IsEnabled = true;
                    StatusTextBlock.Text = "巡检仪连接已断开";
                    UpdateConnectionStatus();
                    WriteOperation("断开巡检仪", "成功", $"串口 {disconnectedPort} 已关闭", string.Empty);
                    WriteRuntime("信息", "通信", "断开串口", $"串口 {disconnectedPort} 已关闭");
                    return;
                }

                if (PortComboBox.SelectedItem is not string portName)
                    throw new InvalidOperationException("没有可用串口，请重新插入巡检仪后刷新串口。");
                int baudRate = int.Parse((BaudRateComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "115200");
                if (!_modbusClient.IsOpen || !string.Equals(_modbusClient.PortName, portName, StringComparison.OrdinalIgnoreCase))
                    _modbusClient.Open(portName, baudRate);

                ConnectDeviceButton.IsEnabled = true;
                StartAcquisitionButton.IsEnabled = true;
                PortComboBox.IsEnabled = false;
                BaudRateComboBox.IsEnabled = false;
                StatusTextBlock.Text = "串口已打开，点击开始实时测量";
                UpdateConnectionStatus();
                WriteOperation("连接巡检仪", "成功", $"串口 {portName}，波特率 {baudRate}", string.Empty);
                WriteRuntime("信息", "通信", "打开串口", $"串口 {portName}，波特率 {baudRate}");
            }
            catch (UnauthorizedAccessException)
            {
                UpdateConnectionStatus();
                WriteOperation("连接巡检仪", "失败", "串口被占用或访问被拒绝", string.Empty);
                WriteRuntime("错误", "通信", "打开串口失败", "串口被占用或访问被拒绝");
                MessageBox.Show("串口当前不可用，可能已被其他程序占用或设备已断开。请关闭 Qt/串口工具后刷新端口。", "连接巡检仪失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                UpdateConnectionStatus();
                WriteOperation("连接巡检仪", "失败", ex.Message, string.Empty);
                WriteRuntime("错误", "通信", "打开串口失败", ex.Message);
                MessageBox.Show(ex.Message, "连接巡检仪失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 校验串口和临时测量参数后启动连续实时采集。该操作不会自动开始正式校准。
        /// </summary>
        private void StartAcquisitionButton_Click(object sender, RoutedEventArgs e)
        {
            bool realtimeSessionStarted = false;
            try
            {
                if (PortComboBox.SelectedItem is not string portName)
                    throw new InvalidOperationException("没有选择可用串口，请先刷新并选择巡检仪端口。");
                if (!byte.TryParse(SlaveAddressTextBox.Text.Trim(), out byte slaveAddress) || slaveAddress == 0 || slaveAddress > 247)
                    throw new InvalidOperationException("从站地址必须是 1~247 的整数。");
                if (!int.TryParse(IntervalTextBox.Text.Trim(), out int interval) || interval < 200)
                    throw new InvalidOperationException("读取周期不能小于 200 ms。");
                if (HasTemperatureMode() && GetPointCount(TemperaturePointCountComboBox) < 1)
                    throw new InvalidOperationException("温度测点数必须大于 0，矩阵列数会按该数量生成。");
                if (HasHumidityMode() && GetPointCount(HumidityPointCountComboBox) < 1)
                    throw new InvalidOperationException("湿度测点数必须大于 0，矩阵列数会按该数量生成。");
                if (HasTemperatureMode() && SensorTypeComboBox.SelectedIndex < 0)
                    throw new InvalidOperationException("请选择巡检仪实际接入的温度传感器类型。");
                int baudRate = int.Parse((BaudRateComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "115200");
                if (!_modbusClient.IsOpen || !string.Equals(_modbusClient.PortName, portName, StringComparison.OrdinalIgnoreCase))
                    _modbusClient.Open(portName, baudRate);

                string calibrationType = (CalibrationTypeComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "温度";
                if (SaveRealtimeRecordCheckBox.IsChecked == true)
                {
                    RealtimeMeasurementSessionInfo sessionInfo = BuildRealtimeMeasurementSessionInfo(
                        portName,
                        baudRate,
                        slaveAddress,
                        interval,
                        calibrationType);
                    if (!_realtimeStorageService.TryBeginSession(sessionInfo, out string realtimeStorageError))
                        throw new InvalidOperationException(realtimeStorageError);
                    realtimeSessionStarted = true;
                }

                if (!_acquisitionService.Start(slaveAddress, interval, calibrationType))
                    throw new InvalidOperationException("上一次采集仍在停止，请等待巡检仪当前请求结束后再试。");
                _viewModel.IsAcquiring = true;
                _trendLooksStable = false;
                _requiredChannelsValid = false;
                _calibrationRunning = false;
                _setPointReachedAt = null;
                SetPointReachedCheckBox.IsChecked = false;
                ConfirmStableCheckBox.IsChecked = false;
                ConnectDeviceButton.IsEnabled = false;
                StartAcquisitionButton.IsEnabled = false;
                StopAcquisitionButton.IsEnabled = true;
                StartCalibrationButton.IsEnabled = false;
                SetExecutionParametersEnabled(false);
                StabilityTextBlock.Text = "采集中，形成趋势";
                StabilityTextBlock.Foreground = Brushes.DarkOrange;
                StatusTextBlock.Text = "实时测量中，等待稳定条件确认";
                UpdateConnectionStatus();
                UpdateRealtimeRecordStatus();
                EvaluateFormalReadiness();
                string sessionDirectory = _realtimeStorageService.CurrentSessionDirectory ?? string.Empty;
                WriteOperation("开始实时测量", "成功", $"{portName} / 从站 {slaveAddress} / 周期 {interval} ms / {calibrationType}", sessionDirectory);
                WriteRuntime("信息", "采集", "开始实时测量", $"{portName} / 从站 {slaveAddress} / 周期 {interval} ms / {calibrationType}", sessionDirectory);
            }
            catch (Exception ex)
            {
                if (realtimeSessionStarted)
                    _realtimeStorageService.TryEndSession("启动失败", ex.Message, out _);
                SaveRealtimeRecordCheckBox.IsEnabled = true;
                UpdateRealtimeRecordStatus(ex.Message);
                UpdateConnectionStatus();
                WriteOperation("开始实时测量", "失败", ex.Message, _realtimeStorageService.CurrentSessionDirectory ?? string.Empty);
                WriteRuntime("错误", "采集", "启动实时测量失败", ex.Message, _realtimeStorageService.CurrentSessionDirectory ?? string.Empty);
                MessageBox.Show(ex.Message, "启动实时测量失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>把当前连接、测点和可选任务信息冻结为本次普通实时测量会话的摘要。</summary>
        private RealtimeMeasurementSessionInfo BuildRealtimeMeasurementSessionInfo(
            string portName,
            int baudRate,
            byte slaveAddress,
            int interval,
            string calibrationType)
        {
            string standard = CalibrationTaskContext.IsConfigured
                ? CalibrationTaskContext.StandardIndex == 1 ? "JJF 1376-2012" : "JJF 1101-2019"
                : "未建立任务（设备联调）";
            return new RealtimeMeasurementSessionInfo
            {
                PortName = portName,
                BaudRate = baudRate,
                SlaveAddress = slaveAddress,
                IntervalMilliseconds = interval,
                CalibrationType = calibrationType,
                SensorType = SensorTypeComboBox.SelectedItem?.ToString() ?? string.Empty,
                TemperaturePointCount = HasTemperatureMode() ? GetPointCount(TemperaturePointCountComboBox) : 0,
                HumidityPointCount = HasHumidityMode() ? GetPointCount(HumidityPointCountComboBox) : 0,
                HasCalibrationTask = CalibrationTaskContext.IsConfigured,
                Standard = standard,
                EquipmentName = CalibrationTaskContext.EquipmentName,
                EquipmentSerialNumber = CalibrationTaskContext.EquipmentSerialNumber
            };
        }

        /// <summary>
        /// 在实时采集保持运行的前提下启动正式校准：读取被校设备示值、清空正式样本并建立本地作业。
        /// </summary>
        private void StartCalibrationButton_Click(object sender, RoutedEventArgs e)
        {
            // 正式采样开始前再冻结一次当前系统资料，保证屏幕、原始记录和报告采用同一组修正值。
            if (!CalibrationTaskContext.TrySnapshotCurrentStandardSettings(
                    CalibrationTaskContext.StandardIndex,
                    CalibrationTaskContext.IncludesHumidity,
                    out string standardSettingsError))
            {
                MessageBox.Show(
                    $"{standardSettingsError}\n\n请先在系统设置中完善标准器资料。",
                    "标准器资料无法冻结",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
            // 任务可能提前配置；以正式采样实际启动日覆盖配置日，供原始记录和证书使用。
            CalibrationTaskContext.CalibrationDate = DateTime.Today;
            CalibrationTaskContext.Save();

            if (!EvaluateFormalReadiness())
            {
                MessageBox.Show(FormalReadinessTextBlock.Text, "暂不能启动正式校准", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!TryReadDutDisplays(out double? dutTemperature, out double? dutHumidity)) return;
            CalibrationTaskContext.DutDisplayTemperature = dutTemperature;
            CalibrationTaskContext.DutDisplayHumidity = dutHumidity;
            CalibrationTaskContext.Save();

            if (!TryAlignRealtimeIntervalForFormalCalibration(out string intervalError))
            {
                MessageBox.Show(intervalError, "正式采样间隔无法执行", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            CalibrationRunContext.Begin();
            if (!CalibrationFileStorageService.Default.TryBeginJob(out string storageError))
            {
                RestoreRealtimeIntervalAfterFormalCalibration();
                CalibrationRunContext.Clear();
                MessageBox.Show(storageError, "无法建立本地作业", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _calibrationRunning = true;
            _calibrationSampleCount = 0;
            _nextCalibrationSampleAt = DateTime.Now;
            StartCalibrationButton.Content = "校准采样中";
            StartCalibrationButton.IsEnabled = false;
            ViewResultButton.IsEnabled = false;
            FormalSampleProgressTextBlock.Text = $"正式样本 0 / {CalibrationTaskContext.PlannedCount}";
            string intervalStatus = _realtimeIntervalBeforeFormalCalibration.HasValue
                ? $"设备轮询已由 {_realtimeIntervalBeforeFormalCalibration.Value} ms 自动调整为 {_acquisitionService.CurrentIntervalMilliseconds} ms"
                : $"设备轮询 {_acquisitionService.CurrentIntervalMilliseconds} ms";
            StatusTextBlock.Text = $"正式校准已启动；按 {CalibrationTaskContext.SamplingIntervalSeconds} s 间隔留存样本；{intervalStatus}";
            PublishGlobalRunStatus($"{_modbusClient.PortName} · 正式采样 0/{CalibrationTaskContext.PlannedCount}", GlobalRunStatusTone.Active);
            UpdateParameterVisibility();
            WriteOperation(
                "启动正式校准采样",
                "成功",
                $"计划 {CalibrationTaskContext.PlannedCount} 组，间隔 {CalibrationTaskContext.SamplingIntervalSeconds} s",
                CalibrationFileStorageService.Default.CurrentJobDirectory ?? string.Empty);
            WriteRuntime(
                "信息",
                "校准",
                "启动正式采样",
                $"计划 {CalibrationTaskContext.PlannedCount} 组",
                CalibrationFileStorageService.Default.CurrentJobDirectory ?? string.Empty);
        }

        /// <summary>
        /// 正式采样开始时校验设备完整读取耗时，并在实时轮询慢于正式间隔时动态加快轮询。
        /// 串口和采集循环保持运行，避免实时观察到正式校准之间出现断流。
        /// </summary>
        private bool TryAlignRealtimeIntervalForFormalCalibration(out string error)
        {
            long requestedMilliseconds = (long)Math.Max(1, CalibrationTaskContext.SamplingIntervalSeconds) * 1000L;
            if (requestedMilliseconds > int.MaxValue)
            {
                error = "正式采样间隔超出系统可执行范围，请返回任务配置重新设置。";
                return false;
            }

            int formalIntervalMilliseconds = (int)requestedMilliseconds;
            int lastReadDurationMilliseconds = _acquisitionService.LastReadDurationMilliseconds;
            if (lastReadDurationMilliseconds >= formalIntervalMilliseconds)
            {
                error = $"巡检仪最近一次完整读取耗时约 {lastReadDurationMilliseconds} ms，已经达到或超过正式采样间隔 {formalIntervalMilliseconds} ms，无法保证每组都是新的完整数据。请增大正式采样间隔。";
                return false;
            }

            int currentIntervalMilliseconds = _acquisitionService.CurrentIntervalMilliseconds;
            _realtimeIntervalBeforeFormalCalibration = null;
            if (currentIntervalMilliseconds <= formalIntervalMilliseconds)
            {
                error = string.Empty;
                return true;
            }

            if (!_acquisitionService.TryUpdateInterval(formalIntervalMilliseconds))
            {
                error = "实时采集循环当前不可调整，请确认实时测量仍在运行后重试。";
                return false;
            }

            _realtimeIntervalBeforeFormalCalibration = currentIntervalMilliseconds;
            IntervalTextBox.Text = formalIntervalMilliseconds.ToString();
            WriteRuntime(
                "信息",
                "校准",
                "正式采样自动调整设备轮询周期",
                $"{currentIntervalMilliseconds} ms → {formalIntervalMilliseconds} ms",
                _realtimeStorageService.CurrentSessionDirectory ?? string.Empty);
            error = string.Empty;
            return true;
        }

        /// <summary>正式校准完成、中断或失败后恢复操作人员启动实时测量时使用的轮询周期。</summary>
        private void RestoreRealtimeIntervalAfterFormalCalibration()
        {
            if (!_realtimeIntervalBeforeFormalCalibration.HasValue) return;
            int originalIntervalMilliseconds = _realtimeIntervalBeforeFormalCalibration.Value;
            _realtimeIntervalBeforeFormalCalibration = null;
            if (_acquisitionService.IsRunning)
                _acquisitionService.TryUpdateInterval(originalIntervalMilliseconds);
            IntervalTextBox.Text = originalIntervalMilliseconds.ToString();
            WriteRuntime(
                "信息",
                "校准",
                "正式采样结束并恢复实时轮询周期",
                $"恢复为 {originalIntervalMilliseconds} ms",
                _realtimeStorageService.CurrentSessionDirectory ?? string.Empty);
        }

        /// <summary>从上一个计划采样时刻递增到下一时刻，避免以实际处理完成时间为基准造成累计漂移。</summary>
        private void AdvanceNextCalibrationSampleAt(DateTime currentSnapshotTime, int intervalSeconds)
        {
            TimeSpan interval = TimeSpan.FromSeconds(Math.Max(1, intervalSeconds));
            do
            {
                _nextCalibrationSampleAt = _nextCalibrationSampleAt.Add(interval);
            }
            while (_nextCalibrationSampleAt <= currentSnapshotTime);
        }

        /// <summary>读取本次正式样本要配对的被校设备温湿度示值，并保存到任务上下文。</summary>
        private bool TryReadDutDisplays(out double? temperature, out double? humidity)
        {
            temperature = null;
            humidity = null;
            if (!double.TryParse(DutDisplayTemperatureTextBox.Text.Trim(), out double temperatureValue) || !double.IsFinite(temperatureValue))
            {
                MessageBox.Show("请填写被校设备当前温度示值。该示值会随正式样本一起记录。", "被校设备示值", MessageBoxButton.OK, MessageBoxImage.Warning);
                DutDisplayTemperatureTextBox.Focus();
                return false;
            }
            temperature = temperatureValue;
            if (CalibrationTaskContext.IncludesHumidity)
            {
                if (!double.TryParse(DutDisplayHumidityTextBox.Text.Trim(), out double humidityValue) || !double.IsFinite(humidityValue))
                {
                    MessageBox.Show("请填写被校设备当前湿度示值。", "被校设备示值", MessageBoxButton.OK, MessageBoxImage.Warning);
                    DutDisplayHumidityTextBox.Focus();
                    return false;
                }
                humidity = humidityValue;
            }
            return true;
        }

        /// <summary>
        /// 暂停实时采集并等待当前串口请求完整退出；若正式校准尚未完成，同时将本地作业标记为中断。
        /// </summary>
        private async void StopAcquisitionButton_Click(object sender, RoutedEventArgs e)
        {
            // 先禁止可能创建新请求的按钮，再异步等待后台循环退出，等待期间 UI 仍可正常重绘。
            StopAcquisitionButton.IsEnabled = false;
            StartAcquisitionButton.IsEnabled = false;
            ConnectDeviceButton.IsEnabled = false;
            StatusTextBlock.Text = "正在暂停，等待当前巡检仪请求结束…";
            PublishGlobalRunStatus("正在安全停止巡检仪请求…", GlobalRunStatusTone.Warning);
            string storageWarning = string.Empty;
            string realtimeStorageWarning = string.Empty;
            bool interruptedFormalCalibration = _calibrationRunning;
            if (interruptedFormalCalibration)
                CalibrationFileStorageService.Default.TryMarkInterrupted("操作人员停止了正式校准采样", out storageWarning);
            _realtimeStorageService.TryEndSession("已停止", "操作人员停止实时测量", out realtimeStorageWarning);
            _calibrationRunning = false;
            RestoreRealtimeIntervalAfterFormalCalibration();
            try
            {
                await _acquisitionService.StopAsync();
            }
            catch (Exception ex)
            {
                realtimeStorageWarning = string.IsNullOrWhiteSpace(realtimeStorageWarning)
                    ? "停止采集循环时发生异常：" + ex.Message
                    : realtimeStorageWarning + "；停止采集循环时发生异常：" + ex.Message;
            }

            _viewModel.IsAcquiring = false;
            _trendLooksStable = false;
            _requiredChannelsValid = false;
            StartCalibrationButton.Content = "启动校准";
            StartCalibrationButton.IsEnabled = false;
            StartAcquisitionButton.IsEnabled = _modbusClient.IsOpen;
            StopAcquisitionButton.IsEnabled = false;
            ConnectDeviceButton.IsEnabled = true;
            PortComboBox.IsEnabled = true;
            BaudRateComboBox.IsEnabled = true;
            SetExecutionParametersEnabled(true);
            StabilityTextBlock.Text = "已暂停";
            StabilityTextBlock.Foreground = Brushes.DarkOrange;
            StatusTextBlock.Text = "已暂停实时测量";
            UpdateConnectionStatus();
            List<string> stopDetails = new() { "采集循环已退出" };
            if (interruptedFormalCalibration) stopDetails.Add("未完成的正式校准已标记中断");
            if (!string.IsNullOrWhiteSpace(storageWarning)) stopDetails.Add(storageWarning);
            if (!string.IsNullOrWhiteSpace(realtimeStorageWarning)) stopDetails.Add(realtimeStorageWarning);
            string stopDescription = string.Join("；", stopDetails);
            bool stopHasWarning = !string.IsNullOrWhiteSpace(storageWarning) || !string.IsNullOrWhiteSpace(realtimeStorageWarning);
            WriteOperation("暂停实时测量", stopHasWarning ? "警告" : "成功", stopDescription, _realtimeStorageService.CurrentSessionDirectory ?? string.Empty);
            WriteRuntime(stopHasWarning ? "警告" : "信息", "采集", "暂停实时测量", stopDescription, _realtimeStorageService.CurrentSessionDirectory ?? string.Empty);
            UpdateRealtimeRecordStatus(realtimeStorageWarning);
            if (!string.IsNullOrWhiteSpace(storageWarning))
                MessageBox.Show(storageWarning, "本地作业状态未保存", MessageBoxButton.OK, MessageBoxImage.Warning);
            if (!string.IsNullOrWhiteSpace(realtimeStorageWarning))
                MessageBox.Show(realtimeStorageWarning, "实时记录状态未保存", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        /// <summary>经用户确认后清空工作台实时快照和正式样本，但保持当前设备连接状态。</summary>
        private void ClearDataButton_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("确定清空当前校准工作台的所有采集快照吗？", "确认", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            if (_calibrationRunning &&
                !CalibrationFileStorageService.Default.TryMarkInterrupted("操作人员清空了当前工作台数据", out string storageWarning))
                MessageBox.Show(storageWarning, "本地作业状态未保存", MessageBoxButton.OK, MessageBoxImage.Warning);
            ResetMeasurementDisplay("实时数据已清空，采集连接保持当前状态", false);
            WriteOperation("清空工作台显示", "成功", "仅清空内存矩阵和趋势，不删除已落盘 CSV", CalibrationFileStorageService.Default.CurrentJobDirectory ?? string.Empty);
        }

        /// <summary>统一重置矩阵、曲线、摘要和正式采样状态，并可选择同时重置设备响应标志。</summary>
        private void ResetMeasurementDisplay(string status, bool resetDeviceState)
        {
            _viewModel.ClearSnapshots();
            _measurementTable.Rows.Clear();
            CurrentSequenceTextBlock.Text = "-";
            CurrentTimeTextBlock.Text = "-";
            LiveValueSummaryTextBlock.Text = "等待设备数据";
            StabilityTextBlock.Text = "未开始";
            CenterSummaryTextBlock.Text = "点 1：- / -";
            QualitySummaryTextBlock.Text = "任务通道 0/0";
            QualitySummaryTextBlock.ToolTip = null;
            MeasurementChartCanvas.Children.Clear();
            CalibrationRunContext.Clear();
            _calibrationRunning = false;
            RestoreRealtimeIntervalAfterFormalCalibration();
            _calibrationSampleCount = 0;
            _requiredChannelsValid = false;
            if (resetDeviceState) _deviceResponding = false;
            FormalSampleProgressTextBlock.Text = $"正式样本 0 / {(CalibrationTaskContext.IsConfigured ? CalibrationTaskContext.PlannedCount : 0)}";
            StartCalibrationButton.Content = "启动校准采样";
            StartCalibrationButton.IsEnabled = false;
            ViewResultButton.IsEnabled = false;
            StatusTextBlock.Text = status;
            ChartScaleTextBlock.Text = "等待有效任务通道";
            UpdateConnectionStatus();
            EvaluateFormalReadiness();
        }

        /// <summary>
        /// 处理一组完整设备响应：应用证书修正、生成实时快照、刷新 UI，
        /// 并在正式采样到点时把同一快照持久化；达到计划组数后计算并完成归档。
        /// </summary>
        private void OnDataAcquired(long acquisitionId, List<InspectionChannelData> data)
        {
            bool firstResponseOrRecovered = !_deviceResponding;
            _deviceResponding = true;
            if (firstResponseOrRecovered)
            {
                WriteRuntime(
                    "信息",
                    "通信",
                    "巡检仪有效响应",
                    $"采集序号 {acquisitionId}，返回通道 {data.Count} 个",
                    _realtimeStorageService.CurrentSessionDirectory ?? string.Empty);
            }
            Dispatcher.Invoke(() =>
            {
                // 普通实时测量跟随最新系统设置；正式采样锁定任务快照，且 RawValue 始终保留修正前值。
                ChannelCorrectionService.ApplyForMeasurement(data,
                    _calibrationRunning,
                    SystemSettingsContext.TemperatureChannelCorrections,
                    SystemSettingsContext.HumidityChannelCorrections,
                    CalibrationTaskContext.ReferencedTemperatureCorrections,
                    CalibrationTaskContext.ReferencedHumidityCorrections);
                int temperaturePointCount = HasTemperatureMode() ? GetPointCount(TemperaturePointCountComboBox) : 0;
                int humidityPointCount = HasHumidityMode() ? GetPointCount(HumidityPointCountComboBox) : 0;
                List<InspectionChannelData> requiredChannels = MeasurementChannelSelectionService.SelectRequired(
                    data, temperaturePointCount, humidityPointCount);
                int expectedCount = (HasTemperatureMode() ? temperaturePointCount : 0) + humidityPointCount;
                int validCount = requiredChannels.FindAll(item => item.IsValid).Count;
                _requiredChannelsValid = expectedCount > 0 && requiredChannels.Count == expectedCount && validCount == expectedCount;
                MeasurementSnapshot snapshot = new MeasurementSnapshot
                {
                    Sequence = acquisitionId,
                    Timestamp = DateTime.Now,
                    Channels = new List<InspectionChannelData>(data),
                    ValidChannelCount = validCount,
                    InvalidChannelCount = Math.Max(0, expectedCount - validCount)
                };

                _viewModel.AddSnapshot(snapshot);
                AppendMeasurementMatrixRow(snapshot);
                UpdateConnectionStatus();
                UpdateMeasurementSummary(snapshot);
                EvaluateStability();
                DrawMeasurementChart();

                // 普通实时记录独立于正式校准样本：每组完整响应立即保存，即使尚未启动正式校准也可追溯。
                if (_realtimeStorageService.IsActive)
                {
                    if (!_realtimeStorageService.TryAppendSnapshot(snapshot, out string realtimeStorageError))
                    {
                        UpdateRealtimeRecordStatus(realtimeStorageError);
                        MessageBox.Show(realtimeStorageError, "实时记录保存失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    else
                    {
                        UpdateRealtimeRecordStatus();
                    }
                }

                // 实时快照每轮都刷新；只有到达正式采样时刻才进入以下留存流程。
                if (_calibrationRunning && snapshot.Timestamp >= _nextCalibrationSampleAt)
                {
                    if (!double.TryParse(DutDisplayTemperatureTextBox.Text.Trim(), out double dutTemperatureValue) || !double.IsFinite(dutTemperatureValue))
                    {
                        StatusTextBlock.Text = "被校设备温度示值无效，本组正式样本尚未记录";
                        return;
                    }
                    double? dutHumidity = null;
                    if (CalibrationTaskContext.IncludesHumidity)
                    {
                        if (!double.TryParse(DutDisplayHumidityTextBox.Text.Trim(), out double dutHumidityValue) || !double.IsFinite(dutHumidityValue))
                        {
                            StatusTextBlock.Text = "被校设备湿度示值无效，本组正式样本尚未记录";
                            return;
                        }
                        dutHumidity = dutHumidityValue;
                    }
                    CalibrationSampleRecord formalRecord = CalibrationRunContext.Add(snapshot, dutTemperatureValue, dutHumidity);
                    // 先确认样本成功落盘，再增加页面上的正式样本进度。
                    if (!CalibrationFileStorageService.Default.TryAppendSample(formalRecord, out string storageError))
                    {
                        _calibrationRunning = false;
                        RestoreRealtimeIntervalAfterFormalCalibration();
                        CalibrationTaskContext.HasCompletedCalibration = false;
                        StartCalibrationButton.Content = "正式采样保存失败";
                        StartCalibrationButton.IsEnabled = false;
                        ViewResultButton.IsEnabled = false;
                        FormalReadinessTextBlock.Text = "正式样本未能完整保存，已停止正式校准；实时测量仍保持运行。";
                        FormalReadinessTextBlock.Foreground = Brushes.DarkRed;
                        StatusTextBlock.Text = "正式采样保存异常，请检查本地数据目录和磁盘权限";
                        UpdateParameterVisibility();
                        MessageBox.Show(storageError, "正式样本保存失败", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                    _calibrationSampleCount = CalibrationRunContext.Samples.Count;
                    int intervalSeconds = CalibrationTaskContext.IsConfigured
                        ? CalibrationTaskContext.SamplingIntervalSeconds
                        : int.TryParse(CalibrationIntervalTextBox.Text.Trim(), out int interval) ? interval : 60;
                    AdvanceNextCalibrationSampleAt(snapshot.Timestamp, intervalSeconds);
                    if (TryGetPlannedCount(out int plannedCount) && plannedCount > 0)
                    {
                        FormalSampleProgressTextBlock.Text = $"正式样本 {_calibrationSampleCount} / {plannedCount}";
                        StatusTextBlock.Text = $"正式校准采样：{_calibrationSampleCount} / {plannedCount} 组；下一组间隔 {intervalSeconds} s";
                        PublishGlobalRunStatus($"{_modbusClient.PortName} · 正式采样 {_calibrationSampleCount}/{plannedCount}", GlobalRunStatusTone.Active);
                        if (_calibrationSampleCount >= plannedCount)
                        {
                            // 计划样本完成后执行“计算→结果落盘→任务完成标记”，任何一步失败都不宣称完成。
                            _calibrationRunning = false;
                            RestoreRealtimeIntervalAfterFormalCalibration();
                            CalibrationResultSummary result = CalibrationResultCalculator.Calculate();
                            string completionError = result.Message;
                            if (!result.IsValid)
                                CalibrationFileStorageService.Default.TryMarkInterrupted("正式样本结果计算失败：" + result.Message, out _);
                            bool archiveCompleted = result.IsValid && CalibrationFileStorageService.Default.TryCompleteJob(result, out completionError);
                            if (!archiveCompleted)
                            {
                                WriteOperation("完成正式校准", "失败", completionError, CalibrationFileStorageService.Default.CurrentJobDirectory ?? string.Empty);
                                WriteRuntime("错误", "校准", "结果计算或归档失败", completionError, CalibrationFileStorageService.Default.CurrentJobDirectory ?? string.Empty);
                                CalibrationTaskContext.HasCompletedCalibration = false;
                                StartCalibrationButton.Content = "校准结果保存失败";
                                ViewResultButton.IsEnabled = false;
                                FormalReadinessTextBlock.Text = completionError;
                                FormalReadinessTextBlock.Foreground = Brushes.DarkRed;
                                StatusTextBlock.Text = "正式样本已采满，但结果计算或本地归档失败";
                                PublishGlobalRunStatus("校准结果归档失败", GlobalRunStatusTone.Error);
                                MessageBox.Show(FormalReadinessTextBlock.Text, "校准作业未完成", MessageBoxButton.OK, MessageBoxImage.Error);
                            }
                            else
                            {
                                WriteOperation("完成正式校准", "成功", $"已保存 {_calibrationSampleCount} 组正式样本及计算结果", CalibrationFileStorageService.Default.CurrentJobDirectory ?? string.Empty);
                                WriteRuntime("信息", "校准", "正式校准完成", $"正式样本 {_calibrationSampleCount} 组", CalibrationFileStorageService.Default.CurrentJobDirectory ?? string.Empty);
                                CalibrationTaskContext.HasCompletedCalibration = true;
                                CalibrationTaskContext.Save();
                                StartCalibrationButton.Content = "校准采样完成";
                                ViewResultButton.IsEnabled = true;
                                FormalReadinessTextBlock.Text = "正式校准采样、结果计算和本地归档均已完成。";
                                FormalReadinessTextBlock.Foreground = Brushes.DarkGreen;
                                StatusTextBlock.Text = $"校准作业已保存：{CalibrationFileStorageService.Default.CurrentJobDirectory}";
                                PublishGlobalRunStatus("校准完成 · 实时测量仍在运行", GlobalRunStatusTone.Completed);
                            }
                            UpdateParameterVisibility();
                        }
                    }
                }
                else if (_calibrationRunning)
                {
                    int remainingSeconds = Math.Max(0, (int)Math.Ceiling((_nextCalibrationSampleAt - DateTime.Now).TotalSeconds));
                    StatusTextBlock.Text = $"正式校准采样：{_calibrationSampleCount} / {CalibrationTaskContext.PlannedCount} 组；下一组约 {remainingSeconds} s";
                    PublishGlobalRunStatus($"{_modbusClient.PortName} · 正式采样 {_calibrationSampleCount}/{CalibrationTaskContext.PlannedCount} · {remainingSeconds}s", GlobalRunStatusTone.Active);
                }
                else
                {
                    StatusTextBlock.Text = $"实时测量中，已采集 {_viewModel.AcquisitionCount} 组";
                    PublishGlobalRunStatus($"{_modbusClient.PortName} · 实时测量 {_viewModel.AcquisitionCount} 组", GlobalRunStatusTone.Active);
                }
            });
        }

        /// <summary>更新当前值、有效通道质量、中心点值和异常详情提示。</summary>
        private void UpdateMeasurementSummary(MeasurementSnapshot snapshot)
        {
            CurrentSequenceTextBlock.Text = snapshot.Sequence.ToString();
            CurrentTimeTextBlock.Text = snapshot.Timestamp.ToString("HH:mm:ss");
            int temperaturePointCount = HasTemperatureMode() ? GetPointCount(TemperaturePointCountComboBox) : 0;
            int humidityPointCount = HasHumidityMode() ? GetPointCount(HumidityPointCountComboBox) : 0;
            List<InspectionChannelData> required = MeasurementChannelSelectionService.SelectRequired(
                snapshot.Channels, temperaturePointCount, humidityPointCount);
            List<InspectionChannelData> temperatures = MeasurementChannelSelectionService.SelectRequired(
                snapshot.Channels, ChannelType.Temperature, temperaturePointCount, true);
            List<InspectionChannelData> humidities = MeasurementChannelSelectionService.SelectRequired(
                snapshot.Channels, ChannelType.Humidity, humidityPointCount, true);

            int expectedCount = temperaturePointCount + humidityPointCount;
            QualitySummaryTextBlock.Text = $"有效 {snapshot.ValidChannelCount}/{expectedCount}\n异常 {snapshot.InvalidChannelCount}";
            List<string> invalidDescriptions = new List<string>();
            foreach (InspectionChannelData channel in required)
            {
                if (!channel.IsValid)
                {
                    string parsedValue = double.IsFinite(channel.Value)
                        ? $"{channel.Value:F2} {channel.Unit}"
                        : "不可解析";
                    invalidDescriptions.Add(
                        $"{GetChannelDisplayName(channel)}：{parsedValue}，{channel.Status}（原始HEX {channel.RawHex}）");
                }
            }
            for (int channel = 1; channel <= temperaturePointCount; channel++)
            {
                if (!required.Exists(item => item.Role == ChannelRole.PrimaryTemperature && item.Channel == channel))
                    invalidDescriptions.Add($"温度{channel}：响应中缺少该通道");
            }
            for (int channel = 1; channel <= humidityPointCount; channel++)
            {
                if (!required.Exists(item => item.Role == ChannelRole.Humidity && item.Channel == channel))
                    invalidDescriptions.Add($"湿度{channel}：响应中缺少该通道");
            }
            string qualityTooltip = invalidDescriptions.Count == 0
                ? "本次任务要求的测点均已返回有效数据。湿度探头伴随温度不计入任务通道。"
                : string.Join("\n", invalidDescriptions);
            if (humidityPointCount > 0)
            {
                List<string> probeTemperatureDescriptions = new List<string>();
                foreach (InspectionChannelData channel in snapshot.Channels)
                {
                    if (channel.Role != ChannelRole.HumidityProbeTemperature ||
                        channel.Channel < 1 ||
                        channel.Channel > humidityPointCount)
                        continue;

                    string parsedValue = double.IsFinite(channel.Value)
                        ? $"{channel.Value:F2} ℃"
                        : "不可解析";
                    string state = channel.IsValid ? string.Empty : $"，{channel.Status}";
                    probeTemperatureDescriptions.Add($"探头{channel.Channel}温度：{parsedValue}{state}");
                }

                //qualityTooltip += "\n\n湿度探头伴随温度（诊断，不参与校准）：\n" +
                //                  (probeTemperatureDescriptions.Count == 0
                //                      ? "设备响应未包含伴随温度"
                //                      : string.Join("  |  ", probeTemperatureDescriptions));
            }
            QualitySummaryTextBlock.ToolTip = qualityTooltip;

            List<string> liveLines = new List<string>();
            if (temperaturePointCount > 0) liveLines.Add(FormatSummary(temperatures, "温度", "℃"));
            if (humidityPointCount > 0) liveLines.Add(FormatSummary(humidities, "湿度", "%RH"));
            LiveValueSummaryTextBlock.Text = string.Join("\n", liveLines);

            int temperatureCenter = CalibrationTaskContext.IsConfigured
                ? CalibrationTaskContext.TemperatureCenterPoint
                : int.TryParse(CenterPointComboBox.SelectedItem as string, out int centerPoint) ? centerPoint : 1;
            int humidityCenter = CalibrationTaskContext.IsConfigured
                ? Math.Max(1, CalibrationTaskContext.HumidityCenterPoint)
                : temperatureCenter;
            InspectionChannelData? centerTemperature = temperatures.Find(c => c.Channel == temperatureCenter);
            InspectionChannelData? centerHumidity = humidities.Find(c => c.Channel == humidityCenter);
            string centerTemperatureText = centerTemperature == null ? "-" : $"{centerTemperature.Value:F3} ℃";
            string centerHumidityText = centerHumidity == null ? "-" : $"{centerHumidity.Value:F3} %RH";
            CenterTemperatureTextBlock.Text = centerTemperatureText;
            CenterHumidityTextBlock.Text = centerHumidityText;
            CenterSummaryTextBlock.Text = HasHumidityMode()
                ? $"T CH{temperatureCenter}  {centerTemperatureText}\nH O/CH{humidityCenter}  {centerHumidityText}"
                : $"CH{temperatureCenter}  {centerTemperatureText}";
        }

        /// <summary>把一组有效通道汇总为平均值及最小～最大范围。</summary>
        private static string FormatSummary(List<InspectionChannelData> channels, string label, string unit)
        {
            if (channels.Count == 0) return $"{label}：无有效数据";
            double min = double.MaxValue;
            double max = double.MinValue;
            double sum = 0;
            foreach (InspectionChannelData channel in channels)
            {
                min = Math.Min(min, channel.Value);
                max = Math.Max(max, channel.Value);
                sum += channel.Value;
            }
            string prefix = label == "温度" ? "T" : "H";
            return $"{prefix} 均值 {sum / channels.Count:F3} {unit}\n{min:F2}～{max:F2}";
        }

        /// <summary>按业务角色生成用户能理解的通道名称。</summary>
        private static string GetChannelDisplayName(InspectionChannelData channel) => channel.Role switch
        {
            ChannelRole.PrimaryTemperature => $"温度{channel.Channel}",
            ChannelRole.Humidity => $"湿度{channel.Channel}",
            _ => $"湿度探头温度{channel.Channel}"
        };

        /// <summary>
        /// 使用最近 5 组完整测点平均值计算趋势极差。
        /// 此结果只提示“趋势是否稳定”，正式采样仍必须满足任务中的规范确认条件。
        /// </summary>
        private void EvaluateStability()
        {
            if (_viewModel.Snapshots.Count < 5)
            {
                _trendLooksStable = false;
                StabilityTextBlock.Text = $"采集中（{_viewModel.Snapshots.Count}/5）";
                StabilityTextBlock.Foreground = Brushes.DarkOrange;
                EvaluateFormalReadiness();
                return;
            }

            bool hasTemperature = HasTemperatureMode();
            bool hasHumidity = HasHumidityMode();
            List<double> temperatureAverages = new List<double>();
            List<double> humidityAverages = new List<double>();
            int temperaturePointCount = hasTemperature ? GetPointCount(TemperaturePointCountComboBox) : 0;
            int humidityPointCount = hasHumidity ? GetPointCount(HumidityPointCountComboBox) : 0;
            int start = Math.Max(0, _viewModel.Snapshots.Count - 5);
            for (int i = start; i < _viewModel.Snapshots.Count; i++)
            {
                MeasurementSnapshot snapshot = _viewModel.Snapshots[i];
                AddAverage(snapshot.Channels, ChannelType.Temperature, temperaturePointCount, temperatureAverages);
                AddAverage(snapshot.Channels, ChannelType.Humidity, humidityPointCount, humidityAverages);
            }

            double temperatureRange = CalculateRange(temperatureAverages);
            double humidityRange = CalculateRange(humidityAverages);
            bool temperatureStable = !hasTemperature || temperatureRange <= 0.2;
            bool humidityStable = !hasHumidity || humidityRange <= 1.0;
            _trendLooksStable = temperatureStable && humidityStable;
            string temperatureText = hasTemperature ? $"ΔT {temperatureRange:F3} ℃" : string.Empty;
            string humidityText = hasHumidity ? $"ΔH {humidityRange:F3} %RH" : string.Empty;
            StabilityTextBlock.Text = $"近5组\n{temperatureText}{(hasTemperature && hasHumidity ? "\n" : string.Empty)}{humidityText}";
            StabilityTextBlock.Foreground = _trendLooksStable ? Brushes.DarkGreen : Brushes.DarkOrange;
            EvaluateFormalReadiness();
        }

        /// <summary>当指定类型的所有必需测点均有效时，计算一组空间平均值并追加到目标序列。</summary>
        private static void AddAverage(List<InspectionChannelData> channels, ChannelType type, int pointCount, List<double> target)
        {
            double sum = 0;
            int count = 0;
            foreach (InspectionChannelData channel in MeasurementChannelSelectionService.SelectRequired(
                         channels, type, pointCount, true))
            {
                sum += channel.Value;
                count++;
            }
            if (count == pointCount && count > 0) target.Add(sum / count);
        }

        /// <summary>计算最大值与最小值之差；无有效值时返回正无穷表示不能判稳。</summary>
        private static double CalculateRange(List<double> values)
        {
            if (values.Count == 0) return double.PositiveInfinity;
            double min = double.MaxValue;
            double max = double.MinValue;
            foreach (double value in values) { min = Math.Min(min, value); max = Math.Max(max, value); }
            return max - min;
        }

        /// <summary>当前界面模式是否包含主温度通道。</summary>
        private bool HasTemperatureMode() => ((CalibrationTypeComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "温度").Contains("温度");
        /// <summary>当前界面模式是否包含湿度通道；已配置任务优先以任务上下文为准。</summary>
        private bool HasHumidityMode() => CalibrationTaskContext.IsConfigured
            ? CalibrationTaskContext.IncludesHumidity
            : ((CalibrationTypeComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "温度").Contains("湿度");

        /// <summary>取得正式样本计划数；已配置任务优先使用固化值。</summary>
        private bool TryGetPlannedCount(out int count)
        {
            if (CalibrationTaskContext.IsConfigured)
            {
                count = CalibrationTaskContext.PlannedCount;
                return count > 0;
            }
            return int.TryParse(CalibrationCountComboBox.SelectedItem as string, out count);
        }




        /// <summary>
        /// 重绘最近 60 组中心温度和湿度 O 点趋势。
        /// 温度、湿度使用各自有单位的纵轴，避免不同量纲在无刻度画布上产生错误对比。
        /// </summary>
        private void DrawMeasurementChart()
        {
            CloseChartToolTip();
            MeasurementChartCanvas.Children.Clear();
            double width = MeasurementChartCanvas.ActualWidth;
            double height = MeasurementChartCanvas.ActualHeight;
            if (width < 240 || height < 120) return;

            int start = Math.Max(0, _viewModel.Snapshots.Count - 60);
            List<double> temperatures = new List<double>();
            List<double> humidities = new List<double>();
            int temperaturePointCount = HasTemperatureMode() ? GetPointCount(TemperaturePointCountComboBox) : 0;
            int humidityPointCount = HasHumidityMode() ? GetPointCount(HumidityPointCountComboBox) : 0;
            int temperatureCenter = CalibrationTaskContext.IsConfigured
                ? CalibrationTaskContext.TemperatureCenterPoint
                : int.TryParse(CenterPointComboBox.SelectedItem as string, out int centerPoint) ? centerPoint : 1;
            int humidityCenter = CalibrationTaskContext.IsConfigured
                ? Math.Max(1, CalibrationTaskContext.HumidityCenterPoint)
                : temperatureCenter;

            _visibleTrendSamples.Clear();
            for (int i = start; i < _viewModel.Snapshots.Count; i++)
            {
                MeasurementSnapshot snapshot = _viewModel.Snapshots[i];
                double temperature = FindTrendValue(snapshot, ChannelType.Temperature, temperatureCenter, temperaturePointCount);
                double humidity = FindTrendValue(snapshot, ChannelType.Humidity, humidityCenter, humidityPointCount);
                temperatures.Add(temperature);
                humidities.Add(humidity);
                _visibleTrendSamples.Add(new TrendSample(snapshot.Sequence, snapshot.Timestamp, temperature, humidity));
            }

            bool showTemperature = temperaturePointCount > 0;
            bool showHumidity = humidityPointCount > 0;
            UpdateTrendLegend(showTemperature, showHumidity, temperatureCenter, humidityCenter);

            double leftMargin = 56;
            double rightMargin = showTemperature && showHumidity ? 62 : 16;
            double topMargin = 21;
            double bottomMargin = 30;
            _chartPlotArea = new Rect(leftMargin, topMargin,
                Math.Max(1, width - leftMargin - rightMargin),
                Math.Max(1, height - topMargin - bottomMargin));

            ChartAxisScale? temperatureScale = showTemperature
                ? CreateAxisScale(temperatures, CalibrationTaskContext.SetTemperature, 0.5)
                : null;
            ChartAxisScale? humidityScale = showHumidity
                ? CreateAxisScale(humidities, CalibrationTaskContext.SetHumidity, 2.0)
                : null;

            DrawChartGridAndAxes(temperatureScale, humidityScale, showTemperature, showHumidity);
            DrawTimeAxis(_visibleTrendSamples);
            if (temperatureScale != null)
            {
                DrawSetPointLine(CalibrationTaskContext.SetTemperature, temperatureScale, Brushes.DodgerBlue);
                AddChartLine(temperatures, temperatureScale, Brushes.DodgerBlue);
            }
            if (humidityScale != null)
            {
                DrawSetPointLine(CalibrationTaskContext.SetHumidity, humidityScale, Brushes.SeaGreen);
                AddChartLine(humidities, humidityScale, Brushes.SeaGreen);
            }

            ChartScaleTextBlock.Text = BuildCurrentTrendText(
                temperatures, humidities, showTemperature, showHumidity, temperatureCenter, humidityCenter);
        }

        /// <summary>取得指定中心通道的有效值；通道缺失或无效时返回 NaN，使图中形成断点。</summary>
        private static double FindTrendValue(MeasurementSnapshot snapshot, ChannelType type, int channelNumber, int pointCount)
        {
            if (channelNumber < 1 || channelNumber > pointCount) return double.NaN;
            InspectionChannelData? channel = snapshot.Channels.Find(item =>
                item.Channel == channelNumber &&
                item.IsValid &&
                MeasurementChannelSelectionService.IsCalibrationChannel(item, type));
            return channel == null || !double.IsFinite(channel.Value) ? double.NaN : channel.Value;
        }

        /// <summary>按数据、任务设定值和最小可视跨度生成纵轴量程，避免微小噪声被铺满整张图。</summary>
        private static ChartAxisScale CreateAxisScale(List<double> values, double? setPoint, double minimumSpan)
        {
            List<double> valid = values.FindAll(double.IsFinite);
            if (setPoint.HasValue && double.IsFinite(setPoint.Value)) valid.Add(setPoint.Value);
            if (valid.Count == 0) return new ChartAxisScale(0, minimumSpan);

            double min = valid[0];
            double max = valid[0];
            foreach (double value in valid)
            {
                min = Math.Min(min, value);
                max = Math.Max(max, value);
            }

            double span = Math.Max(max - min, minimumSpan);
            double center = (min + max) / 2;
            double paddedSpan = span * 1.2;
            return new ChartAxisScale(center - paddedSpan / 2, center + paddedSpan / 2);
        }

        /// <summary>绘制带单位和刻度的温度左轴、湿度右轴以及共用网格。</summary>
        private void DrawChartGridAndAxes(
            ChartAxisScale? temperatureScale,
            ChartAxisScale? humidityScale,
            bool showTemperature,
            bool showHumidity)
        {
            Brush gridBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240));
            Brush borderBrush = new SolidColorBrush(Color.FromRgb(148, 163, 184));
            const int verticalIntervals = 4;
            for (int i = 0; i <= verticalIntervals; i++)
            {
                double y = _chartPlotArea.Top + _chartPlotArea.Height * i / verticalIntervals;
                AddChartLineShape(_chartPlotArea.Left, y, _chartPlotArea.Right, y, gridBrush, 1, i is 0 or verticalIntervals ? null : new DoubleCollection { 2, 3 });

                if (showTemperature && temperatureScale != null)
                {
                    double value = temperatureScale.Maximum - temperatureScale.Span * i / verticalIntervals;
                    AddChartText(FormatAxisValue(value, temperatureScale.Span / verticalIntervals), 2, y - 8, 48, TextAlignment.Right, Brushes.DodgerBlue);
                }
                if (showHumidity && humidityScale != null)
                {
                    double value = humidityScale.Maximum - humidityScale.Span * i / verticalIntervals;
                    double x = showTemperature ? _chartPlotArea.Right + 7 : 2;
                    TextAlignment alignment = showTemperature ? TextAlignment.Left : TextAlignment.Right;
                    AddChartText(FormatAxisValue(value, humidityScale.Span / verticalIntervals), x, y - 8, showTemperature ? 52 : 48, alignment, Brushes.SeaGreen);
                }
            }

            AddChartLineShape(_chartPlotArea.Left, _chartPlotArea.Top, _chartPlotArea.Left, _chartPlotArea.Bottom, borderBrush, 1, null);
            AddChartLineShape(_chartPlotArea.Right, _chartPlotArea.Top, _chartPlotArea.Right, _chartPlotArea.Bottom, borderBrush, 1, null);
            AddChartText(showTemperature ? "温度 (℃)" : "湿度 (%RH)", 2, 1, 72, TextAlignment.Left, showTemperature ? Brushes.DodgerBlue : Brushes.SeaGreen, FontWeights.SemiBold);
            if (showTemperature && showHumidity)
                AddChartText("湿度 (%RH)", _chartPlotArea.Right - 18, 1, 78, TextAlignment.Right, Brushes.SeaGreen, FontWeights.SemiBold);
        }

        /// <summary>按实际采集时间绘制横轴刻度，最多显示六个标签以保持可读性。</summary>
        private void DrawTimeAxis(List<TrendSample> samples)
        {
            if (samples.Count == 0)
            {
                AddChartText("等待采集数据", _chartPlotArea.Left, _chartPlotArea.Top + _chartPlotArea.Height / 2 - 9,
                    _chartPlotArea.Width, TextAlignment.Center, new SolidColorBrush(Color.FromRgb(148, 163, 184)));
                return;
            }

            Brush gridBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240));
            int labelCount = Math.Min(6, samples.Count);
            HashSet<int> drawnIndexes = new HashSet<int>();
            for (int i = 0; i < labelCount; i++)
            {
                int index = labelCount == 1
                    ? 0
                    : (int)Math.Round((samples.Count - 1) * i / (double)(labelCount - 1));
                if (!drawnIndexes.Add(index)) continue;
                double x = GetTrendX(index, samples.Count);
                AddChartLineShape(x, _chartPlotArea.Top, x, _chartPlotArea.Bottom, gridBrush, 1,
                    i is 0 or 5 ? null : new DoubleCollection { 2, 3 });
                AddChartText(samples[index].Timestamp.ToString("HH:mm:ss"), x - 30, _chartPlotArea.Bottom + 6,
                    60, TextAlignment.Center, new SolidColorBrush(Color.FromRgb(100, 116, 139)));
            }
        }

        /// <summary>将一条数据序列按指定纵轴绘制；无效值会结束当前折线段，避免跨故障点连线。</summary>
        private void AddChartLine(List<double> values, ChartAxisScale scale, Brush brush)
        {
            Polyline? segment = null;
            for (int i = 0; i < values.Count; i++)
            {
                if (!double.IsFinite(values[i]))
                {
                    segment = null;
                    continue;
                }

                if (segment == null)
                {
                    segment = new Polyline
                    {
                        Stroke = brush,
                        StrokeThickness = 2,
                        StrokeLineJoin = PenLineJoin.Round,
                        Points = new PointCollection()
                    };
                    MeasurementChartCanvas.Children.Add(segment);
                }
                double x = GetTrendX(i, values.Count);
                double y = GetTrendY(values[i], scale);
                segment.Points.Add(new Point(x, y));
                bool hasPrevious = i > 0 && double.IsFinite(values[i - 1]);
                bool hasNext = i + 1 < values.Count && double.IsFinite(values[i + 1]);
                if (!hasPrevious && !hasNext) AddChartPoint(x, y, brush);
            }
        }

        /// <summary>为只有一个有效采样的孤立折线段绘制圆点，使首组数据和短暂恢复值仍然可见。</summary>
        private void AddChartPoint(double x, double y, Brush brush)
        {
            Ellipse point = new Ellipse
            {
                Width = 6,
                Height = 6,
                Fill = brush,
                Stroke = Brushes.White,
                StrokeThickness = 1,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(point, x - 3);
            Canvas.SetTop(point, y - 3);
            MeasurementChartCanvas.Children.Add(point);
        }

        /// <summary>在任务设定值位置绘制同色虚线；设定值已参与量程计算，因此不会落到图外。</summary>
        private void DrawSetPointLine(double? setPoint, ChartAxisScale scale, Brush brush)
        {
            if (!setPoint.HasValue || !double.IsFinite(setPoint.Value)) return;
            double y = GetTrendY(setPoint.Value, scale);
            AddChartLineShape(_chartPlotArea.Left, y, _chartPlotArea.Right, y, brush, 1, new DoubleCollection { 7, 4 }, 0.65);
        }

        /// <summary>同步图例名称和可见性，使单温度、单湿度及温湿度任务均不出现无关图例。</summary>
        private void UpdateTrendLegend(bool showTemperature, bool showHumidity, int temperatureCenter, int humidityCenter)
        {
            TemperatureLegendLine.Visibility = showTemperature ? Visibility.Visible : Visibility.Collapsed;
            TemperatureLegendTextBlock.Visibility = showTemperature ? Visibility.Visible : Visibility.Collapsed;
            HumidityLegendLine.Visibility = showHumidity ? Visibility.Visible : Visibility.Collapsed;
            HumidityLegendTextBlock.Visibility = showHumidity ? Visibility.Visible : Visibility.Collapsed;
            TemperatureLegendTextBlock.Text = $"中心温度 T{temperatureCenter}";
            HumidityLegendTextBlock.Text = $"湿度 O点 H{humidityCenter}";
            SetPointLegendTextBlock.Visibility = (showTemperature && CalibrationTaskContext.SetTemperature.HasValue) ||
                                                 (showHumidity && CalibrationTaskContext.SetHumidity.HasValue)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        /// <summary>生成图表右上角的最新有效中心测点值，而不是用无刻度的最小/最大范围替代坐标轴。</summary>
        private static string BuildCurrentTrendText(
            List<double> temperatures,
            List<double> humidities,
            bool showTemperature,
            bool showHumidity,
            int temperatureCenter,
            int humidityCenter)
        {
            List<string> values = new List<string>();
            if (showTemperature)
                values.Add($"T{temperatureCenter} {FormatLatestValue(temperatures, "℃")}");
            if (showHumidity)
                values.Add($"O/H{humidityCenter} {FormatLatestValue(humidities, "%RH")}");
            return values.Count == 0 ? "等待有效任务通道" : string.Join("  ·  ", values);
        }

        /// <summary>从末尾查找最近一个有效值，避免最新一次通信缺失时显示伪造的零值。</summary>
        private static string FormatLatestValue(List<double> values, string unit)
        {
            for (int i = values.Count - 1; i >= 0; i--)
            {
                if (double.IsFinite(values[i])) return $"{values[i]:F3} {unit}";
            }
            return $"-- {unit}";
        }

        /// <summary>鼠标在绘图区移动时定位最近采样组，并显示时间、序号及两种中心测点值。</summary>
        private void MeasurementChartCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_visibleTrendSamples.Count == 0 || _chartPlotArea.IsEmpty) return;
            Point point = e.GetPosition(MeasurementChartCanvas);
            if (!_chartPlotArea.Contains(point))
            {
                CloseChartToolTip();
                return;
            }

            int index = _visibleTrendSamples.Count == 1
                ? 0
                : (int)Math.Round((point.X - _chartPlotArea.Left) / _chartPlotArea.Width * (_visibleTrendSamples.Count - 1));
            index = Math.Clamp(index, 0, _visibleTrendSamples.Count - 1);
            double x = GetTrendX(index, _visibleTrendSamples.Count);
            if (_chartCursorLine != null) MeasurementChartCanvas.Children.Remove(_chartCursorLine);
            _chartCursorLine = new Line
            {
                X1 = x,
                X2 = x,
                Y1 = _chartPlotArea.Top,
                Y2 = _chartPlotArea.Bottom,
                Stroke = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 3, 3 },
                IsHitTestVisible = false
            };
            MeasurementChartCanvas.Children.Add(_chartCursorLine);

            TrendSample sample = _visibleTrendSamples[index];
            List<string> lines = new List<string>
            {
                $"第 {sample.Sequence} 组  {sample.Timestamp:HH:mm:ss.fff}"
            };
            if (double.IsFinite(sample.Temperature)) lines.Add($"中心温度  {sample.Temperature:F3} ℃");
            if (double.IsFinite(sample.Humidity)) lines.Add($"湿度 O点  {sample.Humidity:F3} %RH");
            if (lines.Count == 1) lines.Add("本组中心测点无有效数据");
            _measurementChartToolTip.Content = string.Join("\n", lines);
            _measurementChartToolTip.IsOpen = true;
        }

        /// <summary>鼠标离开趋势图后关闭读数提示并移除游标。</summary>
        private void MeasurementChartCanvas_MouseLeave(object sender, MouseEventArgs e) => CloseChartToolTip();

        /// <summary>统一关闭趋势提示，避免重绘或切页后残留悬浮窗口。</summary>
        private void CloseChartToolTip()
        {
            _measurementChartToolTip.IsOpen = false;
            if (_chartCursorLine != null)
            {
                MeasurementChartCanvas.Children.Remove(_chartCursorLine);
                _chartCursorLine = null;
            }
        }

        /// <summary>把采样序号转换为绘图区横坐标。</summary>
        private double GetTrendX(int index, int count) => count <= 1
            ? _chartPlotArea.Left + _chartPlotArea.Width / 2
            : _chartPlotArea.Left + _chartPlotArea.Width * index / (count - 1);

        /// <summary>把某一纵轴上的物理量转换为绘图区纵坐标。</summary>
        private double GetTrendY(double value, ChartAxisScale scale) =>
            _chartPlotArea.Bottom - (value - scale.Minimum) / scale.Span * _chartPlotArea.Height;

        /// <summary>创建图表线条，集中设置抗干扰所需的非命中测试属性。</summary>
        private void AddChartLineShape(
            double x1, double y1, double x2, double y2,
            Brush brush, double thickness, DoubleCollection? dashArray, double opacity = 1)
        {
            MeasurementChartCanvas.Children.Add(new Line
            {
                X1 = x1,
                Y1 = y1,
                X2 = x2,
                Y2 = y2,
                Stroke = brush,
                StrokeThickness = thickness,
                StrokeDashArray = dashArray,
                Opacity = opacity,
                IsHitTestVisible = false
            });
        }

        /// <summary>在画布上创建定位文字，用于坐标单位、刻度和时间标签。</summary>
        private void AddChartText(
            string text, double left, double top, double width,
            TextAlignment alignment, Brush foreground, FontWeight? fontWeight = null)
        {
            TextBlock textBlock = new TextBlock
            {
                Text = text,
                Width = Math.Max(1, width),
                TextAlignment = alignment,
                Foreground = foreground,
                FontSize = 10.5,
                FontWeight = fontWeight ?? FontWeights.Normal,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(textBlock, left);
            Canvas.SetTop(textBlock, top);
            MeasurementChartCanvas.Children.Add(textBlock);
        }

        /// <summary>根据刻度间隔选择合适的小数位数，兼顾可读性和测量分辨率。</summary>
        private static string FormatAxisValue(double value, double interval)
        {
            if (interval >= 10) return value.ToString("F0");
            if (interval >= 1) return value.ToString("F1");
            if (interval >= 0.1) return value.ToString("F2");
            return value.ToString("F3");
        }

        /// <summary>画布尺寸变化后按新尺寸重绘趋势线。</summary>
        private void MeasurementChartCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => DrawMeasurementChart();

        /// <summary>当前趋势图中一组与采集快照对应的中心测点数据。</summary>
        private sealed record TrendSample(long Sequence, DateTime Timestamp, double Temperature, double Humidity);

        /// <summary>图表纵轴范围；构造时确保跨度大于零，防止常量数据导致除零。</summary>
        private sealed class ChartAxisScale
        {
            public ChartAxisScale(double minimum, double maximum)
            {
                Minimum = minimum;
                Maximum = maximum > minimum ? maximum : minimum + 1;
            }

            public double Minimum { get; }
            public double Maximum { get; }
            public double Span => Maximum - Minimum;
        }

        /// <summary>采集开始/停止时统一锁定或解锁会影响执行流程的参数。</summary>
        private void SetExecutionParametersEnabled(bool enabled)
        {
            SlaveAddressTextBox.IsEnabled = enabled;
            IntervalTextBox.IsEnabled = enabled;
            CalibrationCountComboBox.IsEnabled = enabled;
            CalibrationIntervalTextBox.IsEnabled = enabled && !CalibrationTaskContext.IsConfigured;
            CenterIntervalTextBox.IsEnabled = enabled && !CalibrationTaskContext.IsConfigured;
            SaveRealtimeRecordCheckBox.IsEnabled = enabled;
            UpdateParameterVisibility();
        }

        /// <summary>
        /// 处理采集异常：前两次按退避时间自动重试；连续失败达到上限后才中断作业并释放串口。
        /// </summary>
        private void OnAcquisitionError(Exception ex)
        {
            int failureCount = _acquisitionService.ConsecutiveFailureCount;
            int retryDelay = _acquisitionService.NextRetryDelayMilliseconds;
            if (failureCount < _acquisitionService.MaxConsecutiveFailures)
            {
                _deviceResponding = false;
                WriteRuntime(
                    "警告",
                    "通信",
                    "巡检仪读取失败，准备重试",
                    $"连续失败 {failureCount}/{_acquisitionService.MaxConsecutiveFailures}；{retryDelay} ms 后重试；{ex.Message}",
                    _realtimeStorageService.CurrentSessionDirectory ?? string.Empty);
                Dispatcher.BeginInvoke(() =>
                {
                    UpdateConnectionStatus();
                    StabilityTextBlock.Text = $"通信波动，正在重试（{failureCount}/{_acquisitionService.MaxConsecutiveFailures}）";
                    StabilityTextBlock.Foreground = Brushes.DarkOrange;
                    StatusTextBlock.Text = $"巡检仪本轮读取失败，{retryDelay / 1000.0:0.#} s 后自动重试：{ex.Message}";
                    PublishGlobalRunStatus($"通信波动 · {retryDelay / 1000.0:0.#}s 后重试", GlobalRunStatusTone.Warning);
                });
                return;
            }

            if (_calibrationRunning)
                CalibrationFileStorageService.Default.TryMarkInterrupted($"巡检仪连续 {failureCount} 次采集异常导致正式校准中断：{ex.Message}", out _);
            _calibrationRunning = false;
            _realtimeStorageService.TryEndSession("采集异常", $"连续 {failureCount} 次读取失败：{ex.Message}", out _);
            _deviceResponding = false;
            _requiredChannelsValid = false;
            _trendLooksStable = false;
            _acquisitionService.Stop();
            _viewModel.IsAcquiring = false;
            _modbusClient.Close();
            WriteOperation("实时测量异常中断", "失败", $"连续 {failureCount} 次读取失败：{ex.Message}", _realtimeStorageService.CurrentSessionDirectory ?? string.Empty);
            WriteRuntime("错误", "通信", "巡检仪连续失败，停止请求", $"连续 {failureCount} 次；{ex.Message}", _realtimeStorageService.CurrentSessionDirectory ?? string.Empty);
            Dispatcher.Invoke(() =>
            {
                RestoreRealtimeIntervalAfterFormalCalibration();
                ConnectDeviceButton.IsEnabled = true;
                StartAcquisitionButton.IsEnabled = false;
                StopAcquisitionButton.IsEnabled = false;
                StartCalibrationButton.IsEnabled = false;
                PortComboBox.IsEnabled = true;
                BaudRateComboBox.IsEnabled = true;
                SetExecutionParametersEnabled(true);
                UpdateConnectionStatus();
                UpdateRealtimeRecordStatus(ex.Message);
                EvaluateFormalReadiness();
                StatusTextBlock.Text = $"连续 {failureCount} 次读取失败，已停止并释放串口";
                PublishGlobalRunStatus("通信异常，已停止并释放串口", GlobalRunStatusTone.Error, false);
                MessageBox.Show(
                    $"巡检仪连续 {failureCount} 次读取失败，系统已停止请求以保护设备。\n\n最后一次错误：{ex.Message}\n\n请检查接线、电源、从站地址后重新连接。",
                    "采集已安全停止",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            });
        }

        /// <summary>在未采集时返回任务配置；采集运行中阻止修改任务参数。</summary>
        private void BackHomeButton_Click(object sender, RoutedEventArgs e)
        {
            if (_acquisitionService.IsRunning)
            {
                MessageBox.Show("请先暂停实时测量，再修改任务配置。", "采集正在运行", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (Application.Current.MainWindow is MainWindow mainWindow)
                mainWindow.ShowTaskConfigurationPage();
        }

        /// <summary>正式校准完成后进入结果页。</summary>
        private void ViewResultButton_Click(object sender, RoutedEventArgs e)
        {
            if (!CalibrationTaskContext.HasCompletedCalibration)
            {
                MessageBox.Show("请先完成计划校准采样。", "结果尚不可用", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (Application.Current.MainWindow is MainWindow mainWindow)
                mainWindow.ShowResultPage();
        }

        /// <summary>把用户业务动作写入数据根目录下的“操作记录.csv”。</summary>
        private static void WriteOperation(string operation, string result, string description, string relatedPath)
        {
            LocalTraceService.Default.TryWriteOperation(
                operation,
                result,
                CalibrationFileStorageService.Default.CurrentJobId,
                description,
                relatedPath,
                out _);
        }

        /// <summary>把通信、采集和校准运行状态写入当日 CSV 日志。</summary>
        private static void WriteRuntime(string level, string category, string eventName, string details, string relatedPath = "")
        {
            LocalTraceService.Default.TryWriteRuntime(
                level,
                category,
                eventName,
                details,
                CalibrationFileStorageService.Default.CurrentJobId,
                relatedPath,
                out _);
        }

        /// <summary>同时刷新顶部状态、设备卡片和连接按钮，区分“串口打开”与“设备已响应”。</summary>
        private void UpdateConnectionStatus()
        {
            bool connected = _modbusClient.IsOpen;
            ConnectionStatusEllipse.Fill = connected && _deviceResponding ? Brushes.LimeGreen : Brushes.DarkOrange;
            ConnectionStatusTextBlock.Text = connected && _deviceResponding ? "设备已响应" : connected ? "串口已打开，等待设备响应" : "未连接";
            ConnectionStatusTextBlock.Foreground = connected && _deviceResponding ? Brushes.DarkGreen : Brushes.DarkOrange;
            DeviceCardStatusEllipse.Fill = connected && _deviceResponding ? Brushes.LimeGreen : Brushes.DarkOrange;
            DeviceCardStatusTextBlock.Text = connected && _deviceResponding ? "设备已响应" : connected ? "串口已打开" : "设备未连接";
            DeviceCardStatusTextBlock.Foreground = connected && _deviceResponding ? Brushes.DarkGreen : Brushes.DarkOrange;
            PortTextBlock.Text = connected ? $"串口：{_modbusClient.PortName}" : "串口：未连接";
            BaudRateTextBlock.Text = connected ? $"波特率：{_modbusClient.BaudRate}" : "波特率：未设置";
            ConnectDeviceButton.Content = connected ? "断开巡检仪" : "连接巡检仪";
            if (_calibrationRunning)
                PublishGlobalRunStatus($"{_modbusClient.PortName} · 正式采样 {_calibrationSampleCount}/{CalibrationTaskContext.PlannedCount}", GlobalRunStatusTone.Active);
            else if (_viewModel.IsAcquiring && CalibrationTaskContext.HasCompletedCalibration)
                PublishGlobalRunStatus("校准完成 · 实时测量仍在运行", GlobalRunStatusTone.Completed);
            else if (_viewModel.IsAcquiring)
                PublishGlobalRunStatus($"{_modbusClient.PortName} · 实时测量中", GlobalRunStatusTone.Active);
            else if (connected && _deviceResponding)
                PublishGlobalRunStatus($"{_modbusClient.PortName} · 设备已响应", GlobalRunStatusTone.Connected);
            else if (connected)
                PublishGlobalRunStatus($"{_modbusClient.PortName} · 串口已打开", GlobalRunStatusTone.Connected);
            else
                PublishGlobalRunStatus("巡检仪未连接", GlobalRunStatusTone.Warning, false);
        }

        /// <summary>把设备与作业状态发布到主窗口，使切换页面后仍能看到后台采集进度。</summary>
        private static void PublishGlobalRunStatus(string message, GlobalRunStatusTone tone, bool returnsToWorkbench = true)
        {
            if (Application.Current.MainWindow is MainWindow mainWindow)
                mainWindow.SetGlobalRunStatus(message, tone, returnsToWorkbench);
        }

        /// <summary>打开当前或最近一次实时测量会话目录；尚未测量时打开实时记录根目录。</summary>
        private void OpenRealtimeRecordButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string directory = _realtimeStorageService.CurrentSessionDirectory ?? _realtimeStorageService.DataRootPath;
                Directory.CreateDirectory(directory);
                Process.Start(new ProcessStartInfo
                {
                    FileName = directory,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "无法打开实时记录目录", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>以短状态提示记录是否正在写盘，完整路径放在悬停提示中，避免挤占控制栏。</summary>
        private void UpdateRealtimeRecordStatus(string? warning = null)
        {
            string? directory = _realtimeStorageService.CurrentSessionDirectory;
            RealtimeRecordStatusTextBlock.ToolTip = directory ?? _realtimeStorageService.DataRootPath;

            if (!string.IsNullOrWhiteSpace(warning))
            {
                RealtimeRecordStatusTextBlock.Text = "实时记录异常：" + warning.Split('\n')[0];
                RealtimeRecordStatusTextBlock.Foreground = Brushes.DarkRed;
                return;
            }
            if (_realtimeStorageService.IsActive)
            {
                RealtimeRecordStatusTextBlock.Text = $"实时记录中：已写入 {_realtimeStorageService.SavedSnapshotCount} 组";
                RealtimeRecordStatusTextBlock.Foreground = Brushes.DarkGreen;
                return;
            }
            if (_acquisitionService.IsRunning && SaveRealtimeRecordCheckBox.IsChecked != true)
            {
                RealtimeRecordStatusTextBlock.Text = "本次实时测量未启用文件记录";
                RealtimeRecordStatusTextBlock.Foreground = Brushes.DarkOrange;
                return;
            }
            if (!string.IsNullOrWhiteSpace(directory))
            {
                RealtimeRecordStatusTextBlock.Text = $"最近实时记录：{_realtimeStorageService.SavedSnapshotCount} 组";
                RealtimeRecordStatusTextBlock.Foreground = Brushes.DarkGreen;
                return;
            }

            RealtimeRecordStatusTextBlock.Text = "开始测量后，每组完整响应立即写入本地";
            RealtimeRecordStatusTextBlock.Foreground = Brushes.DarkGreen;
        }
    }
}
