//using System.Drawing;
using System.Globalization;
using System.IO;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using SpreadsheetBorder = DocumentFormat.OpenXml.Spreadsheet.Border;
namespace UpperComInspectionInstrument2022.Services
{
    /// <summary>
    /// 只从已归档 CSV 生成 Excel 原始记录，不读取当前任务内存或系统设置。
    /// 因此软件重启或系统资料后续变更后，历史报告仍可重复生成相同业务内容。
    /// </summary>
    public sealed class CalibrationExcelReportService
    {
        private const string SummaryFileName = "作业摘要.csv";
        private const string TaskFileName = "任务信息.csv";
        private const string SampleFileName = "正式采样.csv";
        private const string ResultFileName = "校准结果.csv";
        private const string ReportDirectoryName = "报告";
        private const string ReportFileName = "校准原始记录.xlsx";

        /// <summary>应用内共享的 Excel 报告生成服务。</summary>
        public static CalibrationExcelReportService Default { get; } = new();

        /// <summary>
        /// 验证作业归档完整性，从固化 CSV 生成可用 Excel/WPS 打开的原始记录工作簿。
        /// 新格式只要求作业摘要、任务信息、正式采样和校准结果四份业务 CSV；
        /// 旧作业附带的原始通道与不确定度分量文件不再是报告生成前提。
        /// 写入先落到临时文件，结构验证通过后才覆盖正式报告。
        /// </summary>
        public bool TryGenerate(string jobDirectory, out string reportPath, out string error)
        {
            reportPath = string.Empty;
            if (string.IsNullOrWhiteSpace(jobDirectory))
            {
                error = "未选择有效的本地校准作业。";
                return false;
            }

            string fullJobDirectory;
            try { fullJobDirectory = Path.GetFullPath(jobDirectory); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                error = "作业目录无效：" + ex.Message;
                return false;
            }

            string summaryPath = Path.Combine(fullJobDirectory, SummaryFileName);
            string taskPath = Path.Combine(fullJobDirectory, TaskFileName);
            string samplePath = Path.Combine(fullJobDirectory, SampleFileName);
            string resultPath = Path.Combine(fullJobDirectory, ResultFileName);

            foreach (string requiredPath in new[] { summaryPath, taskPath, samplePath, resultPath })
            {
                if (File.Exists(requiredPath)) continue;
                error = $"作业归档不完整，缺少文件：{Path.GetFileName(requiredPath)}";
                return false;
            }

            try
            {
                List<string[]> summaryRows = CalibrationFileStorageService.ReadCsvFile(summaryPath);
                List<string[]> taskRows = CalibrationFileStorageService.ReadCsvFile(taskPath);
                List<string[]> sampleRows = CalibrationFileStorageService.ReadCsvFile(samplePath);
                List<string[]> resultRows = CalibrationFileStorageService.ReadCsvFile(resultPath);
                Dictionary<string, string> summary = ToHeaderDictionary(summaryRows);
                if (!summary.TryGetValue("状态", out string? status) || status != "已完成")
                {
                    error = $"只有状态为“已完成”的作业才能生成正式 Excel 原始记录，当前状态：{status ?? "未知"}。";
                    return false;
                }
                Dictionary<string, string> task = ToPairDictionary(taskRows);
                string reportDirectory = Path.Combine(fullJobDirectory, ReportDirectoryName);
                Directory.CreateDirectory(reportDirectory);
                reportPath = Path.Combine(reportDirectory, ReportFileName);
                string temporaryPath = Path.Combine(reportDirectory, $".{ReportFileName}.{Guid.NewGuid():N}.tmp");
                try
                {
                    CreateWorkbook(temporaryPath, summary, task, sampleRows, resultRows);
                    ValidateWorkbook(temporaryPath);
                    File.Move(temporaryPath, reportPath, overwrite: true);
                }
                finally
                {
                    if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
                }

                error = string.Empty;
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or OpenXmlPackageException or ArgumentException)
            {
                error = $"Excel 原始记录生成失败：{ex.Message}\n作业目录：{fullJobDirectory}";
                reportPath = string.Empty;
                return false;
            }
        }

        /// <summary>创建规范记录、正式采样和任务快照三个工作表。</summary>
        private static void CreateWorkbook(
            string path,
            IReadOnlyDictionary<string, string> summary,
            IReadOnlyDictionary<string, string> task,
            IReadOnlyList<string[]> sampleRows,
            IReadOnlyList<string[]> resultRows)
        {
            using SpreadsheetDocument document = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
            WorkbookPart workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();
            WorkbookStylesPart stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
            stylesPart.Stylesheet = CreateStylesheet();
            stylesPart.Stylesheet.Save();

            Sheets sheets = workbookPart.Workbook.AppendChild(new Sheets());
            uint sheetId = 1;
            bool isJjf1101 = Get(summary, "校准规范").StartsWith("JJF 1101", StringComparison.Ordinal);
            bool isJjf1376 = Get(summary, "校准规范").StartsWith("JJF 1376", StringComparison.Ordinal);
            if (isJjf1101)
            {
                AddJjf1101RecordSheet(workbookPart, sheets, sheetId++, summary, task, sampleRows, resultRows);
                AddCsvSheet(workbookPart, sheets, sheetId++, "正式采样", sampleRows, freezeTopRow: true, hidden: true);
                AddCsvSheet(workbookPart, sheets, sheetId, "任务快照", BuildTaskSnapshotRows(task), freezeTopRow: true, hidden: true);
            }
            else if (isJjf1376)
            {
                AddJjf1376RecordSheet(workbookPart, sheets, sheetId++, summary, task, sampleRows, resultRows);
                AddCsvSheet(workbookPart, sheets, sheetId++, "正式采样", sampleRows, freezeTopRow: true, hidden: true);
                AddCsvSheet(workbookPart, sheets, sheetId, "任务快照", BuildTaskSnapshotRows(task), freezeTopRow: true, hidden: true);
            }
            else
            {
                AddSummarySheet(workbookPart, sheets, sheetId++, summary, task, resultRows);
                AddCsvSheet(workbookPart, sheets, sheetId++, "正式采样", sampleRows, freezeTopRow: true);
                AddCsvSheet(workbookPart, sheets, sheetId, "任务快照", BuildTaskSnapshotRows(task), freezeTopRow: true);
            }
            workbookPart.Workbook.CalculationProperties = new CalculationProperties { CalculationMode = CalculateModeValues.Auto };
            workbookPart.Workbook.Save();
        }

