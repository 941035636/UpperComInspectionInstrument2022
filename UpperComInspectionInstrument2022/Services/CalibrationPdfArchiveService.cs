using System.IO;
using System.Globalization;
using System.Text;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Fonts;
using PdfSharp.Pdf.IO;

namespace UpperComInspectionInstrument2022.Services
{
    /// <summary>
    /// 从已完成作业的冻结 CSV 生成可独立交付的 PDF 归档报告。
    /// 服务不读取当前任务内存或系统设置，保证历史作业可重复生成一致的业务内容。
    /// </summary>
    public sealed class CalibrationPdfArchiveService
    {
        private const string SummaryFileName = "作业摘要.csv";
        private const string TaskFileName = "任务信息.csv";
        private const string ResultFileName = "校准结果.csv";
        private const string UncertaintyFileName = "不确定度分量.csv";
        private const string ReportDirectoryName = "报告";
        private const string ArchiveFileName = "校准归档.pdf";
        private const string ReportFontFamily = "CalibrationChinese";

        /// <summary>在首次排版前注册 Windows 中文字体解析器。</summary>
        static CalibrationPdfArchiveService()
        {
            GlobalFontSettings.FontResolver ??= new WindowsChineseFontResolver();
        }

        /// <summary>应用内共享的 PDF 归档报告生成服务。</summary>
        public static CalibrationPdfArchiveService Default { get; } = new();

        /// <summary>
        /// 校验作业状态和必需文件，原子生成 PDF，并重新打开检查 PDF 头、页数和标题。
        /// 数据格式 1.1 必须包含不确定度分量；1.0 历史作业允许生成，但会标明追溯限制。
        /// </summary>
        public bool TryGenerate(string jobDirectory, out string archivePath, out string error)
        {
            archivePath = string.Empty;
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
            string resultPath = Path.Combine(fullJobDirectory, ResultFileName);
            string uncertaintyPath = Path.Combine(fullJobDirectory, UncertaintyFileName);
            foreach (string requiredPath in new[] { summaryPath, taskPath, resultPath })
            {
                if (File.Exists(requiredPath)) continue;
                error = $"作业归档不完整，缺少文件：{Path.GetFileName(requiredPath)}";
                return false;
            }

            try
            {
                Dictionary<string, string> summary = ToHeaderDictionary(CalibrationFileStorageService.ReadCsvFile(summaryPath));
                if (!summary.TryGetValue("状态", out string? status) || status != "已完成")
                {
                    error = $"只有状态为“已完成”的作业才能生成 PDF 归档报告，当前状态：{status ?? "未知"}。";
                    return false;
                }

                bool requiresUncertaintyFile = Version.TryParse(Get(summary, "数据格式版本", "1.0"), out Version? version) &&
                                               version.CompareTo(new Version(1, 1)) >= 0;
                if (requiresUncertaintyFile && !File.Exists(uncertaintyPath))
                {
                    error = $"作业归档不完整，数据格式 {version} 必须包含文件：{UncertaintyFileName}";
                    return false;
                }

                Dictionary<string, string> task = ToPairDictionary(CalibrationFileStorageService.ReadCsvFile(taskPath));
                List<string[]> resultRows = CalibrationFileStorageService.ReadCsvFile(resultPath);
                List<string[]> uncertaintyRows = File.Exists(uncertaintyPath)
                    ? CalibrationFileStorageService.ReadCsvFile(uncertaintyPath)
                    : new List<string[]>();

                string reportDirectory = Path.Combine(fullJobDirectory, ReportDirectoryName);
                Directory.CreateDirectory(reportDirectory);
                archivePath = Path.Combine(reportDirectory, ArchiveFileName);
                string temporaryPath = Path.Combine(reportDirectory, $".{Guid.NewGuid():N}.校准归档.tmp.pdf");
                try
                {
                    CreateArchive(temporaryPath, summary, task, resultRows, uncertaintyRows);
                    ValidateArchive(temporaryPath);
                    File.Move(temporaryPath, archivePath, overwrite: true);
                }
                finally
                {
                    if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
                }

                error = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                error = $"PDF 归档报告生成失败：{ex.Message}\n作业目录：{fullJobDirectory}";
                archivePath = string.Empty;
                return false;
            }
        }

