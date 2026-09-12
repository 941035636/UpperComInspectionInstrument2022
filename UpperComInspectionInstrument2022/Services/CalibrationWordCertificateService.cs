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
        private const string ReportDirectoryName = "报告";
        private const string CertificateFileName = "校准证书.docx";
        private const int ContentWidth = 9360;
        private const int TableIndent = 120;

        /// <summary>应用内共享的 Word 证书生成服务。</summary>
        public static CalibrationWordCertificateService Default { get; } = new();

        /// <summary>
        /// 检查归档状态和必需文件，生成证书后重新打开并执行 OpenXML 结构验证。
        /// 报告只读取作业摘要、任务信息和校准结果，最终扩展不确定度已固化在校准结果中。
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

                Dictionary<string, string> task = ToPairDictionary(CalibrationFileStorageService.ReadCsvFile(taskPath));
                List<string[]> resultRows = CalibrationFileStorageService.ReadCsvFile(resultPath);

                string reportDirectory = Path.Combine(fullJobDirectory, ReportDirectoryName);
                Directory.CreateDirectory(reportDirectory);
                certificatePath = Path.Combine(reportDirectory, CertificateFileName);
                string temporaryPath = Path.Combine(reportDirectory, $".{Guid.NewGuid():N}.校准证书.tmp.docx");
                try
                {
                    CreateCertificate(temporaryPath, summary, task, resultRows);
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

        /// <summary>创建标题、归档快照和结果正文，并把声明及签发区域统一放在报告末尾。</summary>
        private static void CreateCertificate(
            string path,
            IReadOnlyDictionary<string, string> summary,
            IReadOnlyDictionary<string, string> task,
            IReadOnlyList<string[]> resultRows)
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
            string standard = Get(summary, "校准规范", "校准规范未填写");
            bool isJjf1101 = standard.StartsWith("JJF 1101", StringComparison.Ordinal);
            bool isJjf1376 = standard.StartsWith("JJF 1376", StringComparison.Ordinal);
            (string headerId, string footerId) = AddHeaderAndFooter(mainPart, summary, standard);

            W.Body body = new();
            mainPart.Document.Append(body);
            if (isJjf1101)
            {
                AppendJjf1101Certificate(body, summary, task, resultRows);
            }
            else if (isJjf1376)
            {
                AppendJjf1376Certificate(body, summary, task, resultRows);
            }
            else
            {
            string specificTitle = standard.StartsWith("JJF 1376", StringComparison.Ordinal)
                ? "箱式电阻炉校准证书"
                : "环境试验设备温湿度参数校准证书";
            body.Append(
                Paragraph("校准证书", "CertificateKicker", W.JustificationValues.Center),
                Paragraph(specificTitle, "Title", W.JustificationValues.Center),
                Paragraph($"证书编号：{Get(summary, "任务编号", "-")}", "Subtitle", W.JustificationValues.Center),
                Paragraph("待审核签发", "Status", W.JustificationValues.Center));

            body.Append(Heading("一、基本信息"));
            body.Append(KeyValueTable(new[]
            {
                Pair("实验室名称", Get(task, "实验室名称", "未填写（签发前补全）")), Pair("实验室地址", Get(task, "实验室地址", "未填写（签发前补全）")),
                Pair("校准地点", Get(task, "校准地点", "未填写（签发前补全）")), Pair("校准日期", Get(task, "校准日期", "-")),
                Pair("委托单位", Get(task, "委托单位", "未填写（可选）")), Pair("委托单位地址", Get(task, "委托单位地址", "未填写（可选）")),
                Pair("被校设备", Get(task, "被校设备名称", "未填写（可选）")), Pair("设备编号", Get(task, "设备编号", "未填写（可选）")),
                Pair("型号规格", Get(task, "型号规格", "未填写（可选）")), Pair("制造单位", Get(task, "制造单位", "未填写（可选）")),
                Pair("测量范围", Get(task, "测量范围", "未填写（可选）")), Pair("校准项目", Get(summary, "校准类型", "-"))
            }));

            body.Append(Heading("二、校准依据、测量标准与环境"));
            body.Append(KeyValueTable(new[]
            {
                Pair("校准依据", standard), Pair("偏离说明", Get(task, "偏离说明", "无")),
                Pair("标准器名称", Get(task, "标准器名称", "-")), Pair("标准器编号", Get(task, "标准器编号", "-")),
                Pair("型号", Get(task, "标准器型号", "-")), Pair("证书编号", Get(task, "标准器证书编号", "-")),
                Pair("有效期", Get(task, "标准器有效期", "-")), Pair("溯源机构", Get(task, "标准器溯源机构", "-")),
                Pair("温度范围", Get(task, "标准器温度范围", "-")), Pair("湿度范围", Get(task, "标准器湿度范围", "-")),
                Pair("温度分辨力", AppendUnit(Get(task, "标准器温度分辨力"), "℃")), Pair("湿度分辨力", AppendUnit(Get(task, "标准器湿度分辨力"), "%RH")),
                Pair("环境条件", BuildEnvironment(task)), Pair("负载说明", Get(task, "负载说明", "无/未填写")),
                Pair("准确度/最大允许误差", Get(task, "标准器准确度", "-")), Pair("测温仪器/热电偶等级", BuildFurnaceStandard(task))
            }));

            body.Append(Heading("三、校准结果"));
            body.Append(ResultTable(resultRows));
            body.Append(Paragraph("以上量值来自本次作业的正式样本和规范计算结果；实时趋势数据不参与正式结果计算。", "Note"));

            body.Append(Heading("四、声明与签发"));
            body.Append(Paragraph(
                "本证书所列校准结果仅对本次被校对象、所列布点和正式样本有效。签发前应核验原始记录、测量标准溯源状态、结果和签字信息。未经实验室书面批准，不得部分复制本证书。",
                "Normal"));
            body.Append(SignatureTable(task));
            body.Append(Paragraph($"证书生成时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}    数据格式版本：{Get(summary, "数据格式版本", "1.0")}", "FooterNote", W.JustificationValues.Center));
            }

            body.Append(new W.SectionProperties(
                new W.HeaderReference { Type = W.HeaderFooterValues.Default, Id = headerId },
                new W.FooterReference { Type = W.HeaderFooterValues.Default, Id = footerId },
                new W.PageSize { Width = 11906U, Height = 16838U },
                new W.PageMargin { Top = 1080, Right = 1150U, Bottom = 1080, Left = 1150U, Header = 560U, Footer = 560U, Gutter = 0U }));
            mainPart.Document.Save();

        }

        /// <summary>建立 standard_business_brief 对应的中文字体、字号、颜色与段落节奏。</summary>
        private static void AddStyles(MainDocumentPart mainPart)
        {
            StyleDefinitionsPart stylePart = mainPart.AddNewPart<StyleDefinitionsPart>();
            W.Styles styles = new();
            styles.Append(
                ParagraphStyle("Normal", "正文", 20, "000000", 0, 100, 252),
                ParagraphStyle("Title", "标题", 36, "000000", 0, 180, 280, bold: true, centered: true),
                ParagraphStyle("Subtitle", "副标题", 22, "000000", 0, 140, 252, centered: true),
                ParagraphStyle("CertificateKicker", "证书标识", 20, "000000", 0, 80, 240, bold: true, centered: true),
                //ParagraphStyle("Status", "签发状态", 18, "000000", 0, 160, 240, centered: true),
                ParagraphStyle("Heading1", "一级标题", 24, "000000", 260, 120, 252, bold: true, keepNext: true),
                ParagraphStyle("Subheading", "二级标题", 20, "000000", 220, 100, 240, keepNext: true),
                ParagraphStyle("Note", "说明", 18, "000000", 80, 100, 240),
                ParagraphStyle("FooterNote", "页尾说明", 17, "000000", 120, 0, 240, centered: true));
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

        /// <summary>
        /// 生成 JJF 1101-2019 校准证书正文。
        /// 第一页保留证书身份、溯源和校准条件；后续内容按附录 B 展示布点图和最终结果，声明与签发统一收尾。
        /// 原始的逐次测量值不在证书内重复堆叠，而由同一作业目录下的 Excel《校准原始记录》承载。
        /// </summary>
        private static void AppendJjf1101Certificate(
            W.Body body,
            IReadOnlyDictionary<string, string> summary,
            IReadOnlyDictionary<string, string> task,
            IReadOnlyList<string[]> resultRows)
        {
            body.Append(
                Paragraph("校 准 证 书", "Title", W.JustificationValues.Center),
                Paragraph("环境试验设备温度湿度参数校准", "Subtitle", W.JustificationValues.Center),
                Paragraph($"证书编号：{Get(summary, "任务编号", "-")}", "Subtitle", W.JustificationValues.Center));

            body.Append(Heading("一、基本信息"));
            body.Append(FormalKeyValueTable(new[]
            {
                Pair("实验室名称", Get(task, "实验室名称", "未填写（签发前补全）")), Pair("实验室地址", Get(task, "实验室地址", "未填写（签发前补全）")),
                Pair("委托单位", Get(task, "委托单位", "未填写（可选）")), Pair("校准地点", Get(task, "校准地点", "未填写（签发前补全）")),
                Pair("被校设备", Get(task, "被校设备名称", "未填写（可选）")), Pair("设备编号", Get(task, "设备编号", "未填写（可选）")),
                Pair("型号规格", Get(task, "型号规格", "未填写（可选）")), Pair("制造单位", Get(task, "制造单位", "未填写（可选）")),
                Pair("校准日期", Get(task, "校准日期", "-")), Pair("记录编号", Get(summary, "任务编号", "-"))
            }));

            body.Append(Heading("二、校准依据与测量标准"));
            body.Append(FormalKeyValueTable(new[]
            {
                Pair("校准依据", Get(summary, "校准规范", "JJF 1101-2019")), Pair("偏离说明", Get(task, "偏离说明", "无")),
                Pair("标准器名称", Get(task, "标准器名称", "未填写")), Pair("型号/规格", Get(task, "标准器型号", "未填写")),
                Pair("标准器编号", Get(task, "标准器编号", "未填写")), Pair("证书编号", Get(task, "标准器证书编号", "未填写")),
                Pair("有效期至", Get(task, "标准器有效期", "未填写")), Pair("溯源机构", Get(task, "标准器溯源机构", "未填写")),
                Pair("准确度/最大允许误差", Get(task, "标准器准确度", "未填写")), Pair("测量范围", Get(task, "测量范围", "未填写（可选）"))
            }));

            body.Append(Heading("三、校准条件"));
            body.Append(FormalKeyValueTable(new[]
            {
                Pair("环境条件", BuildEnvironment(task)), Pair("负载说明", Get(task, "负载说明", "无/未填写")),
                Pair("设定值", BuildJjf1101SetPoint(task)), Pair("测点配置", $"温度 {BuildPointSummary(task, "温度")}；湿度 {BuildPointSummary(task, "湿度")}"),
                Pair("采样方案", $"{Get(task, "正式样本数", Get(summary, "计划样本数", "-"))} 组，间隔 {AppendUnit(Get(task, "采样间隔(s)"), "s")}"), Pair("布点方式", Get(task, "布点方式", "按任务配置"))
            }));

            body.Append(PageBreak());
            body.Append(
                Paragraph("环境试验设备校准证书内页", "Subtitle", W.JustificationValues.Center),
                Paragraph("校 准 结 果", "Title", W.JustificationValues.Center),
                Paragraph("1.  布点示意图", "Subheading"));
            body.Append(Jjf1101PointLayoutFigure(task));
            body.Append(Paragraph("图 B1  布点示意图", "FooterNote", W.JustificationValues.Center));
            body.Append(Paragraph(BuildJjf1101HumidityPointNote(task), "Note"));
            body.Append(Paragraph("2.  校准结果", "Subheading"));
            body.Append(Jjf1101ResultTable(task, resultRows));
            body.Append(Paragraph("注：未填写的身份信息和签字项应在证书审核签发前补全。", "Note"));
            AppendCertificateClosing(
                body,
                "四、声明与签发",
                "本证书所列校准结果仅对本次被校对象、所列布点和正式样本有效。签发前应核验原始记录、测量标准溯源状态、校准结果和签字信息。未经实验室书面批准，不得部分复制本证书。",
                task);
        }

        /// <summary>创建 JJF 1101 正式黑白表格中的“标签—值—标签—值”信息行。</summary>
        private static W.Table FormalKeyValueTable(IReadOnlyList<(string Label, string Value)> fields)
        {
            List<string[]> rows = new();
            for (int index = 0; index < fields.Count; index += 2)
            {
                (string Label, string Value) left = fields[index];
                (string Label, string Value) right = index + 1 < fields.Count ? fields[index + 1] : (string.Empty, string.Empty);
                rows.Add(new[] { left.Label, left.Value, right.Label, right.Value });
            }
            return FormalTable(rows, new[] { 1500, 3180, 1500, 3180 }, labelColumns: new HashSet<int> { 0, 2 });
        }

        /// <summary>创建附录 B 风格的三层空间布点示意图；调整布点时改为可复核的点位摘要。</summary>
        private static W.Table Jjf1101PointLayoutFigure(IReadOnlyDictionary<string, string> task)
        {
            int pointCount = ParsePositiveInt(Get(task, "温度测点数", "0"));
            int centerPoint = ParsePositiveInt(Get(task, "温度中心点", "0"));
            int humidityPointCount = ParsePositiveInt(Get(task, "湿度测点数", "0"));
            bool isDefaultLayout = !int.TryParse(Get(task, "布点方式索引", "0"), NumberStyles.Integer,
                                       CultureInfo.InvariantCulture, out int layoutMode) || layoutMode == 0;
            if (!isDefaultLayout || pointCount is not (9 or 15) || centerPoint != (pointCount == 9 ? 5 : 15))
                return Jjf1101PointLayoutTable(task);

            string Label(int point, string humidity = "") => humidityPointCount > 0 && !string.IsNullOrWhiteSpace(humidity)
                ? $"{point} {humidity}"
                : point.ToString(CultureInfo.InvariantCulture);
            string[,] upper = new string[3, 3];
            string[,] middle = new string[3, 3];
            string[,] lower = new string[3, 3];

            upper[0, 0] = Label(1, "A"); upper[0, 2] = Label(2); upper[2, 0] = Label(4); upper[2, 2] = Label(3);
            lower[0, 0] = Label(6); lower[0, 2] = Label(7); lower[2, 0] = Label(9); lower[2, 2] = Label(8, "B");
            if (pointCount == 9)
            {
                middle[1, 1] = Label(5, "O");
            }
            else
            {
                upper[1, 1] = Label(5);
                middle[0, 1] = Label(11);
                middle[1, 0] = Label(14);
                middle[1, 1] = Label(15, "O");
                middle[1, 2] = Label(12);
                middle[2, 1] = Label(13, "C");
                lower[1, 1] = Label(10);
            }

            W.Table table = new();
            table.Append(new W.TableProperties(
                new W.TableWidth { Type = W.TableWidthUnitValues.Dxa, Width = ContentWidth.ToString(CultureInfo.InvariantCulture) },
                new W.TableIndentation { Type = W.TableWidthUnitValues.Dxa, Width = TableIndent },
                new W.TableBorders(
                    NilBorder<W.TopBorder>(), NilBorder<W.LeftBorder>(), NilBorder<W.BottomBorder>(), NilBorder<W.RightBorder>(),
                    NilBorder<W.InsideHorizontalBorder>(), NilBorder<W.InsideVerticalBorder>()),
                new W.TableLayout { Type = W.TableLayoutValues.Fixed }));
            table.Append(new W.TableGrid(
                new W.GridColumn { Width = "3120" }, new W.GridColumn { Width = "3120" }, new W.GridColumn { Width = "3120" }));
            W.TableRow row = new();
            row.Append(DiagramCell("上  层", upper), DiagramCell("中  层", middle), DiagramCell("下  层", lower));
            table.Append(row);
            return table;
        }

        /// <summary>创建布点示意图中的一个层面方框。</summary>
        private static W.TableCell DiagramCell(string title, string[,] labels)
        {
            W.Table diagram = new();
            diagram.Append(new W.TableProperties(
                new W.TableWidth { Type = W.TableWidthUnitValues.Dxa, Width = "2700" },
                new W.TableBorders(
                    BlackBorder<W.TopBorder>(), BlackBorder<W.LeftBorder>(), BlackBorder<W.BottomBorder>(), BlackBorder<W.RightBorder>(),
                    NilBorder<W.InsideHorizontalBorder>(), NilBorder<W.InsideVerticalBorder>()),
                new W.TableLayout { Type = W.TableLayoutValues.Fixed }));
            diagram.Append(new W.TableGrid(
                new W.GridColumn { Width = "900" }, new W.GridColumn { Width = "900" }, new W.GridColumn { Width = "900" }));
            for (int rowIndex = 0; rowIndex < 3; rowIndex++)
            {
                W.TableRow row = new(new W.TableRowProperties(new W.TableRowHeight { Val = 520U, HeightType = W.HeightRuleValues.AtLeast }));
                for (int columnIndex = 0; columnIndex < 3; columnIndex++)
                {
                    W.Paragraph paragraph = Paragraph(labels[rowIndex, columnIndex] ?? string.Empty, "Normal", W.JustificationValues.Center);
                    row.Append(new W.TableCell(
                        new W.TableCellProperties(
                            new W.TableCellWidth { Type = W.TableWidthUnitValues.Dxa, Width = "900" },
                            new W.TableCellVerticalAlignment { Val = W.TableVerticalAlignmentValues.Center }),
                        paragraph));
                }
                diagram.Append(row);
            }

            return new W.TableCell(
                new W.TableCellProperties(
                    new W.TableCellWidth { Type = W.TableWidthUnitValues.Dxa, Width = "3120" },
                    new W.TableCellMargin(
                        new W.TableCellLeftMargin { Width = 180, Type = W.TableWidthValues.Dxa },
                        new W.TableCellRightMargin { Width = 180, Type = W.TableWidthValues.Dxa }),
                    new W.TableCellVerticalAlignment { Val = W.TableVerticalAlignmentValues.Top }),
                Paragraph(title, "Normal", W.JustificationValues.Center),
                diagram,
                Paragraph("门", "FooterNote", W.JustificationValues.Center));
        }

        /// <summary>插入显式分页符，使证书正文与附录 B 内页稳定分开。</summary>
        private static W.Paragraph PageBreak() => new(new W.Run(new W.Break { Type = W.BreakValues.Page }));

        /// <summary>
        /// 生成 JJF 1376-2012 校准证书正文和附录 B 风格的结果内页。
        /// 原始 20 组测量值、修正值和实际温度由 Excel 附录 A 记录承载。
        /// </summary>
        private static void AppendJjf1376Certificate(
            W.Body body,
            IReadOnlyDictionary<string, string> summary,
            IReadOnlyDictionary<string, string> task,
            IReadOnlyList<string[]> resultRows)
        {
            body.Append(
                Paragraph("校 准 证 书", "Title", W.JustificationValues.Center),
                Paragraph("箱式电阻炉校准", "Subtitle", W.JustificationValues.Center),
                Paragraph($"证书编号：{Get(summary, "任务编号", "-")}", "Subtitle", W.JustificationValues.Center));

            body.Append(Heading("一、基本信息"));
            body.Append(FormalKeyValueTable(new[]
            {
                Pair("实验室名称", Get(task, "实验室名称", "未填写（签发前补全）")), Pair("实验室地址", Get(task, "实验室地址", "未填写（签发前补全）")),
                Pair("委托单位", Get(task, "委托单位", "未填写（可选）")), Pair("校准地点", Get(task, "校准地点", "未填写（签发前补全）")),
                Pair("被校设备", Get(task, "被校设备名称", "未填写（可选）")), Pair("设备编号", Get(task, "设备编号", "未填写（可选）")),
                Pair("型号规格", Get(task, "型号规格", "未填写（可选）")), Pair("制造单位", Get(task, "制造单位", "未填写（可选）")),
                Pair("校准日期", Get(task, "校准日期", "-")), Pair("流水号", Get(summary, "任务编号", "-"))
            }));

            body.Append(Heading("二、校准依据与条件"));
            body.Append(FormalKeyValueTable(new[]
            {
                Pair("校准依据", Get(summary, "校准规范", "JJF 1376-2012")), Pair("偏离说明", Get(task, "偏离说明", "无")),
                Pair("标准设备", Get(task, "标准器名称", "未填写")), Pair("型号/编号", $"{Get(task, "标准器型号", "-")} / {Get(task, "标准器编号", "-")}"),
                Pair("证书编号", Get(task, "标准器证书编号", "未填写")), Pair("有效期至", Get(task, "标准器有效期", "未填写")),
                Pair("环境温度", AppendUnit(Get(task, "环境温度(℃)"), "℃")), Pair("相对湿度", AppendUnit(Get(task, "环境湿度(%RH)"), "%RH")),
                Pair("炉膛尺寸", BuildFurnaceChamber(task)), Pair("测温区尺寸", BuildWorkZone(task))
            }));

            body.Append(PageBreak());
            body.Append(
                Paragraph("箱式电阻炉校准结果", "Subtitle", W.JustificationValues.Center),
                Paragraph("校 准 结 果", "Title", W.JustificationValues.Center),
                FurnaceResultTable(task, resultRows));
            AppendCertificateClosing(
                body,
                "三、声明与签发",
                "本证书所列校准结果仅对本次被校对象、所列测温区和正式样本有效。签发前应核验原始记录、测量标准溯源状态、校准结果和签字信息。未经实验室书面批准，不得部分复制本证书。",
                task);
            //body.Append(Paragraph("（以下空白）", "FooterNote", W.JustificationValues.Center));
        }

        /// <summary>生成 JJF 1376 附录 B 的外观检查、五类结果和两项均匀度不确定度。</summary>
        private static W.Table FurnaceResultTable(
            IReadOnlyDictionary<string, string> task,
            IReadOnlyList<string[]> resultRows)
        {
            Dictionary<string, string> values = resultRows.Skip(1)
                .Where(row => row.Length >= 2 && !string.IsNullOrWhiteSpace(row[0]))
                .GroupBy(row => row[0], StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First()[1], StringComparer.Ordinal);
            string Value(string key) => values.TryGetValue(key, out string? value) ? FormatResultValue(value) : "—";
            string coverage = Get(task, "标准器温度包含因子", "2");
            string[][] display =
            {
                new[] { "序号", "校准项目", "符号", "校准结果" },
                new[] { "1", "外观检查", "—", Get(task, "外观检查", "待检查") },
                new[] { "2", "炉温均匀度", "Δθ+", $"{Value("炉温均匀度上偏差")} ℃" },
                new[] { "", "炉温均匀度", "Δθ−", $"{Value("炉温均匀度下偏差")} ℃" },
                new[] { "3", "炉温稳定度", "δ+", $"{Value("炉温稳定度上偏差")} ℃" },
                new[] { "", "炉温稳定度", "δ−", $"{Value("炉温稳定度下偏差")} ℃" },
                new[] { "4", "炉温偏差", "Δt+", $"{Value("炉温偏差上偏差")} ℃" },
                new[] { "", "炉温偏差", "Δt−", $"{Value("炉温偏差下偏差")} ℃" },
                new[] { "5", "炉内最大温差", "Δts", $"{Value("炉内最大温差")} ℃" },
                new[] { "6", "炉温均匀度测量结果的扩展不确定度", "Δθ+", $"U={Value("炉温均匀度上偏差扩展不确定度")} ℃；k={coverage}" },
                new[] { "", "炉温均匀度测量结果的扩展不确定度", "Δθ−", $"U={Value("炉温均匀度下偏差扩展不确定度")} ℃；k={coverage}" }
            };
            return FormalTable(display, new[] { 900, 3500, 1300, 3660 }, headerRow: true,
                centeredColumns: new HashSet<int> { 0, 2, 3 });
        }

        /// <summary>创建安静的运行页眉与“第 X 页 共 Y 页”页脚。</summary>
        private static (string HeaderId, string FooterId) AddHeaderAndFooter(
            MainDocumentPart mainPart,
            IReadOnlyDictionary<string, string> summary,
            string standard)
        {
            HeaderPart headerPart = mainPart.AddNewPart<HeaderPart>();
            bool isJjf1101 = standard.StartsWith("JJF 1101", StringComparison.Ordinal);
            bool isJjf1376 = standard.StartsWith("JJF 1376", StringComparison.Ordinal);
            bool useStandardHeader = isJjf1101 || isJjf1376;
            W.Paragraph headerParagraph = Paragraph(
                isJjf1101 ? "JJF 1101—2019" : isJjf1376 ? "JJF 1376—2012" : $"温湿度校准系统  |  {Get(summary, "任务编号", "-")}",
                "FooterNote",
                useStandardHeader ? W.JustificationValues.Center : W.JustificationValues.Right);
            if (useStandardHeader)
            {
                headerParagraph.ParagraphProperties = new W.ParagraphProperties(
                    new W.ParagraphStyleId { Val = "FooterNote" },
                    new W.ParagraphBorders(
                        new W.BottomBorder { Val = W.BorderValues.Single, Color = "000000", Size = 8U, Space = 6U }),
                    new W.Justification { Val = W.JustificationValues.Center });
            }
            headerPart.Header = new W.Header(headerParagraph);
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
                display.Add(new[] { Cell(row, 0), FormatResultValue(Cell(row, 1)), Cell(row, 2), Cell(row, 3) });
            }
            if (display.Count == 1) display.Add(new[] { "无可用结果", "-", "-", "请核验校准结果.csv" });
            return Table(display, new[] { 2700, 1400, 900, 4360 }, headerRow: true, centeredColumns: new HashSet<int> { 1, 2 });
        }

        /// <summary>
        /// 生成 JJF 1101-2019 附录 B 所需的布点示意表。
        /// 实际测点编号来自任务快照，中心点单独放在中层，其他点均匀分列到上、下层。
        /// </summary>
        private static W.Table Jjf1101PointLayoutTable(IReadOnlyDictionary<string, string> task)
        {
            int pointCount = ParsePositiveInt(Get(task, "温度测点数", "0"));
            int centerPoint = ParsePositiveInt(Get(task, "温度中心点", "0"));
            int humidityPointCount = ParsePositiveInt(Get(task, "湿度测点数", "0"));
            bool hasHumidity = humidityPointCount > 0;
            bool isDefaultLayout = !int.TryParse(Get(task, "布点方式索引", "0"), NumberStyles.Integer,
                                       CultureInfo.InvariantCulture, out int layoutMode) || layoutMode == 0;
            string Point(int number, string humidityPoint = "") => hasHumidity && !string.IsNullOrWhiteSpace(humidityPoint)
                ? $"T{number} / {humidityPoint}"
                : $"T{number}";

            if (isDefaultLayout && pointCount == 9 && centerPoint == 5)
            {
                return Table(new[]
                {
                    new[] { "层面", "左侧", "中心", "右侧" },
                    new[] { "上层", Point(1, "A"), "—", Point(2) },
                    new[] { "", Point(4), "—", Point(3) },
                    new[] { "中层", "—", Point(5, "O"), "—" },
                    new[] { "下层", Point(6), "—", Point(7) },
                    new[] { "", Point(9), "—", Point(8, "B") }
                }, new[] { 1100, 2750, 2750, 2760 }, headerRow: true,
                    centeredColumns: new HashSet<int> { 0, 1, 2, 3 });
            }

            if (isDefaultLayout && pointCount == 15 && centerPoint == 15)
            {
                return Table(new[]
                {
                    new[] { "层面", "左侧", "中心", "右侧" },
                    new[] { "上层", Point(1, "A"), "—", Point(2) },
                    new[] { "", "—", Point(5), "—" },
                    new[] { "", Point(4), "—", Point(3) },
                    new[] { "中层", "—", Point(11), "—" },
                    new[] { "", Point(14), Point(15, "O"), Point(12) },
                    new[] { "", "—", Point(13, "C"), "—" },
                    new[] { "下层", Point(6), "—", Point(7) },
                    new[] { "", "—", Point(10), "—" },
                    new[] { "", Point(9), "—", Point(8, "B") }
                }, new[] { 1100, 2750, 2750, 2760 }, headerRow: true,
                    centeredColumns: new HashSet<int> { 0, 1, 2, 3 });
            }

            string temperaturePoints = pointCount > 0
                ? FormatPointList(Enumerable.Range(1, pointCount))
                : "—";
            string humidityPoints = humidityPointCount > 0
                ? string.Join("、", Enumerable.Range(1, humidityPointCount).Select(index => $"H{index}"))
                : "—";
            string centerText = centerPoint > 0 && centerPoint <= pointCount ? $"T{centerPoint}" : "—";
            return Table(new[]
            {
                new[] { "布点方式", "温度测点", "温度中心点", "湿度测点" },
                new[] { "自定义/调整布点", temperaturePoints, centerText, humidityPoints }
            }, new[] { 1800, 3960, 1600, 2000 }, headerRow: true,
                centeredColumns: new HashSet<int> { 0, 2, 3 });
        }

        /// <summary>组合温度布点说明和湿度 O 点映射，供证书布点示意下方复核。</summary>
        private static string BuildJjf1101HumidityPointNote(IReadOnlyDictionary<string, string> task)
        {
            int humidityPointCount = ParsePositiveInt(Get(task, "湿度测点数", "0"));
            int humidityCenterPoint = ParsePositiveInt(Get(task, "湿度中心点", "0"));
            string humidityText;
            if (humidityPointCount <= 0)
            {
                humidityText = "本工况不包含湿度参数。";
            }
            else
            {
                string points = string.Join("、", Enumerable.Range(1, humidityPointCount).Select(index => $"H{index}"));
                string center = humidityCenterPoint > 0 && humidityCenterPoint <= humidityPointCount
                    ? $"H{humidityCenterPoint}"
                    : "按任务配置";
                humidityText = $"湿度通道：{points}；规范图中的 O 点对应 {center}，A/B/C 点按实际接线关系核对。";
            }
            return $"温度布点：{Get(task, "布点说明", "按任务配置")} {humidityText}";
        }

        /// <summary>生成 JJF 1101-2019 附录 B 的精简证书结果表。</summary>
        private static W.Table Jjf1101ResultTable(
            IReadOnlyDictionary<string, string> task,
            IReadOnlyList<string[]> resultRows)
        {
            Dictionary<string, string> values = resultRows.Skip(1)
                .Where(row => row.Length >= 2 && !string.IsNullOrWhiteSpace(row[0]))
                .GroupBy(row => row[0], StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First()[1], StringComparer.Ordinal);
            string Value(string key) => values.TryGetValue(key, out string? value) ? FormatResultValue(value) : "—";

            bool hasHumidity = ParsePositiveInt(Get(task, "湿度测点数", "0")) > 0;
            string HumidityValue(string key) => hasHumidity ? Value(key) : "—";
            string temperatureCoverage = Get(task, "标准器温度包含因子", "2");
            string humidityCoverage = Get(task, "标准器湿度包含因子", "2");
            string[][] display =
            {
                new[] { "校准参数", "温度 / ℃", "湿度 / %RH" },
                new[] { "设定值", Get(task, "设定温度(℃)", "—"), hasHumidity ? Get(task, "设定湿度(%RH)", "—") : "—" },
                new[] { "上偏差", Value("温度上偏差"), HumidityValue("湿度上偏差") },
                new[] { "下偏差", Value("温度下偏差"), HumidityValue("湿度下偏差") },
                new[] { "均匀度", Value("温度均匀度"), HumidityValue("湿度均匀度") },
                new[] { "波动度", Value("温度波动度"), HumidityValue("湿度波动度") },
                new[]
                {
                    "校准不确定度",
                    $"{Value("温度扩展不确定度")}（k={temperatureCoverage}）",
                    hasHumidity ? $"{Value("湿度扩展不确定度")}（k={humidityCoverage}）" : "—"
                }
            };
            return FormalTable(display, new[] { 3600, 2880, 2880 }, headerRow: true, centeredColumns: new HashSet<int> { 1, 2 });
        }

        /// <summary>把测点编号格式化为证书中的 T1、T2 列表。</summary>
        private static string FormatPointList(IEnumerable<int> pointNumbers)
        {
            string[] points = pointNumbers.Select(index => $"T{index}").ToArray();
            return points.Length == 0 ? "—" : string.Join("、", points);
        }

        /// <summary>安全解析任务快照中的正整数；空值或异常值按零处理。</summary>
        private static int ParsePositiveInt(string value) =>
            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) && parsed > 0
                ? parsed
                : 0;

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

        /// <summary>
        /// 在全部校准结果之后追加证书声明和签字区。
        /// 集中使用该方法可防止不同规范的报告再次把签发内容插到结果正文之前。
        /// </summary>
        private static void AppendCertificateClosing(
            W.Body body,
            string heading,
            string declaration,
            IReadOnlyDictionary<string, string> task)
        {
            body.Append(Heading(heading));
            body.Append(Paragraph(declaration, "Normal"));
            //body.Append(Paragraph("签发状态：待审核", "Status", W.JustificationValues.Center));
            body.Append(SignatureTable(task));
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

        /// <summary>
        /// 创建 JJF 附录表式使用的黑白细线表格。
        /// 与应用界面的蓝灰视觉样式分离，保证打印、复印和归档时接近规范参考格式。
        /// </summary>
        private static W.Table FormalTable(
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
                    BlackBorder<W.TopBorder>(), BlackBorder<W.LeftBorder>(), BlackBorder<W.BottomBorder>(), BlackBorder<W.RightBorder>(),
                    BlackBorder<W.InsideHorizontalBorder>(), BlackBorder<W.InsideVerticalBorder>()),
                new W.TableLayout { Type = W.TableLayoutValues.Fixed },
                new W.TableCellMarginDefault(
                    new W.TopMargin { Width = "60", Type = W.TableWidthUnitValues.Dxa },
                    new W.TableCellLeftMargin { Width = 90, Type = W.TableWidthValues.Dxa },
                    new W.BottomMargin { Width = "60", Type = W.TableWidthUnitValues.Dxa },
                    new W.TableCellRightMargin { Width = 90, Type = W.TableWidthValues.Dxa })));
            table.Append(new W.TableGrid(widths.Select(width => new W.GridColumn { Width = width.ToString(CultureInfo.InvariantCulture) })));

            for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                W.TableRow row = new();
                if (headerRow && rowIndex == 0)
                    row.AppendChild(new W.TableRowProperties(new W.TableHeader { Val = W.OnOffOnlyValues.On }));
                for (int column = 0; column < widths.Count; column++)
                {
                    bool emphasized = headerRow && rowIndex == 0 || labelColumns?.Contains(column) == true;
                    W.RunProperties runProperties = new();
                    if (emphasized) runProperties.Append(new W.Bold(), new W.BoldComplexScript());
                    W.ParagraphProperties paragraphProperties = new(
                        new W.ParagraphStyleId { Val = "Normal" },
                        new W.SpacingBetweenLines { Before = "0", After = "0", Line = "240", LineRule = W.LineSpacingRuleValues.Auto });
                    if (centeredColumns?.Contains(column) == true || headerRow && rowIndex == 0)
                        paragraphProperties.Append(new W.Justification { Val = W.JustificationValues.Center });
                    row.Append(new W.TableCell(
                        new W.TableCellProperties(
                            new W.TableCellWidth { Type = W.TableWidthUnitValues.Dxa, Width = widths[column].ToString(CultureInfo.InvariantCulture) },
                            new W.TableCellVerticalAlignment { Val = W.TableVerticalAlignmentValues.Center }),
                        new W.Paragraph(paragraphProperties,
                            new W.Run(runProperties,
                                new W.Text(column < rows[rowIndex].Length ? rows[rowIndex][column] : string.Empty)
                                { Space = SpaceProcessingModeValues.Preserve }))));
                }
                table.Append(row);
            }
            return table;
        }

        /// <summary>创建统一的浅蓝灰细线边框。</summary>
        private static T Border<T>() where T : W.BorderType, new() => new() { Val = W.BorderValues.Single, Color = "CBD5E1", Size = 6U };

        /// <summary>创建适合正式记录打印的黑色细线边框。</summary>
        private static T BlackBorder<T>() where T : W.BorderType, new() => new() { Val = W.BorderValues.Single, Color = "000000", Size = 6U };

        /// <summary>关闭指定表格边框。</summary>
        private static T NilBorder<T>() where T : W.BorderType, new() => new() { Val = W.BorderValues.Nil };

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
            string text = body.InnerText + string.Concat(mainPart.HeaderParts.Select(part => part.Header?.InnerText));
            string normalized = text.Replace(" ", string.Empty, StringComparison.Ordinal);
            bool isJjf1101 = normalized.Contains("JJF1101", StringComparison.Ordinal);
            bool isJjf1376 = normalized.Contains("JJF1376", StringComparison.Ordinal);
            string[] requiredText = isJjf1101
                ? new[]
                {
                    "校准证书", "基本信息", "校准依据", "声明与签发", "环境试验设备校准证书内页",
                    "校准结果", "布点示意图", "图B1", "设定值", "上偏差", "下偏差", "均匀度", "波动度", "校准不确定度"
                }
                : isJjf1376
                    ? new[]
                    {
                        "校准证书", "基本信息", "校准依据", "声明与签发", "箱式电阻炉校准结果",
                        "外观检查", "炉温均匀度", "炉温稳定度", "炉温偏差", "炉内最大温差", "扩展不确定度"/*, "以下空白"*/
                    }
                : new[] { "校准证书", "基本信息", "校准依据、测量标准与环境", "校准结果", "声明与签发" };
            if (requiredText.Any(required => !normalized.Contains(required.Replace(" ", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal)) ||
                body.Descendants<W.Table>().Count() < 4)
                throw new InvalidDataException("生成的 Word 证书章节或表格结构不完整。");

            // 证书的声明和签发必须在规范结果正文之后，批准人签字区必须位于声明之后。
            if (isJjf1101 || isJjf1376)
            {
                string resultAnchor = isJjf1101 ? "校准不确定度" : "扩展不确定度";
                int resultIndex = normalized.LastIndexOf(resultAnchor, StringComparison.Ordinal);
                int closingIndex = normalized.LastIndexOf("声明与签发", StringComparison.Ordinal);
                int approvalIndex = normalized.LastIndexOf("批准人", StringComparison.Ordinal);
                if (resultIndex < 0 || closingIndex <= resultIndex || approvalIndex <= closingIndex)
                    throw new InvalidDataException("Word 证书的声明或签发区域未置于全部校准结果之后。");
            }

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
        /// <summary>将新旧归档中的最终结果统一格式化为三位小数，非数值文本保持原样。</summary>
        private static string FormatResultValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "—";
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) && double.IsFinite(number)
                ? number.ToString("0.000", CultureInfo.InvariantCulture)
                : value;
        }
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

        /// <summary>按本次校准项目组合温度、湿度设定值，避免无湿度任务出现无意义空栏。</summary>
        private static string BuildJjf1101SetPoint(IReadOnlyDictionary<string, string> task)
        {
            string temperature = AppendUnit(Get(task, "设定温度(℃)"), "℃");
            return ParsePositiveInt(Get(task, "湿度测点数", "0")) > 0
                ? $"温度 {temperature}；湿度 {AppendUnit(Get(task, "设定湿度(%RH)"), "%RH")}"
                : $"温度 {temperature}";
        }

        /// <summary>组合长、宽、高工作区尺寸。</summary>
        private static string BuildWorkZone(IReadOnlyDictionary<string, string> task) =>
            $"{Get(task, "工作区长度(mm)", "-")} × {Get(task, "工作区宽度(mm)", "-")} × {Get(task, "工作区高度(mm)", "-")} mm";

        /// <summary>组合 JJF 1376 原始记录要求的炉膛 L、W、H 尺寸。</summary>
        private static string BuildFurnaceChamber(IReadOnlyDictionary<string, string> task) =>
            $"L={Get(task, "炉膛长度(mm)", "-")} mm；W={Get(task, "炉膛宽度(mm)", "-")} mm；H={Get(task, "炉膛高度(mm)", "-")} mm";

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
