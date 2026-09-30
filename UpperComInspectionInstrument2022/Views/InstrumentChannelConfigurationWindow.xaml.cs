using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using UpperComInspectionInstrument2022.Communication;
using UpperComInspectionInstrument2022.Services;

namespace UpperComInspectionInstrument2022.Views
{
    /// <summary>维护任务测点绑定，并安全同步巡检仪24路温度、9路湿度物理接口使能。</summary>
    public partial class InstrumentChannelConfigurationWindow : Window
    {
        private readonly ModbusRtuClient _client;
        private readonly InspectionDataAcquisitionService _acquisitionService;
        private readonly InspectionInstrumentConfigurationService _configurationService;
        private readonly InspectionInstrumentChannelProfileService _profileService =
            InspectionInstrumentChannelProfileService.Default;
        private readonly byte _slaveAddress;
        private readonly ObservableCollection<TaskChannelBindingRow> _temperatureMappingRows = new();
        private readonly ObservableCollection<TaskChannelBindingRow> _humidityMappingRows = new();
        private readonly ObservableCollection<TemperatureChannelRow> _temperatureRows = new();
        private readonly ObservableCollection<HumidityChannelRow> _humidityRows = new();
        private InspectionInstrumentConfiguration? _loadedConfiguration;
        private IReadOnlyList<int> _appliedTemperatureMapping = Array.Empty<int>();
        private IReadOnlyList<int> _appliedHumidityMapping = Array.Empty<int>();
        private bool _isBusy;
        private ConfigurationOperation _activeOperation;
        private CancellationTokenSource? _readCancellation;
        private bool _closeAfterReadCancellation;

        public InstrumentChannelConfigurationWindow(
            ModbusRtuClient client,
            InspectionDataAcquisitionService acquisitionService,
            byte slaveAddress,
            int temperaturePointCount,
            int humidityPointCount,
            IEnumerable<int>? temperatureMapping,
            IEnumerable<int>? humidityMapping)
        {
            InitializeComponent();
            _client = client;
            _acquisitionService = acquisitionService;
            _slaveAddress = slaveAddress;
            _configurationService = new InspectionInstrumentConfigurationService(client);

            List<int> normalizedTemperature = MeasurementChannelMappingService.Normalize(
                temperatureMapping,
                temperaturePointCount,
                InspectionInstrumentProtocol.PhysicalTemperatureChannelCount);
            List<int> normalizedHumidity = MeasurementChannelMappingService.Normalize(
                humidityMapping,
                humidityPointCount,
                InspectionInstrumentProtocol.PhysicalHumidityChannelCount);
            foreach ((int physicalChannel, int index) in normalizedTemperature.Select((physical, index) => (physical, index)))
                _temperatureMappingRows.Add(new TaskChannelBindingRow($"T{index + 1}", physicalChannel, RefreshBindingPreview));
            foreach ((int physicalChannel, int index) in normalizedHumidity.Select((physical, index) => (physical, index)))
                _humidityMappingRows.Add(new TaskChannelBindingRow($"H{index + 1}", physicalChannel, RefreshBindingPreview));

            TemperaturePhysicalColumn.ItemsSource = Enumerable.Range(
                    1, InspectionInstrumentProtocol.PhysicalTemperatureChannelCount)
                .Select(channel => new PhysicalChannelOption(channel, $"CH{channel}"))
                .ToList();
            HumidityPhysicalColumn.ItemsSource = Enumerable.Range(
                    1, InspectionInstrumentProtocol.PhysicalHumidityChannelCount)
                .Select(channel => new PhysicalChannelOption(channel, $"H{channel}"))
                .ToList();
            TemperatureMappingGrid.ItemsSource = _temperatureMappingRows;
            HumidityMappingGrid.ItemsSource = _humidityMappingRows;
            TemperatureGrid.ItemsSource = _temperatureRows;
            HumidityGrid.ItemsSource = _humidityRows;
            HumidityTab.Visibility = humidityPointCount > 0 ? Visibility.Visible : Visibility.Collapsed;
            RefreshBindingPreview();
        }

        /// <summary>设备写入并读回成功后确认的温度测点绑定。</summary>
        public IReadOnlyList<int> AppliedTemperatureMapping => _appliedTemperatureMapping;

