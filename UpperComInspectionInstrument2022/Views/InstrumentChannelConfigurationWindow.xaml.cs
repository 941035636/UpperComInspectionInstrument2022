using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using UpperComInspectionInstrument2022.Communication;
using UpperComInspectionInstrument2022.Services;

namespace UpperComInspectionInstrument2022.Views
{
    /// <summary>读取并安全写入巡检仪 24 路温度、9 路湿度物理接口配置。</summary>
    public partial class InstrumentChannelConfigurationWindow : Window
    {
        private readonly ModbusRtuClient _client;
        private readonly InspectionDataAcquisitionService _acquisitionService;
        private readonly InspectionInstrumentConfigurationService _configurationService;
        private readonly InspectionInstrumentChannelProfileService _profileService =
            InspectionInstrumentChannelProfileService.Default;
        private readonly byte _slaveAddress;
        private readonly ObservableCollection<TemperatureChannelRow> _temperatureRows = new();
        private readonly ObservableCollection<HumidityChannelRow> _humidityRows = new();
        private InspectionInstrumentConfiguration? _loadedConfiguration;
        private bool _isBusy;

        public InstrumentChannelConfigurationWindow(
            ModbusRtuClient client,
            InspectionDataAcquisitionService acquisitionService,
            byte slaveAddress)
        {
            InitializeComponent();
            _client = client;
            _acquisitionService = acquisitionService;
            _slaveAddress = slaveAddress;
            _configurationService = new InspectionInstrumentConfigurationService(client);
            SensorTypeColumn.ItemsSource = SensorTypes;
            TemperatureGrid.ItemsSource = _temperatureRows;
            HumidityGrid.ItemsSource = _humidityRows;
        }

        private static IReadOnlyList<SensorTypeOption> SensorTypes { get; } = new[]
        {
            new SensorTypeOption(0, "NC（未接入）"), new SensorTypeOption(1, "Pt100"),
            new SensorTypeOption(2, "Cu50"), new SensorTypeOption(3, "Cu100"),
            new SensorTypeOption(4, "S 型热电偶"), new SensorTypeOption(5, "R 型热电偶"),
            new SensorTypeOption(6, "B 型热电偶"), new SensorTypeOption(7, "K 型热电偶"),
            new SensorTypeOption(8, "N 型热电偶"), new SensorTypeOption(9, "J 型热电偶"),
            new SensorTypeOption(10, "E 型热电偶"), new SensorTypeOption(11, "T 型热电偶"),
            new SensorTypeOption(12, "mV"), new SensorTypeOption(13, "Ω")
        };