        /// <summary>创建标题、任务快照、结果、不确定度摘要、声明和签字区域。</summary>
        private static void CreateArchive(
            string path,
            IReadOnlyDictionary<string, string> summary,
            IReadOnlyDictionary<string, string> task,
            IReadOnlyList<string[]> resultRows,
            IReadOnlyList<string[]> uncertaintyRows)
        {
            Document document = new();
            document.Info.Title = "Calibration Archive Report";
            document.Info.Subject = Get(summary, "校准规范");
            document.Info.Author = "Calibration System";
            ConfigureStyles(document);

            Section section = document.AddSection();
            section.PageSetup.PageFormat = PageFormat.A4;
            section.PageSetup.Orientation = Orientation.Portrait;
            section.PageSetup.TopMargin = Unit.FromMillimeter(18);
            section.PageSetup.RightMargin = Unit.FromMillimeter(17);
            section.PageSetup.BottomMargin = Unit.FromMillimeter(18);
            section.PageSetup.LeftMargin = Unit.FromMillimeter(17);
            section.PageSetup.HeaderDistance = Unit.FromMillimeter(8);
            section.PageSetup.FooterDistance = Unit.FromMillimeter(8);
            AddHeaderAndFooter(section, summary);

            string standard = Get(summary, "校准规范", "校准规范未填写");
            string specificTitle = standard.StartsWith("JJF 1376", StringComparison.Ordinal)
                ? "箱式电阻炉校准归档报告"
                : "环境试验设备温湿度参数校准归档报告";
            AddCenteredParagraph(section, "校准归档报告", 10, true, Colors.DodgerBlue, 0, 1);
            AddCenteredParagraph(section, specificTitle, 20, true, Colors.DarkSlateGray, 0, 2);
            AddCenteredParagraph(section, $"依据 {standard}  ·  单工况作业  ·  任务编号 {Get(summary, "任务编号", "-")}", 9, false, Colors.DimGray, 0, 2);
            AddCenteredParagraph(section, "待审核签发", 9, true, Colors.DarkOrange, 0, 4);
            AddCallout(section,
                "本报告由系统根据已完成作业的冻结 CSV 自动生成。签发前应核验原始记录、标准器溯源状态、计算结果和签字信息；软件未根据参考技术指标自动给出合格或不合格结论。",
                Colors.LightBlue);

            AddHeading(section, "一、作业与被校设备信息");
            AddKeyValueTable(section, new[]
            {
                Pair("任务编号", Get(summary, "任务编号", "-")), Pair("作业状态", Get(summary, "状态", "-")),
                Pair("校准规范", standard), Pair("校准类型", Get(summary, "校准类型", "-")),
                Pair("被校设备", Get(task, "被校设备名称", "未填写（可选）")), Pair("设备编号", Get(task, "设备编号", "未填写（可选）")),
                Pair("型号规格", Get(task, "型号规格", "未填写（可选）")), Pair("制造单位", Get(task, "制造单位", "未填写（可选）")),
                Pair("测量范围", Get(task, "测量范围", "未填写（可选）")), Pair("委托单位", Get(task, "委托单位", "未填写（可选）")),
                Pair("校准地点", Get(task, "校准地点", "未填写（可选）")), Pair("校准日期", Get(task, "校准日期", "-"))
            });

            AddHeading(section, "二、标准器及溯源信息");
            AddKeyValueTable(section, new[]
            {
                Pair("标准器名称", Get(task, "标准器名称", "-")), Pair("标准器编号", Get(task, "标准器编号", "-")),
                Pair("型号", Get(task, "标准器型号", "-")), Pair("证书编号", Get(task, "标准器证书编号", "-")),
                Pair("有效期", Get(task, "标准器有效期", "-")), Pair("溯源机构", Get(task, "标准器溯源机构", "-")),
                Pair("温度范围", Get(task, "标准器温度范围", "-")), Pair("湿度范围", Get(task, "标准器湿度范围", "-")),
                Pair("温度分辨力", AppendUnit(Get(task, "标准器温度分辨力"), "℃")), Pair("湿度分辨力", AppendUnit(Get(task, "标准器湿度分辨力"), "%RH")),
                Pair("准确度/最大允许误差", Get(task, "标准器准确度", "-")), Pair("测温仪器/热电偶等级", BuildFurnaceStandard(task))
            });

            AddHeading(section, "三、校准条件与执行方案");
            AddKeyValueTable(section, new[]
            {
                Pair("设定温度", AppendUnit(Get(task, "设定温度(℃)"), "℃")), Pair("设定湿度", AppendUnit(Get(task, "设定湿度(%RH)"), "%RH")),
                Pair("温度测点", BuildPointSummary(task, "温度")), Pair("湿度测点", BuildPointSummary(task, "湿度")),
                Pair("传感器类型", Get(task, "传感器类型", "-")), Pair("工作区尺寸", BuildWorkZone(task)),
                Pair("环境条件", BuildEnvironment(task)), Pair("负载说明", Get(task, "负载说明", "无/未填写")),
                Pair("正式采样计划", $"{Get(task, "计划样本数", "-")} 组，每 {Get(task, "采样间隔(s)", "-")} s"), Pair("稳定等待", AppendUnit(Get(task, "稳定等待(min)"), "min")),
                Pair("布点说明", Get(task, "布点说明", "-")), Pair("偏离说明", Get(task, "偏离说明", "无"))
            });

            AddHeading(section, "四、校准结果");
            AddResultTable(section, resultRows);
            Paragraph resultNote = section.AddParagraph(BreakCjk("结果说明：以上量值来自归档的正式样本和规范计算结果；实时趋势数据不参与正式结果计算。"));
            resultNote.Style = "Note";

            AddHeading(section, "五、测量不确定度摘要");
            if (!AddUncertaintyTable(section, uncertaintyRows))
            {
                AddCallout(section,
                    "该历史归档未保存结构化不确定度分量，只能展示校准结果文件中的最终量值。签发前应查验原始评定资料。",
                    Colors.LightYellow);
            }

            AddHeading(section, "六、声明与签发");
            Paragraph declaration = section.AddParagraph(BreakCjk(
                "本报告仅对本次单工况、所列布点和归档正式样本负责。被校设备与委托档案中标记为“未填写（可选）”的字段，应在正式签发前按实验室管理程序补全或确认不适用。未经书面批准，不得部分复制本报告。"));
            declaration.Format.SpaceAfter = Unit.FromMillimeter(4);
            declaration.Format.KeepWithNext = true;
            Paragraph review = section.AddParagraph(BreakCjk("签发复核：□ 原始记录与结果一致  □ 标准器证书在有效期内  □ 偏离与环境条件已确认"));
            review.Format.Font.Size = Unit.FromPoint(8);
            review.Format.SpaceAfter = Unit.FromMillimeter(4);
            review.Format.KeepWithNext = true;
            AddSignatureTable(section, task);
            AddCenteredParagraph(section,
                $"报告生成时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}    数据格式版本：{Get(summary, "数据格式版本", "1.0")}",
                7.5, false, Colors.Gray, 4, 0);

            PdfDocumentRenderer renderer = new() { Document = document };
            renderer.RenderDocument();
            renderer.PdfDocument.Info.Title = "Calibration Archive Report";
            renderer.PdfDocument.Info.Subject = standard;
            renderer.PdfDocument.Info.Author = "Calibration System";
            renderer.Save(path);
        }

