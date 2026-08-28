//using System.Drawing;
using System.Globalization;
using System.IO;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
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
        private const string RawChannelFileName = "正式采样原始通道.csv";
        private const string ResultFileName = "校准结果.csv";
        private const string UncertaintyFileName = "不确定度分量.csv";
        private const string ReportDirectoryName = "报告";
        private const string ReportFileName = "校准原始记录.xlsx";

        /// <summary>应用内共享的 Excel 报告生成服务。</summary>
        public static CalibrationExcelReportService Default { get; } = new();

        /// <summary>
        /// 验证作业归档完整性，从固化 CSV 生成可用 Excel/WPS 打开的原始记录工作簿。
        /// 1.0 历史作业没有不确定度分量文件时仍允许生成，并在工作表中明确标记该追溯限制。
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
            string rawPath = Path.Combine(fullJobDirectory, RawChannelFileName);
            string resultPath = Path.Combine(fullJobDirectory, ResultFileName);
            string uncertaintyPath = Path.Combine(fullJobDirectory, UncertaintyFileName);

            foreach (string requiredPath in new[] { summaryPath, taskPath, samplePath, rawPath, resultPath })
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
                List<string[]> rawRows = CalibrationFileStorageService.ReadCsvFile(rawPath);
                List<string[]> resultRows = CalibrationFileStorageService.ReadCsvFile(resultPath);
                Dictionary<string, string> summary = ToHeaderDictionary(summaryRows);
                if (!summary.TryGetValue("状态", out string? status) || status != "已完成")
                {
                    error = $"只有状态为“已完成”的作业才能生成正式 Excel 原始记录，当前状态：{status ?? "未知"}。";
                    return false;
                }
                bool requiresUncertaintyFile = Version.TryParse(Get(summary, "数据格式版本", "1.0"), out Version? dataVersion) &&
                                               dataVersion.CompareTo(new Version(1, 1)) >= 0;
                if (requiresUncertaintyFile && !File.Exists(uncertaintyPath))
                {
                    error = $"作业归档不完整，数据格式 {dataVersion} 必须包含文件：{UncertaintyFileName}";
                    return false;
                }

                Dictionary<string, string> task = ToPairDictionary(taskRows);
                List<string[]> uncertaintyRows = File.Exists(uncertaintyPath)
                    ? CalibrationFileStorageService.ReadCsvFile(uncertaintyPath)
                    : BuildLegacyUncertaintyRows(summary);
                string reportDirectory = Path.Combine(fullJobDirectory, ReportDirectoryName);
                Directory.CreateDirectory(reportDirectory);
                reportPath = Path.Combine(reportDirectory, ReportFileName);
                string temporaryPath = Path.Combine(reportDirectory, $".{ReportFileName}.{Guid.NewGuid():N}.tmp");
                try
                {
                    CreateWorkbook(temporaryPath, summary, task, sampleRows, rawRows, resultRows, uncertaintyRows);
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

        /// <summary>创建工作簿、公共样式以及五个固定工作表。</summary>
        private static void CreateWorkbook(
            string path,
            IReadOnlyDictionary<string, string> summary,
            IReadOnlyDictionary<string, string> task,
            IReadOnlyList<string[]> sampleRows,
            IReadOnlyList<string[]> rawRows,
            IReadOnlyList<string[]> resultRows,
            IReadOnlyList<string[]> uncertaintyRows)
        {
            using SpreadsheetDocument document = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
            WorkbookPart workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();
            WorkbookStylesPart stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
            stylesPart.Stylesheet = CreateStylesheet();
            stylesPart.Stylesheet.Save();

            Sheets sheets = workbookPart.Workbook.AppendChild(new Sheets());
            uint sheetId = 1;
            AddSummarySheet(workbookPart, sheets, sheetId++, summary, task, resultRows);
            AddCsvSheet(workbookPart, sheets, sheetId++, "不确定度分量", uncertaintyRows, freezeTopRow: true);
            AddCsvSheet(workbookPart, sheets, sheetId++, "正式采样", sampleRows, freezeTopRow: true);
            AddCsvSheet(workbookPart, sheets, sheetId++, "原始通道", rawRows, freezeTopRow: true);
            AddCsvSheet(workbookPart, sheets, sheetId, "任务快照", BuildTaskSnapshotRows(task), freezeTopRow: true);
            workbookPart.Workbook.CalculationProperties = new CalculationProperties { CalculationMode = CalculateModeValues.Auto };
            workbookPart.Workbook.Save();
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
            bool freezeTopRow)
        {
            WorksheetPart part = workbookPart.AddNewPart<WorksheetPart>();
            SheetData data = new();
            int columnCount = rows.Count == 0 ? 1 : Math.Max(1, rows.Max(row => row.Length));
            for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                string[] source = rows[rowIndex];
                bool longText = rowIndex > 0 &&
                                ((name == "任务快照" && source.Any(value => value?.Length > 18)) ||
                                 (name == "不确定度分量" && source.Any(value => value?.Length > 24)));
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
            sheets.Append(new Sheet { Id = workbookPart.GetIdOfPart(part), SheetId = sheetId, Name = name });
        }

        /// <summary>把任务字段字典恢复为两列明细表。</summary>
        private static IReadOnlyList<string[]> BuildTaskSnapshotRows(IReadOnlyDictionary<string, string> task)
        {
            List<string[]> rows = new() { new[] { "字段", "值" } };
            rows.AddRange(task.Select(item => new[] { item.Key, item.Value }));
            return rows;
        }

        /// <summary>
        /// 为 1.0 旧作业生成明确的兼容提示行。旧归档仍能打开，但不会伪造当时未保存的分量数据。
        /// </summary>
        private static List<string[]> BuildLegacyUncertaintyRows(IReadOnlyDictionary<string, string> summary)
        {
            return new List<string[]>
            {
                new[]
                {
                    "结果项目", "评定点", "分量序号", "符号", "不确定度来源", "类别", "分布",
                    "输入量", "单位", "除数", "除数表达式", "标准不确定度ui", "灵敏系数ci", "贡献ci×ui",
                    "合成标准不确定度uc", "包含因子k", "扩展不确定度U", "分量依据", "合成依据"
                },
                new[]
                {
                    "历史归档未保存分量明细", string.Empty, string.Empty, string.Empty,
                    $"数据格式版本 {Get(summary, "数据格式版本", "1.0")}", string.Empty, string.Empty,
                    string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
                    string.Empty, string.Empty, string.Empty,
                    "该作业仅保存最终结果，无法可靠反推各分量。", "请保留原始 CSV，并在具备原始输入时人工复核。"
                }
            };
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
                CreateFont("Microsoft YaHei", 9D, false, "FF8A4B08")) { Count = 5U };
            Fills fills = new(
                new Fill(new PatternFill { PatternType = PatternValues.None }),
                new Fill(new PatternFill { PatternType = PatternValues.Gray125 }),
                SolidFill("FF17365D"),
                SolidFill("FFD9EAF7"),
                SolidFill("FF2F75B5"),
                SolidFill("FFEEF5FB"),
                SolidFill("FFFFF7E6")) { Count = 7U };
            Borders borders = new(
                new Border(),
                new Border(
                    ThinBorder<LeftBorder>(), ThinBorder<RightBorder>(), ThinBorder<TopBorder>(), ThinBorder<BottomBorder>(), new DiagonalBorder())) { Count = 2U };
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
                Format(0, 0, 1, numberFormatId: 1U)) { Count = 11U };
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

        /// <summary>重新打开生成文件并检查五个必需工作表，尽早发现不完整工作簿。</summary>
        private static void ValidateWorkbook(string path)
        {
            using SpreadsheetDocument document = SpreadsheetDocument.Open(path, false);
            WorkbookPart? workbookPart = document.WorkbookPart;
            if (workbookPart?.Workbook is not Workbook workbook)
                throw new InvalidDataException("生成的 Excel 缺少工作簿结构。");
            Sheet[] sheets = workbook.GetFirstChild<Sheets>()?.Elements<Sheet>().ToArray() ?? Array.Empty<Sheet>();
            string[] required = { "任务与结果", "不确定度分量", "正式采样", "原始通道", "任务快照" };
            if (sheets.Length != required.Length || required.Any(name => sheets.All(sheet => sheet.Name?.Value != name)))
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
