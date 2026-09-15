using System.Globalization;
using System.IO;
using System.Text;
using UpperComInspectionInstrument2022.Models;

namespace UpperComInspectionInstrument2022.Services
{
    /// <summary>
    /// 一次校准作业对应一个普通文件夹，业务数据均保存为带 UTF-8 BOM 的 CSV。
    /// 不使用数据库或不可读二进制索引，确保操作人员可直接用 Excel/WPS 查看和备份。
    /// </summary>
    public sealed class CalibrationFileStorageService
    {
        private const string SummaryFileName = "作业摘要.csv";
        private const string TaskFileName = "任务信息.csv";
        private const string SampleFileName = "正式采样.csv";
        private const string ResultFileName = "校准结果.csv";
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

        /// <summary>当前正式作业编号；没有活动或已完成作业时仍保留最近一次编号供界面记录。</summary>
        public string CurrentJobId
        {
            get { lock (_syncRoot) return _currentJobId; }
        }

        /// <summary>
        /// 启动时扫描遗留的“采样中”作业并标记为“已中断”。
        /// 该方法只修改摘要状态，已经落盘的任务和正式样本不会被删除或覆盖。
        /// </summary>
        public int RecoverAbandonedJobs(out string error)
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
                    error = $"无法扫描历史作业：{ex.Message}\n数据目录：{DataRootPath}";
                    return 0;
                }

                foreach (string directory in directories)
                {
                    string summaryPath = Path.Combine(directory, SummaryFileName);
                    if (!File.Exists(summaryPath)) continue;
                    try
                    {
                        List<string[]> rows = ParseCsv(File.ReadAllText(summaryPath, Encoding.UTF8));
                        if (rows.Count < 2) continue;
                        string[] header = rows[0];
                        int statusIndex = Array.IndexOf(header, "状态");
                        int finishedAtIndex = Array.IndexOf(header, "结束时间");
                        int messageIndex = Array.IndexOf(header, "状态说明");
                        if (statusIndex < 0 || finishedAtIndex < 0 || messageIndex < 0) continue;

                        string[] values = rows[1];
                        int requiredLength = Math.Max(statusIndex, Math.Max(finishedAtIndex, messageIndex)) + 1;
                        if (values.Length < requiredLength) Array.Resize(ref values, requiredLength);
                        if (!string.Equals(values[statusIndex], "采样中", StringComparison.Ordinal)) continue;

                        // 1.3 起摘要把日期与时间分列，旧格式仍保留完整日期时间写法。
                        values[finishedAtIndex] = Array.IndexOf(header, "校准日期") >= 0
                            ? FormatSummaryTime(DateTime.Now)
                            : DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
                        values[statusIndex] = "已中断";
                        values[messageIndex] = "检测到程序上次未正常结束，启动时已自动标记为中断；已保存样本继续保留。";
                        rows[1] = values;
                        WriteCsvAtomic(summaryPath, rows);
                        recoveredCount++;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or FormatException)
                    {
                        failures.Add($"{Path.GetFileName(directory)}：{ex.Message}");
                    }
                }