        /// <summary>建立中文字体、标题和说明样式。</summary>
        private static void ConfigureStyles(Document document)
        {
            Style normal = document.Styles[StyleNames.Normal]!;
            normal.Font.Name = ReportFontFamily;
            normal.Font.Size = Unit.FromPoint(8.5);
            normal.Font.Color = Colors.Black;
            normal.ParagraphFormat.LineSpacingRule = LineSpacingRule.Single;
            normal.ParagraphFormat.SpaceAfter = Unit.FromPoint(2);

            Style heading = document.Styles.AddStyle("SectionHeading", StyleNames.Normal);
            heading.Font.Name = ReportFontFamily;
            heading.Font.Size = Unit.FromPoint(12);
            heading.Font.Bold = true;
            heading.Font.Color = Colors.DarkSlateGray;
            heading.ParagraphFormat.SpaceBefore = Unit.FromMillimeter(4);
            heading.ParagraphFormat.SpaceAfter = Unit.FromMillimeter(2);
            heading.ParagraphFormat.KeepWithNext = true;

            Style note = document.Styles.AddStyle("Note", StyleNames.Normal);
            note.Font.Name = ReportFontFamily;
            note.Font.Size = Unit.FromPoint(7.5);
            note.Font.Color = Colors.DimGray;
            note.ParagraphFormat.SpaceBefore = Unit.FromMillimeter(1.5);
            note.ParagraphFormat.SpaceAfter = Unit.FromMillimeter(2);
        }

