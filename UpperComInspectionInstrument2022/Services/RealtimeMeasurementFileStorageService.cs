using System.Globalization;
using System.IO;
using System.Text;
using UpperComInspectionInstrument2022.Models;

namespace UpperComInspectionInstrument2022.Services
{
    /// <summary>
    /// 启动一次实时测量记录所需的固定参数。会话开始后参数不再随界面或任务修改而变化。
    /// </summary>
    public sealed class RealtimeMeasurementSessionInfo
    {
        public string PortName { get; init; } = string.Empty;
        public int BaudRate { get; init; }
        public byte SlaveAddress { get; init; }
        public int IntervalMilliseconds { get; init; }
        public string CalibrationType { get; init; } = string.Empty;
        public string SensorType { get; init; } = string.Empty;
        public int TemperaturePointCount { get; init; }
        public int HumidityPointCount { get; init; }
        public bool HasCalibrationTask { get; init; }
        public string Standard { get; init; } = string.Empty;
        public string EquipmentName { get; init; } = string.Empty;
        public string EquipmentSerialNumber { get; init; } = string.Empty;
    }

    /// <summary>
    /// 把普通实时测量保存为独立 CSV 会话，不加入正式校准样本，也不参与规范结果计算。
    /// 每次完整设备响应立即写入测点矩阵和原始通道文件，便于联调、趋势复查和通信追溯。
    /// </summary>
    public sealed class RealtimeMeasurementFileStorageService
    {
        private const string SummaryFileName = "实时测量摘要.csv";
        private const string MeasurementFileName = "实时测量.csv";
        private const string RawChannelFileName = "实时测量原始通道.csv";
        private static readonly UTF8Encoding Utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);
        private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);
        private readonly object _syncRoot = new();
        private RealtimeMeasurementSessionInfo? _sessionInfo;
        private string? _currentSessionDirectory;
        private DateTime? _startedAt;
        private DateTime? _finishedAt;
        private string _status = string.Empty;
        private string _statusMessage = string.Empty;
        private int _savedSnapshotCount;
        private bool _isActive;

        /// <summary>应用内共享的实时记录服务。</summary>
        public static RealtimeMeasurementFileStorageService Default { get; } = new();

        /// <summary>
        /// 创建实时记录服务。应用默认保存到“文档\温湿度校准数据\实时测量记录”，测试可传入独立根目录。
        /// </summary>
        public RealtimeMeasurementFileStorageService(string? dataRootPath = null)
        {
            DataRootPath = string.IsNullOrWhiteSpace(dataRootPath)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "温湿度校准数据", "实时测量记录")
                : Path.GetFullPath(dataRootPath);
        }

        public string DataRootPath { get; }
        public string? CurrentSessionDirectory
        {
            get { lock (_syncRoot) return _currentSessionDirectory; }
        }
        public bool IsActive
        {
            get { lock (_syncRoot) return _isActive; }
        }
        public int SavedSnapshotCount
        {
            get { lock (_syncRoot) return _savedSnapshotCount; }
        }

        /// <summary>
        /// 启动时把上次异常退出遗留的“记录中”会话标记为“已中断”，已写入的实时 CSV 保持不变。
        /// </summary>
        public int RecoverAbandonedSessions(out string error)
        {
            lock (_syncRoot)
            {
                if (!Directory.Exists(DataRootPath))
                {
                    error = string.Empty;
                    return 0;
                }

                int recoveredCount = 0;
                List<string> failures = new();
                string[] directories;
                try
                {
                    directories = Directory.GetDirectories(DataRootPath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    error = $"无法扫描实时测量记录：{ex.Message}\n数据目录：{DataRootPath}";
                    return 0;
                }

                foreach (string directory in directories)
                {
                    string summaryPath = Path.Combine(directory, SummaryFileName);
                    if (!File.Exists(summaryPath)) continue;
                    try
                    {
                        List<string[]> rows = CalibrationFileStorageService.ReadCsvFile(summaryPath);
                        if (rows.Count < 2) continue;
                        string[] header = rows[0];
                        int statusIndex = Array.IndexOf(header, "状态");
                        int finishedAtIndex = Array.IndexOf(header, "结束时间");
                        int messageIndex = Array.IndexOf(header, "状态说明");
                        if (statusIndex < 0 || finishedAtIndex < 0 || messageIndex < 0) continue;

                        string[] values = rows[1];
                        int requiredLength = Math.Max(statusIndex, Math.Max(finishedAtIndex, messageIndex)) + 1;
                        if (values.Length < requiredLength) Array.Resize(ref values, requiredLength);
                        if (!string.Equals(values[statusIndex], "记录中", StringComparison.Ordinal)) continue;

                        values[finishedAtIndex] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
                        values[statusIndex] = "已中断";
                        values[messageIndex] = "检测到程序上次未正常结束，启动时已自动标记为中断；已保存实时数据继续保留。";
                        rows[1] = values;
                        WriteCsvAtomic(summaryPath, rows);
                        recoveredCount++;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or FormatException)
                    {
                        failures.Add($"{Path.GetFileName(directory)}：{ex.Message}");
                    }
                }

                error = failures.Count == 0 ? string.Empty : "部分实时测量会话未能恢复：" + string.Join("；", failures);
                return recoveredCount;
            }
        }

        /// <summary>
        /// 建立一个新的实时记录目录并写入摘要及动态表头。若记录目录无法建立，则不应启动带保存承诺的实时测量。
        /// </summary>
        public bool TryBeginSession(RealtimeMeasurementSessionInfo info, out string error)
        {
            ArgumentNullException.ThrowIfNull(info);
            lock (_syncRoot)
            {
                if (_isActive)
                {
                    error = "已有正在记录的实时测量会话，请先停止当前测量。";
                    return false;
                }
                if (info.TemperaturePointCount < 0 || info.TemperaturePointCount > 50 ||
                    info.HumidityPointCount < 0 || info.HumidityPointCount > 10 ||
                    info.TemperaturePointCount + info.HumidityPointCount == 0)
                {
                    error = "实时记录测点数无效：温度应为 0~50 点、湿度应为 0~10 点，且至少配置一种测量量。";
                    return false;
                }

                DateTime now = DateTime.Now;
                string device = MakeSafeFileName(string.IsNullOrWhiteSpace(info.EquipmentName) ? "设备联调" : info.EquipmentName.Trim());
                string directory = Path.Combine(DataRootPath, $"RT-{now:yyyyMMdd-HHmmss-fff}_{device}");
                try
                {
                    Directory.CreateDirectory(directory);
                    _sessionInfo = info;
                    _currentSessionDirectory = directory;
                    _startedAt = now;
                    _finishedAt = null;
                    _status = "记录中";
                    _statusMessage = "实时测量已启动";
                    _savedSnapshotCount = 0;
                    _isActive = true;

                    WriteCsvAtomic(Path.Combine(directory, MeasurementFileName), new[] { BuildMeasurementHeader(info) });
                    WriteCsvAtomic(Path.Combine(directory, RawChannelFileName), new[] { BuildRawChannelHeader() });
                    WriteSummary();
                    error = string.Empty;
                    return true;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
                {
                    _isActive = false;
                    _status = "保存异常";
                    _statusMessage = "建立实时记录失败：" + ex.Message;
                    TryWriteSummaryAfterFailure();
                    error = $"无法建立实时测量记录：{ex.Message}\n目标位置：{directory}";
                    return false;
                }
            }
        }

        /// <summary>把一组完整设备响应追加到实时测点矩阵和逐通道原始记录。</summary>
        public bool TryAppendSnapshot(MeasurementSnapshot snapshot, out string error)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            lock (_syncRoot)
            {
                if (!_isActive || _sessionInfo == null || _currentSessionDirectory == null)
                {
                    error = "当前没有处于记录状态的实时测量会话。";
                    return false;
                }

                try
                {
                    int sessionSequence = _savedSnapshotCount + 1;
                    List<InspectionChannelData> selected = MeasurementChannelSelectionService.SelectRequired(
                        snapshot.Channels,
                        _sessionInfo.TemperaturePointCount,
                        _sessionInfo.HumidityPointCount);
                    AppendCsvRows(
                        Path.Combine(_currentSessionDirectory, MeasurementFileName),
                        new[] { BuildMeasurementRow(sessionSequence, snapshot, selected, _sessionInfo) });
                    AppendCsvRows(
                        Path.Combine(_currentSessionDirectory, RawChannelFileName),
                        BuildRawChannelRows(sessionSequence, snapshot, selected, _sessionInfo));
                    _savedSnapshotCount = sessionSequence;
                    _statusMessage = $"已保存实时测量 {_savedSnapshotCount} 组";
                    WriteSummary();
                    error = string.Empty;
                    return true;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
                {
                    _finishedAt = DateTime.Now;
                    _status = "保存异常";
                    _statusMessage = $"第 {_savedSnapshotCount + 1} 组实时测量保存失败：{ex.Message}";
                    _isActive = false;
                    TryWriteSummaryAfterFailure();
                    error = $"实时测量记录写入失败：{ex.Message}\n记录目录：{_currentSessionDirectory}";
                    return false;


                }
            }
        }

        /// <summary>
        /// 结束当前实时记录并固化最终状态。没有活动会话时视为成功，便于停止采集和关闭程序统一调用。
        /// </summary>
        public bool TryEndSession(string status, string reason, out string error)
        {
            lock (_syncRoot)
            {
                if (!_isActive || _currentSessionDirectory == null)
                {
                    error = string.Empty;
                    return true;
                }

                _finishedAt = DateTime.Now;
                _status = string.IsNullOrWhiteSpace(status) ? "已停止" : status.Trim();
                _statusMessage = string.IsNullOrWhiteSpace(reason) ? "实时测量记录已结束" : reason.Trim();
                _isActive = false;
                try
                {
                    WriteSummary();
                    error = string.Empty;
                    return true;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
                {
                    error = $"实时测量结束状态未能保存：{ex.Message}\n记录目录：{_currentSessionDirectory}";
                    return false;
                }
            }
        }

        /// <summary>以原子替换方式写入本次会话摘要，避免状态更新中断留下半个文件。</summary>
        private void WriteSummary()
        {
            if (_currentSessionDirectory == null || _sessionInfo == null)
                throw new InvalidOperationException("实时测量会话尚未建立。");
            WriteCsvAtomic(Path.Combine(_currentSessionDirectory, SummaryFileName), new[]
            {
                new[]
                {
                    "数据格式版本", "会话类型", "开始时间", "结束时间", "状态", "状态说明", "已保存组数",
                    "串口", "波特率", "从站地址", "读取周期(ms)", "校准类型", "传感器类型", "温度测点数", "湿度测点数",
                    "关联校准任务", "校准规范", "被校设备", "设备编号", "会话目录"
                },
                new[]
                {
                    "1.0", "普通实时测量", FormatDateTime(_startedAt), FormatDateTime(_finishedAt), _status, _statusMessage,
                    _savedSnapshotCount.ToString(CultureInfo.InvariantCulture), _sessionInfo.PortName,
                    _sessionInfo.BaudRate.ToString(CultureInfo.InvariantCulture), _sessionInfo.SlaveAddress.ToString(CultureInfo.InvariantCulture),
                    _sessionInfo.IntervalMilliseconds.ToString(CultureInfo.InvariantCulture), _sessionInfo.CalibrationType, _sessionInfo.SensorType,
                    _sessionInfo.TemperaturePointCount.ToString(CultureInfo.InvariantCulture),
                    _sessionInfo.HumidityPointCount.ToString(CultureInfo.InvariantCulture),
                    _sessionInfo.HasCalibrationTask ? "是" : "否", _sessionInfo.Standard, _sessionInfo.EquipmentName,
                    _sessionInfo.EquipmentSerialNumber, _currentSessionDirectory
                }
            });
        }

        /// <summary>根据本次温湿度测点数生成便于 Excel/WPS 查看的一行一组矩阵表头。</summary>
        private static string[] BuildMeasurementHeader(RealtimeMeasurementSessionInfo info)
        {
            List<string> header = new() { "会话序号", "采集序号", "采集时间", "有效通道数", "异常通道数", "异常通道" };
            header.AddRange(Enumerable.Range(1, info.TemperaturePointCount).Select(index => $"温度{index}(℃)"));
            header.AddRange(Enumerable.Range(1, info.HumidityPointCount).Select(index => $"湿度{index}(%RH)"));
            return header.ToArray();
        }

        /// <summary>把要求通道按固定顺序写成实时矩阵行，缺失或异常通道留空并写入异常清单。</summary>
        private static string[] BuildMeasurementRow(
            int sessionSequence,
            MeasurementSnapshot snapshot,
            IReadOnlyList<InspectionChannelData> selected,
            RealtimeMeasurementSessionInfo info)
        {
            Dictionary<(ChannelRole Role, int Channel), InspectionChannelData> channels = selected
                .GroupBy(item => (item.Role, item.Channel))
                .ToDictionary(group => group.Key, group => group.First());
            List<string> invalid = new();
            for (int index = 1; index <= info.TemperaturePointCount; index++)
            {
                if (!channels.TryGetValue((ChannelRole.PrimaryTemperature, index), out InspectionChannelData? item) || !item.IsValid)
                    invalid.Add($"T{index}");
            }
            for (int index = 1; index <= info.HumidityPointCount; index++)
            {
                if (!channels.TryGetValue((ChannelRole.Humidity, index), out InspectionChannelData? item) || !item.IsValid)
                    invalid.Add($"H{index}");
            }

            List<string> row = new()
            {
                sessionSequence.ToString(CultureInfo.InvariantCulture),
                snapshot.Sequence.ToString(CultureInfo.InvariantCulture),
                snapshot.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                (info.TemperaturePointCount + info.HumidityPointCount - invalid.Count).ToString(CultureInfo.InvariantCulture),
                invalid.Count.ToString(CultureInfo.InvariantCulture),
                string.Join(";", invalid)
            };
            for (int index = 1; index <= info.TemperaturePointCount; index++)
                row.Add(channels.TryGetValue((ChannelRole.PrimaryTemperature, index), out InspectionChannelData? item) && item.IsValid ? FormatNumber(item.Value) : string.Empty);
            for (int index = 1; index <= info.HumidityPointCount; index++)
                row.Add(channels.TryGetValue((ChannelRole.Humidity, index), out InspectionChannelData? item) && item.IsValid ? FormatNumber(item.Value) : string.Empty);
            return row.ToArray();
        }

        /// <summary>生成实时原始通道文件表头，字段与正式校准原始通道记录保持一致。</summary>
        private static string[] BuildRawChannelHeader() => new[]
        {
            "会话序号", "采集序号", "采集时间", "通道类型", "通道号", "测量值", "单位", "修正前原始值",
            "证书修正值", "是否已修正", "数据是否有效", "数据状态", "状态说明", "原始HEX",
            "寄存器地址1", "寄存器地址2", "寄存器值1", "寄存器值2"
        };

        /// <summary>为一组实时快照输出每个要求通道；设备未返回的通道仍写出 Missing 行。</summary>
        private static IEnumerable<string[]> BuildRawChannelRows(
            int sessionSequence,
            MeasurementSnapshot snapshot,
            IEnumerable<InspectionChannelData> selected,
            RealtimeMeasurementSessionInfo info)
        {
            Dictionary<(ChannelRole Role, int Channel), InspectionChannelData> channels = selected
                .GroupBy(item => (item.Role, item.Channel))
                .ToDictionary(group => group.Key, group => group.First());
            List<string[]> rows = new();
            for (int index = 1; index <= info.TemperaturePointCount; index++)
            {
                channels.TryGetValue((ChannelRole.PrimaryTemperature, index), out InspectionChannelData? item);
                rows.Add(BuildRawChannelRow(sessionSequence, snapshot, "温度", index, item));
            }
            for (int index = 1; index <= info.HumidityPointCount; index++)
            {
                channels.TryGetValue((ChannelRole.Humidity, index), out InspectionChannelData? item);
                rows.Add(BuildRawChannelRow(sessionSequence, snapshot, "湿度", index, item));
            }
            return rows;
        }

        /// <summary>把一个实时通道转换为可追溯 CSV 行；缺失通道保留通道号和 Missing 原因。</summary>
        private static string[] BuildRawChannelRow(
            int sessionSequence,
            MeasurementSnapshot snapshot,
            string type,
            int channel,
            InspectionChannelData? item)
        {
            if (item == null)
            {
                return new[]
                {
                    sessionSequence.ToString(CultureInfo.InvariantCulture), snapshot.Sequence.ToString(CultureInfo.InvariantCulture),
                    snapshot.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"), type, channel.ToString(CultureInfo.InvariantCulture),
                    string.Empty, type == "温度" ? "℃" : "%RH", string.Empty, string.Empty, "否", "否", "Missing",
                    "实时测量要求通道未返回", string.Empty, string.Empty, string.Empty, string.Empty, string.Empty
                };
            }

            return new[]
            {
                sessionSequence.ToString(CultureInfo.InvariantCulture), snapshot.Sequence.ToString(CultureInfo.InvariantCulture),
                snapshot.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"), type, item.Channel.ToString(CultureInfo.InvariantCulture),
                FormatNumber(item.Value), item.Unit, FormatNumber(item.RawValue), FormatNumber(item.CorrectionValue),
                item.HasAppliedCorrection ? "是" : "否", item.IsValid ? "是" : "否", item.DataStatus.ToString(), item.Status,
                item.RawHex, $"0x{item.RegisterAddress1:X4}", $"0x{item.RegisterAddress2:X4}",
                $"0x{item.Register1:X4}", $"0x{item.Register2:X4}"
            };
        }

        private void TryWriteSummaryAfterFailure()
        {
            try { if (_currentSessionDirectory != null && _sessionInfo != null) WriteSummary(); }
            catch { }
        }

        /// <summary>先写临时文件再替换目标文件，避免摘要或表头只写入一半。</summary>
        private static void WriteCsvAtomic(string path, IEnumerable<string[]> rows)
        {
            string content = string.Join(Environment.NewLine, rows.Select(FormatCsvRow)) + Environment.NewLine;
            string temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
            File.WriteAllText(temporaryPath, content, Utf8WithBom);
            try { File.Move(temporaryPath, path, overwrite: true); }
            finally { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
        }

        /// <summary>将实时数据追加到已有 CSV，追加内容不重复写 UTF-8 BOM。</summary>
        private static void AppendCsvRows(string path, IEnumerable<string[]> rows)
        {
            string content = string.Join(Environment.NewLine, rows.Select(FormatCsvRow));
            if (content.Length == 0) return;
            File.AppendAllText(path, content + Environment.NewLine, Utf8WithoutBom);
        }

        private static string FormatCsvRow(IEnumerable<string> values) =>
            string.Join(",", values.Select(value => $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\""));
        private static string FormatNumber(double value) => value.ToString("0.############", CultureInfo.InvariantCulture);
        private static string FormatDateTime(DateTime? value) => value?.ToString("yyyy-MM-dd HH:mm:ss.fff") ?? string.Empty;

        /// <summary>移除设备名称中不能出现在 Windows 文件夹名里的字符。</summary>
        private static string MakeSafeFileName(string value)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            string safe = new(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
            return string.IsNullOrWhiteSpace(safe) ? "设备联调" : safe;
        }

    }
}
