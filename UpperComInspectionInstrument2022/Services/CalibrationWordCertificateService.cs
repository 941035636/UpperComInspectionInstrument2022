using System.Globalization;
using System.IO;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace UpperComInspectionInstrument2022.Services
{
    /// <summary>
    /// 从已完成作业的固化 CSV 生成单工况 Word 校准证书。
    /// 生成过程不读取当前任务内存或系统设置，保证历史作业可以重复得到一致的业务内容。
    /// </summary>
    public sealed class CalibrationWordCertificateService
    {
        private const string SummaryFileName = "作业摘要.csv";
        private const string TaskFileName = "任务信息.csv";
        private const string ResultFileName = "校准结果.csv";
        private const string UncertaintyFileName = "不确定度分量.csv";
        private const string ReportDirectoryName = "报告";
        private const string CertificateFileName = "校准证书.docx";
        private const int ContentWidth = 9360;
        private const int TableIndent = 120;

        /// <summary>应用内共享的 Word 证书生成服务。</summary>
        public static CalibrationWordCertificateService Default { get; } = new();

        /// <summary>
        /// 检查归档状态和必需文件，生成证书后重新打开并执行 OpenXML 结构验证。
        /// 数据格式 1.1 必须包含不确定度分量；1.0 历史作业允许生成，但会明确标注追溯限制。
        /// </summary>
        public bool TryGenerate(string jobDirectory, out string certificatePath, out string error)
        {
            certificatePath = string.Empty;
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
                    error = $"只有状态为“已完成”的作业才能生成 Word 校准证书，当前状态：{status ?? "未知"}。";
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
                certificatePath = Path.Combine(reportDirectory, CertificateFileName);
                string temporaryPath = Path.Combine(reportDirectory, $".{Guid.NewGuid():N}.校准证书.tmp.docx");
                try
                {
                    CreateCertificate(temporaryPath, summary, task, resultRows, uncertaintyRows);
                    ValidateCertificate(temporaryPath);
                    File.Move(temporaryPath, certificatePath, overwrite: true);
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
                error = $"Word 校准证书生成失败：{ex.Message}\n作业目录：{fullJobDirectory}";
                certificatePath = string.Empty;
                return false;
            }
        }

        /// <summary>创建标题、归档快照、结果、不确定度摘要、声明和签字区域。</summary>
        private static void CreateCertificate(
            string path,
            IReadOnlyDictionary<string, string> summary,
            IReadOnlyDictionary<string, string> task,
            IReadOnlyList<string[]> resultRows,
            IReadOnlyList<string[]> uncertaintyRows)
        {
            using WordprocessingDocument document = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
            document.PackageProperties.Title = "单工况校准证书";
            document.PackageProperties.Subject = Get(summary, "校准规范");
            document.PackageProperties.Creator = "温湿度校准系统";
            document.PackageProperties.Created = DateTime.UtcNow;

            MainDocumentPart mainPart = document.AddMainDocumentPart();
            mainPart.Document = new W.Document();
            AddStyles(mainPart);
            AddSettings(mainPart);
            (string headerId, string footerId) = AddHeaderAndFooter(mainPart, summary);

            W.Body body = new();
            mainPart.Document.Append(body);
            string standard = Get(summary, "校准规范", "校准规范未填写");
            string specificTitle = standard.StartsWith("JJF 1376", StringComparison.Ordinal)
                ? "箱式电阻炉校准证书"
                : "环境试验设备温湿度参数校准证书";
            body.Append(
                Paragraph("校准证书", "CertificateKicker", W.JustificationValues.Center),
                Paragraph(specificTitle, "Title", W.JustificationValues.Center),
                Paragraph($"依据 {standard}  ·  单工况作业  ·  任务编号 {Get(summary, "任务编号", "-")}", "Subtitle", W.JustificationValues.Center),
                Paragraph("待审核签发", "Status", W.JustificationValues.Center));

            body.Append(Callout(
                "本证书由系统根据已完成作业的固化 CSV 自动生成。签发前应核验原始记录、标准器溯源状态、计算结果及签字信息；本系统未根据参考技术指标自动作出合格或不合格判定。"));

            body.Append(Heading("一、作业与被校设备信息"));
            body.Append(KeyValueTable(new[]
            {
                Pair("任务编号", Get(summary, "任务编号", "-")), Pair("作业状态", Get(summary, "状态", "-")),
                Pair("校准规范", standard), Pair("校准类型", Get(summary, "校准类型", "-")),
                Pair("被校设备", Get(task, "被校设备名称", "未填写（可选）")), Pair("设备编号", Get(task, "设备编号", "未填写（可选）")),
                Pair("型号规格", Get(task, "型号规格", "未填写（可选）")), Pair("制造单位", Get(task, "制造单位", "未填写（可选）")),
                Pair("测量范围", Get(task, "测量范围", "未填写（可选）")), Pair("委托单位", Get(task, "委托单位", "未填写（可选）")),
                Pair("校准地点", Get(task, "校准地点", "未填写（可选）")), Pair("校准日期", Get(task, "校准日期", "-"))
            }));

            body.Append(Heading("二、标准器及溯源信息"));
            body.Append(KeyValueTable(new[]
            {
                Pair("标准器名称", Get(task, "标准器名称", "-")), Pair("标准器编号", Get(task, "标准器编号", "-")),
                Pair("型号", Get(task, "标准器型号", "-")), Pair("证书编号", Get(task, "标准器证书编号", "-")),
                Pair("有效期", Get(task, "标准器有效期", "-")), Pair("溯源机构", Get(task, "标准器溯源机构", "-")),
                Pair("温度范围", Get(task, "标准器温度范围", "-")), Pair("湿度范围", Get(task, "标准器湿度范围", "-")),
                Pair("温度分辨力", AppendUnit(Get(task, "标准器温度分辨力"), "℃")), Pair("湿度分辨力", AppendUnit(Get(task, "标准器湿度分辨力"), "%RH")),
                Pair("准确度/最大允许误差", Get(task, "标准器准确度", "-")), Pair("测温仪器/热电偶等级", BuildFurnaceStandard(task))
            }));

            body.Append(Heading("三、校准条件与执行方案"));
            body.Append(KeyValueTable(new[]
            {
                Pair("设定温度", AppendUnit(Get(task, "设定温度(℃)"), "℃")), Pair("设定湿度", AppendUnit(Get(task, "设定湿度(%RH)"), "%RH")),
                Pair("温度测点", BuildPointSummary(task, "温度")), Pair("湿度测点", BuildPointSummary(task, "湿度")),
                Pair("传感器类型", Get(task, "传感器类型", "-")), Pair("工作区尺寸", BuildWorkZone(task)),
                Pair("环境条件", BuildEnvironment(task)), Pair("负载说明", Get(task, "负载说明", "无/未填写")),
                Pair("正式采样计划", $"{Get(task, "计划样本数", "-")} 组，每 {Get(task, "采样间隔(s)", "-")} s"), Pair("稳定等待", AppendUnit(Get(task, "稳定等待(min)"), "min")),
                Pair("布点说明", Get(task, "布点说明", "-")), Pair("偏离说明", Get(task, "偏离说明", "无"))
            }));

            body.Append(Heading("四、校准结果"));
            body.Append(ResultTable(resultRows));
            body.Append(Paragraph("结果说明：以上量值来自归档的正式样本和规范计算结果；实时趋势数据不参与正式结果计算。", "Note"));

            body.Append(Heading("五、测量不确定度摘要"));
            W.Table? uncertaintyTable = BuildUncertaintySummaryTable(uncertaintyRows);
            if (uncertaintyTable != null)
                body.Append(uncertaintyTable);
            else
                body.Append(Callout("该历史归档未保存结构化不确定度分量，证书只能展示校准结果文件中的最终量值；签发前应查验原始评定资料。"));

            body.Append(Heading("六、声明与签发"));
            body.Append(Paragraph(
                "本证书仅对本次单工况、所列布点和归档正式样本负责。被校设备与委托档案中标记为“未填写（可选）”的字段应在正式签发前按实验室管理程序补全或确认不适用。未经书面批准，不得部分复制本证书。",
                "Normal"));
            body.Append(SignatureTable(task));
            body.Append(Paragraph($"证书生成时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}    数据格式版本：{Get(summary, "数据格式版本", "1.0")}", "FooterNote", W.JustificationValues.Center));

            body.Append(new W.SectionProperties(
                new W.HeaderReference { Type = W.HeaderFooterValues.Default, Id = headerId },
                new W.FooterReference { Type = W.HeaderFooterValues.Default, Id = footerId },
                new W.PageSize { Width = 12240U, Height = 15840U },
                new W.PageMargin { Top = 1440, Right = 1440U, Bottom = 1440, Left = 1440U, Header = 708U, Footer = 708U, Gutter = 0U }));
            mainPart.Document.Save();
        }

        /// <summary>建立 standard_business_brief 对应的中文字体、字号、颜色与段落节奏。</summary>
        private static void AddStyles(MainDocumentPart mainPart)
        {
            StyleDefinitionsPart stylePart = mainPart.AddNewPart<StyleDefinitionsPart>();
            W.Styles styles = new();
            styles.Append(
                ParagraphStyle("Normal", "正文", 21, "111827", 0, 120, 264),
                ParagraphStyle("Title", "标题", 44, "0F172A", 0, 100, 240, bold: true, centered: true),
                ParagraphStyle("Subtitle", "副标题", 22, "475569", 0, 160, 240, centered: true),
                ParagraphStyle("CertificateKicker", "证书标识", 20, "2563EB", 0, 60, 240, bold: true, centered: true),
                ParagraphStyle("Status", "签发状态", 18, "B45309", 0, 180, 240, bold: true, centered: true),
                ParagraphStyle("Heading1", "一级标题", 32, "2E74B5", 320, 160, 264, bold: true, keepNext: true),
                ParagraphStyle("Note", "说明", 19, "475569", 100, 120, 264),
                ParagraphStyle("FooterNote", "页尾说明", 17, "64748B", 180, 0, 240, centered: true));
            stylePart.Styles = styles;
            stylePart.Styles.Save();
        }

        /// <summary>允许 Word/WPS 打开文件时刷新页码和总页数字段。</summary>
        private static void AddSettings(MainDocumentPart mainPart)
        {
            DocumentSettingsPart settingsPart = mainPart.AddNewPart<DocumentSettingsPart>();
            settingsPart.Settings = new W.Settings(new W.UpdateFieldsOnOpen { Val = true });
            settingsPart.Settings.Save();
        }

        /// <summary>创建安静的运行页眉与“第 X 页 共 Y 页”页脚。</summary>
        private static (string HeaderId, string FooterId) AddHeaderAndFooter(MainDocumentPart mainPart, IReadOnlyDictionary<string, string> summary)
        {
            HeaderPart headerPart = mainPart.AddNewPart<HeaderPart>();
            headerPart.Header = new W.Header(Paragraph(
                $"温湿度校准系统  |  {Get(summary, "任务编号", "-")}",
                "FooterNote",
                W.JustificationValues.Right));
            headerPart.Header.Save();

            FooterPart footerPart = mainPart.AddNewPart<FooterPart>();
            W.Paragraph footer = new(new W.ParagraphProperties(new W.Justification { Val = W.JustificationValues.Center }));
            footer.Append(new W.Run(new W.Text("第 ")));
            AppendField(footer, "PAGE", "1");
            footer.Append(new W.Run(new W.Text(" 页  共 ")));
            AppendField(footer, "NUMPAGES", "1");
            footer.Append(new W.Run(new W.Text(" 页")));
            footerPart.Footer = new W.Footer(footer);
            footerPart.Footer.Save();
            return (mainPart.GetIdOfPart(headerPart), mainPart.GetIdOfPart(footerPart));
        }

        /// <summary>把一个 Word 字段追加到段落，用于自动页码。</summary>
        private static void AppendField(W.Paragraph paragraph, string code, string fallback)
        {
            paragraph.Append(
                new W.Run(new W.FieldChar { FieldCharType = W.FieldCharValues.Begin }),
                new W.Run(new W.FieldCode($" {code} ") { Space = SpaceProcessingModeValues.Preserve }),
                new W.Run(new W.FieldChar { FieldCharType = W.FieldCharValues.Separate }),
                new W.Run(new W.Text(fallback)),
                new W.Run(new W.FieldChar { FieldCharType = W.FieldCharValues.End }));
        }

        /// <summary>创建统一段落样式定义，字号使用半磅单位。</summary>
        private static W.Style ParagraphStyle(
            string id,
            string name,
            int halfPointSize,
            string color,
            int before,
            int after,
            int line,
            bool bold = false,
            bool centered = false,
            bool keepNext = false)
        {
            W.Style style = new() { Type = W.StyleValues.Paragraph, StyleId = id, CustomStyle = true, Default = id == "Normal" };
            style.Append(new W.StyleName { Val = name });
            W.StyleParagraphProperties paragraphProperties = new();
            if (keepNext) paragraphProperties.Append(new W.KeepNext());
            paragraphProperties.Append(new W.SpacingBetweenLines { Before = before.ToString(CultureInfo.InvariantCulture), After = after.ToString(CultureInfo.InvariantCulture), Line = line.ToString(CultureInfo.InvariantCulture), LineRule = W.LineSpacingRuleValues.Auto });
            if (centered) paragraphProperties.Append(new W.Justification { Val = W.JustificationValues.Center });
            style.Append(paragraphProperties);
            W.StyleRunProperties runProperties = new(new W.RunFonts { Ascii = "Microsoft YaHei", HighAnsi = "Microsoft YaHei", EastAsia = "Microsoft YaHei" });
            if (bold) runProperties.Append(new W.Bold(), new W.BoldComplexScript());
            runProperties.Append(
                new W.Color { Val = color },
                new W.FontSize { Val = halfPointSize.ToString(CultureInfo.InvariantCulture) },
                new W.FontSizeComplexScript { Val = halfPointSize.ToString(CultureInfo.InvariantCulture) });
            style.Append(runProperties);
            return style;
        }

        /// <summary>创建普通文本段落并应用命名样式。</summary>
        private static W.Paragraph Paragraph(string text, string styleId, W.JustificationValues? alignment = null)
        {
            W.ParagraphProperties properties = new(new W.ParagraphStyleId { Val = styleId });
            if (alignment.HasValue) properties.Append(new W.Justification { Val = alignment.Value });
            return new W.Paragraph(properties, new W.Run(new W.Text(text ?? string.Empty) { Space = SpaceProcessingModeValues.Preserve }));
        }

        /// <summary>创建保持与后续表格相邻的一级标题。</summary>
        private static W.Paragraph Heading(string text) => Paragraph(text, "Heading1");

        /// <summary>创建浅色提示区，不使用表格承载普通正文。</summary>
        private static W.Paragraph Callout(string text)
        {
            W.Paragraph paragraph = Paragraph(text, "Note");
            paragraph.ParagraphProperties = new W.ParagraphProperties(
                new W.ParagraphStyleId { Val = "Note" },
                new W.ParagraphBorders(new W.LeftBorder { Val = W.BorderValues.Single, Color = "2563EB", Size = 16U, Space = 8U }),
                new W.Shading { Val = W.ShadingPatternValues.Clear, Fill = "F4F6F9", Color = "auto" },
                new W.SpacingBetweenLines { Before = "100", After = "100", Line = "264", LineRule = W.LineSpacingRuleValues.Auto },
                new W.Indentation { Left = "180", Right = "180" });
            return paragraph;
        }

        /// <summary>把成对字段排列成“标签—值—标签—值”固定宽度表格。</summary>
        private static W.Table KeyValueTable(IReadOnlyList<(string Label, string Value)> fields)
        {
            List<string[]> rows = new();
            for (int index = 0; index < fields.Count; index += 2)
            {
                (string Label, string Value) left = fields[index];
                (string Label, string Value) right = index + 1 < fields.Count ? fields[index + 1] : (string.Empty, string.Empty);
                rows.Add(new[] { left.Label, left.Value, right.Label, right.Value });
            }
            return Table(rows, new[] { 1500, 3180, 1500, 3180 }, labelColumns: new HashSet<int> { 0, 2 });
        }

        /// <summary>将结果 CSV 的有效数据行转换为证书结果表。</summary>
        private static W.Table ResultTable(IReadOnlyList<string[]> rows)
        {
            List<string[]> display = new() { new[] { "结果项目", "结果", "单位", "计算口径" } };
            foreach (string[] row in rows.Skip(1))
            {
                if (row.Length == 0 || string.IsNullOrWhiteSpace(row[0])) continue;
                display.Add(new[] { Cell(row, 0), Cell(row, 1), Cell(row, 2), Cell(row, 3) });
            }
            if (display.Count == 1) display.Add(new[] { "无可用结果", "-", "-", "请核验校准结果.csv" });
            return Table(display, new[] { 2700, 1400, 900, 4360 }, headerRow: true, centeredColumns: new HashSet<int> { 1, 2 });
        }

        /// <summary>按结果项目和评定点汇总不确定度 CSV，避免在证书中重复列出每个分量。</summary>
        private static W.Table? BuildUncertaintySummaryTable(IReadOnlyList<string[]> rows)
        {
            if (rows.Count < 2) return null;
            Dictionary<string, int> columns = rows[0]
                .Select((name, index) => (name, index))
                .Where(item => !string.IsNullOrWhiteSpace(item.name))
                .ToDictionary(item => item.name, item => item.index, StringComparer.Ordinal);
            string[] required = { "结果项目", "评定点", "合成标准不确定度uc", "包含因子k", "扩展不确定度U", "合成依据" };
            if (required.Any(name => !columns.ContainsKey(name))) return null;

            List<string[]> display = new() { new[] { "结果项目 / 评定点", "uc", "k", "U", "评定依据" } };
            HashSet<string> seen = new(StringComparer.Ordinal);
            foreach (string[] row in rows.Skip(1))
            {
                string key = $"{Cell(row, columns["结果项目"])}|{Cell(row, columns["评定点"])}";
                if (string.IsNullOrWhiteSpace(key.Replace("|", string.Empty, StringComparison.Ordinal)) || !seen.Add(key)) continue;
                display.Add(new[]
                {
                    key.Replace("|", " / ", StringComparison.Ordinal),
                    Cell(row, columns["合成标准不确定度uc"]),
                    Cell(row, columns["包含因子k"]),
                    Cell(row, columns["扩展不确定度U"]),
                    Cell(row, columns["合成依据"])
                });
            }
            return display.Count > 1
                ? Table(display, new[] { 2400, 950, 650, 950, 4410 }, headerRow: true, centeredColumns: new HashSet<int> { 1, 2, 3 })
                : null;
        }

        /// <summary>创建校准员、核验员和批准人的签字区域。</summary>
        private static W.Table SignatureTable(IReadOnlyDictionary<string, string> task)
        {
            return Table(new[]
            {
                new[] { "校准员", "核验员", "批准人" },
                new[] { Get(task, "校准员", "________________"), Get(task, "核验员", "________________"), "________________" },
                new[] { "日期：________________", "日期：________________", "日期：________________" }
            }, new[] { 3120, 3120, 3120 }, headerRow: true, centeredColumns: new HashSet<int> { 0, 1, 2 });
        }

        /// <summary>按给定列宽创建固定 DXA 几何表格，确保 Word/WPS 和渲染器中的布局一致。</summary>
        private static W.Table Table(
            IReadOnlyList<string[]> rows,
            IReadOnlyList<int> widths,
            bool headerRow = false,
            ISet<int>? labelColumns = null,
            ISet<int>? centeredColumns = null)
        {
            if (widths.Sum() != ContentWidth) throw new InvalidDataException("Word 表格列宽总和必须等于正文宽度。");
            W.Table table = new();
            table.Append(new W.TableProperties(
                new W.TableWidth { Type = W.TableWidthUnitValues.Dxa, Width = ContentWidth.ToString(CultureInfo.InvariantCulture) },
                new W.TableIndentation { Type = W.TableWidthUnitValues.Dxa, Width = TableIndent },
                new W.TableBorders(
                    Border<W.TopBorder>(), Border<W.LeftBorder>(), Border<W.BottomBorder>(), Border<W.RightBorder>(),
                    Border<W.InsideHorizontalBorder>(), Border<W.InsideVerticalBorder>()),
                new W.TableLayout { Type = W.TableLayoutValues.Fixed },
                new W.TableCellMarginDefault(
                    new W.TopMargin { Width = "80", Type = W.TableWidthUnitValues.Dxa },
                    new W.TableCellLeftMargin { Width = 120, Type = W.TableWidthValues.Dxa },
                    new W.BottomMargin { Width = "80", Type = W.TableWidthUnitValues.Dxa },
                    new W.TableCellRightMargin { Width = 120, Type = W.TableWidthValues.Dxa })));
            table.Append(new W.TableGrid(widths.Select(width => new W.GridColumn { Width = width.ToString(CultureInfo.InvariantCulture) })));

            for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                string[] row = rows[rowIndex];
                W.TableRow tableRow = new();
                if (headerRow && rowIndex == 0)
                    tableRow.AppendChild(new W.TableRowProperties(new W.TableHeader { Val = W.OnOffOnlyValues.On }));
                for (int column = 0; column < widths.Count; column++)
                {
                    bool emphasized = headerRow && rowIndex == 0 || labelColumns?.Contains(column) == true;
                    string fill = headerRow && rowIndex == 0 ? "F2F4F7" : labelColumns?.Contains(column) == true ? "F8FAFC" : "FFFFFF";
                    W.TableCellProperties cellProperties = new(
                        new W.TableCellWidth { Type = W.TableWidthUnitValues.Dxa, Width = widths[column].ToString(CultureInfo.InvariantCulture) },
                        new W.Shading { Val = W.ShadingPatternValues.Clear, Fill = fill, Color = "auto" },
                        new W.TableCellVerticalAlignment { Val = W.TableVerticalAlignmentValues.Center });
                    W.ParagraphProperties paragraphProperties = new(
                        new W.ParagraphStyleId { Val = "Normal" },
                        new W.SpacingBetweenLines { Before = "0", After = "0", Line = "264", LineRule = W.LineSpacingRuleValues.Auto });
                    if (centeredColumns?.Contains(column) == true || headerRow && rowIndex == 0)
                        paragraphProperties.Append(new W.Justification { Val = W.JustificationValues.Center });
                    W.RunProperties runProperties = new();
                    if (emphasized) runProperties.Append(new W.Bold(), new W.BoldComplexScript());
                    W.Paragraph paragraph = new(paragraphProperties, new W.Run(runProperties, new W.Text(column < row.Length ? row[column] : string.Empty) { Space = SpaceProcessingModeValues.Preserve }));
                    tableRow.Append(new W.TableCell(cellProperties, paragraph));
                }
                table.Append(tableRow);
            }
            return table;
        }

        /// <summary>创建统一的浅蓝灰细线边框。</summary>
        private static T Border<T>() where T : W.BorderType, new() => new() { Val = W.BorderValues.Single, Color = "CBD5E1", Size = 6U };

        /// <summary>重新打开生成文件并检查文档结构及关键业务章节。</summary>
        private static void ValidateCertificate(string path)
        {
            using WordprocessingDocument document = WordprocessingDocument.Open(path, false);
            MainDocumentPart? mainPart = document.MainDocumentPart;
            if (mainPart == null)
                throw new InvalidDataException("生成的 Word 证书缺少主文档部分。");
            W.Document? root = mainPart.Document;
            if (root == null)
                throw new InvalidDataException("生成的 Word 证书缺少文档根节点。");
            W.Body? body = root.Body;
            if (body == null)
                throw new InvalidDataException("生成的 Word 证书缺少正文。");
            if (mainPart.StyleDefinitionsPart == null || mainPart.HeaderParts.Count() != 1 || mainPart.FooterParts.Count() != 1)
                throw new InvalidDataException("生成的 Word 证书缺少正文、样式、页眉或页脚。");
            string text = body.InnerText;
            string[] requiredText = { "校准证书", "作业与被校设备信息", "标准器及溯源信息", "校准条件与执行方案", "校准结果", "测量不确定度摘要", "声明与签发" };
            if (requiredText.Any(required => !text.Contains(required, StringComparison.Ordinal)) || body.Descendants<W.Table>().Count() < 5)
                throw new InvalidDataException("生成的 Word 证书章节或表格结构不完整。");
            OpenXmlValidator validator = new();
            ValidationErrorInfo[] errors = validator.Validate(document).Take(5).ToArray();
            if (errors.Length > 0)
                throw new InvalidDataException("Word OpenXML 结构校验失败：" + string.Join("；", errors.Select(item => item.Description)));
        }

        /// <summary>将第一行表头和第二行数据转换为字典。</summary>
        private static Dictionary<string, string> ToHeaderDictionary(IReadOnlyList<string[]> rows)
        {
            if (rows.Count < 2) throw new InvalidDataException("作业摘要没有表头和数据行。");
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

        /// <summary>组合箱式炉标准器级别字段；环境设备任务无该信息时显示不适用。</summary>
        private static string BuildFurnaceStandard(IReadOnlyDictionary<string, string> task)
        {
            string instrumentClass = Get(task, "测温仪器级别");
            string thermocoupleGrade = Get(task, "热电偶等级");
            return string.IsNullOrWhiteSpace(instrumentClass) && string.IsNullOrWhiteSpace(thermocoupleGrade)
                ? "不适用"
                : $"{(string.IsNullOrWhiteSpace(instrumentClass) ? "-" : instrumentClass)} / {(string.IsNullOrWhiteSpace(thermocoupleGrade) ? "-" : thermocoupleGrade)}";
        }
    }
}