                error = failures.Count == 0 ? string.Empty : "部分遗留作业未能恢复：" + string.Join("；", failures);
                return recoveredCount;
            }
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
        /// 将一组正式样本追加到面向操作人员的测点矩阵 CSV。
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
            DateTime startedAt = ParseSummaryStartTime(values);
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
                ResultFilePath = Path.Combine(directory, ResultFileName)


               
            };
        }

        /// <summary>以原子替换方式重写当前作业摘要，使状态和样本进度始终可恢复。</summary>
        private void WriteSummary()
        {
            if (_currentJobDirectory == null) throw new InvalidOperationException("本地作业目录尚未建立。");
            WriteCsvAtomic(Path.Combine(_currentJobDirectory, SummaryFileName), new[]
            {
                new[] { "数据格式版本", "任务编号", "校准日期", "开始时间", "结束时间", "校准规范", "校准类型", "委托单位", "被校设备", "型号规格", "设备编号", "标准器证书编号", "设定温度(℃)", "设定湿度(%RH)", "温度测点数", "湿度测点数", "计划样本数", "已存样本数", "状态", "状态说明", "作业目录" },
                new[]
                {
                    "1.3",
                    _currentJobId,
                    FormatSummaryDate(_startedAt),
                    FormatSummaryTime(_startedAt),
                    FormatSummaryTime(_finishedAt),
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
                Pair("设备容积分类索引", CalibrationTaskContext.VolumeIndex.ToString(CultureInfo.InvariantCulture)),
                Pair("布点方式索引", CalibrationTaskContext.PointLayoutModeIndex.ToString(CultureInfo.InvariantCulture)),
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
                Pair("外观检查", CalibrationTaskContext.AppearanceCheckIndex switch { 1 => "符合", 2 => "不符合", _ => "待检查" }),
                Pair("炉膛长度(mm)", FormatNumber(CalibrationTaskContext.FurnaceChamberLengthMm)),
                Pair("炉膛宽度(mm)", FormatNumber(CalibrationTaskContext.FurnaceChamberWidthMm)),
                Pair("炉膛高度(mm)", FormatNumber(CalibrationTaskContext.FurnaceChamberHeightMm)),
                Pair("工作区长度(mm)", FormatNumber(CalibrationTaskContext.WorkZoneLengthMm)),
                Pair("工作区宽度(mm)", FormatNumber(CalibrationTaskContext.WorkZoneWidthMm)),
                Pair("工作区高度(mm)", FormatNumber(CalibrationTaskContext.WorkZoneHeightMm)),
                Pair("负载说明", CalibrationTaskContext.LoadDescription),
                Pair("布点说明", CalibrationTaskContext.PointLayoutDescription),
                Pair("偏离说明", CalibrationTaskContext.DeviationDescription),
                Pair("环境温度(℃)", FormatNumber(CalibrationTaskContext.AmbientTemperature)),
                Pair("环境湿度(%RH)", FormatNumber(CalibrationTaskContext.AmbientHumidity)),
                Pair("环境气压(kPa)", FormatNumber(CalibrationTaskContext.AmbientPressure)),
                Pair("实验室名称", CalibrationTaskContext.ReferencedLaboratoryName),
                Pair("实验室地址", CalibrationTaskContext.ReferencedLaboratoryAddress),
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

        /// <summary>根据当前温湿度测点数动态生成正式采样矩阵表头，日期与时分秒分列便于办公软件直接查看。</summary>
        private static string[] BuildSampleHeader()
        {
            List<string> header = new() { "样本序号", "采样日期", "采样时间", "有效通道数", "异常通道数", "异常通道", "被校设备温度示值(℃)", "被校设备湿度示值(%RH)" };
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
                FormatSpreadsheetText(record.Timestamp, "yyyy-MM-dd"),
                FormatSpreadsheetText(record.Timestamp, "HH:mm:ss.fff"),
                (CalibrationTaskContext.TemperaturePointCount + CalibrationTaskContext.HumidityPointCount - invalid.Count).ToString(CultureInfo.InvariantCulture),
                invalid.Count.ToString(CultureInfo.InvariantCulture),
                string.Join(";", invalid),
                FormatSampleNumber(record.DutDisplayTemperature),
                FormatSampleNumber(record.DutDisplayHumidity)
            };
            for (int index = 1; index <= CalibrationTaskContext.TemperaturePointCount; index++)
                row.Add(channels.TryGetValue((ChannelRole.PrimaryTemperature, index), out InspectionChannelData? item) && item.IsValid ? FormatSampleNumber(item.Value) : string.Empty);
            for (int index = 1; index <= CalibrationTaskContext.HumidityPointCount; index++)
                row.Add(channels.TryGetValue((ChannelRole.Humidity, index), out InspectionChannelData? item) && item.IsValid ? FormatSampleNumber(item.Value) : string.Empty);
            return row.ToArray();
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
        /// <summary>从 1.3 的“校准日期 + 开始时间”或 1.0～1.2 的完整开始时间恢复历史排序时间。</summary>
        private static DateTime ParseSummaryStartTime(IReadOnlyDictionary<string, string> values)
        {
            // 新格式末尾带不可见制表符，防止 Excel/WPS 把 CSV 日期转为窄列中的 #####；解析时清理该标记。
            string date = GetValue(values, "校准日期").Trim();
            string time = GetValue(values, "开始时间").Trim();
            if (!string.IsNullOrWhiteSpace(date) &&
                DateTime.TryParseExact(
                    $"{date} {time}",
                    new[] { "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm:ss.fff" },
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out DateTime separated))
                return separated;

            return DateTime.TryParse(time, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime legacy)
                ? legacy
                : default;
        }

        /// <summary>摘要中的校准日期单独保存为办公软件友好的文本，避免默认列宽下显示为井号。</summary>
        private static string FormatSummaryDate(DateTime? value) => value.HasValue ? FormatSpreadsheetText(value.Value, "yyyy-MM-dd") : string.Empty;
        /// <summary>摘要起止时间保存为办公软件友好的文本并显示到秒；逐组正式采样文件仍保留毫秒。</summary>
        private static string FormatSummaryTime(DateTime? value) => value.HasValue ? FormatSpreadsheetText(value.Value, "HH:mm:ss") : string.Empty;
        /// <summary>
        /// 日期时间字段末尾附加不可见制表符，使 Excel/WPS 直接打开 CSV 时按文本显示而不是自动转为日期数值。
        /// CSV 无法保存列宽；该标记可避免列宽不足时出现 #####，程序读取时会 Trim 清理。
        /// </summary>
        private static string FormatSpreadsheetText(DateTime value, string format) =>
            value.ToString(format, CultureInfo.InvariantCulture) + "\t";
        /// <summary>把可空有限数格式化为 CSV 数值文本。</summary>
        private static string FormatNumber(double? value) => value.HasValue && double.IsFinite(value.Value) ? value.Value.ToString("0.############", CultureInfo.InvariantCulture) : string.Empty;
        /// <summary>把有限数格式化为 CSV 数值文本，非有限数输出空白。</summary>
        private static string FormatNumber(double value) => double.IsFinite(value) ? value.ToString("0.############", CultureInfo.InvariantCulture) : string.Empty;
        /// <summary>正式采样中的标准器读数和被校设备示值固定保留三位小数，消除浮点转换尾差。</summary>
        private static string FormatSampleNumber(double? value) => value.HasValue && double.IsFinite(value.Value) ? value.Value.ToString("0.000", CultureInfo.InvariantCulture) : string.Empty;
        /// <summary>最终校准结果固定保留三位小数；原始读数和中间分量仍使用完整可追溯精度。</summary>
        private static string FormatResultNumber(double value) => double.IsFinite(value) ? value.ToString("0.000", CultureInfo.InvariantCulture) : string.Empty;
        /// <summary>创建任务快照中的“字段—值”行。</summary>
        private static string[] Pair(string name, string value) => new[] { name, value ?? string.Empty };
        /// <summary>创建结果文件中的“指标—数值—单位—说明”行。</summary>
        private static string[] ResultRow(string name, double value, string unit, string note) => new[] { name, FormatResultNumber(value), unit, note };
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