        /// <summary>添加每页可追溯的页眉和页码页脚。</summary>
        private static void AddHeaderAndFooter(Section section, IReadOnlyDictionary<string, string> summary)
        {
            Paragraph header = section.Headers.Primary.AddParagraph();
            header.AddText($"温湿度校准系统  |  {Get(summary, "任务编号", "-")}  |  {Get(summary, "校准规范", "-")}");
            header.Format.Font.Name = ReportFontFamily;
            header.Format.Font.Size = Unit.FromPoint(7.5);
            header.Format.Font.Color = Colors.Gray;
            header.Format.Borders.Bottom.Width = Unit.FromPoint(0.5);
            header.Format.Borders.Bottom.Color = Colors.LightGray;
            header.Format.SpaceAfter = Unit.FromMillimeter(2);

            Paragraph footer = section.Footers.Primary.AddParagraph();
            footer.Format.Alignment = ParagraphAlignment.Center;
            footer.Format.Font.Name = ReportFontFamily;
            footer.Format.Font.Size = Unit.FromPoint(7.5);
            footer.Format.Font.Color = Colors.Gray;
            footer.AddText("第 ");
            footer.AddPageField();
            footer.AddText(" 页 / 共 ");
            footer.AddNumPagesField();
            footer.AddText(" 页  ·  本地冻结 CSV 归档");
        }

        /// <summary>添加居中标题或状态文字。</summary>
        private static void AddCenteredParagraph(Section section, string text, double size, bool bold, Color color, double spaceBeforeMm, double spaceAfterMm)
        {
            Paragraph paragraph = section.AddParagraph(BreakCjk(text));
            paragraph.Format.Alignment = ParagraphAlignment.Center;
            paragraph.Format.Font.Name = ReportFontFamily;
            paragraph.Format.Font.Size = Unit.FromPoint(size);
            paragraph.Format.Font.Bold = bold;
            paragraph.Format.Font.Color = color;
            paragraph.Format.SpaceBefore = Unit.FromMillimeter(spaceBeforeMm);
            paragraph.Format.SpaceAfter = Unit.FromMillimeter(spaceAfterMm);
        }

        /// <summary>添加带底色的审阅提示框。</summary>
        private static void AddCallout(Section section, string text, Color background)
        {
            Table table = section.AddTable();
            table.AddColumn(Unit.FromMillimeter(176));
            table.Borders.Width = Unit.FromPoint(0.5);
            table.Borders.Color = Colors.LightGray;
            Row row = table.AddRow();
            row.Shading.Color = background;
            row.TopPadding = Unit.FromMillimeter(2.5);
            row.BottomPadding = Unit.FromMillimeter(2.5);
            Paragraph paragraph = row.Cells[0].AddParagraph(BreakCjk(text));
            paragraph.Format.Font.Size = Unit.FromPoint(8);
            paragraph.Format.LeftIndent = Unit.FromMillimeter(3);
            paragraph.Format.RightIndent = Unit.FromMillimeter(3);
            table.Format.SpaceAfter = Unit.FromMillimeter(3);
        }

        /// <summary>添加章节标题。</summary>
        private static void AddHeading(Section section, string text)
        {
            Paragraph paragraph = section.AddParagraph(BreakCjk(text));
            paragraph.Style = "SectionHeading";
        }

        /// <summary>以“标签—值—标签—值”形式添加任务快照。</summary>
        private static void AddKeyValueTable(Section section, IReadOnlyList<(string Label, string Value)> values)
        {
            Table table = section.AddTable();
            table.AddColumn(Unit.FromMillimeter(27));
            table.AddColumn(Unit.FromMillimeter(61));
            table.AddColumn(Unit.FromMillimeter(27));
            table.AddColumn(Unit.FromMillimeter(61));
            ConfigureTable(table);
            for (int index = 0; index < values.Count; index += 2)
            {
                Row row = table.AddRow();
                SetKeyValueCell(row.Cells[0], values[index].Label, true);
                SetKeyValueCell(row.Cells[1], values[index].Value, false);
                if (index + 1 < values.Count)
                {
                    SetKeyValueCell(row.Cells[2], values[index + 1].Label, true);
                    SetKeyValueCell(row.Cells[3], values[index + 1].Value, false);
                }
            }
            table.Format.SpaceAfter = Unit.FromMillimeter(2);
        }