        /// <summary>
        /// 按 JJF 1376-2012 附录 A 创建箱式电阻炉原始记录。
        /// 记录逐次标准器读数、平均值、证书修正值、实际温度以及规范要求的五类结果。
        /// </summary>
        private static void AddJjf1376RecordSheet(
            WorkbookPart workbookPart,
            Sheets sheets,
            uint sheetId,
            IReadOnlyDictionary<string, string> summary,
            IReadOnlyDictionary<string, string> task,
            IReadOnlyList<string[]> sampleRows,
            IReadOnlyList<string[]> resultRows)
        {
            WorksheetPart part = workbookPart.AddNewPart<WorksheetPart>();
            SheetData data = new();
            MergeCells merges = new();
            string[] sampleHeader = sampleRows.Count > 0 ? sampleRows[0] : Array.Empty<string>();
            int[] temperatureColumns = sampleHeader.Select((name, index) => (name, index))
                .Where(item => item.name.StartsWith("温度", StringComparison.Ordinal) && item.name.Contains("(℃)", StringComparison.Ordinal))
                .Select(item => item.index).ToArray();
            int columnCount = Math.Max(10, temperatureColumns.Length + 1);
            uint rowIndex = 1;
            Dictionary<int, double> corrections = ParseTemperatureCorrections(task);

            AppendMergedRow(data, merges, ref rowIndex, columnCount, "JJF 1376—2012", 11U, 20);
            //AppendMergedRow(data, merges, ref rowIndex, columnCount, "附录 A", 18U, 20, HorizontalAlignmentValues.Left);
            AppendMergedRow(data, merges, ref rowIndex, columnCount, "箱式电阻炉校准记录", 12U, 30);
            AppendMergedRow(data, merges, ref rowIndex, columnCount, $"{Get(summary, "任务编号", "-")} 校准记录", 11U, 22);
            rowIndex++;

            AppendFormalPairRow(data, merges, ref rowIndex, columnCount,
                "委托单位", Get(task, "委托单位", "未填写（可选）"),
                "流水号", Get(summary, "任务编号", "-"));
            AppendFormalPairRow(data, merges, ref rowIndex, columnCount,
                "型号/规格", Get(task, "型号规格", "未填写（可选）"),
                "出厂编号", Get(task, "设备编号", "未填写（可选）"));
            AppendFormalPairRow(data, merges, ref rowIndex, columnCount,
                "制造厂", Get(task, "制造单位", "未填写（可选）"),
                "校准地点", Get(task, "校准地点", "未填写（可选）"));
            AppendFormalPairRow(data, merges, ref rowIndex, columnCount,
                "测量范围", Get(task, "测量范围", "未填写（可选）"),
                "环境温度", AppendUnit(Get(task, "环境温度(℃)"), "℃"));
            AppendFormalPairRow(data, merges, ref rowIndex, columnCount,
                "相对湿度", AppendUnit(Get(task, "环境湿度(%RH)"), "%RH"),
                "校准日期", Get(task, "校准日期", "-"));
            AppendFormalPairRow(data, merges, ref rowIndex, columnCount,
                "校准用标准设备", Get(task, "标准器名称", "未填写"),
                "型号/编号", $"{Get(task, "标准器型号", "-")} / {Get(task, "标准器编号", "-")}" );
            AppendFormalPairRow(data, merges, ref rowIndex, columnCount,
                "证书编号", Get(task, "标准器证书编号", "未填写"),
                "证书有效日期", Get(task, "标准器有效期", "未填写"));
            AppendFormalPairRow(data, merges, ref rowIndex, columnCount,
                "技术依据", Get(summary, "校准规范", "JJF 1376-2012"),
                "外观检查", Get(task, "外观检查", "待检查"));
            AppendFormalPairRow(data, merges, ref rowIndex, columnCount,
                "标称温度", AppendUnit(Get(task, "设定温度(℃)"), "℃"),
                "负载说明", Get(task, "负载说明", "无/未填写"));

            Row groupHeader = CreateFormalRow(rowIndex++, columnCount, 16U, 24);
            SetCell(groupHeader, 1, "次数", 16U);
            SetCell(groupHeader, 2, "校准结果 / ℃", 16U);
            merges.Append(new MergeCell { Reference = $"B{groupHeader.RowIndex}:{ColumnName(columnCount)}{groupHeader.RowIndex}" });
            data.Append(groupHeader);
            Row pointHeader = CreateFormalRow(rowIndex++, columnCount, 16U, 22);
            SetCell(pointHeader, 1, "测温点", 16U);
            for (int point = 0; point < temperatureColumns.Length; point++)
                SetCell(pointHeader, point + 2, (point + 1).ToString(CultureInfo.InvariantCulture), 16U);
            data.Append(pointHeader);

            List<double>[] rawByPoint = Enumerable.Range(0, temperatureColumns.Length).Select(_ => new List<double>()).ToArray();
            List<double>[] correctionByPoint = Enumerable.Range(0, temperatureColumns.Length).Select(_ => new List<double>()).ToArray();
            List<double>[] actualByPoint = Enumerable.Range(0, temperatureColumns.Length).Select(_ => new List<double>()).ToArray();
            int fallbackSampleNumber = 1;
            foreach (string[] sourceRow in sampleRows.Skip(1))
            {
                int sampleNumber = sourceRow.Length > 0 && int.TryParse(sourceRow[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedSample)
                    ? parsedSample
                    : fallbackSampleNumber;
                Row row = CreateFormalRow(rowIndex++, columnCount, 17U, 21);
                SetCell(row, 1, sampleNumber.ToString(CultureInfo.InvariantCulture), 17U);
                for (int point = 0; point < temperatureColumns.Length; point++)
                {
                    double? actual = TryNumber(sourceRow, temperatureColumns[point]);
                    double correction = corrections.TryGetValue(point + 1, out double configuredCorrection)
                        ? configuredCorrection
                        : 0;
                    double? measured = actual.HasValue ? actual.Value - correction : null;
                    if (measured.HasValue)
                    {
                        rawByPoint[point].Add(measured.Value);
                        SetCell(row, point + 2, measured.Value, 19U);
                    }
                    if (actual.HasValue) correctionByPoint[point].Add(correction);
                    if (actual.HasValue) actualByPoint[point].Add(actual.Value);
                }
                data.Append(row);
                fallbackSampleNumber++;
            }

            AppendAverageRow(data, ref rowIndex, columnCount, "平均值", rawByPoint);
            AppendAverageRow(data, ref rowIndex, columnCount, "修正值", correctionByPoint);
            AppendAverageRow(data, ref rowIndex, columnCount, "实际温度", actualByPoint);

            Dictionary<string, string> results = ResultDictionary(resultRows);
            AppendFormalPairRow(data, merges, ref rowIndex, columnCount,
                "炉温均匀度", $"Δθ+={ResultValue(results, "炉温均匀度上偏差", "℃")}；Δθ−={ResultValue(results, "炉温均匀度下偏差", "℃")}",
                "炉温稳定度", $"δ+={ResultValue(results, "炉温稳定度上偏差", "℃")}；δ−={ResultValue(results, "炉温稳定度下偏差", "℃")}");
            AppendFormalPairRow(data, merges, ref rowIndex, columnCount,
                "炉温偏差", $"Δt+={ResultValue(results, "炉温偏差上偏差", "℃")}；Δt−={ResultValue(results, "炉温偏差下偏差", "℃")}",
                "炉内最大温差", $"Δts={ResultValue(results, "炉内最大温差", "℃")}");
            AppendFormalPairRow(data, merges, ref rowIndex, columnCount,
                "炉膛尺寸", BuildFurnaceSize(task),
                "测温区尺寸", BuildWorkZoneSize(task));
            string coverage = Get(task, "标准器温度包含因子", "2");
            AppendFormalPairRow(data, merges, ref rowIndex, columnCount,
                "均匀度 Δθ+ 不确定度", $"U={ResultValue(results, "炉温均匀度上偏差扩展不确定度", "℃")}；k={coverage}",
                "均匀度 Δθ− 不确定度", $"U={ResultValue(results, "炉温均匀度下偏差扩展不确定度", "℃")}；k={coverage}", 42);
            AppendFormalPairRow(data, merges, ref rowIndex, columnCount,
                "校准员", Get(task, "校准员", "________________"),
                "核验员", Get(task, "核验员", "________________"));

            Worksheet worksheet = new();
            worksheet.Append(CreateSheetViews(freezeTopRow: false));
            worksheet.Append(CreateColumns(Enumerable.Repeat(columnCount > 10 ? 8.5D : 10D, columnCount).ToArray()));
            worksheet.Append(data);
            worksheet.Append(merges);
            worksheet.Append(
                new PrintOptions { HorizontalCentered = true },
                CreatePageMargins(),
                new PageSetup { PaperSize = 9U, Orientation = OrientationValues.Portrait, FitToWidth = 1, FitToHeight = 1 });
            part.Worksheet = worksheet;
            part.Worksheet.Save();
            sheets.Append(new Sheet { Id = workbookPart.GetIdOfPart(part), SheetId = sheetId, Name = "校准记录" });
        }

        /// <summary>从任务快照读取各温度通道的证书修正值，用于还原箱式炉记录中的修正前读数。</summary>
        private static Dictionary<int, double> ParseTemperatureCorrections(IReadOnlyDictionary<string, string> task)
        {
            return ChannelCorrectionService.TryParse(
                Get(task, "标准器温度修正值"),
                50,
                out Dictionary<int, double> corrections,
                out _)
                ? corrections
                : new Dictionary<int, double>();
        }

        /// <summary>追加每个测温点的平均值、修正值或实际温度行。</summary>
        private static void AppendAverageRow(SheetData data, ref uint rowIndex, int columnCount, string label, IReadOnlyList<List<double>> values)
        {
            Row row = CreateFormalRow(rowIndex++, columnCount, 17U, 21);
            SetCell(row, 1, label, 16U);
            for (int point = 0; point < values.Count; point++)
            {
                if (values[point].Count > 0) SetCell(row, point + 2, values[point].Average(), 19U);
            }
            data.Append(row);
        }

        /// <summary>读取 CSV 指定列的有限数值。</summary>
        private static double? TryNumber(string[] row, int index) =>
            index >= 0 && index < row.Length && double.TryParse(row[index], NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && double.IsFinite(value)
                ? value
                : null;

        /// <summary>格式化 JJF 1376 附录 A 中的炉膛 L/W/H。</summary>
        private static string BuildFurnaceSize(IReadOnlyDictionary<string, string> task) =>
            $"L={Get(task, "炉膛长度(mm)", "-")} mm；W={Get(task, "炉膛宽度(mm)", "-")} mm；H={Get(task, "炉膛高度(mm)", "-")} mm";

        /// <summary>格式化 JJF 1376 附录 A 中的测温区 l′/w′/h′。</summary>
        private static string BuildWorkZoneSize(IReadOnlyDictionary<string, string> task) =>
            $"l′={Get(task, "工作区长度(mm)", "-")} mm；w′={Get(task, "工作区宽度(mm)", "-")} mm；h′={Get(task, "工作区高度(mm)", "-")} mm";

        /// <summary>
        /// 按 JJF 1101-2019 附录 A 创建正式原始记录首页。
        /// 逐次温湿度实测值、统计结果、标准器信息和布点复核集中在一张可打印工作表中。
        /// </summary>
        private static void AddJjf1101RecordSheet(
            WorkbookPart workbookPart,
            Sheets sheets,
            uint sheetId,
            IReadOnlyDictionary<string, string> summary,
            IReadOnlyDictionary<string, string> task,
            IReadOnlyList<string[]> sampleRows,
            IReadOnlyList<string[]> resultRows)
        {
            WorksheetPart part = workbookPart.AddNewPart<WorksheetPart>();
            SheetData data = new();
            MergeCells merges = new();
            string[] sampleHeader = sampleRows.Count > 0 ? sampleRows[0] : Array.Empty<string>();
            int[] temperatureColumns = sampleHeader.Select((name, index) => (name, index))
                .Where(item => item.name.StartsWith("温度", StringComparison.Ordinal) && item.name.Contains("(℃)", StringComparison.Ordinal))
                .Select(item => item.index).ToArray();
            int[] humidityColumns = sampleHeader.Select((name, index) => (name, index))
                .Where(item => item.name.StartsWith("湿度", StringComparison.Ordinal) && item.name.Contains("(%RH)", StringComparison.Ordinal))
                .Select(item => item.index).ToArray();
            int columnCount = Math.Max(10, 1 + Math.Max(temperatureColumns.Length, humidityColumns.Length));
            string lastColumn = ColumnName(columnCount);
            uint rowIndex = 1;

            AppendMergedRow(data, merges, ref rowIndex, columnCount, "JJF 1101—2019", 11U, 20);
            rowIndex++;
            //AppendMergedRow(data, merges, ref rowIndex, columnCount, "附录 A", 18U, 20, HorizontalAlignmentValues.Left);
            AppendMergedRow(data, merges, ref rowIndex, columnCount, "环境试验设备校准记录", 12U, 30);
            rowIndex++;

            AppendFormalPairRow(data, merges, ref rowIndex, columnCount,
                "委托单位", Get(task, "委托单位", "未填写（可选）"),
                "仪器名称", Get(task, "被校设备名称", "未填写（可选）"));
            AppendFormalPairRow(data, merges, ref rowIndex, columnCount,
                "制造厂", Get(task, "制造单位", "未填写（可选）"),
                "型号规格", Get(task, "型号规格", "未填写（可选）"));
            AppendFormalPairRow(data, merges, ref rowIndex, columnCount,
                "记录编号", Get(summary, "任务编号", "-"),
                "出厂编号", Get(task, "设备编号", "未填写（可选）"));
            AppendFormalPairRow(data, merges, ref rowIndex, columnCount,
                "校准地点", Get(task, "校准地点", "未填写（可选）"),
                "环境条件", BuildEnvironment(task));
            AppendFormalPairRow(data, merges, ref rowIndex, columnCount,
                "校准日期", Get(task, "校准日期", "-"),
                "校准依据", Get(summary, "校准规范", "JJF 1101-2019"));
            rowIndex++;

            AppendFormalPairRow(data, merges, ref rowIndex, columnCount,
                "标准器名称", Get(task, "标准器名称", "未填写"),
                "型号/规格", Get(task, "标准器型号", "未填写"));
            AppendFormalPairRow(data, merges, ref rowIndex, columnCount,
                "证书编号", Get(task, "标准器证书编号", "未填写"),
                "有效期至", Get(task, "标准器有效期", "未填写"));
            AppendFormalPairRow(data, merges, ref rowIndex, columnCount,
                "准确度/最大允许误差", Get(task, "标准器准确度", "未填写"),
                "标准器编号", Get(task, "标准器编号", "未填写"));
            rowIndex++;

            AppendMergedRow(data, merges, ref rowIndex, columnCount, "1. 校准记录", 18U, 22, HorizontalAlignmentValues.Left);
            AppendParameterRecord(data, merges, ref rowIndex, columnCount, "温度", "℃", Get(task, "设定温度(℃)", "—"),
                temperatureColumns, sampleRows, resultRows, task);
            if (humidityColumns.Length > 0)
            {
                rowIndex++;
                AppendParameterRecord(data, merges, ref rowIndex, columnCount, "湿度", "%RH", Get(task, "设定湿度(%RH)", "—"),
                    OrderHumidityColumns(humidityColumns, task), sampleRows, resultRows, task);
            }

            rowIndex++;
            AppendMergedRow(data, merges, ref rowIndex, columnCount, "2. 传感器布点示意图", 18U, 22, HorizontalAlignmentValues.Left);
            AppendJjf1101LayoutRows(data, merges, ref rowIndex, columnCount, task);
            rowIndex++;
            AppendFormalPairRow(data, merges, ref rowIndex, columnCount,
                "校准员", Get(task, "校准员", "________________"),
                "核验员", Get(task, "核验员", "________________"));

            Worksheet worksheet = new();
            worksheet.Append(CreateSheetViews(freezeTopRow: false));
            worksheet.Append(CreateColumns(Enumerable.Repeat(columnCount > 10 ? 8.5D : 10D, columnCount).ToArray()));
            worksheet.Append(data);
            worksheet.Append(merges);
            worksheet.Append(
                new PrintOptions { HorizontalCentered = true },
                CreatePageMargins(),
                new PageSetup
                {
                    PaperSize = 9U,
                    Orientation = columnCount > 10 ? OrientationValues.Landscape : OrientationValues.Portrait,
                    FitToWidth = 1,
                    FitToHeight = 0
                });
            part.Worksheet = worksheet;
            part.Worksheet.Save();
            sheets.Append(new Sheet { Id = workbookPart.GetIdOfPart(part), SheetId = sheetId, Name = "校准记录" });
        }

        /// <summary>追加温度或湿度的逐次实测值、最大最小值以及附录 A 结果项目。</summary>
        private static void AppendParameterRecord(
            SheetData data,
            MergeCells merges,
            ref uint rowIndex,
            int columnCount,
            string parameter,
            string unit,
            string setPoint,
            IReadOnlyList<int> sourceColumns,
            IReadOnlyList<string[]> sampleRows,
            IReadOnlyList<string[]> resultRows,
            IReadOnlyDictionary<string, string> task)
        {
            AppendMergedRow(data, merges, ref rowIndex, columnCount, $"{parameter}参数校准记录", 15U, 23, HorizontalAlignmentValues.Left);
            AppendMergedRow(data, merges, ref rowIndex, columnCount, $"{parameter}设定值：{setPoint} {unit}    单位：{unit}", 18U, 22, HorizontalAlignmentValues.Left);

            Row groupHeader = CreateFormalRow(rowIndex++, columnCount, 16U, 22);
            SetCell(groupHeader, 1, "次数", 16U);
            SetCell(groupHeader, 2, $"实测{parameter}值", 16U);
            if (columnCount > 2)
                merges.Append(new MergeCell { Reference = $"B{groupHeader.RowIndex}:{ColumnName(columnCount)}{groupHeader.RowIndex}" });
            data.Append(groupHeader);

            Row pointHeader = CreateFormalRow(rowIndex++, columnCount, 16U, 22);
            SetCell(pointHeader, 1, "次数", 16U);
            string[] labels = BuildPointLabels(parameter, sourceColumns.Count, task);
            for (int index = 0; index < sourceColumns.Count; index++)
                SetCell(pointHeader, index + 2, labels[index], 16U);
            data.Append(pointHeader);

            List<double>[] valuesByPoint = Enumerable.Range(0, sourceColumns.Count).Select(_ => new List<double>()).ToArray();
            int sampleNumber = 1;
            foreach (string[] sourceRow in sampleRows.Skip(1))
            {
                Row row = CreateFormalRow(rowIndex++, columnCount, 17U, 21);
                SetCell(row, 1, sampleNumber++.ToString(CultureInfo.InvariantCulture), 17U);
                for (int point = 0; point < sourceColumns.Count; point++)
                {
                    string value = sourceColumns[point] < sourceRow.Length ? sourceRow[sourceColumns[point]] : string.Empty;
                    if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
                    {
                        valuesByPoint[point].Add(number);
                        SetCell(row, point + 2, number, 19U);
                    }
                    else
                    {
                        SetCell(row, point + 2, value, 17U);
                    }
                }
                data.Append(row);
            }

            AppendStatisticRow(data, ref rowIndex, columnCount, "最大值", valuesByPoint, values => values.Max());
            AppendStatisticRow(data, ref rowIndex, columnCount, "最小值", valuesByPoint, values => values.Min());

            Dictionary<string, string> results = ResultDictionary(resultRows);
            AppendFormalPairRow(data, merges, ref rowIndex, columnCount,
                "上偏差", ResultValue(results, parameter + "上偏差", unit),
                "下偏差", ResultValue(results, parameter + "下偏差", unit));
            AppendFormalPairRow(data, merges, ref rowIndex, columnCount,
                "均匀度", ResultValue(results, parameter + "均匀度", unit),
                "波动度", ResultValue(results, parameter + "波动度", unit));
            string coverage = Get(task, $"标准器{parameter}包含因子", "2");
            AppendFormalPairRow(data, merges, ref rowIndex, columnCount,
                "校准不确定度", $"{ResultValue(results, parameter + "扩展不确定度", unit)}（k={coverage}）",
                "正式样本", $"{Math.Max(0, sampleRows.Count - 1)} 组");
        }

        /// <summary>将湿度中心通道映射为附录 A 的 O 点，其余通道依次映射为 A、B、C。</summary>
        private static int[] OrderHumidityColumns(IReadOnlyList<int> columns, IReadOnlyDictionary<string, string> task)
        {
            int center = int.TryParse(Get(task, "湿度中心点", "0"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : 0;
            if (center < 1 || center > columns.Count) return columns.ToArray();
            return new[] { columns[center - 1] }.Concat(columns.Where((_, index) => index != center - 1)).ToArray();
        }

        /// <summary>为温度使用数字点号，为湿度使用 O/A/B/C 空间点号。</summary>
        private static string[] BuildPointLabels(string parameter, int count, IReadOnlyDictionary<string, string> task)
        {
            if (parameter == "温度")
                return Enumerable.Range(1, count).Select(index => index.ToString(CultureInfo.InvariantCulture)).ToArray();
            string[] spatial = { "O", "A", "B", "C" };
            return Enumerable.Range(0, count)
                .Select(index => index < spatial.Length ? spatial[index] : $"H{index + 1}")
                .ToArray();
        }

        /// <summary>追加每个测点的最大值或最小值统计行。</summary>
        private static void AppendStatisticRow(
            SheetData data,
            ref uint rowIndex,
            int columnCount,
            string label,
            IReadOnlyList<List<double>> valuesByPoint,
            Func<IEnumerable<double>, double> selector)
        {
            Row row = CreateFormalRow(rowIndex++, columnCount, 17U, 21);
            SetCell(row, 1, label, 16U);
            for (int point = 0; point < valuesByPoint.Count; point++)
            {
                if (valuesByPoint[point].Count > 0)
                    SetCell(row, point + 2, selector(valuesByPoint[point]), 19U);
            }
            data.Append(row);
        }

        /// <summary>
        /// 创建 JJF 1101 三层传感器布点图。规范默认方案使用带边框的空间示意图，
        /// 自定义方案只保留实际点位说明，避免把非标准接线错误地画成规范图。
        /// </summary>
        private static void AppendJjf1101LayoutRows(
            SheetData data,
            MergeCells merges,
            ref uint rowIndex,
            int columnCount,
            IReadOnlyDictionary<string, string> task)
        {
            int pointCount = int.TryParse(Get(task, "温度测点数", "0"), out int parsed) ? parsed : 0;
            bool defaultLayout = Get(task, "布点方式索引", "0") == "0" && pointCount is 9 or 15;

            if (defaultLayout)
            {
                string[] titles = { "上 层", "中 层", "下 层" };
                int panelWidth = pointCount == 15 ? 4 : 2;
                const int panelGap = 1;
                int[] panelStarts =
                {
                    2,
                    2 + panelWidth + panelGap,
                    2 + (panelWidth + panelGap) * 2
                };

                // 点号严格按 JJF 1101 图示放置；A/B/C 为湿度点，O 为温度中心点。
                Dictionary<(int Panel, int Row, int Column), string> labels = new();
                if (pointCount == 9)
                {
                    labels[(0, 0, 0)] = "1 A";
                    labels[(0, 0, 1)] = "2";
                    labels[(0, 4, 0)] = "4";
                    labels[(0, 4, 1)] = "3";
                    labels[(1, 2, 0)] = "5 O";
                    labels[(2, 0, 0)] = "6";
                    labels[(2, 0, 1)] = "7";
                    labels[(2, 4, 0)] = "9";
                    labels[(2, 4, 1)] = "8 B";
                }
                else
                {
                    labels[(0, 0, 0)] = "1 A";
                    labels[(0, 0, 3)] = "2";
                    labels[(0, 2, 1)] = "5";
                    labels[(0, 4, 0)] = "4";
                    labels[(0, 4, 3)] = "3";
                    labels[(1, 0, 1)] = "11";
                    labels[(1, 2, 0)] = "14";
                    labels[(1, 2, 1)] = "15 O";
                    labels[(1, 2, 3)] = "12";
                    labels[(1, 4, 1)] = "13 C";
                    labels[(2, 0, 0)] = "6";
                    labels[(2, 0, 3)] = "7";
                    labels[(2, 2, 1)] = "10";
                    labels[(2, 4, 0)] = "9";
                    labels[(2, 4, 3)] = "8 B";
                }

                Row layoutTitleRow = CreateFormalRow(rowIndex, columnCount, 11U, 22);
                for (int panel = 0; panel < 3; panel++)
                {
                    int start = panelStarts[panel];
                    int end = start + panelWidth - 1;
                    SetCell(layoutTitleRow, start, titles[panel], 21U);
                    merges.Append(new MergeCell { Reference = $"{ColumnName(start)}{rowIndex}:{ColumnName(end)}{rowIndex}" });
                }
                data.Append(layoutTitleRow);
                rowIndex++;

                // 五行边框组成三个独立箱体，点号位于规范所示的角点、中心或中层位置。
                for (int diagramRow = 0; diagramRow < 5; diagramRow++)
                {
                    uint currentRowIndex = rowIndex++;
                    Row row = CreateFormalRow(currentRowIndex, columnCount, 11U, 22);
                    for (int panel = 0; panel < 3; panel++)
                    {
                        int start = panelStarts[panel];
                        for (int panelColumn = 0; panelColumn < panelWidth; panelColumn++)
                        {
                            int column = start + panelColumn;
                            uint style = LayoutCellStyle(
                                top: diagramRow == 0,
                                bottom: diagramRow == 4,
                                left: panelColumn == 0,
                                right: panelColumn == panelWidth - 1);
                            labels.TryGetValue((panel, diagramRow, panelColumn), out string? label);
                            SetCell(row, column, label ?? string.Empty, style);
                        }

                        bool hasCenteredLabel = pointCount == 9
                            ? panel == 1 && diagramRow == 2
                            : (panel == 0 && diagramRow == 2) ||
                              (panel == 1 && diagramRow is 0 or 2 or 4) ||
                              (panel == 2 && diagramRow == 2);
                        if (hasCenteredLabel)
                        {
                            int centerStart = pointCount == 9 ? start : start + 1;
                            int centerEnd = pointCount == 9 ? start + 1 : start + 2;
                            merges.Append(new MergeCell
                            {
                                Reference = $"{ColumnName(centerStart)}{currentRowIndex}:{ColumnName(centerEnd)}{currentRowIndex}"
                            });
                        }
                    }
                    data.Append(row);
                }

                // 门的朝向是布点复核的一部分，使用独立门框行保持三层方向一致。
                Row doorFrameRow = CreateFormalRow(rowIndex, columnCount, 11U, 16);
                for (int panel = 0; panel < 3; panel++)
                {
                    int start = panelStarts[panel];
                    int end = start + panelWidth - 1;
                    SetCell(doorFrameRow, start, "┌────┐", 11U);
                    merges.Append(new MergeCell { Reference = $"{ColumnName(start)}{rowIndex}:{ColumnName(end)}{rowIndex}" });
                }
                data.Append(doorFrameRow);
                rowIndex++;

                Row doorRow = CreateFormalRow(rowIndex, columnCount, 11U, 18);
                for (int panel = 0; panel < 3; panel++)
                {
                    int start = panelStarts[panel];
                    int end = start + panelWidth - 1;
                    SetCell(doorRow, start, "门", 11U);
                    merges.Append(new MergeCell { Reference = $"{ColumnName(start)}{rowIndex}:{ColumnName(end)}{rowIndex}" });
                }
                data.Append(doorRow);
                rowIndex++;

                AppendMergedRow(data, merges, ref rowIndex, columnCount,
                    pointCount == 9 ? "图 B1  布点示意图" : "传感器布点示意图（15 点）",
                    11U, 22, HorizontalAlignmentValues.Center);
            }
            else
            {
                AppendMergedRow(data, merges, ref rowIndex, columnCount,
                    $"调整布点：温度 {pointCount} 点，中心点 {Get(task, "温度中心点", "-")}；应按实际点位记录复核。",
                    20U, 28, HorizontalAlignmentValues.Left);
            }
            AppendMergedRow(data, merges, ref rowIndex, columnCount, Get(task, "布点说明", "按任务配置"), 20U, 34, HorizontalAlignmentValues.Left);
        }

        /// <summary>返回布点示意图单元格的边框样式编号。</summary>
        private static uint LayoutCellStyle(bool top, bool bottom, bool left, bool right)
        {
            if (top && left) return 22U;
            if (top && right) return 24U;
            if (top) return 23U;
            if (bottom && left) return 27U;
            if (bottom && right) return 29U;
            if (bottom) return 28U;
            if (left) return 25U;
            if (right) return 26U;
            return 11U;
        }

        /// <summary>创建指定列数且已经带 A1 引用的正式记录行。</summary>
        private static Row CreateFormalRow(uint rowIndex, int columnCount, uint styleIndex, double height)
        {
            Row row = new() { RowIndex = rowIndex, Height = height, CustomHeight = true };
            for (int column = 0; column < columnCount; column++)
                row.Append(TextCell(string.Empty, styleIndex));
            AssignCellReferences(row);
            return row;
        }

        /// <summary>向记录表追加一行通栏标题或说明。</summary>
        private static void AppendMergedRow(
            SheetData data,
            MergeCells merges,
            ref uint rowIndex,
            int columnCount,
            string text,
            uint styleIndex,
            double height,
            HorizontalAlignmentValues? alignment = null)
        {
            Row row = CreateFormalRow(rowIndex, columnCount, styleIndex, height);
            SetCell(row, 1, text, styleIndex);
            data.Append(row);
            if (columnCount > 1)
                merges.Append(new MergeCell { Reference = $"A{rowIndex}:{ColumnName(columnCount)}{rowIndex}" });
            rowIndex++;
        }

        /// <summary>追加“标签—值—标签—值”双栏信息行，并将值区域合并为稳定比例。</summary>
        private static void AppendFormalPairRow(
            SheetData data,
            MergeCells merges,
            ref uint rowIndex,
            int columnCount,
            string leftLabel,
            string leftValue,
            string rightLabel,
            string rightValue,
            double height = 24)
        {
            int half = columnCount / 2;
            int rightLabelColumn = half + 1;
            Row row = CreateFormalRow(rowIndex, columnCount, 14U, height);
            SetCell(row, 1, leftLabel, 13U);
            SetCell(row, 2, leftValue, 14U);
            SetCell(row, rightLabelColumn, rightLabel, 13U);
            if (rightLabelColumn < columnCount) SetCell(row, rightLabelColumn + 1, rightValue, 14U);
            data.Append(row);
            if (half > 2)
                merges.Append(new MergeCell { Reference = $"B{rowIndex}:{ColumnName(half)}{rowIndex}" });
            if (rightLabelColumn + 1 < columnCount)
                merges.Append(new MergeCell { Reference = $"{ColumnName(rightLabelColumn + 1)}{rowIndex}:{ColumnName(columnCount)}{rowIndex}" });
            rowIndex++;
        }

        /// <summary>替换行中指定位置的文本单元格，同时保留已分配的 A1 引用。</summary>
        private static void SetCell(Row row, int oneBasedColumn, string value, uint styleIndex)
        {
            Cell? oldCell = row.Elements<Cell>().ElementAtOrDefault(oneBasedColumn - 1);
            if (oldCell == null) return;
            Cell replacement = TextCell(value, styleIndex);
            replacement.CellReference = oldCell.CellReference;
            row.ReplaceChild(replacement, oldCell);
        }

        /// <summary>替换行中指定位置的数值单元格。</summary>
        private static void SetCell(Row row, int oneBasedColumn, double value, uint styleIndex)
        {
            Cell? oldCell = row.Elements<Cell>().ElementAtOrDefault(oneBasedColumn - 1);
            if (oldCell == null) return;
            Cell replacement = NumberCell(value, styleIndex);
            replacement.CellReference = oldCell.CellReference;
            row.ReplaceChild(replacement, oldCell);
        }

        /// <summary>把校准结果 CSV 转换为按项目名称读取的字典。</summary>
        private static Dictionary<string, string> ResultDictionary(IReadOnlyList<string[]> rows) => rows.Skip(1)
            .Where(row => row.Length >= 2 && !string.IsNullOrWhiteSpace(row[0]))
            .GroupBy(row => row[0], StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First()[1], StringComparer.Ordinal);

        /// <summary>取得结果值并按报告口径保留三位小数；缺失时明确留空而不是伪造零值。</summary>
        private static string ResultValue(IReadOnlyDictionary<string, string> results, string key, string unit)
        {
            if (!results.TryGetValue(key, out string? value) || string.IsNullOrWhiteSpace(value)) return "—";
            string formatted = double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) && double.IsFinite(number)
                ? number.ToString("0.000", CultureInfo.InvariantCulture)
                : value;
            return $"{formatted} {unit}";
        }

        /// <summary>添加便于打印和复核的“任务与结果”汇总页。</summary>
        private static void AddSummarySheet(
            WorkbookPart workbookPart,
            Sheets sheets,
            uint sheetId,
            IReadOnlyDictionary<string, string> summary,
            IReadOnlyDictionary<string, string> task,
            IReadOnlyList<string[]> resultRows)
        {
            WorksheetPart part = workbookPart.AddNewPart<WorksheetPart>();
            SheetData data = new();
            Row titleRow = new() { RowIndex = 1, Height = 32, CustomHeight = true };
            titleRow.Append(TextCell("温湿度校准原始记录与结果汇总", 1));
            AssignCellReferences(titleRow);
            data.Append(titleRow, new Row { RowIndex = 2, Height = 22, CustomHeight = true }, new Row { RowIndex = 3, Height = 10, CustomHeight = true });

            data.Append(SingleCellRow(4, "作业信息", 2));
            data.Append(SummaryPairRow(5,
                "任务编号", Get(summary, "任务编号"),
                "校准规范", Get(summary, "校准规范"),
                "状态", Get(summary, "状态"),
                "归档样本", $"{Get(summary, "已存样本数")}/{Get(summary, "计划样本数")}"));
            data.Append(SummaryPairRow(6,
                "被校设备", Get(summary, "被校设备", "未填写（可选）"),
                "设备编号", Get(summary, "设备编号", "未填写（可选）"),
                "校准类型", Get(summary, "校准类型"),
                "委托单位", Get(summary, "委托单位", "未填写（可选）")));
            data.Append(SummaryPairRow(7,
                "设定温度", AppendUnit(Get(summary, "设定温度(℃)"), "℃"),
                "设定湿度", AppendUnit(Get(summary, "设定湿度(%RH)"), "%RH"),
                "温度测点", AppendUnit(Get(summary, "温度测点数"), "点"),
                "湿度测点", AppendUnit(Get(summary, "湿度测点数"), "点")));
            data.Append(SummaryPairRow(8,
                "标准器", Get(task, "标准器名称", "未填写"),
                "证书编号", Get(task, "标准器证书编号", "未填写"),
                "有效期", Get(task, "标准器有效期", "未填写"),
                "校准日期", Get(task, "校准日期", "未填写")));
            data.Append(SummaryPairRow(9,
                "环境条件", BuildEnvironment(task),
                "布点说明", Get(task, "布点说明", "未填写"),
                "采样间隔", AppendUnit(Get(task, "采样间隔(s)"), "s"),
                "数据来源", "本地归档 CSV"));

            data.Append(new Row { RowIndex = 10, Height = 10, CustomHeight = true });
            data.Append(SingleCellRow(11, "校准结果", 2));
            Row header = new() { RowIndex = 12, Height = 24, CustomHeight = true };
            foreach (string value in new[] { "指标", "数值", "单位", "说明" }) header.Append(TextCell(value, 5));
            AssignCellReferences(header);
            data.Append(header);

            uint rowIndex = 13;
            foreach (string[] sourceRow in resultRows.Skip(1))
            {
                Row row = new() { RowIndex = rowIndex++ };
                for (int column = 0; column < 4; column++)
                {
                    string value = column < sourceRow.Length ? sourceRow[column] : string.Empty;
                    row.Append(column == 1 ? DataCell(value, "数值") : TextCell(value, 4));
                }
                AssignCellReferences(row);
                data.Append(row);
            }

            uint noteRowIndex = rowIndex + 1;
            Row noteRow = new() { RowIndex = noteRowIndex, Height = 38, CustomHeight = true };
            noteRow.Append(TextCell("说明：本工作簿由作业目录中的固化 CSV 重建，不读取当前系统设置。校准结果用于原始记录与复核；没有明确技术指标时不自动给出合格/不合格结论。", 9));
            AssignCellReferences(noteRow);
            data.Append(noteRow);
            //
            Worksheet worksheet = new();
            worksheet.Append(CreateSheetViews(freezeTopRow: false));
            worksheet.Append(CreateColumns(new[] { 15D, 27D, 15D, 27D, 15D, 27D, 15D, 27D }));
            worksheet.Append(data);
            MergeCells merges = new();
            merges.Append(new MergeCell { Reference = "A1:H2" });
            merges.Append(new MergeCell { Reference = "A4:H4" });
            merges.Append(new MergeCell { Reference = "A11:H11" });
            merges.Append(new MergeCell { Reference = $"A{noteRowIndex}:H{noteRowIndex}" });
            worksheet.Append(merges);
            worksheet.Append(CreatePageMargins(), new PageSetup { Orientation = OrientationValues.Landscape, FitToWidth = 1, FitToHeight = 0 });
            part.Worksheet = worksheet;
            part.Worksheet.Save();
            sheets.Append(new Sheet { Id = workbookPart.GetIdOfPart(part), SheetId = sheetId, Name = "任务与结果" });
        }

        /// <summary>将任意 CSV 行集合写入普通明细表，并按内容自动设置列宽和筛选。</summary>
        private static void AddCsvSheet(
            WorkbookPart workbookPart,
            Sheets sheets,
            uint sheetId,
            string name,
            IReadOnlyList<string[]> rows,
            bool freezeTopRow,
            bool hidden = false)
        {
            WorksheetPart part = workbookPart.AddNewPart<WorksheetPart>();
            SheetData data = new();
            int columnCount = rows.Count == 0 ? 1 : Math.Max(1, rows.Max(row => row.Length));
            for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                string[] source = rows[rowIndex];
                bool longText = rowIndex > 0 &&
                                name == "任务快照" &&
                                source.Any(value => value?.Length > 18);
                Row row = new() { RowIndex = (uint)(rowIndex + 1), Height = rowIndex == 0 ? 28 : longText ? 42 : 20, CustomHeight = true };
                for (int column = 0; column < columnCount; column++)
                {
                    string value = column < source.Length ? source[column] : string.Empty;
                    string header = rows.Count > 0 && column < rows[0].Length ? rows[0][column] : string.Empty;
                    row.Append(rowIndex == 0 ? TextCell(value, 5) : DataCell(value, header));
                }
                AssignCellReferences(row);
                data.Append(row);
            }

            Worksheet worksheet = new();
            worksheet.Append(CreateSheetViews(freezeTopRow));
            worksheet.Append(CreateColumns(name == "任务快照" ? new[] { 25D, 65D } : CalculateColumnWidths(rows, columnCount)));
            worksheet.Append(data);
            if (rows.Count > 0)
                worksheet.Append(new AutoFilter { Reference = $"A1:{ColumnName(columnCount)}{rows.Count}" });
            worksheet.Append(CreatePageMargins(), new PageSetup { Orientation = OrientationValues.Landscape, FitToWidth = 1, FitToHeight = 0 });
            part.Worksheet = worksheet;
            part.Worksheet.Save();
            sheets.Append(new Sheet
            {
                Id = workbookPart.GetIdOfPart(part),
                SheetId = sheetId,
                Name = name,
                State = hidden ? SheetStateValues.Hidden : SheetStateValues.Visible
            });
        }

        /// <summary>把任务字段字典恢复为两列明细表。</summary>
        private static IReadOnlyList<string[]> BuildTaskSnapshotRows(IReadOnlyDictionary<string, string> task)
        {
            List<string[]> rows = new() { new[] { "字段", "值" } };
            rows.AddRange(task.Select(item => new[] { item.Key, item.Value }));
            return rows;
        }

        /// <summary>创建“标签—值”交替排列的汇总页行。</summary>
        private static Row SummaryPairRow(uint index, params string[] cells)
        {
            Row row = new() { RowIndex = index, Height = 24, CustomHeight = true };
            for (int column = 0; column < cells.Length; column++)
                row.Append(TextCell(cells[column], column % 2 == 0 ? 3U : 4U));
            AssignCellReferences(row);
            return row;
        }

        /// <summary>创建只有首单元格有内容的标题或说明行。</summary>
        private static Row SingleCellRow(uint index, string text, uint style)
        {
            Row row = new() { RowIndex = index, Height = 24, CustomHeight = true };
            row.Append(TextCell(text, style));
            AssignCellReferences(row);
            return row;
        }

        /// <summary>为行内单元格补全 A1、B1 等引用，确保 Excel 能稳定识别工作表结构。</summary>
        private static void AssignCellReferences(Row row)
        {
            if (row.RowIndex?.Value is not uint rowIndex) return;
            int column = 1;
            foreach (Cell cell in row.Elements<Cell>())
                cell.CellReference = ColumnName(column++) + rowIndex.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>依据表头将 CSV 文本转换为日期、数值或文本单元格，编号类字段始终保留文本格式。</summary>
        private static Cell DataCell(string value, string header)
        {
            if (string.IsNullOrWhiteSpace(value)) return TextCell(string.Empty, 4);
            if ((header.Contains("时间", StringComparison.Ordinal) || header.Contains("日期", StringComparison.Ordinal) || header.Contains("有效期", StringComparison.Ordinal)) &&
                DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out DateTime dateTime))
            {
                return NumberCell(dateTime.ToOADate(), header.Contains("时间", StringComparison.Ordinal) ? 8U : 7U);
            }

            if (!IsIdentifierColumn(header) && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
            {
                bool integer = Math.Abs(number - Math.Round(number)) < 0.0000001 &&
                               (header.Contains("序号", StringComparison.Ordinal) || header.Contains("数量", StringComparison.Ordinal) || header.Contains("通道", StringComparison.Ordinal) || header.Contains("样本", StringComparison.Ordinal));
                return NumberCell(number, integer ? 10U : 6U);
            }

            return TextCell(value, 4);
        }

        /// <summary>判断列是否属于不得转成数字的编号、HEX、寄存器或状态字段。</summary>
        private static bool IsIdentifierColumn(string header) =>
            header.Contains("任务编号", StringComparison.Ordinal) ||
            header.Contains("设备编号", StringComparison.Ordinal) ||
            header.Contains("证书编号", StringComparison.Ordinal) ||
            header.Contains("原始HEX", StringComparison.Ordinal) ||
            header.Contains("寄存器", StringComparison.Ordinal) ||
            header.Contains("状态", StringComparison.Ordinal);

        /// <summary>创建保留原始空格的内联文本单元格。</summary>
        private static Cell TextCell(string text, uint styleIndex) => new()
        {
            DataType = CellValues.InlineString,
            InlineString = new InlineString(new Text(text ?? string.Empty) { Space = SpaceProcessingModeValues.Preserve }),
            StyleIndex = styleIndex
        };

        /// <summary>使用不受系统区域影响的格式创建数值单元格。</summary>
        private static Cell NumberCell(double value, uint styleIndex) => new()
        {
            DataType = CellValues.Number,
            CellValue = new CellValue(value.ToString("R", CultureInfo.InvariantCulture)),
            StyleIndex = styleIndex
        };

        /// <summary>创建工作表视图，并按需冻结第一行表头。</summary>
        private static SheetViews CreateSheetViews(bool freezeTopRow)
        {
            SheetView view = new() { WorkbookViewId = 0U, ShowGridLines = false };
            if (freezeTopRow)
            {
                view.Append(new Pane
                {
                    VerticalSplit = 1D,
                    TopLeftCell = "A2",
                    ActivePane = PaneValues.BottomLeft,
                    State = PaneStateValues.Frozen
                });
            }
            return new SheetViews(view);
        }

        /// <summary>根据给定宽度列表创建 OpenXML 列定义。</summary>
        private static Columns CreateColumns(IReadOnlyList<double> widths)
        {
            Columns columns = new();
            for (int index = 0; index < widths.Count; index++)
                columns.Append(new Column { Min = (uint)(index + 1), Max = (uint)(index + 1), Width = widths[index], CustomWidth = true });
            return columns;
        }

        /// <summary>抽样前 200 行估算可读列宽，并限制过窄或过宽的列。</summary>
        private static IReadOnlyList<double> CalculateColumnWidths(IReadOnlyList<string[]> rows, int columnCount)
        {
            List<double> widths = new(columnCount);
            for (int column = 0; column < columnCount; column++)
            {
                int maximum = rows.Take(200)
                    .Select(row => column < row.Length ? row[column]?.Length ?? 0 : 0)
                    .DefaultIfEmpty(0)
                    .Max();
                widths.Add(Math.Clamp(maximum + 3D, 11D, column == 1 ? 24D : 30D));
            }
            return widths;
        }

        /// <summary>集中创建工作簿使用的字体、填充、边框、日期和数值格式。</summary>
        private static Stylesheet CreateStylesheet()
        {
            NumberingFormats numberingFormats = new(
                new NumberingFormat { NumberFormatId = 164U, FormatCode = "yyyy-mm-dd" },
                new NumberingFormat { NumberFormatId = 165U, FormatCode = "yyyy-mm-dd hh:mm:ss.000" },
                new NumberingFormat { NumberFormatId = 166U, FormatCode = "0.000" }) { Count = 3U };
            Fonts fonts = new(
                CreateFont("Microsoft YaHei", 10D, false, "FF1F2937"),
                CreateFont("Microsoft YaHei", 18D, true, "FFFFFFFF"),
                CreateFont("Microsoft YaHei", 10D, true, "FF17365D"),
                CreateFont("Microsoft YaHei", 10D, true, "FFFFFFFF"),
                CreateFont("Microsoft YaHei", 9D, false, "FF8A4B08"),
                CreateFont("SimSun", 10D, false, "FF000000"),
                CreateFont("SimSun", 16D, true, "FF000000"),
                CreateFont("SimSun", 11D, true, "FF000000"),
                CreateFont("SimSun", 9D, false, "FF000000")) { Count = 9U };
            Fills fills = new(
                new Fill(new PatternFill { PatternType = PatternValues.None }),
                new Fill(new PatternFill { PatternType = PatternValues.Gray125 }),
                SolidFill("FF17365D"),
                SolidFill("FFD9EAF7"),
                SolidFill("FF2F75B5"),
                SolidFill("FFEEF5FB"),
                SolidFill("FFFFF7E6")) { Count = 7U };
            Borders borders = new(
                new SpreadsheetBorder(),
                new SpreadsheetBorder(
                    ThinBorder<LeftBorder>(), ThinBorder<RightBorder>(), ThinBorder<TopBorder>(), ThinBorder<BottomBorder>(), new DiagonalBorder()),
                new SpreadsheetBorder(
                    BlackThinBorder<LeftBorder>(), BlackThinBorder<RightBorder>(), BlackThinBorder<TopBorder>(), BlackThinBorder<BottomBorder>(), new DiagonalBorder()),
                LayoutBorder(left: true, top: true),
                LayoutBorder(top: true),
                LayoutBorder(right: true, top: true),
                LayoutBorder(left: true),
                LayoutBorder(right: true),
                LayoutBorder(left: true, bottom: true),
                LayoutBorder(bottom: true),
                LayoutBorder(right: true, bottom: true)) { Count = 11U };
            CellStyleFormats styleFormats = new(new CellFormat()) { Count = 1U };
            CellFormats formats = new(
                new CellFormat(),
                Format(1, 2, 0, center: true, wrap: true),
                Format(2, 3, 0, wrap: true),
                Format(2, 5, 1, wrap: true),
                Format(0, 0, 1, wrap: true),
                Format(3, 4, 1, center: true, wrap: true),
                Format(0, 0, 1, numberFormatId: 166U),
                Format(0, 0, 1, numberFormatId: 164U),
                Format(0, 0, 1, numberFormatId: 165U),
                Format(4, 6, 0, wrap: true),
                Format(0, 0, 1, numberFormatId: 1U),
                Format(5, 0, 0, center: true, wrap: true),
                Format(6, 0, 0, center: true, wrap: true),
                Format(7, 0, 2, center: true, wrap: true),
                Format(5, 0, 2, wrap: true),
                Format(7, 0, 2, center: true, wrap: true),
                Format(7, 0, 2, center: true, wrap: true),
                Format(5, 0, 2, center: true, wrap: true),
                Format(7, 0, 0, wrap: true),
                Format(5, 0, 2, center: true, numberFormatId: 166U),
                Format(8, 0, 0, wrap: true),
                Format(7, 0, 0, center: true, wrap: true),
                Format(5, 0, 3, center: true, wrap: true),
                Format(5, 0, 4, center: true, wrap: true),
                Format(5, 0, 5, center: true, wrap: true),
                Format(5, 0, 6, center: true, wrap: true),
                Format(5, 0, 7, center: true, wrap: true),
                Format(5, 0, 8, center: true, wrap: true),
                Format(5, 0, 9, center: true, wrap: true),
                Format(5, 0, 10, center: true, wrap: true)) { Count = 30U };
            return new Stylesheet(numberingFormats, fonts, fills, borders, styleFormats, formats);
        }

        /// <summary>创建一个 OpenXML 字体定义。</summary>
        private static DocumentFormat.OpenXml.Spreadsheet.Font CreateFont(string name, double size, bool bold, string color)
        {
            DocumentFormat.OpenXml.Spreadsheet.Font font = new(new FontName { Val = name }, new FontSize { Val = size }, new Color { Rgb = color });
            if (bold) font.PrependChild(new Bold());
            return font;
        }

        /// <summary>创建指定 RGB 颜色的纯色填充。</summary>
        private static Fill SolidFill(string color) => new(new PatternFill(new ForegroundColor { Rgb = color }, new BackgroundColor { Indexed = 64U }) { PatternType = PatternValues.Solid });

        /// <summary>创建样式表中使用的细线边框边。</summary>
        private static T ThinBorder<T>() where T : BorderPropertiesType, new() => new() { Style = BorderStyleValues.Thin, Color = new Color { Rgb = "FFB4C7DC" } };

        /// <summary>创建附录 A 正式记录使用的黑色细线边框。</summary>
        private static T BlackThinBorder<T>() where T : BorderPropertiesType, new() => new() { Style = BorderStyleValues.Thin, Color = new Color { Rgb = "FF000000" } };

        /// <summary>按指定方向创建布点箱体的黑色细边框。</summary>
        private static SpreadsheetBorder LayoutBorder(bool left = false, bool right = false, bool top = false, bool bottom = false) =>
            new(
                left ? BlackThinBorder<LeftBorder>() : new LeftBorder(),
                right ? BlackThinBorder<RightBorder>() : new RightBorder(),
                top ? BlackThinBorder<TopBorder>() : new TopBorder(),
                bottom ? BlackThinBorder<BottomBorder>() : new BottomBorder(),
                new DiagonalBorder());

        /// <summary>组合字体、填充、边框、对齐和数值格式为一个单元格样式。</summary>
        private static CellFormat Format(uint fontId, uint fillId, uint borderId, bool center = false, bool wrap = false, uint? numberFormatId = null)
        {
            CellFormat format = new()
            {
                FontId = fontId,
                FillId = fillId,
                BorderId = borderId,
                ApplyFont = true,
                ApplyFill = true,
                ApplyBorder = borderId > 0
            };
            if (numberFormatId.HasValue)
            {
                format.NumberFormatId = numberFormatId.Value;
                format.ApplyNumberFormat = true;
            }
            if (center || wrap)
            {
                format.Alignment = new Alignment
                {
                    Horizontal = center ? HorizontalAlignmentValues.Center : HorizontalAlignmentValues.Left,
                    Vertical = VerticalAlignmentValues.Center,
                    WrapText = wrap
                };
                format.ApplyAlignment = true;
            }
            return format;
        }

        /// <summary>创建适合横向打印原始记录的统一页边距。</summary>
        private static PageMargins CreatePageMargins() => new() { Left = 0.3D, Right = 0.3D, Top = 0.5D, Bottom = 0.5D, Header = 0.2D, Footer = 0.2D };

        /// <summary>将第一行表头和第二行数据转换为字典，用于读取作业摘要。</summary>
        private static Dictionary<string, string> ToHeaderDictionary(IReadOnlyList<string[]> rows)
        {
            if (rows.Count < 2) throw new InvalidDataException("作业摘要没有表头和数据行。");
            Dictionary<string, string> result = new(StringComparer.Ordinal);
            for (int index = 0; index < rows[0].Length; index++)
                result[rows[0][index]] = index < rows[1].Length ? rows[1][index] : string.Empty;
            return result;
        }

        /// <summary>将“字段—值”两列任务快照转换为字典。</summary>
        private static Dictionary<string, string> ToPairDictionary(IReadOnlyList<string[]> rows)
        {
            Dictionary<string, string> result = new(StringComparer.Ordinal);
            foreach (string[] row in rows.Skip(1))
            {
                if (row.Length == 0 || string.IsNullOrWhiteSpace(row[0])) continue;
                result[row[0]] = row.Length > 1 ? row[1] : string.Empty;
            }
            return result;
        }

        /// <summary>重新打开生成文件并检查三个必需工作表及 JJF 1101 的前台/后台可见性。</summary>
        private static void ValidateWorkbook(string path)
        {
            using SpreadsheetDocument document = SpreadsheetDocument.Open(path, false);
            WorkbookPart? workbookPart = document.WorkbookPart;
            if (workbookPart?.Workbook is not Workbook workbook)
                throw new InvalidDataException("生成的 Excel 缺少工作簿结构。");
            Sheet[] sheets = workbook.GetFirstChild<Sheets>()?.Elements<Sheet>().ToArray() ?? Array.Empty<Sheet>();
            bool isJjf1101 = sheets.Any(sheet => sheet.Name?.Value == "校准记录");
            string[] required = isJjf1101
                ? new[] { "校准记录", "正式采样", "任务快照" }
                : new[] { "任务与结果", "正式采样", "任务快照" };
            bool invalidVisibility = isJjf1101 &&
                                     (sheets.Count(sheet => sheet.State?.Value == null || sheet.State.Value == SheetStateValues.Visible) != 1 ||
                                      sheets.Single(sheet => sheet.Name?.Value == "校准记录").State?.Value == SheetStateValues.Hidden);
            if (sheets.Length != required.Length || required.Any(name => sheets.All(sheet => sheet.Name?.Value != name)) || invalidVisibility)
                throw new InvalidDataException("生成的 Excel 工作表结构不完整。");
        }

        /// <summary>读取非空字段值，缺失或空白时使用回退文本。</summary>
        private static string Get(IReadOnlyDictionary<string, string> source, string key, string fallback = "") =>
            source.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;
        /// <summary>为存在的数值文本附加单位，空值显示为短横线。</summary>
        private static string AppendUnit(string value, string unit) => string.IsNullOrWhiteSpace(value) ? "-" : $"{value} {unit}";
        /// <summary>将任务中的温度、湿度和气压拼成一行环境条件。</summary>
        private static string BuildEnvironment(IReadOnlyDictionary<string, string> task)
        {
            string temperature = AppendUnit(Get(task, "环境温度(℃)"), "℃");
            string humidity = AppendUnit(Get(task, "环境湿度(%RH)"), "%RH");
            string pressure = AppendUnit(Get(task, "环境气压(kPa)"), "kPa");
            return $"{temperature} / {humidity} / {pressure}";
        }

        /// <summary>将从 1 开始的列号转换为 Excel 列名，例如 1→A、27→AA。</summary>
        private static string ColumnName(int oneBasedColumn)
        {
            string name = string.Empty;
            int number = oneBasedColumn;
            while (number > 0)
            {
                number--;
                name = (char)('A' + number % 26) + name;
                number /= 26;
            }
            return name;
        }
    }
}
