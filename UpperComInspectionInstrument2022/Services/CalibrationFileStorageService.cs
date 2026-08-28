using System.Globalization;
using System.IO;
using System.Text;
using UpperComInspectionInstrument2022.Models;

namespace UpperComInspectionInstrument2022.Services
{
    /// <summary>
    /// 一次校准作业对应一个普通文件夹，所有业务数据均保存为带 UTF-8 BOM 的 CSV。
    /// 不使用数据库或不可见的二进制索引，确保操作人员可直接用 Excel/WPS 查看和备份。
    /// </summary>
    public sealed class CalibrationFileStorageService
    {
        private const string SummaryFileName = "作业摘要.csv";
        private const string TaskFileName = "任务信息.csv";
        private const string SampleFileName = "正式采样.csv";
        private const string RawChannelFileName = "正式采样原始通道.csv";
        private const string ResultFileName = "校准结果.csv";
        private const string UncertaintyFileName = "不确定度分量.csv";
        private static readonly UTF8Encoding Utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);
        private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);
        private readonly object _syncRoot = new();
        private string? _currentJobDirectory;
        private string _currentJobId = string.Empty;
        private DateTime? _startedAt;
        private DateTime? _finishedAt;
        private string _status = string.Empty;
        private string _statusMessage = string.Empty;
        private int _sampleCount;

        /// <summary>应用内共享的默认存储服务。</summary>
        public static CalibrationFileStorageService Default { get; } = new();

        /// <summary>
        /// 创建文件存储服务。未指定根目录时，数据保存到用户“文档\温湿度校准数据”。
        /// </summary>
        public CalibrationFileStorageService(string? dataRootPath = null)
        {
            DataRootPath = string.IsNullOrWhiteSpace(dataRootPath)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "温湿度校准数据")
                : Path.GetFullPath(dataRootPath);
        }

        /// <summary>所有校准作业文件夹所在的根目录。</summary>
        public string DataRootPath { get; }
        /// <summary>当前正式校准的作业目录；尚未开始作业时为空。</summary>
        public string? CurrentJobDirectory
        {
            get { lock (_syncRoot) return _currentJobDirectory; }
        }

        /// <summary>当前是否存在状态为“采样中”的本地作业。</summary>
        public bool HasActiveJob
        {
            get { lock (_syncRoot) return _currentJobDirectory != null && _status == "采样中"; }
        }

        /// <summary>
        /// 为一轮正式校准建立独立目录，并写入任务快照及带表头的样本文件。
        /// </summary>
        public bool TryBeginJob(out string error)
        {
            lock (_syncRoot)
            {
                if (_currentJobDirectory != null && _status == "采样中")
                {
                    error = "当前已有正在采样的本地作业，请先停止或完成该作业。";
                    return false;
                }

                string jobId = $"JOB-{DateTime.Now:yyyyMMdd-HHmmss-fff}";
                string device = MakeSafeFileName(string.IsNullOrWhiteSpace(CalibrationTaskContext.EquipmentName)
                    ? "未命名设备"
                    : CalibrationTaskContext.EquipmentName.Trim());
                string directory = Path.Combine(DataRootPath, $"{jobId}_{device}");

                try
                {
                    Directory.CreateDirectory(directory);
                    _currentJobDirectory = directory;
                    _currentJobId = jobId;
                    _startedAt = DateTime.Now;
                    _finishedAt = null;
                    _status = "采样中";
                    _statusMessage = "正式校准已启动";
                    _sampleCount = 0;

                    WriteCsvAtomic(Path.Combine(directory, TaskFileName), BuildTaskRows());
                    WriteCsvAtomic(Path.Combine(directory, SampleFileName), new[] { BuildSampleHeader() });
                    WriteCsvAtomic(Path.Combine(directory, RawChannelFileName), new[] { BuildRawChannelHeader() });
                    WriteSummary();
                    error = string.Empty;
                    return true;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
                {
                    _status = "保存异常";
                    _statusMessage = "建立本地作业失败：" + ex.Message;
                    TryWriteSummaryAfterFailure();
                    error = $"无法建立本地校准作业目录：{ex.Message}\n目标位置：{directory}";
                    _currentJobDirectory = null;
                    return false;
                }
            }
        }

        /// <summary>
        /// 将一组正式样本同时追加到便于查看的测点矩阵 CSV 和可追溯的原始通道 CSV。
        /// </summary>
        public bool TryAppendSample(CalibrationSampleRecord record, out string error)
        {
            ArgumentNullException.ThrowIfNull(record);
            lock (_syncRoot)
            {
                if (_currentJobDirectory == null || _status != "采样中")
                {
                    error = "当前没有处于采样状态的本地作业。";
                    return false;
                }

                try
                {
                    List<InspectionChannelData> selected = MeasurementChannelSelectionService.SelectRequired(
                        record.Snapshot.Channels,
                        CalibrationTaskContext.TemperaturePointCount,
                        CalibrationTaskContext.HumidityPointCount);
                    AppendCsvRows(Path.Combine(_currentJobDirectory, SampleFileName), new[] { BuildSampleRow(record, selected) });
                    AppendCsvRows(Path.Combine(_currentJobDirectory, RawChannelFileName), BuildRawChannelRows(record, selected));
                    _sampleCount = record.SampleNumber;
                    _statusMessage = $"已保存正式样本 {_sampleCount}/{CalibrationTaskContext.PlannedCount} 组";
                    WriteSummary();
                    error = string.Empty;
                    return true;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
                {
                    SetFailureStatus($"第 {record.SampleNumber} 组正式样本保存失败：{ex.Message}");
                    error = $"正式样本未能完整写入本地 CSV：{ex.Message}\n作业目录：{_currentJobDirectory}";
                    return false;
                }
            }
        }

        /// <summary>
        /// 保存有效计算结果并把作业状态改为“已完成”。无效结果不会覆盖已有样本。
        /// </summary>
        public bool TryCompleteJob(CalibrationResultSummary result, out string error)
        {
            ArgumentNullException.ThrowIfNull(result);
            lock (_syncRoot)
            {
                if (_currentJobDirectory == null || _status != "采样中")
                {
                    error = "当前没有可完成的本地校准作业。";
                    return false;
                }
                if (!result.IsValid)
                {
                    SetFailureStatus("结果计算未通过：" + result.Message);
                    error = result.Message;
                    return false;
                }

                try
                {
                    WriteCsvAtomic(Path.Combine(_currentJobDirectory, ResultFileName), BuildResultRows(result));
                    WriteCsvAtomic(Path.Combine(_currentJobDirectory, UncertaintyFileName), BuildUncertaintyRows(result));
                    _finishedAt = DateTime.Now;
                    _status = "已完成";
                    _statusMessage = "正式样本和校准结果已完整保存";
                    WriteSummary();
                    error = string.Empty;
                    return true;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
                {
                    SetFailureStatus("校准结果保存失败：" + ex.Message);
                    error = $"校准结果未能写入本地 CSV：{ex.Message}\n作业目录：{_currentJobDirectory}";
                    return false;
                }
            }
        }

        /// <summary>
        /// 将尚在采样的作业标记为“已中断”，保留已经写入的样本供人工追溯。
        /// </summary>
        public bool TryMarkInterrupted(string reason, out string error)
        {
            lock (_syncRoot)
            {
                if (_currentJobDirectory == null || _status != "采样中")
                {
                    error = string.Empty;
                    return true;
                }

                _finishedAt = DateTime.Now;
                _status = "已中断";
                _statusMessage = string.IsNullOrWhiteSpace(reason) ? "正式校准被中断" : reason.Trim();
                try
                {
                    WriteSummary();
                    error = string.Empty;
                    return true;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
                {
                    _status = "保存异常";
                    error = $"无法更新作业中断状态：{ex.Message}";
                    return false;
                }
            }
        }

        /// <summary>
        /// 扫描数据根目录中的作业摘要，并按关键字、规范和状态筛选历史记录。
        /// 单个损坏目录会被跳过，不影响其他作业显示。
        /// </summary>
        public IReadOnlyList<CalibrationArchiveSummary> LoadHistory(string keyword = "", string standard = "", string status = "")
        {
            if (!Directory.Exists(DataRootPath)) return Array.Empty<CalibrationArchiveSummary>();
            string normalizedKeyword = keyword.Trim();
            List<CalibrationArchiveSummary> records = new();
            foreach (string directory in Directory.EnumerateDirectories(DataRootPath))
            {
                try
                {
                    string summaryPath = Path.Combine(directory, SummaryFileName);
                    if (!File.Exists(summaryPath)) continue;
                    List<string[]> rows = ParseCsv(File.ReadAllText(summaryPath, Encoding.UTF8));
                    if (rows.Count < 2) continue;
                    Dictionary<string, string> values = rows[0]
                        .Select((header, index) => new { header, index })
                        .ToDictionary(item => item.header, item => item.index < rows[1].Length ? rows[1][item.index] : string.Empty);
                    CalibrationArchiveSummary record = CreateArchiveSummary(values, directory);
                    if (!string.IsNullOrWhiteSpace(normalizedKeyword) &&
                        !new[] { record.JobId, record.Device, record.EquipmentSerialNumber, record.CertificateNumber, record.CustomerName }
                            .Any(value => value.Contains(normalizedKeyword, StringComparison.OrdinalIgnoreCase)))
                        continue;
                    if (!string.IsNullOrWhiteSpace(standard) && record.Standard != standard) continue;
                    if (!string.IsNullOrWhiteSpace(status) && record.Status != status) continue;
                    records.Add(record);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or FormatException)
                {
                    // 单个手工修改或损坏的目录不影响其他历史作业的浏览。
                }
            }

            return records.OrderByDescending(item => item.StartedAt).ToList();
        }

        /// <summary>把“作业摘要.csv”的表头字典转换为历史列表行模型。</summary>
        private static CalibrationArchiveSummary CreateArchiveSummary(IReadOnlyDictionary<string, string> values, string directory)
        {
            DateTime.TryParse(GetValue(values, "开始时间"), CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime startedAt);
            string sampleCount = GetValue(values, "已存样本数");
            string plannedCount = GetValue(values, "计划样本数");
            return new CalibrationArchiveSummary
            {
                JobId = GetValue(values, "任务编号"),
                StartedAt = startedAt,
                TaskTime = startedAt == default ? "-" : startedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                Standard = GetValue(values, "校准规范"),
                Type = GetValue(values, "校准类型"),
                Device = GetValue(values, "被校设备"),
                CustomerName = GetValue(values, "委托单位"),
                EquipmentSerialNumber = GetValue(values, "设备编号"),
                CertificateNumber = GetValue(values, "标准器证书编号"),
                Status = GetValue(values, "状态"),
                StatusMessage = GetValue(values, "状态说明"),
                SampleProgress = $"{sampleCount}/{plannedCount}",
                DirectoryPath = directory,
                SampleFilePath = Path.Combine(directory, SampleFileName),
                ResultFilePath = Path.Combine(directory, ResultFileName),
                UncertaintyFilePath = Path.Combine(directory, UncertaintyFileName)
            };
        }

        /// <summary>以原子替换方式重写当前作业摘要，使状态和样本进度始终可恢复。</summary>
        private void WriteSummary()
        {
            if (_currentJobDirectory == null) throw new InvalidOperationException("本地作业目录尚未建立。");
            WriteCsvAtomic(Path.Combine(_currentJobDirectory, SummaryFileName), new[]
            {
                new[] { "数据格式版本", "任务编号", "开始时间", "结束时间", "校准规范", "校准类型", "委托单位", "被校设备", "型号规格", "设备编号", "标准器证书编号", "设定温度(℃)", "设定湿度(%RH)", "温度测点数", "湿度测点数", "计划样本数", "已存样本数", "状态", "状态说明", "作业目录" },
                new[]
                {
                    "1.1",
                    _currentJobId,
                    FormatDateTime(_startedAt),
                    FormatDateTime(_finishedAt),
                    GetStandardName(),
                    GetCalibrationTypeName(),
                    CalibrationTaskContext.CustomerName,
                    CalibrationTaskContext.EquipmentName,
                    CalibrationTaskContext.ModelSpecification,
                    CalibrationTaskContext.EquipmentSerialNumber,
                    CalibrationTaskContext.ReferencedCertificateNumber,
                    FormatNumber(CalibrationTaskContext.SetTemperature),
                    FormatNumber(CalibrationTaskContext.SetHumidity),
                    CalibrationTaskContext.TemperaturePointCount.ToString(CultureInfo.InvariantCulture),
                    CalibrationTaskContext.HumidityPointCount.ToString(CultureInfo.InvariantCulture),
                    CalibrationTaskContext.PlannedCount.ToString(CultureInfo.InvariantCulture),
                    _sampleCount.ToString(CultureInfo.InvariantCulture),
                    _status,
                    _statusMessage,
                    _currentJobDirectory
                }
            });
        }

        /// <summary>将当前任务及其标准器快照展开为“字段—值”两列 CSV 行。</summary>
        private static IEnumerable<string[]> BuildTaskRows()
        {
            return new[]
            {
                new[] { "字段", "值" },
                Pair("校准规范", GetStandardName()),
                Pair("校准类型", GetCalibrationTypeName()),
                Pair("被校设备名称", CalibrationTaskContext.EquipmentName),
                Pair("制造单位", CalibrationTaskContext.Manufacturer),
                Pair("型号规格", CalibrationTaskContext.ModelSpecification),
                Pair("设备编号", CalibrationTaskContext.EquipmentSerialNumber),
                Pair("测量范围", CalibrationTaskContext.MeasurementRange),
                Pair("校准地点", CalibrationTaskContext.CalibrationLocation),
                Pair("委托单位", CalibrationTaskContext.CustomerName),
                Pair("委托单位地址", CalibrationTaskContext.CustomerAddress),
                Pair("校准日期", CalibrationTaskContext.CalibrationDate.ToString("yyyy-MM-dd")),
                Pair("校准员", CalibrationTaskContext.Calibrator),
                Pair("核验员", CalibrationTaskContext.Verifier),
                Pair("设定温度(℃)", FormatNumber(CalibrationTaskContext.SetTemperature)),
                Pair("设定湿度(%RH)", FormatNumber(CalibrationTaskContext.SetHumidity)),
                Pair("温度测点数", CalibrationTaskContext.TemperaturePointCount.ToString(CultureInfo.InvariantCulture)),
                Pair("湿度测点数", CalibrationTaskContext.HumidityPointCount.ToString(CultureInfo.InvariantCulture)),
                Pair("温度中心点", CalibrationTaskContext.TemperatureCenterPoint.ToString(CultureInfo.InvariantCulture)),
                Pair("湿度中心点", CalibrationTaskContext.HumidityCenterPoint.ToString(CultureInfo.InvariantCulture)),
                Pair("传感器类型", CalibrationTaskContext.SensorTypeCode),
                Pair("计划样本数", CalibrationTaskContext.PlannedCount.ToString(CultureInfo.InvariantCulture)),
                Pair("采样间隔(s)", CalibrationTaskContext.SamplingIntervalSeconds.ToString(CultureInfo.InvariantCulture)),
                Pair("稳定等待(min)", CalibrationTaskContext.StableWaitMinutes.ToString(CultureInfo.InvariantCulture)),
                Pair("工作区长度(mm)", FormatNumber(CalibrationTaskContext.WorkZoneLengthMm)),
                Pair("工作区宽度(mm)", FormatNumber(CalibrationTaskContext.WorkZoneWidthMm)),
                Pair("工作区高度(mm)", FormatNumber(CalibrationTaskContext.WorkZoneHeightMm)),
                Pair("负载说明", CalibrationTaskContext.LoadDescription),
                Pair("布点说明", CalibrationTaskContext.PointLayoutDescription),
                Pair("偏离说明", CalibrationTaskContext.DeviationDescription),
                Pair("环境温度(℃)", FormatNumber(CalibrationTaskContext.AmbientTemperature)),
                Pair("环境湿度(%RH)", FormatNumber(CalibrationTaskContext.AmbientHumidity)),
                Pair("环境气压(kPa)", FormatNumber(CalibrationTaskContext.AmbientPressure)),
                Pair("标准器名称", CalibrationTaskContext.ReferencedStandardName),
                Pair("标准器证书编号", CalibrationTaskContext.ReferencedCertificateNumber),
                Pair("标准器有效期", CalibrationTaskContext.ReferencedValidityDate?.ToString("yyyy-MM-dd") ?? string.Empty),
                Pair("标准器型号", CalibrationTaskContext.ReferencedModel),
                Pair("标准器编号", CalibrationTaskContext.ReferencedSerialNumber),
                Pair("标准器溯源机构", CalibrationTaskContext.ReferencedOrganization),
                Pair("标准器温度范围", CalibrationTaskContext.ReferencedTemperatureRange),
                Pair("标准器湿度范围", CalibrationTaskContext.ReferencedHumidityRange),
                Pair("标准器温度分辨力", FormatNumber(CalibrationTaskContext.ReferencedTemperatureResolution)),
                Pair("标准器湿度分辨力", FormatNumber(CalibrationTaskContext.ReferencedHumidityResolution)),
                Pair("标准器准确度", CalibrationTaskContext.ReferencedAccuracySpecification),
                Pair("标准器温度修正值", CalibrationTaskContext.ReferencedTemperatureCorrections),
                Pair("标准器湿度修正值", CalibrationTaskContext.ReferencedHumidityCorrections),
                Pair("标准器温度修正值最大变化", FormatNumber(CalibrationTaskContext.ReferencedTemperatureStabilityChange)),
                Pair("标准器湿度修正值最大变化", FormatNumber(CalibrationTaskContext.ReferencedHumidityStabilityChange)),
                Pair("标准器温度不确定度", FormatNumber(CalibrationTaskContext.ReferencedTemperatureUncertainty)),
                Pair("标准器温度包含因子", FormatNumber(CalibrationTaskContext.ReferencedTemperatureCoverage)),
                Pair("标准器湿度不确定度", FormatNumber(CalibrationTaskContext.ReferencedHumidityUncertainty)),
                Pair("标准器湿度包含因子", FormatNumber(CalibrationTaskContext.ReferencedHumidityCoverage)),
                Pair("测温仪器级别", FormatNumber(CalibrationTaskContext.ReferencedMeasuringInstrumentClass)),
                Pair("热电偶等级", CalibrationTaskContext.ReferencedThermocoupleGrade)
            };
        }

        /// <summary>根据当前温湿度测点数动态生成正式采样矩阵表头。</summary>
        private static string[] BuildSampleHeader()
        {
            List<string> header = new() { "样本序号", "采样时间", "有效通道数", "异常通道数", "异常通道", "被校设备温度示值(℃)", "被校设备湿度示值(%RH)" };
            header.AddRange(Enumerable.Range(1, CalibrationTaskContext.TemperaturePointCount).Select(index => $"温度{index}(℃)"));
            header.AddRange(Enumerable.Range(1, CalibrationTaskContext.HumidityPointCount).Select(index => $"湿度{index}(%RH)"));
            return header.ToArray();
        }

        /// <summary>按固定测点顺序把一组正式样本展开为 CSV 行，缺失通道保留为空并记录异常编号。</summary>
        private static string[] BuildSampleRow(CalibrationSampleRecord record, IReadOnlyList<InspectionChannelData> selected)
        {
            Dictionary<(ChannelRole Role, int Channel), InspectionChannelData> channels = selected
                .GroupBy(item => (item.Role, item.Channel))
                .ToDictionary(group => group.Key, group => group.First());
            List<string> invalid = new();
            for (int index = 1; index <= CalibrationTaskContext.TemperaturePointCount; index++)
            {
                if (!channels.TryGetValue((ChannelRole.PrimaryTemperature, index), out InspectionChannelData? item) || !item.IsValid)
                    invalid.Add($"T{index}");
            }
            for (int index = 1; index <= CalibrationTaskContext.HumidityPointCount; index++)
            {
                if (!channels.TryGetValue((ChannelRole.Humidity, index), out InspectionChannelData? item) || !item.IsValid)
                    invalid.Add($"H{index}");
            }
            List<string> row = new()
            {
                record.SampleNumber.ToString(CultureInfo.InvariantCulture),
                record.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                (CalibrationTaskContext.TemperaturePointCount + CalibrationTaskContext.HumidityPointCount - invalid.Count).ToString(CultureInfo.InvariantCulture),
                invalid.Count.ToString(CultureInfo.InvariantCulture),
                string.Join(";", invalid),
                FormatNumber(record.DutDisplayTemperature),
                FormatNumber(record.DutDisplayHumidity)
            };
            for (int index = 1; index <= CalibrationTaskContext.TemperaturePointCount; index++)
                row.Add(channels.TryGetValue((ChannelRole.PrimaryTemperature, index), out InspectionChannelData? item) && item.IsValid ? FormatNumber(item.Value) : string.Empty);
            for (int index = 1; index <= CalibrationTaskContext.HumidityPointCount; index++)
                row.Add(channels.TryGetValue((ChannelRole.Humidity, index), out InspectionChannelData? item) && item.IsValid ? FormatNumber(item.Value) : string.Empty);
            return row.ToArray();
        }

        /// <summary>生成包含寄存器、HEX 和证书修正信息的原始通道表头。</summary>
        private static string[] BuildRawChannelHeader() => new[]
        {
            "样本序号", "采样时间", "通道类型", "通道号", "测量值", "单位", "修正前原始值", "证书修正值", "是否已修正", "数据是否有效", "数据状态", "状态说明", "原始HEX", "寄存器地址1", "寄存器地址2", "寄存器值1", "寄存器值2"
        };

        /// <summary>为一组正式样本生成每通道一行的追溯数据。</summary>
        private static IEnumerable<string[]> BuildRawChannelRows(CalibrationSampleRecord record, IEnumerable<InspectionChannelData> selected)
        {
            Dictionary<(ChannelRole Role, int Channel), InspectionChannelData> channels = selected
                .GroupBy(item => (item.Role, item.Channel))
                .ToDictionary(group => group.Key, group => group.First());
            List<string[]> rows = new();
            for (int index = 1; index <= CalibrationTaskContext.TemperaturePointCount; index++)
            {
                channels.TryGetValue((ChannelRole.PrimaryTemperature, index), out InspectionChannelData? item);
                rows.Add(BuildRawChannelRow(record, "温度", index, item));
            }
            for (int index = 1; index <= CalibrationTaskContext.HumidityPointCount; index++)
            {
                channels.TryGetValue((ChannelRole.Humidity, index), out InspectionChannelData? item);
                rows.Add(BuildRawChannelRow(record, "湿度", index, item));
            }
            return rows;
        }

        /// <summary>生成单个要求通道的原始记录；设备未返回该通道时仍输出 Missing 行。</summary>
        private static string[] BuildRawChannelRow(CalibrationSampleRecord record, string type, int channel, InspectionChannelData? item)
        {
            if (item == null)
            {
                return new[]
                {
                    record.SampleNumber.ToString(CultureInfo.InvariantCulture),
                    record.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                    type,
                    channel.ToString(CultureInfo.InvariantCulture),
                    string.Empty,
                    type == "温度" ? "℃" : "%RH",
                    string.Empty,
                    string.Empty,
                    "否",
                    "否",
                    "Missing",
                    "任务要求通道未返回",
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty
                };
            }

            return new[]
            {
                record.SampleNumber.ToString(CultureInfo.InvariantCulture),
                record.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                type,
                item.Channel.ToString(CultureInfo.InvariantCulture),
                FormatNumber(item.Value),
                item.Unit,
                FormatNumber(item.RawValue),
                FormatNumber(item.CorrectionValue),
                item.HasAppliedCorrection ? "是" : "否",
                item.IsValid ? "是" : "否",
                item.DataStatus.ToString(),
                item.Status,
                item.RawHex,
                $"0x{item.RegisterAddress1:X4}",
                $"0x{item.RegisterAddress2:X4}",
                $"0x{item.Register1:X4}",
                $"0x{item.Register2:X4}"
            };
        }

        /// <summary>根据任务规范选择对应指标，并生成校准结果 CSV 行。</summary>
        private static IEnumerable<string[]> BuildResultRows(CalibrationResultSummary result)
        {
            List<string[]> rows = new() { new[] { "指标", "数值", "单位", "说明" } };
            if (CalibrationTaskContext.StandardIndex == 1)
            {
                rows.Add(ResultRow("炉温均匀度上偏差", result.FurnaceUniformityUpper, "℃", "各点实际温度相对中心监控点"));
                rows.Add(ResultRow("炉温均匀度下偏差", result.FurnaceUniformityLower, "℃", "各点实际温度相对中心监控点"));
                rows.Add(ResultRow("炉温稳定度上偏差", result.FurnaceStabilityUpper, "℃", "中心点最大值相对平均值"));
                rows.Add(ResultRow("炉温稳定度下偏差", result.FurnaceStabilityLower, "℃", "中心点最小值相对平均值"));
                rows.Add(ResultRow("炉温偏差上偏差", result.FurnaceDeviationUpper, "℃", "最高实际温度相对标称温度"));
                rows.Add(ResultRow("炉温偏差下偏差", result.FurnaceDeviationLower, "℃", "最低实际温度相对标称温度"));
                rows.Add(ResultRow("炉内最大温差", result.FurnaceMaximumDifference, "℃", "各测量周期最大温差中的最大值"));
                rows.Add(ResultRow("炉温均匀度上偏差扩展不确定度", result.FurnaceUniformityUpperUncertainty, "℃", "按任务快照中的不确定度分量计算"));
                rows.Add(ResultRow("炉温均匀度下偏差扩展不确定度", result.FurnaceUniformityLowerUncertainty, "℃", "按任务快照中的不确定度分量计算"));
            }
            else
            {
                rows.Add(ResultRow("温度上偏差", result.TemperatureUpperDeviation, "℃", "最高测量值相对设定值"));
                rows.Add(ResultRow("温度下偏差", result.TemperatureLowerDeviation, "℃", "最低测量值相对设定值"));
                rows.Add(ResultRow("温度均匀度", result.TemperatureUniformity, "℃", "各组最大与最小温差的算术平均"));
                rows.Add(ResultRow("温度波动度", result.TemperatureFluctuation, "℃", "各测点极差一半的最大值"));
                rows.Add(ResultRow("温度扩展不确定度", result.TemperatureExpandedUncertainty, "℃", "按任务快照中的不确定度分量计算"));
                if (CalibrationTaskContext.IncludesHumidity)
                {
                    rows.Add(ResultRow("湿度上偏差", result.HumidityUpperDeviation, "%RH", "最高测量值相对设定值"));
                    rows.Add(ResultRow("湿度下偏差", result.HumidityLowerDeviation, "%RH", "最低测量值相对设定值"));
                    rows.Add(ResultRow("湿度均匀度", result.HumidityUniformity, "%RH", "各组最大与最小湿度差的算术平均"));
                    rows.Add(ResultRow("湿度波动度", result.HumidityFluctuation, "%RH", "各湿度测点极差一半的最大值"));
                    rows.Add(ResultRow("湿度扩展不确定度", result.HumidityExpandedUncertainty, "%RH", "按任务快照中的不确定度分量计算"));
                }
            }
            return rows;
        }

        /// <summary>
        /// 将每个结果项目的不确定度预算展开为普通 CSV：每行一个分量，并重复写入该预算的 uc、k 和 U，便于筛选和独立复核。
        /// </summary>
        private static IEnumerable<string[]> BuildUncertaintyRows(CalibrationResultSummary result)
        {
            List<string[]> rows = new()
            {
                new[]
                {
                    "结果项目", "评定点", "分量序号", "符号", "不确定度来源", "类别", "分布",
                    "输入量", "单位", "除数", "除数表达式", "标准不确定度ui", "灵敏系数ci", "贡献ci×ui",
                    "合成标准不确定度uc", "包含因子k", "扩展不确定度U", "分量依据", "合成依据"
                }
            };
            foreach (UncertaintyBudgetSummary budget in result.UncertaintyBudgets)
            {
                for (int index = 0; index < budget.Components.Count; index++)
                {
                    UncertaintyComponentDetail component = budget.Components[index];
                    rows.Add(new[]
                    {
                        budget.ResultItem,
                        budget.EvaluationPoint,
                        (index + 1).ToString(CultureInfo.InvariantCulture),
                        component.Symbol,
                        component.Source,
                        component.Category,
                        component.Distribution,
                        FormatNumber(component.InputValue),
                        component.Unit,
                        FormatNumber(component.Divisor),
                        component.DivisorExpression,
                        FormatNumber(component.StandardUncertainty),
                        FormatNumber(component.SensitivityCoefficient),
                        FormatNumber(component.Contribution),
                        FormatNumber(budget.CombinedStandardUncertainty),
                        FormatNumber(budget.CoverageFactor),
                        FormatNumber(budget.ExpandedUncertainty),
                        component.Basis,
                        budget.Basis
                    });
                }
            }
            return rows;
        }

        /// <summary>记录不可恢复的文件保存异常，并尽力把异常状态写入作业摘要。</summary>
        private void SetFailureStatus(string message)
        {
            _finishedAt = DateTime.Now;
            _status = "保存异常";
            _statusMessage = message;
            TryWriteSummaryAfterFailure();
        }

        /// <summary>异常处理阶段尽力更新摘要；二次写入失败时不再抛出以免掩盖原始错误。</summary>
        private void TryWriteSummaryAfterFailure()
        {
            try { if (_currentJobDirectory != null) WriteSummary(); }
            catch { }
        }

        /// <summary>先写临时文件再原子替换目标文件，避免程序中断留下半个摘要或结果文件。</summary>
        private static void WriteCsvAtomic(string path, IEnumerable<string[]> rows)
        {
            string content = string.Join(Environment.NewLine, rows.Select(FormatCsvRow)) + Environment.NewLine;
            string temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
            File.WriteAllText(temporaryPath, content, Utf8WithBom);
            try
            {
                File.Move(temporaryPath, path, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
        }

        /// <summary>将若干 CSV 行追加到已有文件；追加内容不重复写 UTF-8 BOM。</summary>
        private static void AppendCsvRows(string path, IEnumerable<string[]> rows)
        {
            string content = string.Join(Environment.NewLine, rows.Select(FormatCsvRow));
            if (content.Length == 0) return;
            File.AppendAllText(path, content + Environment.NewLine, Utf8WithoutBom);
        }

        /// <summary>按 RFC 4180 常用规则引用每个字段，并把字段内双引号写成两个双引号。</summary>
        private static string FormatCsvRow(IEnumerable<string> values) =>
            string.Join(",", values.Select(value => $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\""));

        /// <summary>解析本系统生成的带引号 CSV，并正确处理字段内逗号、换行和转义双引号。</summary>
        private static List<string[]> ParseCsv(string content)
        {
            List<string[]> rows = new();
            List<string> row = new();
            StringBuilder field = new();
            bool quoted = false;
            for (int index = 0; index < content.Length; index++)
            {
                char current = content[index];
                if (current == '"')
                {
                    if (quoted && index + 1 < content.Length && content[index + 1] == '"')
                    {
                        field.Append('"');
                        index++;
                    }
                    else quoted = !quoted;
                }
                else if (current == ',' && !quoted)
                {
                    row.Add(field.ToString());
                    field.Clear();
                }
                else if ((current == '\r' || current == '\n') && !quoted)
                {
                    if (current == '\r' && index + 1 < content.Length && content[index + 1] == '\n') index++;
                    row.Add(field.ToString());
                    field.Clear();
                    if (row.Count > 1 || row[0].Length > 0) rows.Add(row.ToArray());
                    row.Clear();
                }
                else field.Append(current);
            }
            if (field.Length > 0 || row.Count > 0)
            {
                row.Add(field.ToString());
                rows.Add(row.ToArray());
            }
            return rows;
        }

        /// <summary>读取并解析归档 CSV，供历史查询和 Excel 报告服务复用。</summary>
        internal static List<string[]> ReadCsvFile(string path) => ParseCsv(File.ReadAllText(path, Encoding.UTF8));

        /// <summary>移除 Windows 文件名非法字符，并限制设备名称片段长度。</summary>
        private static string MakeSafeFileName(string value)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            string safe = new(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
            return safe.Length <= 40 ? safe : safe[..40];
        }

        /// <summary>安全读取字典字段；字段缺失时返回空字符串。</summary>
        private static string GetValue(IReadOnlyDictionary<string, string> values, string key) =>
            values.TryGetValue(key, out string? value) ? value : string.Empty;
        /// <summary>取得当前任务使用的规范代号。</summary>
        private static string GetStandardName() => CalibrationTaskContext.StandardIndex == 1 ? "JJF 1376-2012" : "JJF 1101-2019";
        /// <summary>取得适合归档和历史列表显示的校准类型名称。</summary>
        private static string GetCalibrationTypeName() => CalibrationTaskContext.StandardIndex == 1 ? "箱式电阻炉温度" : CalibrationTaskContext.IncludesHumidity ? "温湿度" : "温度";
        /// <summary>把可空时间格式化为不受区域设置影响的文本。</summary>
        private static string FormatDateTime(DateTime? value) => value?.ToString("yyyy-MM-dd HH:mm:ss.fff") ?? string.Empty;
        /// <summary>把可空有限数格式化为 CSV 数值文本。</summary>
        private static string FormatNumber(double? value) => value.HasValue && double.IsFinite(value.Value) ? value.Value.ToString("0.############", CultureInfo.InvariantCulture) : string.Empty;
        /// <summary>把有限数格式化为 CSV 数值文本，非有限数输出空白。</summary>
        private static string FormatNumber(double value) => double.IsFinite(value) ? value.ToString("0.############", CultureInfo.InvariantCulture) : string.Empty;
        /// <summary>创建任务快照中的“字段—值”行。</summary>
        private static string[] Pair(string name, string value) => new[] { name, value ?? string.Empty };
        /// <summary>创建结果文件中的“指标—数值—单位—说明”行。</summary>
        private static string[] ResultRow(string name, double value, string unit, string note) => new[] { name, FormatNumber(value), unit, note };
    }

    /// <summary>
    /// 历史记录页面使用的轻量作业摘要，只包含列表展示和打开文件所需信息。
    /// </summary>
    public sealed class CalibrationArchiveSummary
    {
        public string JobId { get; init; } = string.Empty;
        public DateTime StartedAt { get; init; }
        public string TaskTime { get; init; } = string.Empty;
        public string Standard { get; init; } = string.Empty;
        public string Type { get; init; } = string.Empty;
        public string Device { get; init; } = string.Empty;
        public string CustomerName { get; init; } = string.Empty;
        public string EquipmentSerialNumber { get; init; } = string.Empty;
        public string CertificateNumber { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public string StatusMessage { get; init; } = string.Empty;
        public string SampleProgress { get; init; } = string.Empty;
        public string DirectoryPath { get; init; } = string.Empty;
        public string SampleFilePath { get; init; } = string.Empty;
        public string ResultFilePath { get; init; } = string.Empty;
        public string UncertaintyFilePath { get; init; } = string.Empty;
        public string ExcelReportFilePath => Path.Combine(DirectoryPath, "报告", "校准原始记录.xlsx");
        /// <summary>供历史列表直接显示 Excel 是否已经生成。</summary>
        public string ExcelReportStatus => File.Exists(ExcelReportFilePath) ? "已生成" : "未生成";
        /// <summary>该作业生成后的 Word 校准证书路径。</summary>
        public string WordCertificateFilePath => Path.Combine(DirectoryPath, "报告", "校准证书.docx");
        /// <summary>供历史列表直接显示 Word 是否已经生成。</summary>
        public string WordCertificateStatus => File.Exists(WordCertificateFilePath) ? "已生成" : "未生成";
        /// <summary>该作业生成后的 PDF 归档报告路径。</summary>
        public string PdfArchiveFilePath => Path.Combine(DirectoryPath, "报告", "校准归档.pdf");
        /// <summary>供历史列表直接显示 PDF 是否已经生成。</summary>
        public string PdfArchiveStatus => File.Exists(PdfArchiveFilePath) ? "已生成" : "未生成";
    }
}