        /// <summary>添加校准结果明细表。</summary>
        private static void AddResultTable(Section section, IReadOnlyList<string[]> rows)
        {
            Table table = section.AddTable();
            table.AddColumn(Unit.FromMillimeter(42));
            table.AddColumn(Unit.FromMillimeter(25));
            table.AddColumn(Unit.FromMillimeter(18));
            table.AddColumn(Unit.FromMillimeter(91));
            ConfigureTable(table);
            AddCsvRows(table, rows, 4);
            table.Format.SpaceAfter = Unit.FromMillimeter(1);
        }

        /// <summary>从分量 CSV 提取每个评定项目的 uc、k、U 汇总；无可用分量时返回 false。</summary>
        private static bool AddUncertaintyTable(Section section, IReadOnlyList<string[]> rows)
        {
            if (rows.Count < 2) return false;
            Dictionary<string, int> columns = rows[0]
                .Select((value, index) => (value, index))
                .Where(item => !string.IsNullOrWhiteSpace(item.value))
                .ToDictionary(item => item.value, item => item.index, StringComparer.Ordinal);
            string[] required = { "结果项目", "评定点", "合成标准不确定度uc", "包含因子k", "扩展不确定度U" };
            if (required.Any(name => !columns.ContainsKey(name))) return false;

            List<string[]> summaryRows = new() { new[] { "结果项目", "评定点", "uc", "k", "U" } };
            foreach (string[] row in rows.Skip(1)
                         .GroupBy(row => $"{Cell(row, columns["结果项目"])}\u001f{Cell(row, columns["评定点"])}", StringComparer.Ordinal)
                         .Select(group => group.First()))
            {
                summaryRows.Add(new[]
                {
                    Cell(row, columns["结果项目"]), Cell(row, columns["评定点"]),
                    Cell(row, columns["合成标准不确定度uc"]), Cell(row, columns["包含因子k"]), Cell(row, columns["扩展不确定度U"])
                });
            }
            if (summaryRows.Count == 1) return false;

            Table table = section.AddTable();
            table.AddColumn(Unit.FromMillimeter(49));
            table.AddColumn(Unit.FromMillimeter(49));
            table.AddColumn(Unit.FromMillimeter(26));
            table.AddColumn(Unit.FromMillimeter(18));
            table.AddColumn(Unit.FromMillimeter(34));
            ConfigureTable(table);
            AddCsvRows(table, summaryRows, 5);
            table.Format.SpaceAfter = Unit.FromMillimeter(2);
            return true;
        }

        /// <summary>添加校准员、核验员和批准人的签字留白。</summary>
        private static void AddSignatureTable(Section section, IReadOnlyDictionary<string, string> task)
        {
            Table table = section.AddTable();
            table.AddColumn(Unit.FromMillimeter(58.7));
            table.AddColumn(Unit.FromMillimeter(58.7));
            table.AddColumn(Unit.FromMillimeter(58.6));
            ConfigureTable(table);
            Row header = table.AddRow();
            string[] labels = { "校准员", "核验员", "批准人" };
            for (int index = 0; index < labels.Length; index++) SetKeyValueCell(header.Cells[index], labels[index], true);
            Row names = table.AddRow();
            names.TopPadding = Unit.FromMillimeter(5);
            names.BottomPadding = Unit.FromMillimeter(5);
            string[] values = { Get(task, "校准员", "签字：____________"), Get(task, "核验员", "签字：____________"), "签字：____________" };
            for (int index = 0; index < values.Length; index++)
            {
                Paragraph paragraph = names.Cells[index].AddParagraph(values[index]);
                paragraph.Format.Alignment = ParagraphAlignment.Center;
            }
        }