        /// <summary>设备写入并读回成功后确认的湿度测点绑定。</summary>
        public IReadOnlyList<int> AppliedHumidityMapping => _appliedHumidityMapping;

        /// <summary>是否已经完成“设备使能确认 + 任务绑定确认”的联合保存。</summary>
        public bool HasAppliedTaskMapping { get; private set; }

        /// <summary>联合保存完成后通知工作台立即持久化已确认的任务测点绑定。</summary>
        public event EventHandler? TaskMappingApplied;

        private async void ReadButton_Click(object sender, RoutedEventArgs e)
        {
            if (!CanConfigure()) return;
            InvalidateLoadedConfiguration();
            _readCancellation?.Dispose();
            _readCancellation = new CancellationTokenSource();
            CancellationTokenSource readCancellation = _readCancellation;
            SetBusy(true, "正在读取设备配置……", ConfigurationOperation.Reading);
            try
            {
                BackgroundOperationResult<InspectionInstrumentConfiguration> result = await Task.Run(() =>
                    RunSafely(() => _configurationService.ReadChannelConfiguration(
                        _slaveAddress,
                        readCancellation.Token)));
                if (readCancellation.IsCancellationRequested)
                {
                    TraceConfiguration("读取通道配置", "已取消", "用户关闭配置窗口，已停止后续分段读取");
                    return;
                }
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
                    $"已读取33路通道使能；分段={configuration.ConfigurationChunkRegisterCount}");
                _loadedConfiguration = configuration;
                ShowConfiguration(configuration);
                SaveButton.IsEnabled = true;
                if (string.IsNullOrWhiteSpace(configuration.CommunicationWarning))
                    SetStatus($"设备配置读取成功（每段 {configuration.ConfigurationChunkRegisterCount} 个寄存器），可以修改后保存。", false);
                else
                    SetWarning($"设备配置已读取（每段 {configuration.ConfigurationChunkRegisterCount} 个寄存器）；{configuration.CommunicationWarning}。");
            }
            catch (Exception ex)
            {
                if (readCancellation.IsCancellationRequested && ex is OperationCanceledException)
                    return;
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
                if (ReferenceEquals(_readCancellation, readCancellation))
                {
                    _readCancellation.Dispose();
                    _readCancellation = null;
                }
                SetBusy(false);
                UpdateFrameText();
                if (_closeAfterReadCancellation)
                {
                    _closeAfterReadCancellation = false;
                    _ = Dispatcher.BeginInvoke(new Action(Close));
                }
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
                TemperatureMappingGrid.CommitEdit(DataGridEditingUnit.Cell, true);
                TemperatureMappingGrid.CommitEdit(DataGridEditingUnit.Row, true);
                HumidityMappingGrid.CommitEdit(DataGridEditingUnit.Cell, true);
                HumidityMappingGrid.CommitEdit(DataGridEditingUnit.Row, true);
                if (!TryBuildDesiredChannelState(
                        out List<int> temperatureMapping,
                        out List<int> humidityMapping,
                        out bool[] enabled,
                        out string validationError))
                {
                    SetStatus(validationError, true);
                    MessageBox.Show(validationError, "无法保存通道配置", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (loadedConfiguration.ChannelEnabled.SequenceEqual(enabled) &&
                    !loadedConfiguration.HasEnableRegistersNeedingRepair)
                {
                    if (_profileService.TrySave(
                            _client.PortName ?? string.Empty,
                            _slaveAddress,
                            enabled,
                            out string baselineError))
                    {
                        CompleteJointSave(temperatureMapping, humidityMapping);
                        SetStatus("设备使能已与任务绑定一致，无需重复写入；任务绑定已保存。", false);
                        ShowSaveResult(
                            "保存成功。\n\n任务测点绑定已保存；设备通道使能与目标一致，无需重复写入。",
                            false);
                    }
                    else
                    {
                        SetWarning($"当前设置与设备一致，但保存启动核对基准失败：{baselineError}");
                    }
                    return;
                }

                string repairText = loadedConfiguration.HasEnableRegistersNeedingRepair
                    ? "\n\n检测到通道使能区存在异常值或可疑旧字节序；继续保存将按标准 Modbus 写法重新修复。"
                    : string.Empty;
                MessageBoxResult confirmation = MessageBox.Show(
                    BuildChangeConfirmation(loadedConfiguration.ChannelEnabled, enabled) +
                    "\n\n将只把相对于本次读取快照发生变化或需要修复的通道写入巡检仪，并只读回变化地址核对。" +
                    $"保存期间请勿同时操作巡检仪本机配置界面。{repairText}\n\n是否继续？",
                    "确认写入巡检仪",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (confirmation != MessageBoxResult.Yes) return;

                SetBusy(true, "正在写入、核对并保存设备配置……", ConfigurationOperation.Writing);
                BackgroundOperationResult<InspectionInstrumentConfiguration> result;
                while (true)
                {
                    result = await Task.Run(() =>
                        RunSafely(() => _configurationService.WriteChannelEnabledAndSave(
                            _slaveAddress,
                            loadedConfiguration,
                            enabled)));
                    if (result.Success && result.Value != null) break;

                    string error = result.ErrorMessage ?? "写入设备配置失败。";
                    if (result.Exception is InspectionInstrumentConfigurationWriteException
                        {
                            CanRetryWithCurrentSnapshot: true,
                            DeviceMayHaveChanged: false
                        })
                    {
                        TraceConfiguration("写入前核对暂时无响应", "可重试", error);
                        SetWarning("写入前通信核对暂时无响应；设备尚未被修改，当前快照和编辑内容已保留。可直接重试写入。");
                        MessageBoxResult retry = MessageBox.Show(
                            error +
                            "\n\n本次失败发生在任何配置写命令之前，设备配置尚未被修改，当前勾选和读取快照均已保留。" +
                            "\n\n选择“是”立即重新执行完整核对和写入；选择“否”返回页面，稍后可直接点击“写入并保存”。",
                            "设备暂时无响应",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Warning);
                        if (retry == MessageBoxResult.Yes)
                        {
                            SetStatus("正在重新核对设备配置并重试写入……", false);
                            continue;
                        }
                        return;
                    }

                    TraceConfiguration("写入通道配置", "失败", error);
                    InvalidateLoadedConfiguration();
                    SetStatus(error, true);
                    MessageBox.Show(
                        error + "\n\n配置写入可能已经开始，或设备状态已偏离读取快照。为防止覆盖设备端变化，旧快照已作废，请重新读取后再操作。",
                        "写入通道配置失败",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }
                InspectionInstrumentConfiguration saved = result.Value!;
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
                CompleteJointSave(temperatureMapping, humidityMapping);
                int changedEnableCount = loadedConfiguration.ChannelEnabled
                    .Zip(enabled, (before, after) => before != after)
                    .Count(changed => changed);
                int repairedEnableCount = loadedConfiguration.InvalidEnableRegisters
                    .Select((invalid, index) =>
                        invalid ||
                        (loadedConfiguration.ChannelEnabled[index] &&
                         loadedConfiguration.EnableRegisterLowByteFirst[index] !=
                         loadedConfiguration.EnableRegistersUseLowByteFirst))
                    .Count(repair => repair);
                TraceConfiguration(
                    "写入通道配置",
                    string.IsNullOrWhiteSpace(saved.CommunicationWarning) ? "成功" : "完成但有警告",
                    $"通道使能变更={changedEnableCount}；通道使能修复={repairedEnableCount}；" +
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
                SetBusy(false);
                if (string.IsNullOrWhiteSpace(saved.CommunicationWarning))
                {
                    ShowSaveResult(
                        "保存成功。\n\n任务测点绑定已保存；设备通道使能已写入并回读确认；巡检仪采集状态已恢复。",
                        false);
                }
                else
                {
                    ShowSaveResult(
                        "设备通道配置已写入并回读，任务测点绑定已保存，但存在以下提示：\n\n" +
                        saved.CommunicationWarning +
                        "\n\n请按提示确认设备状态。",
                        true);
                }
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
                    CurrentStateLabel = configuration.ChannelEnabled[index] ? "开启" : "关闭",
                    ConfigurationStatus = configuration.InvalidEnableRegisters[index]
                        ? $"异常 0x{configuration.EnableRegisterRawValues[index]:X4}，保存后修复"
                        : configuration.ChannelEnabled[index] &&
                          configuration.EnableRegisterLowByteFirst[index] != configuration.EnableRegistersUseLowByteFirst
                            ? "可疑旧字节序，保存后修复"
                            : "正常"
                });
            }
            _humidityRows.Clear();
            for (int index = 0; index < InspectionInstrumentProtocol.PhysicalHumidityChannelCount; index++)
            {
                _humidityRows.Add(new HumidityChannelRow
                {
                    ChannelLabel = $"H{index + 1}",
                    CurrentStateLabel = configuration.ChannelEnabled[
                        InspectionInstrumentProtocol.PhysicalTemperatureChannelCount + index] ? "开启" : "关闭",
                    ConfigurationStatus = configuration.InvalidEnableRegisters[
                        InspectionInstrumentProtocol.PhysicalTemperatureChannelCount + index]
                        ? $"异常 0x{configuration.EnableRegisterRawValues[InspectionInstrumentProtocol.PhysicalTemperatureChannelCount + index]:X4}，保存后修复"
                        : configuration.ChannelEnabled[InspectionInstrumentProtocol.PhysicalTemperatureChannelCount + index] &&
                          configuration.EnableRegisterLowByteFirst[InspectionInstrumentProtocol.PhysicalTemperatureChannelCount + index] !=
                          configuration.EnableRegistersUseLowByteFirst
                            ? "可疑旧字节序，保存后修复"
                        : "正常"
                });
            }
            bool hasMixedByteOrder = configuration.EnableRegisterLowByteFirst.Distinct().Count() > 1;
            ByteOrderTextBlock.Text = hasMixedByteOrder
                ? "已兼容设备逐通道混合字节序"
                : configuration.EnableRegistersUseLowByteFirst
                    ? "已适配当前设备配置区字节序"
                    : "标准 Modbus 配置字节序";
            RefreshBindingPreview();
        }

        /// <summary>
        /// 根据当前任务绑定刷新33路目标使能状态。用户只维护逻辑点到物理接口的对应关系，
        /// 设备使能完全由绑定推导，避免同一信息在两个位置重复配置。
        /// </summary>
        private void RefreshBindingPreview()
        {
            List<int> temperatureMapping = _temperatureMappingRows.Select(row => row.PhysicalChannel).ToList();
            List<int> humidityMapping = _humidityMappingRows.Select(row => row.PhysicalChannel).ToList();
            HashSet<int> temperatureInterfaces = temperatureMapping.ToHashSet();
            HashSet<int> humidityInterfaces = humidityMapping.ToHashSet();

            foreach (TemperatureChannelRow row in _temperatureRows)
            {
                int channel = ParseChannelNumber(row.ChannelLabel);
                string boundPoints = string.Join("、", _temperatureMappingRows
                    .Where(mapping => mapping.PhysicalChannel == channel)
                    .Select(mapping => mapping.LogicalPointLabel));
                row.UpdateBinding(boundPoints, temperatureInterfaces.Contains(channel));
            }
            foreach (HumidityChannelRow row in _humidityRows)
            {
                int channel = ParseChannelNumber(row.ChannelLabel);
                string boundPoints = string.Join("、", _humidityMappingRows
                    .Where(mapping => mapping.PhysicalChannel == channel)
                    .Select(mapping => mapping.LogicalPointLabel));
                row.UpdateBinding(boundPoints, humidityInterfaces.Contains(channel));
            }

            string temperatureText = temperatureMapping.Count == 0
                ? "无温度测点"
                : $"开启 {string.Join("、", temperatureInterfaces.OrderBy(value => value).Select(value => $"CH{value}"))}";
            string humidityText = humidityMapping.Count == 0
                ? "无湿度测点"
                : $"开启 {string.Join("、", humidityInterfaces.OrderBy(value => value).Select(value => $"H{value}"))}";
            bool hasDuplicate = !HasUniqueMappings(temperatureMapping) || !HasUniqueMappings(humidityMapping);
            BindingSummaryTextBlock.Text = hasDuplicate
                ? "绑定冲突：同一类中的多个逻辑测点不能共用一个物理接口，请重新选择。"
                : $"保存后：{temperatureText}；{humidityText}；其余物理通道自动关闭。";
            BindingSummaryTextBlock.Foreground = hasDuplicate
                ? System.Windows.Media.Brushes.Firebrick
                : System.Windows.Media.Brushes.RoyalBlue;
            SaveButton.IsEnabled = !_isBusy && _loadedConfiguration != null;
        }

        /// <summary>校验绑定并生成与协议33路使能寄存器一一对应的目标状态。</summary>
        private bool TryBuildDesiredChannelState(
            out List<int> temperatureMapping,
            out List<int> humidityMapping,
            out bool[] enabled,
            out string error)
        {
            temperatureMapping = _temperatureMappingRows.Select(row => row.PhysicalChannel).ToList();
            humidityMapping = _humidityMappingRows.Select(row => row.PhysicalChannel).ToList();
            enabled = new bool[InspectionInstrumentProtocol.ConfigurableEnableChannelCount];
            error = string.Empty;

            if (temperatureMapping.Count == 0)
            {
                error = "当前任务没有温度测点，无法生成设备通道配置。";
                return false;
            }
            if (temperatureMapping.Any(channel => channel < 1 || channel > InspectionInstrumentProtocol.PhysicalTemperatureChannelCount))
            {
                error = "温度测点存在无效物理接口，请重新选择 CH1～CH24。";
                return false;
            }
            if (!HasUniqueMappings(temperatureMapping))
            {
                error = "温度测点不能重复绑定同一个物理接口。";
                return false;
            }
            if (humidityMapping.Any(channel => channel < 1 || channel > InspectionInstrumentProtocol.PhysicalHumidityChannelCount))
            {
                error = "湿度测点存在无效物理接口，请重新选择 H1～H9。";
                return false;
            }
            if (!HasUniqueMappings(humidityMapping))
            {
                error = "湿度测点不能重复绑定同一个物理接口。";
                return false;
            }

            enabled = MeasurementChannelMappingService.BuildDeviceEnableState(temperatureMapping, humidityMapping);
            return true;
        }

        /// <summary>生成写入前可核对的通道变化摘要。</summary>
        private static string BuildChangeConfirmation(IReadOnlyList<bool> before, IReadOnlyList<bool> after)
        {
            List<string> opening = new();
            List<string> closing = new();
            for (int index = 0; index < after.Count; index++)
            {
                if (before[index] == after[index]) continue;
                string label = index < InspectionInstrumentProtocol.PhysicalTemperatureChannelCount
                    ? $"CH{index + 1}"
                    : $"H{index - InspectionInstrumentProtocol.PhysicalTemperatureChannelCount + 1}";
                (after[index] ? opening : closing).Add(label);
            }

            string openingText = opening.Count == 0 ? "无" : string.Join("、", opening);
            string closingText = closing.Count == 0 ? "无" : string.Join("、", closing);
            return $"本次任务绑定将自动开启：{openingText}\n将自动关闭未绑定通道：{closingText}";
        }

        /// <summary>只在设备使能保存成功后冻结本次任务映射，并通知工作台立即写入任务文件。</summary>
        private void CompleteJointSave(IReadOnlyList<int> temperatureMapping, IReadOnlyList<int> humidityMapping)
        {
            _appliedTemperatureMapping = temperatureMapping.ToArray();
            _appliedHumidityMapping = humidityMapping.ToArray();
            HasAppliedTaskMapping = true;
            TaskMappingApplied?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>反馈联合保存结果，但保留配置窗口，便于用户立即重新读取设备进行复核。</summary>
        private void ShowSaveResult(string message, bool warning)
        {
            MessageBox.Show(
                this,
                message + "\n\n配置窗口将保留，您可以点击“读取设备配置”再次核对；确认无误后再手动关闭。",
                warning ? "保存完成（有提示）" : "保存成功",
                MessageBoxButton.OK,
                warning ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }

        private static bool HasUniqueMappings(IReadOnlyCollection<int> mapping) =>
            mapping.Distinct().Count() == mapping.Count;

        private static int ParseChannelNumber(string label) =>
            int.TryParse(new string(label.Where(char.IsDigit).ToArray()), out int value) ? value : 0;

        private void SetBusy(
            bool busy,
            string? message = null,
            ConfigurationOperation operation = ConfigurationOperation.None)
        {
            _isBusy = busy;
            _activeOperation = busy ? operation : ConfigurationOperation.None;
            ReadButton.IsEnabled = !busy;
            SaveButton.IsEnabled = !busy && _loadedConfiguration != null && _temperatureRows.Count > 0;
            TemperatureMappingGrid.IsEnabled = !busy;
            HumidityMappingGrid.IsEnabled = !busy;
            TemperatureGrid.IsEnabled = !busy;
            HumidityGrid.IsEnabled = !busy;
            CloseButton.IsEnabled = !busy || _activeOperation == ConfigurationOperation.Reading;
            if (!string.IsNullOrWhiteSpace(message)) SetStatus(message, false);
        }

        /// <summary>
        /// 读取阶段允许取消并在当前单次串口请求结束后自动关闭；写入阶段仍禁止退出，
        /// 防止设备停在部分写入或尚未恢复传感器扫描的状态。
        /// </summary>
        protected override void OnClosing(CancelEventArgs e)
        {
            if (_isBusy)
            {
                e.Cancel = true;
                if (_activeOperation == ConfigurationOperation.Reading)
                {
                    _closeAfterReadCancellation = true;
                    _readCancellation?.Cancel();
                    SetWarning("正在取消设备配置读取，当前串口请求结束后将自动关闭……");
                    return;
                }
                MessageBox.Show(
                    "设备配置写入尚未结束，请等待写入、核对和运行状态恢复完成。",
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
                return new BackgroundOperationResult<T>(true, operation(), null, null);
            }
            catch (Exception ex)
            {
                return new BackgroundOperationResult<T>(false, default, ex.Message, ex);
            }
        }

        private sealed record BackgroundOperationResult<T>(
            bool Success,
            T? Value,
            string? ErrorMessage,
            Exception? Exception);

        private enum ConfigurationOperation
        {
            None,
            Reading,
            Writing
        }

        public sealed class TemperatureChannelRow : INotifyPropertyChanged
        {
            public string ChannelLabel { get; init; } = string.Empty;
            public string CurrentStateLabel { get; init; } = "关闭";
            public string ConfigurationStatus { get; init; } = string.Empty;
            public string BoundPointLabel { get; private set; } = "未绑定";
            public string DesiredStateLabel { get; private set; } = "将关闭";

            public event PropertyChangedEventHandler? PropertyChanged;

            public void UpdateBinding(string boundPointLabel, bool enabled)
            {
                BoundPointLabel = string.IsNullOrWhiteSpace(boundPointLabel) ? "未绑定" : boundPointLabel;
                DesiredStateLabel = enabled ? "开启" : "关闭";
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BoundPointLabel)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DesiredStateLabel)));
            }
        }

        public sealed class HumidityChannelRow : INotifyPropertyChanged
        {
            public string ChannelLabel { get; init; } = string.Empty;
            public string CurrentStateLabel { get; init; } = "关闭";
            public string ConfigurationStatus { get; init; } = string.Empty;
            public string BoundPointLabel { get; private set; } = "未绑定";
            public string DesiredStateLabel { get; private set; } = "将关闭";

            public event PropertyChangedEventHandler? PropertyChanged;

            public void UpdateBinding(string boundPointLabel, bool enabled)
            {
                BoundPointLabel = string.IsNullOrWhiteSpace(boundPointLabel) ? "未绑定" : boundPointLabel;
                DesiredStateLabel = enabled ? "开启" : "关闭";
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BoundPointLabel)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DesiredStateLabel)));
            }
        }

        /// <summary>任务逻辑测点与设备物理接口之间的可编辑关系。</summary>
        private sealed class TaskChannelBindingRow : INotifyPropertyChanged
        {
            private readonly Action _changed;
            private int _physicalChannel;

            public TaskChannelBindingRow(string logicalPointLabel, int physicalChannel, Action changed)
            {
                LogicalPointLabel = logicalPointLabel;
                _physicalChannel = physicalChannel;
                _changed = changed;
            }

            public string LogicalPointLabel { get; }

            public int PhysicalChannel
            {
                get => _physicalChannel;
                set
                {
                    if (_physicalChannel == value) return;
                    _physicalChannel = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PhysicalChannel)));
                    _changed();
                }
            }

            public event PropertyChangedEventHandler? PropertyChanged;
        }

        /// <summary>下拉框显示明确接口名，业务层仍保存从1开始的物理通道序号。</summary>
        private sealed record PhysicalChannelOption(int ChannelNumber, string Label);
    }
}
