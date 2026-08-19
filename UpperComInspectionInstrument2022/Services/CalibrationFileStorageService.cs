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

        public static CalibrationFileStorageService Default { get; } = new();

        public CalibrationFileStorageService(string? dataRootPath = null)
        {
            DataRootPath = string.IsNullOrWhiteSpace(dataRootPath)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "温湿度校准数据")
                : Path.GetFullPath(dataRootPath);
        }

        public string DataRootPath { get; }
        public string? CurrentJobDirectory
        {
            get { lock (_syncRoot) return _currentJobDirectory; }
        }

        public bool HasActiveJob
        {
            get { lock (_syncRoot) return _currentJobDirectory != null && _status == "采样中"; }
        }

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
                ResultFilePath = Path.Combine(directory, ResultFileName)
            };
        }

        private void WriteSummary()
        {
            if (_currentJobDirectory == null) throw new InvalidOperationException("本地作业目录尚未建立。");
            WriteCsvAtomic(Path.Combine(_currentJobDirectory, SummaryFileName), new[]
            {
                new[] { "数据格式版本", "任务编号", "开始时间", "结束时间", "校准规范", "校准类型", "委托单位", "被校设备", "型号规格", "设备编号", "标准器证书编号", "设定温度(℃)", "设定湿度(%RH)", "温度测点数", "湿度测点数", "计划样本数", "已存样本数", "状态", "状态说明", "作业目录" },
                new[]
                {
                    "1.0",
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
                Pair("标准器准确度", CalibrationTaskContext.ReferencedAccuracySpecification),
                Pair("标准器温度修正值", CalibrationTaskContext.ReferencedTemperatureCorrections),
                Pair("标准器湿度修正值", CalibrationTaskContext.ReferencedHumidityCorrections),
                Pair("标准器温度不确定度", FormatNumber(CalibrationTaskContext.ReferencedTemperatureUncertainty)),
                Pair("标准器温度包含因子", FormatNumber(CalibrationTaskContext.ReferencedTemperatureCoverage)),
                Pair("标准器湿度不确定度", FormatNumber(CalibrationTaskContext.ReferencedHumidityUncertainty)),
                Pair("标准器湿度包含因子", FormatNumber(CalibrationTaskContext.ReferencedHumidityCoverage))
            };
        }

        private static string[] BuildSampleHeader()
        {
            List<string> header = new() { "样本序号", "采样时间", "有效通道数", "异常通道数", "异常通道", "被校设备温度示值(℃)", "被校设备湿度示值(%RH)" };
            header.AddRange(Enumerable.Range(1, CalibrationTaskContext.TemperaturePointCount).Select(index => $"温度{index}(℃)"));
            header.AddRange(Enumerable.Range(1, CalibrationTaskContext.HumidityPointCount).Select(index => $"湿度{index}(%RH)"));
            return header.ToArray();
        }

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

        private static string[] BuildRawChannelHeader() => new[]
        {
            "样本序号", "采样时间", "通道类型", "通道号", "测量值", "单位", "修正前原始值", "证书修正值", "是否已修正", "数据是否有效", "数据状态", "状态说明", "原始HEX", "寄存器地址1", "寄存器地址2", "寄存器值1", "寄存器值2"
        };

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

        private void SetFailureStatus(string message)
        {
            _finishedAt = DateTime.Now;
            _status = "保存异常";
            _statusMessage = message;
            TryWriteSummaryAfterFailure();
        }

        private void TryWriteSummaryAfterFailure()
        {
            try { if (_currentJobDirectory != null) WriteSummary(); }
            catch { }
        }

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

        private static void AppendCsvRows(string path, IEnumerable<string[]> rows)
        {
            string content = string.Join(Environment.NewLine, rows.Select(FormatCsvRow));
            if (content.Length == 0) return;
            File.AppendAllText(path, content + Environment.NewLine, Utf8WithoutBom);
        }

        private static string FormatCsvRow(IEnumerable<string> values) =>
            string.Join(",", values.Select(value => $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\""));

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

        private static string MakeSafeFileName(string value)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            string safe = new(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
            return safe.Length <= 40 ? safe : safe[..40];
        }

        private static string GetValue(IReadOnlyDictionary<string, string> values, string key) =>
            values.TryGetValue(key, out string? value) ? value : string.Empty;
        private static string GetStandardName() => CalibrationTaskContext.StandardIndex == 1 ? "JJF 1376-2012" : "JJF 1101-2019";
        private static string GetCalibrationTypeName() => CalibrationTaskContext.StandardIndex == 1 ? "箱式电阻炉温度" : CalibrationTaskContext.IncludesHumidity ? "温湿度" : "温度";
        private static string FormatDateTime(DateTime? value) => value?.ToString("yyyy-MM-dd HH:mm:ss.fff") ?? string.Empty;
        private static string FormatNumber(double? value) => value.HasValue && double.IsFinite(value.Value) ? value.Value.ToString("0.############", CultureInfo.InvariantCulture) : string.Empty;
        private static string FormatNumber(double value) => double.IsFinite(value) ? value.ToString("0.############", CultureInfo.InvariantCulture) : string.Empty;
        private static string[] Pair(string name, string value) => new[] { name, value ?? string.Empty };
        private static string[] ResultRow(string name, double value, string unit, string note) => new[] { name, FormatNumber(value), unit, note };
    }

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
    }
}