        /// <summary>设置所有归档表格的统一边框、内边距和字号。</summary>
        private static void ConfigureTable(Table table)
        {
            table.Borders.Width = Unit.FromPoint(0.5);
            table.Borders.Color = Colors.LightGray;
            table.Format.Font.Name = ReportFontFamily;
            table.Format.Font.Size = Unit.FromPoint(7.5);
            table.Rows.LeftIndent = Unit.Zero;
        }

        /// <summary>写入普通 CSV 表格，首行作为可跨页重复的表头。</summary>
        private static void AddCsvRows(Table table, IReadOnlyList<string[]> rows, int columnCount)
        {
            for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                Row row = table.AddRow();
                row.HeadingFormat = rowIndex == 0;
                row.TopPadding = Unit.FromMillimeter(1.2);
                row.BottomPadding = Unit.FromMillimeter(1.2);
                if (rowIndex == 0)
                {
                    row.Shading.Color = Colors.LightBlue;
                    row.Format.Font.Bold = true;
                }
                for (int columnIndex = 0; columnIndex < columnCount; columnIndex++)
                {
                    string value = Cell(rows[rowIndex], columnIndex);
                    string displayValue = FormatTableValue(value, rowIndex, columnIndex);
                    Paragraph paragraph = row.Cells[columnIndex].AddParagraph(BreakCjk(string.IsNullOrWhiteSpace(displayValue) ? "-" : displayValue));
                    paragraph.Format.Alignment = columnIndex is 1 or 2 ? ParagraphAlignment.Center : ParagraphAlignment.Left;
                    paragraph.Format.LeftIndent = Unit.FromMillimeter(1.5);
                    paragraph.Format.RightIndent = Unit.FromMillimeter(1.5);
                    row.Cells[columnIndex].VerticalAlignment = VerticalAlignment.Center;
                }
            }
        }

        /// <summary>设置键值表中的标签或内容单元格。</summary>
        private static void SetKeyValueCell(Cell cell, string text, bool isLabel)
        {
            cell.Shading.Color = isLabel ? Colors.LightBlue : Colors.White;
            cell.VerticalAlignment = VerticalAlignment.Center;
            Paragraph paragraph = cell.AddParagraph(BreakCjk(string.IsNullOrWhiteSpace(text) ? "-" : text));
            paragraph.Format.Font.Bold = isLabel;
            paragraph.Format.LeftIndent = Unit.FromMillimeter(1.8);
            paragraph.Format.RightIndent = Unit.FromMillimeter(1.8);
        }

        /// <summary>重新打开 PDF，检查文件标识、页数和元数据标题。</summary>
        private static void ValidateArchive(string path)
        {
            using FileStream stream = File.OpenRead(path);
            byte[] header = new byte[5];
            if (stream.Read(header, 0, header.Length) != header.Length || System.Text.Encoding.ASCII.GetString(header) != "%PDF-")
                throw new InvalidDataException("生成文件不是有效的 PDF。 ");
            if (stream.Length < 5000)
                throw new InvalidDataException("生成的 PDF 内容异常短。 ");
            stream.Position = 0;
            using PdfSharp.Pdf.PdfDocument document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
            if (document.PageCount < 1)
                throw new InvalidDataException("生成的 PDF 没有可用页面。 ");
            if (!string.Equals(document.Info.Title, "Calibration Archive Report", StringComparison.Ordinal))
                throw new InvalidDataException("生成的 PDF 缺少归档标题元数据。 ");
        }

        /// <summary>将第一行表头和第二行数据转换为字典。</summary>
        private static Dictionary<string, string> ToHeaderDictionary(IReadOnlyList<string[]> rows)
        {
            if (rows.Count < 2) throw new InvalidDataException("作业摘要没有表头和数据行。 ");
            Dictionary<string, string> result = new(StringComparer.Ordinal);
            for (int index = 0; index < rows[0].Length; index++)
                result[rows[0][index]] = index < rows[1].Length ? rows[1][index] : string.Empty;
            return result;
        }

        /// <summary>将任务快照的“字段—值”两列转换为字典。</summary>
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

