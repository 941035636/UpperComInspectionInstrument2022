using System;
using System.Collections.Generic;
using System.Data;
using System.IO.Ports;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using UpperComInspectionInstrument2022.Communication;
using UpperComInspectionInstrument2022.Models;
using UpperComInspectionInstrument2022.Services;
using UpperComInspectionInstrument2022.ViewModels;

namespace UpperComInspectionInstrument2022.Views
{
    public partial class RealTimeMeasurementPage : Page
    {
        private readonly RealTimeMeasurementViewModel _viewModel;
        private readonly InspectionDataAcquisitionService _acquisitionService;
        private readonly ModbusRtuClient _modbusClient;
        private bool _deviceResponding;
        private bool _requiredChannelsValid;
        private bool _trendLooksStable;
        private bool _calibrationRunning;
        private int _calibrationSampleCount;
        private DateTime? _setPointReachedAt;
        private DateTime _nextCalibrationSampleAt;
        private DataTable _measurementTable = new DataTable();
        private string _appliedTaskSignature = string.Empty;

        public RealTimeMeasurementPage(InspectionDataAcquisitionService acquisitionService, ModbusRtuClient modbusClient)
        {
            InitializeComponent();
            _acquisitionService = acquisitionService ?? throw new ArgumentNullException(nameof(acquisitionService));
            _modbusClient = modbusClient ?? throw new ArgumentNullException(nameof(modbusClient));
            _viewModel = new RealTimeMeasurementViewModel();
            DataContext = _viewModel;

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
            _appliedTaskSignature = BuildTaskSignature();
        }

        private void ApplyTaskContext()
        {
            if (!CalibrationTaskContext.IsConfigured)
            {
                TaskSummaryTextBlock.Text = "尚未配置任务，任务参数可在下方填写。";
                TaskParameterPanel.Visibility = Visibility.Visible;
                FormalRuleTextBlock.Text = "正式校准前必须先建立任务，实时测量可用于设备联调。";
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
            FormalRuleTextBlock.Text = $"{rule.StabilityRuleText}\n正式采样：{rule.SamplingRuleText}";
            DutDisplayTemperatureTextBox.Text = CalibrationTaskContext.DutDisplayTemperature?.ToString("0.###") ?? string.Empty;
            DutDisplayHumidityTextBox.Text = CalibrationTaskContext.DutDisplayHumidity?.ToString("0.###") ?? string.Empty;
            FormalSampleProgressTextBlock.Text = $"正式样本 0 / {CalibrationTaskContext.PlannedCount}";
            TaskParameterPanel.Visibility = Visibility.Collapsed;
        }

        public void RefreshTaskContext()
        {
            if (_acquisitionService.IsRunning) return;

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
        }

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

        private void StabilityConfirmation_Changed(object sender, RoutedEventArgs e)
        {
            if (SetPointReachedCheckBox.IsChecked == true)
                _setPointReachedAt ??= DateTime.Now;
            else
                _setPointReachedAt = null;
            EvaluateFormalReadiness();
        }

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
            if (string.IsNullOrWhiteSpace(CalibrationTaskContext.ReferencedStandardName) ||
                string.IsNullOrWhiteSpace(CalibrationTaskContext.ReferencedCertificateNumber) ||
                !CalibrationTaskContext.ReferencedValidityDate.HasValue)
                blockers.Add("标准器名称、证书编号或有效期不完整，请同步或维护标准器资料");
            else if (CalibrationTaskContext.ReferencedValidityDate.Value.Date < DateTime.Today)
                blockers.Add($"标准器证书已于 {CalibrationTaskContext.ReferencedValidityDate:yyyy-MM-dd} 到期");

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

        private static bool HasInvalidTaskStandardReference() =>
            string.IsNullOrWhiteSpace(CalibrationTaskContext.ReferencedStandardName) ||
            string.IsNullOrWhiteSpace(CalibrationTaskContext.ReferencedCertificateNumber) ||
            !CalibrationTaskContext.ReferencedValidityDate.HasValue ||
            CalibrationTaskContext.ReferencedValidityDate.Value.Date < DateTime.Today;

        private void RefreshStandardReferenceButton_Click(object sender, RoutedEventArgs e)
        {
            if (CalibrationTaskContext.TrySnapshotCurrentStandardSettings(out string error))
            {
                CalibrationTaskContext.Save();
                EvaluateFormalReadiness();
                StatusTextBlock.Text = "已将系统设置中的标准器资料同步到当前任务";
                MessageBox.Show("标准器名称、证书和有效期已同步到当前任务。实时测量数据未中断。", "标准器资料已同步", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            MessageBoxResult result = MessageBox.Show(
                $"{error}\n\n是否现在前往系统设置维护？实时测量会继续运行，保存后从左侧返回“校准作业”，再点击同步按钮。",
                "标准器资料未完成", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes && Application.Current.MainWindow is MainWindow mainWindow)
                mainWindow.ShowSettingsPage();
        }

        private void RefreshPortsButton_Click(object sender, RoutedEventArgs e) => RefreshPorts();

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

        private void PointCountComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CenterPointComboBox != null) UpdateCenterPointOptions();
            if (MeasurementMatrixDataGrid != null) UpdateMeasurementMatrixColumns();
        }

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

        private void CalibrationTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CalibrationTypeComboBox != null) UpdateParameterVisibility();
        }

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