        private async void ReadButton_Click(object sender, RoutedEventArgs e)
        {
            if (!CanConfigure()) return;
            InvalidateLoadedConfiguration();
            SetBusy(true, "正在读取设备配置……");
            try
            {
                BackgroundOperationResult<InspectionInstrumentConfiguration> result = await Task.Run(() =>
                    RunSafely(() => _configurationService.Read(_slaveAddress)));
                if (!result.Success || result.Value == null)
                {
                    string error = result.ErrorMessage ?? "读取设备配置失败。";
                    TraceConfiguration("读取通道配置", "失败", error);
                    SetStatus(error, true);
                    MessageBox.Show(
                        error + "\n\n本次读取未切换设备配置模式。请确认没有其他串口程序占用同一端口后重试。",
                        "读取通道配置失败",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }
                InspectionInstrumentConfiguration configuration = result.Value;
                TraceConfiguration(
                    "读取通道配置",
                    "成功",
                    $"已读取24路温度传感器类型和33路通道使能；分段={configuration.ConfigurationChunkRegisterCount}");
                _loadedConfiguration = configuration;
                ShowConfiguration(configuration);
                TemperatureGrid.IsReadOnly = false;
                HumidityGrid.IsReadOnly = false;
                SaveButton.IsEnabled = true;
                if (string.IsNullOrWhiteSpace(configuration.CommunicationWarning))
                    SetStatus($"设备配置读取成功（每段 {configuration.ConfigurationChunkRegisterCount} 个寄存器），可以修改后保存。", false);
                else
                    SetWarning($"设备配置已读取（每段 {configuration.ConfigurationChunkRegisterCount} 个寄存器）；{configuration.CommunicationWarning}。");
            }
            catch (Exception ex)
            {
                TraceConfiguration("读取通道配置", "失败", ex.Message);
                SetStatus(ex.Message, true);
                MessageBox.Show(
                    ex.Message + "\n\n本次读取未切换设备配置模式。请确认没有其他串口程序占用同一端口后重试。",
                    "读取通道配置失败",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                SetBusy(false);
                UpdateFrameText();
            }
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!CanConfigure()) return;
                if (_loadedConfiguration == null)
                {
                    MessageBox.Show("请先成功读取一次设备配置，再修改并保存。", "无法保存设备配置", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                InspectionInstrumentConfiguration loadedConfiguration = _loadedConfiguration;
                TemperatureGrid.CommitEdit(DataGridEditingUnit.Cell, true);
                TemperatureGrid.CommitEdit(DataGridEditingUnit.Row, true);
                HumidityGrid.CommitEdit(DataGridEditingUnit.Cell, true);
                HumidityGrid.CommitEdit(DataGridEditingUnit.Row, true);

                ushort[] sensorTypes = _temperatureRows.Select(row => row.SensorTypeCode).ToArray();
                bool[] enabled = _temperatureRows.Select(row => row.Enabled)
                    .Concat(_humidityRows.Select(row => row.Enabled)).ToArray();
                if (sensorTypes.Any(value => value > 13))
                {
                    const string invalidTypeMessage = "存在无法识别的传感器类型，请重新读取设备配置后再保存。";
                    SetStatus(invalidTypeMessage, true);
                    MessageBox.Show(invalidTypeMessage, "无法保存设备配置", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (loadedConfiguration.TemperatureSensorTypes.SequenceEqual(sensorTypes) &&
                    loadedConfiguration.ChannelEnabled.SequenceEqual(enabled) &&
                    !loadedConfiguration.HasInvalidRegisters)
                {
                    if (_profileService.TrySave(
                            _client.PortName ?? string.Empty,
                            _slaveAddress,
                            enabled,
                            out string baselineError))
                    {
                        SetStatus("当前设置与设备读取快照一致，无需写入；已记录为实时测量启动前的核对基准。", false);
                    }
                    else
                    {
                        SetWarning($"当前设置与设备一致，但保存启动核对基准失败：{baselineError}");
                    }
                    return;
                }

                string repairText = loadedConfiguration.HasInvalidRegisters
                    ? "\n\n检测到设备中存在稳定异常配置值；异常行已使用安全默认值，继续保存将同时修复这些寄存器。"
                    : string.Empty;
                MessageBoxResult confirmation = MessageBox.Show(
                    "将只把相对于本次读取快照发生变化或需要修复的通道写入巡检仪，并只读回变化地址核对。" +
                    $"保存期间请勿同时操作巡检仪本机配置界面。{repairText}\n\n是否继续？",
                    "确认写入巡检仪",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (confirmation != MessageBoxResult.Yes) return;

                SetBusy(true, "正在写入、核对并保存设备配置……");
                BackgroundOperationResult<InspectionInstrumentConfiguration> result = await Task.Run(() =>
                    RunSafely(() => _configurationService.WriteAndSave(
                        _slaveAddress,
                        loadedConfiguration,
                        sensorTypes,
                        enabled)));
                if (!result.Success || result.Value == null)
                {
                    string error = result.ErrorMessage ?? "写入设备配置失败。";
                    TraceConfiguration("写入通道配置", "失败", error);
                    InvalidateLoadedConfiguration();
                    SetStatus(error, true);
                    MessageBox.Show(
                        error + "\n\n设备状态可能已发生部分变化，旧快照已作废。请确认巡检仪退出配置模式并重新读取后再操作。",
                        "写入通道配置失败",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }
                InspectionInstrumentConfiguration saved = result.Value;
                if (!_profileService.TrySave(
                        _client.PortName ?? string.Empty,
                        _slaveAddress,
                        saved.ChannelEnabled,
                        out string profileError))
                {
                    saved.CommunicationWarning = string.IsNullOrWhiteSpace(saved.CommunicationWarning)
                        ? profileError
                        : $"{saved.CommunicationWarning}；{profileError}";
                }
                int changedSensorTypeCount = loadedConfiguration.TemperatureSensorTypes
                    .Zip(sensorTypes, (before, after) => before != after)
                    .Count(changed => changed);
                int changedEnableCount = loadedConfiguration.ChannelEnabled
                    .Zip(enabled, (before, after) => before != after)
                    .Count(changed => changed);
                TraceConfiguration(
                    "写入通道配置",
                    string.IsNullOrWhiteSpace(saved.CommunicationWarning) ? "成功" : "完成但有警告",
                    $"传感器类型变更={changedSensorTypeCount}；通道使能变更={changedEnableCount}；" +
                    $"模式={(saved.UsedCompatibilityConfigurationMode ? "兼容配置模式" : "直接写入")}；" +
                    $"设备运行确认={saved.DeviceRunningConfirmed}；{saved.CommunicationWarning}");
                _loadedConfiguration = saved;
                ShowConfiguration(saved);
                if (string.IsNullOrWhiteSpace(saved.CommunicationWarning))
                    SetStatus(
                        saved.UsedCompatibilityConfigurationMode
                            ? "设备配置已读回确认并保存（使用兼容配置模式），巡检仪传感器采集已恢复。"
                            : "设备配置已通过直接写入并读回确认，巡检仪传感器采集保持运行。",
                        false);
                else
                    SetWarning(
                        $"配置寄存器已写入并读回一致（{(saved.UsedCompatibilityConfigurationMode ? "兼容配置模式" : "直接写入")}）；" +
                        $"{saved.CommunicationWarning}。工作台开始测量时会再次检查运行状态。");
            }
            catch (Exception ex)
            {
                TraceConfiguration("写入通道配置", "失败", ex.Message);
                InvalidateLoadedConfiguration();
                SetStatus(ex.Message, true);
                MessageBox.Show(ex.Message, "写入通道配置失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetBusy(false);
                UpdateFrameText();
            }
        }

        private bool CanConfigure()
        {
            if (!_client.IsOpen)
            {
                MessageBox.Show("请先连接巡检仪。", "通道配置", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            if (_acquisitionService.IsRunning)
            {
                MessageBox.Show("请先暂停实时测量，等待当前请求结束后再配置通道。", "通道配置", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            return true;
        }

        private void ShowConfiguration(InspectionInstrumentConfiguration configuration)
        {
            _temperatureRows.Clear();
            for (int index = 0; index < InspectionInstrumentProtocol.PhysicalTemperatureChannelCount; index++)
            {
                _temperatureRows.Add(new TemperatureChannelRow
                {
                    ChannelLabel = $"CH{index + 1}",
                    SensorTypeCode = configuration.TemperatureSensorTypes[index],
                    Enabled = configuration.ChannelEnabled[index],
                    ConfigurationStatus = configuration.InvalidSensorTypeRegisters[index]
                        ? $"异常 0x{configuration.SensorTypeRegisterRawValues[index]:X4}，保存后修复"
                        : configuration.InvalidEnableRegisters[index]
                            ? $"使能异常 0x{configuration.EnableRegisterRawValues[index]:X4}，保存后修复"
                            : "正常"
                });
            }
            _humidityRows.Clear();
            for (int index = 0; index < InspectionInstrumentProtocol.PhysicalHumidityChannelCount; index++)
            {
                _humidityRows.Add(new HumidityChannelRow
                {
                    ChannelLabel = $"H{index + 1}",
                    DeviceChannelLabel = $"通道{InspectionInstrumentProtocol.PhysicalTemperatureChannelCount + index + 1}",
                    Enabled = configuration.ChannelEnabled[InspectionInstrumentProtocol.PhysicalTemperatureChannelCount + index],
                    ConfigurationStatus = configuration.InvalidEnableRegisters[
                        InspectionInstrumentProtocol.PhysicalTemperatureChannelCount + index]
                        ? $"异常 0x{configuration.EnableRegisterRawValues[InspectionInstrumentProtocol.PhysicalTemperatureChannelCount + index]:X4}，保存后修复"
                        : "正常"
                });
            }
            bool hasMixedByteOrder = configuration.SensorTypeRegisterLowByteFirst.Distinct().Count() > 1 ||
                                     configuration.EnableRegisterLowByteFirst.Distinct().Count() > 1;
            ByteOrderTextBlock.Text = hasMixedByteOrder
                ? "已兼容设备逐通道混合字节序"
                : configuration.SensorTypeRegistersUseLowByteFirst || configuration.EnableRegistersUseLowByteFirst
                    ? "已适配当前设备配置区字节序"
                    : "标准 Modbus 配置字节序";
        }

        private void SetBusy(bool busy, string? message = null)
        {
            _isBusy = busy;
            ReadButton.IsEnabled = !busy;
            SaveButton.IsEnabled = !busy && _loadedConfiguration != null && _temperatureRows.Count > 0;
            TemperatureGrid.IsEnabled = !busy;
            HumidityGrid.IsEnabled = !busy;
            CloseButton.IsEnabled = !busy;
            if (!string.IsNullOrWhiteSpace(message)) SetStatus(message, false);
        }

        /// <summary>
        /// 配置读写是一个独占串口事务。后台操作尚未结束时禁止关闭窗口，
        /// 防止用户返回工作台后启动实时采集，与仍在执行的配置命令交错。
        /// </summary>
        protected override void OnClosing(CancelEventArgs e)
        {
            if (_isBusy)
            {
                e.Cancel = true;
                MessageBox.Show(
                    "设备配置事务尚未结束，请等待当前读取、写入和运行状态恢复完成。",
                    "正在配置巡检仪",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }
            base.OnClosing(e);
        }

        /// <summary>通信失败后作废可能已经过期的设备快照，禁止基于不确定状态继续写入。</summary>
        private void InvalidateLoadedConfiguration()
        {
            _loadedConfiguration = null;
            TemperatureGrid.IsReadOnly = true;
            HumidityGrid.IsReadOnly = true;
            SaveButton.IsEnabled = false;
        }

        private void SetStatus(string message, bool error)
        {
            StatusTextBlock.Text = message;
            StatusTextBlock.Foreground = error
                ? System.Windows.Media.Brushes.Firebrick
                : System.Windows.Media.Brushes.DarkGreen;
        }

        private void SetWarning(string message)
        {
            StatusTextBlock.Text = message;
            StatusTextBlock.Foreground = System.Windows.Media.Brushes.DarkOrange;
        }

        private void UpdateFrameText()
        {
            FrameTextBlock.Text = $"请求：{_client.LastRequestHex}    响应：{_client.LastResponseHex}";
        }

        /// <summary>把配置事务及最后一组请求/响应写入本地 CSV 运行日志，便于现场复盘。</summary>
        private void TraceConfiguration(string eventName, string result, string details)
        {
            LocalTraceService.Default.TryWriteRuntime(
                result is "失败" ? "错误" : result.Contains("警告", StringComparison.Ordinal) ? "警告" : "信息",
                "巡检仪通道配置",
                eventName,
                $"{result}；{details}；最后请求={_client.LastRequestHex}；最后响应={_client.LastResponseHex}",
                $"COM={_client.PortName};Slave={_slaveAddress}",
                string.Empty,
                out _);
        }

        /// <summary>
        /// 在后台工作线程内部消化设备通信异常，避免 Visual Studio 把通过 Task 传播、
        /// 最终会由界面提示的可预期异常误判为“用户未处理异常”并中断调试。
        /// </summary>
        private static BackgroundOperationResult<T> RunSafely<T>(Func<T> operation)
        {
            try
            {
                return new BackgroundOperationResult<T>(true, operation(), null);
            }
            catch (Exception ex)
            {
                return new BackgroundOperationResult<T>(false, default, ex.Message);
            }
        }

        private sealed record BackgroundOperationResult<T>(bool Success, T? Value, string? ErrorMessage);

        public sealed record SensorTypeOption(ushort Code, string Name);
        public sealed class TemperatureChannelRow
        {
            public string ChannelLabel { get; init; } = string.Empty;
            public ushort SensorTypeCode { get; set; }
            public bool Enabled { get; set; }
            public string ConfigurationStatus { get; init; } = string.Empty;
        }
        public sealed class HumidityChannelRow
        {
            public string ChannelLabel { get; init; } = string.Empty;
            public string DeviceChannelLabel { get; init; } = string.Empty;
            public bool Enabled { get; set; }
            public string ConfigurationStatus { get; init; } = string.Empty;
        }
    }
}