        /// <summary>安全取得数组单元格。</summary>
        private static string Cell(string[] row, int index) => index >= 0 && index < row.Length ? row[index] : string.Empty;
        /// <summary>安全取得字典字段，空值使用回退文本。</summary>
        private static string Get(IReadOnlyDictionary<string, string> source, string key, string fallback = "") =>
            source.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;
        /// <summary>空白值显示短横线，否则附加单位。</summary>
        private static string AppendUnit(string value, string unit) => string.IsNullOrWhiteSpace(value) ? "-" : $"{value} {unit}";
        /// <summary>创建标签和值字段。</summary>
        private static (string Label, string Value) Pair(string label, string value) => (label, value);
        /// <summary>组合温度、湿度和气压环境条件。</summary>
        private static string BuildEnvironment(IReadOnlyDictionary<string, string> task) =>
            $"{AppendUnit(Get(task, "环境温度(℃)"), "℃")} / {AppendUnit(Get(task, "环境湿度(%RH)"), "%RH")} / {AppendUnit(Get(task, "环境气压(kPa)"), "kPa")}";
        /// <summary>组合长、宽、高工作区尺寸。</summary>
        private static string BuildWorkZone(IReadOnlyDictionary<string, string> task) =>
            $"{Get(task, "工作区长度(mm)", "-")} × {Get(task, "工作区宽度(mm)", "-")} × {Get(task, "工作区高度(mm)", "-")} mm";
        /// <summary>组合温度或湿度点数和中心点。</summary>
        private static string BuildPointSummary(IReadOnlyDictionary<string, string> task, string type)
        {
            string count = Get(task, type + "测点数", "0");
            string center = Get(task, type + "中心点", "0");
            return count == "0" ? "不适用" : $"{count} 点，中心点 {center}";
        }
        /// <summary>组合箱式炉标准器级别字段。</summary>
        private static string BuildFurnaceStandard(IReadOnlyDictionary<string, string> task)
        {
            string instrumentClass = Get(task, "测温仪器级别");
            string thermocoupleGrade = Get(task, "热电偶等级");
            return string.IsNullOrWhiteSpace(instrumentClass) && string.IsNullOrWhiteSpace(thermocoupleGrade)
                ? "不适用"
                : $"{(string.IsNullOrWhiteSpace(instrumentClass) ? "-" : instrumentClass)} / {(string.IsNullOrWhiteSpace(thermocoupleGrade) ? "-" : thermocoupleGrade)}";
        }

        /// <summary>控制报告表格中的浮点显示长度，CSV 原值仍完整保留。</summary>
        private static string FormatTableValue(string value, int rowIndex, int columnIndex)
        {
            if (rowIndex == 0 || columnIndex == 0 ||
                !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) ||
                !double.IsFinite(number))
                return value;
            return number.ToString("0.######", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// 在中日韩文字后插入不可见换行机会，解决 MigraDoc 对无空格中文长句不自动换行的问题。
        /// </summary>
        private static string BreakCjk(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            StringBuilder builder = new(text.Length * 2);
            foreach (char character in text)
            {
                builder.Append(character);
                if ((character >= '\u2E80' && character <= '\u9FFF') ||
                    (character >= '\uF900' && character <= '\uFAFF'))
                    builder.Append('\u200B');
            }
            return builder.ToString();
        }

        /// <summary>
        /// 将报告内的逻辑中文字体映射到 Windows 自带的等线字体，并把字形嵌入 PDF。
        /// 直接读取 TTF 可避免不同 PDFsharp 后端对中文字体家族名称识别不一致。
        /// </summary>
        private sealed class WindowsChineseFontResolver : IFontResolver
        {
            private const string RegularFace = "CalibrationChinese-Regular";
            private const string BoldFace = "CalibrationChinese-Bold";

            /// <summary>根据粗体请求返回对应的内部字形标识。</summary>
            public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic) =>
                new(isBold ? BoldFace : RegularFace);

            /// <summary>读取 Windows 字体文件；缺少粗体文件时安全回退到常规字体。</summary>
            public byte[] GetFont(string faceName)
            {
                string fontDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
                string fileName = faceName == BoldFace ? "Dengb.ttf" : "Deng.ttf";
                string path = Path.Combine(fontDirectory, fileName);
                if (!File.Exists(path)) path = Path.Combine(fontDirectory, "simhei.ttf");
                if (!File.Exists(path))
                    throw new FileNotFoundException("系统缺少生成中文 PDF 所需的等线或黑体字体。", path);
                return File.ReadAllBytes(path);
            }
        }
    }
}