        private static int GetPointCount(ComboBox comboBox)
        {
            return int.TryParse(comboBox.SelectedItem as string, out int count) ? Math.Max(0, count) : 0;
        }

        private void AppendMeasurementMatrixRow(MeasurementSnapshot snapshot)
        {
            if (_measurementTable.Columns.Count < 3) return;
            DataRow row = _measurementTable.NewRow();
            row["采集序号"] = snapshot.Sequence;
            row["采集时间"] = snapshot.Timestamp.ToString("HH:mm:ss.fff");
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
                    row[columnName] = channel.IsValid ? channel.Value.ToString("F2") : "异常";
            }
            _measurementTable.Rows.InsertAt(row, 0);
            while (_measurementTable.Rows.Count > 200) _measurementTable.Rows.RemoveAt(_measurementTable.Rows.Count - 1);
        }

        private void ConnectDeviceButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_modbusClient.IsOpen)
                {
                    _modbusClient.Close();
                    _deviceResponding = false;
                    _requiredChannelsValid = false;
                    StartAcquisitionButton.IsEnabled = false;
                    PortComboBox.IsEnabled = true;
                    BaudRateComboBox.IsEnabled = true;
                    StatusTextBlock.Text = "巡检仪连接已断开";
                    UpdateConnectionStatus();
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
            }
            catch (UnauthorizedAccessException)
            {
                UpdateConnectionStatus();
                MessageBox.Show("串口当前不可用，可能已被其他程序占用或设备已断开。请关闭 Qt/串口工具后刷新端口。", "连接巡检仪失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                UpdateConnectionStatus();
                MessageBox.Show(ex.Message, "连接巡检仪失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void StartAcquisitionButton_Click(object sender, RoutedEventArgs e)
        {
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
                _acquisitionService.Start(slaveAddress, interval, calibrationType);
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
                EvaluateFormalReadiness();
            }
            catch (Exception ex)
            {
                UpdateConnectionStatus();
                MessageBox.Show(ex.Message, "启动实时测量失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void StartCalibrationButton_Click(object sender, RoutedEventArgs e)
        {
            if (!EvaluateFormalReadiness())
            {
                MessageBox.Show(FormalReadinessTextBlock.Text, "暂不能启动正式校准", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!TryReadDutDisplays(out double? dutTemperature, out double? dutHumidity)) return;
            CalibrationTaskContext.DutDisplayTemperature = dutTemperature;
            CalibrationTaskContext.DutDisplayHumidity = dutHumidity;
            CalibrationTaskContext.Save();

            CalibrationRunContext.Begin();
            if (!CalibrationFileStorageService.Default.TryBeginJob(out string storageError))
            {
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
            StatusTextBlock.Text = $"正式校准已启动；实时测量保持运行，按 {CalibrationTaskContext.SamplingIntervalSeconds} s 间隔留存样本";
            UpdateParameterVisibility();
        }

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

        private void StopAcquisitionButton_Click(object sender, RoutedEventArgs e)
        {
            // 只有用户明确点击“暂停/停止”时才停止后台采集。
            string storageWarning = string.Empty;
            if (_calibrationRunning)
                CalibrationFileStorageService.Default.TryMarkInterrupted("操作人员停止了正式校准采样", out storageWarning);
            _acquisitionService.Stop();
            _viewModel.IsAcquiring = false;
            _calibrationRunning = false;
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
            if (!string.IsNullOrWhiteSpace(storageWarning))
                MessageBox.Show(storageWarning, "本地作业状态未保存", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void ClearDataButton_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("确定清空当前校准工作台的所有采集快照吗？", "确认", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            if (_calibrationRunning &&
                !CalibrationFileStorageService.Default.TryMarkInterrupted("操作人员清空了当前工作台数据", out string storageWarning))
                MessageBox.Show(storageWarning, "本地作业状态未保存", MessageBoxButton.OK, MessageBoxImage.Warning);
            ResetMeasurementDisplay("实时数据已清空，采集连接保持当前状态", false);
        }

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

        private void OnDataAcquired(long acquisitionId, List<InspectionChannelData> data)
        {
            _deviceResponding = true;
            Dispatcher.Invoke(() =>
            {
                if (CalibrationTaskContext.IsConfigured)
                {
                    ChannelCorrectionService.Apply(data,
                        CalibrationTaskContext.ReferencedTemperatureCorrections,
                        CalibrationTaskContext.ReferencedHumidityCorrections);
                }
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

                if (_calibrationRunning && DateTime.Now >= _nextCalibrationSampleAt)
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
                    if (!CalibrationFileStorageService.Default.TryAppendSample(formalRecord, out string storageError))
                    {
                        _calibrationRunning = false;
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
                    _nextCalibrationSampleAt = DateTime.Now.AddSeconds(Math.Max(1, intervalSeconds));
                    if (TryGetPlannedCount(out int plannedCount) && plannedCount > 0)
                    {
                        FormalSampleProgressTextBlock.Text = $"正式样本 {_calibrationSampleCount} / {plannedCount}";
                        StatusTextBlock.Text = $"正式校准采样：{_calibrationSampleCount} / {plannedCount} 组；下一组间隔 {intervalSeconds} s";
                        if (_calibrationSampleCount >= plannedCount)
                        {
                            _calibrationRunning = false;
                            CalibrationResultSummary result = CalibrationResultCalculator.Calculate();
                            string completionError = result.Message;
                            if (!result.IsValid)
                                CalibrationFileStorageService.Default.TryMarkInterrupted("正式样本结果计算失败：" + result.Message, out _);
                            bool archiveCompleted = result.IsValid && CalibrationFileStorageService.Default.TryCompleteJob(result, out completionError);
                            if (!archiveCompleted)
                            {
                                CalibrationTaskContext.HasCompletedCalibration = false;
                                StartCalibrationButton.Content = "校准结果保存失败";
                                ViewResultButton.IsEnabled = false;
                                FormalReadinessTextBlock.Text = completionError;
                                FormalReadinessTextBlock.Foreground = Brushes.DarkRed;
                                StatusTextBlock.Text = "正式样本已采满，但结果计算或本地归档失败";
                                MessageBox.Show(FormalReadinessTextBlock.Text, "校准作业未完成", MessageBoxButton.OK, MessageBoxImage.Error);
                            }
                            else
                            {
                                CalibrationTaskContext.HasCompletedCalibration = true;
                                CalibrationTaskContext.Save();
                                StartCalibrationButton.Content = "校准采样完成";
                                ViewResultButton.IsEnabled = true;
                                FormalReadinessTextBlock.Text = "正式校准采样、结果计算和本地归档均已完成。";
                                FormalReadinessTextBlock.Foreground = Brushes.DarkGreen;
                                StatusTextBlock.Text = $"校准作业已保存：{CalibrationFileStorageService.Default.CurrentJobDirectory}";
                            }
                            UpdateParameterVisibility();
                        }
                    }
                }
                else if (_calibrationRunning)
                {
                    int remainingSeconds = Math.Max(0, (int)Math.Ceiling((_nextCalibrationSampleAt - DateTime.Now).TotalSeconds));
                    StatusTextBlock.Text = $"正式校准采样：{_calibrationSampleCount} / {CalibrationTaskContext.PlannedCount} 组；下一组约 {remainingSeconds} s";
                }
                else
                {
                    StatusTextBlock.Text = $"实时测量中，已采集 {_viewModel.AcquisitionCount} 组";
                }
            });
        }

        private void UpdateMeasurementSummary(MeasurementSnapshot snapshot)
        {
            CurrentSequenceTextBlock.Text = snapshot.Sequence.ToString();
            CurrentTimeTextBlock.Text = snapshot.Timestamp.ToString("HH:mm:ss.fff");
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
                    invalidDescriptions.Add($"{GetChannelDisplayName(channel)}：{channel.Status}（{channel.RawHex}）");
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
            QualitySummaryTextBlock.ToolTip = invalidDescriptions.Count == 0
                ? "本次任务要求的测点均已返回有效数据。湿度探头伴随温度不计入任务通道。"
                : string.Join("\n", invalidDescriptions);

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

        private static string GetChannelDisplayName(InspectionChannelData channel) => channel.Role switch
        {
            ChannelRole.PrimaryTemperature => $"温度{channel.Channel}",
            ChannelRole.Humidity => $"湿度{channel.Channel}",
            _ => $"湿度探头温度{channel.Channel}"
        };

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

        private static double CalculateRange(List<double> values)
        {
            if (values.Count == 0) return double.PositiveInfinity;
            double min = double.MaxValue;
            double max = double.MinValue;
            foreach (double value in values) { min = Math.Min(min, value); max = Math.Max(max, value); }
            return max - min;
        }

        private bool HasTemperatureMode() => ((CalibrationTypeComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "温度").Contains("温度");
        private bool HasHumidityMode() => CalibrationTaskContext.IsConfigured
            ? CalibrationTaskContext.IncludesHumidity
            : ((CalibrationTypeComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "温度").Contains("湿度");

        private bool TryGetPlannedCount(out int count)
        {
            if (CalibrationTaskContext.IsConfigured)
            {
                count = CalibrationTaskContext.PlannedCount;
                return count > 0;
            }
            return int.TryParse(CalibrationCountComboBox.SelectedItem as string, out count);
        }

        private void DrawMeasurementChart()
        {
            MeasurementChartCanvas.Children.Clear();
            double width = MeasurementChartCanvas.ActualWidth;
            double height = MeasurementChartCanvas.ActualHeight;
            if (width < 100 || height < 80) return;

            for (int i = 1; i < 5; i++)
            {
                double y = height * i / 5;
                MeasurementChartCanvas.Children.Add(new Line { X1 = 0, X2 = width, Y1 = y, Y2 = y, Stroke = new SolidColorBrush(Color.FromRgb(226, 232, 240)), StrokeDashArray = new DoubleCollection { 2, 3 } });
            }
            for (int i = 1; i < 8; i++)
            {
                double x = width * i / 8;
                MeasurementChartCanvas.Children.Add(new Line { X1 = x, X2 = x, Y1 = 0, Y2 = height, Stroke = new SolidColorBrush(Color.FromRgb(226, 232, 240)), StrokeDashArray = new DoubleCollection { 2, 3 } });
            }

            int start = Math.Max(0, _viewModel.Snapshots.Count - 60);
            List<double> temperatures = new List<double>();
            List<double> humidities = new List<double>();
            int temperaturePointCount = HasTemperatureMode() ? GetPointCount(TemperaturePointCountComboBox) : 0;
            int humidityPointCount = HasHumidityMode() ? GetPointCount(HumidityPointCountComboBox) : 0;
            for (int i = start; i < _viewModel.Snapshots.Count; i++)
            {
                List<double> values = new List<double>();
                AddAverage(_viewModel.Snapshots[i].Channels, ChannelType.Temperature, temperaturePointCount, values);
                temperatures.Add(values.Count == 0 ? double.NaN : values[0]);
                values.Clear();
                AddAverage(_viewModel.Snapshots[i].Channels, ChannelType.Humidity, humidityPointCount, values);
                humidities.Add(values.Count == 0 ? double.NaN : values[0]);
            }
            AddChartLine(temperatures, width, height, Brushes.DodgerBlue);
            AddChartLine(humidities, width, height, Brushes.SeaGreen);
            ChartScaleTextBlock.Text = $"{FormatChartRange(temperatures, "T", "℃")}{(temperaturePointCount > 0 && humidityPointCount > 0 ? "  ·  " : string.Empty)}{FormatChartRange(humidities, "H", "%RH")}";
        }

        private void AddChartLine(List<double> values, double width, double height, Brush brush)
        {
            List<double> valid = values.FindAll(v => !double.IsNaN(v) && !double.IsInfinity(v));
            if (valid.Count < 2) return;
            double min = double.MaxValue;
            double max = double.MinValue;
            foreach (double value in valid) { min = Math.Min(min, value); max = Math.Max(max, value); }
            double range = max - min;
            Polyline line = new Polyline { Stroke = brush, StrokeThickness = 2, Points = new PointCollection() };
            for (int i = 0; i < values.Count; i++)
            {
                if (double.IsNaN(values[i])) continue;
                double x = values.Count == 1 ? 0 : width * i / (values.Count - 1);
                double y = range < 0.0001
                    ? height / 2
                    : height - ((values[i] - min) / range * (height - 10)) - 5;
                line.Points.Add(new Point(x, y));
            }
            MeasurementChartCanvas.Children.Add(line);
        }

        private static string FormatChartRange(List<double> values, string label, string unit)
        {
            List<double> valid = values.FindAll(value => double.IsFinite(value));
            if (valid.Count == 0) return string.Empty;
            double min = double.MaxValue;
            double max = double.MinValue;
            foreach (double value in valid) { min = Math.Min(min, value); max = Math.Max(max, value); }
            return $"{label} {min:F2}～{max:F2} {unit}";
        }

        private void MeasurementChartCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => DrawMeasurementChart();

        private void SetExecutionParametersEnabled(bool enabled)
        {
            SlaveAddressTextBox.IsEnabled = enabled;
            IntervalTextBox.IsEnabled = enabled;
            CalibrationCountComboBox.IsEnabled = enabled;
            CalibrationIntervalTextBox.IsEnabled = enabled && !CalibrationTaskContext.IsConfigured;
            CenterIntervalTextBox.IsEnabled = enabled && !CalibrationTaskContext.IsConfigured;
            UpdateParameterVisibility();
        }

        private void OnAcquisitionError(Exception ex)
        {
            if (_calibrationRunning)
                CalibrationFileStorageService.Default.TryMarkInterrupted("巡检仪采集异常导致正式校准中断：" + ex.Message, out _);
            _deviceResponding = false;
            _requiredChannelsValid = false;
            _trendLooksStable = false;
            _acquisitionService.Stop();
            _viewModel.IsAcquiring = false;
            _modbusClient.Close();
            Dispatcher.Invoke(() =>
            {
                ConnectDeviceButton.IsEnabled = true;
                StartAcquisitionButton.IsEnabled = false;
                StopAcquisitionButton.IsEnabled = false;
                StartCalibrationButton.IsEnabled = false;
                PortComboBox.IsEnabled = true;
                BaudRateComboBox.IsEnabled = true;
                SetExecutionParametersEnabled(true);
                UpdateConnectionStatus();
                EvaluateFormalReadiness();
                StatusTextBlock.Text = "采集异常，已停止并释放串口";
                MessageBox.Show(ex.Message, "采集异常", MessageBoxButton.OK, MessageBoxImage.Error);
            });
        }

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
        }
    }
}
