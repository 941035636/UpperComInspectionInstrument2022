using UpperComInspectionInstrument2022.Services;
using UpperComInspectionInstrument2022.Models;
using System.Collections;
using System.IO.Compression;
using System.Resources;
using System.Text;

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

CalibrationStandardRule smallChamber = CalibrationStandardRuleService.GetRule(0, 0, true);
Assert(smallChamber.TemperaturePointCount == 9 && smallChamber.HumidityPointCount == 3, "JJF1101 small point count");
Assert(smallChamber.TemperatureCenterPoint == 5 && smallChamber.SampleCount == 16 && smallChamber.SampleIntervalSeconds == 120, "JJF1101 small plan");
Assert(smallChamber.HumidityCenterPoint == 3, "JJF1101 small humidity O mapping");
Assert(smallChamber.MinimumAmbientPressure == 80 && smallChamber.MaximumAmbientPressure == 106, "JJF1101 pressure");
Assert(smallChamber.SupportsCustomPointLayout && smallChamber.PointLayoutModeOptions.Length == 3 &&
       smallChamber.PointLayoutModeOptions[1].Contains("点数可自定义") &&
       smallChamber.CustomPointCountModeIndex == 2 &&
       CalibrationStandardRuleService.AllowsCustomPointInput(smallChamber, 1) &&
       CalibrationStandardRuleService.AllowsCustomPointInput(smallChamber, 2),
    "JJF1101 separates position adjustment from extreme-volume point count adjustment");
Assert(CalibrationStandardRuleService.RequiresDeviationForPointCountChange(smallChamber, 1, 8, 3) &&
       !CalibrationStandardRuleService.RequiresDeviationForPointCountChange(smallChamber, 1, 9, 3),
    "JJF1101 custom point count requires a traceable deviation while position-only adjustment does not");
Assert(CalibrationStandardRuleService.MatchesVolumeClass(0, 0, 2) &&
       CalibrationStandardRuleService.MatchesVolumeClass(0, 1, 2.000001) &&
       CalibrationStandardRuleService.AllowsJjf1101PointCountAdjustment(0.049) &&
       !CalibrationStandardRuleService.AllowsJjf1101PointCountAdjustment(0.05) &&
       !CalibrationStandardRuleService.AllowsJjf1101PointCountAdjustment(50) &&
       CalibrationStandardRuleService.AllowsJjf1101PointCountAdjustment(50.001),
    "JJF1101 volume linkage and extreme-volume boundaries");
Assert(CalibrationStandardRuleService.GetCalibrationPointRuleText(smallChamber, 1).Contains("下限") &&
       CalibrationStandardRuleService.GetCalibrationPointRuleText(smallChamber, 1).Contains("中间点"),
    "JJF1101 calibration point strategy");

CalibrationStandardRule largeChamber = CalibrationStandardRuleService.GetRule(0, 1, true);
Assert(largeChamber.TemperaturePointCount == 15 && largeChamber.HumidityPointCount == 4 && largeChamber.TemperatureCenterPoint == 15, "JJF1101 large plan");
Assert(largeChamber.HumidityCenterPoint == 4, "JJF1101 large humidity O mapping");

CalibrationStandardRule smallFurnace = CalibrationStandardRuleService.GetRule(1, 0, false);
Assert(smallFurnace.TemperaturePointCount == 5 && smallFurnace.TemperatureCenterPoint == 3, "JJF1376 small plan");
Assert(smallFurnace.SampleCount == 20 && smallFurnace.SampleIntervalSeconds == 180, "JJF1376 sample plan");
Assert(smallFurnace.SupportsCustomPointLayout && smallFurnace.CustomPointCountModeIndex == 2 &&
       smallFurnace.PointLayoutModeOptions.Length == 3 &&
       !CalibrationStandardRuleService.AllowsCustomPointInput(smallFurnace, 0) &&
       !CalibrationStandardRuleService.AllowsCustomPointInput(smallFurnace, 1) &&
       CalibrationStandardRuleService.AllowsCustomPointInput(smallFurnace, 2) &&
       smallFurnace.PointLayoutModeOptions[0].Contains("工作区尺寸") &&
       smallFurnace.PointLayoutModeOptions[1].Contains("炉膛尺寸"),
    "JJF1376 locks normative layouts and exposes a separate traceable custom layout mode");
Assert(CalibrationStandardRuleService.MatchesVolumeClass(1, 0, 0.15) &&
       CalibrationStandardRuleService.MatchesVolumeClass(1, 1, 0.150001),
    "JJF1376 measurement-zone volume linkage");
Assert(smallFurnace.CalibrationPointOptions[1].Contains("最低和最高") &&
       !smallFurnace.CalibrationPointOptions[1].Contains("中间"),
    "JJF1376 calibration temperature strategy");

string generatedResourceName = typeof(CalibrationStandardRuleService).Assembly.GetManifestResourceNames()
    .Single(name => name.EndsWith(".g.resources", StringComparison.OrdinalIgnoreCase));
using Stream generatedResourceStream = typeof(CalibrationStandardRuleService).Assembly
    .GetManifestResourceStream(generatedResourceName)!;
using ResourceReader resourceReader = new(generatedResourceStream);
HashSet<string> embeddedResourceKeys = resourceReader.Cast<DictionaryEntry>()
    .Select(entry => entry.Key?.ToString() ?? string.Empty)
    .ToHashSet(StringComparer.OrdinalIgnoreCase);
Assert(embeddedResourceKeys.Contains("resources/standards/jjf1376-figure1.png") &&
       embeddedResourceKeys.Contains("resources/standards/jjf1376-figure2.png"),
    "JJF1376 layout figures are embedded WPF resources");

CalibrationStandardRule largeFurnace = CalibrationStandardRuleService.GetRule(1, 1, false);
Assert(largeFurnace.TemperaturePointCount == 9 && largeFurnace.TemperatureCenterPoint == 9, "JJF1376 large plan");

SystemSettingsContext.StandardName = "温湿度巡检仪";
SystemSettingsContext.CertificateNumber = "CERT-001";
SystemSettingsContext.ValidityDate = DateTime.Today.AddDays(1);
Assert(CalibrationTaskContext.TrySnapshotCurrentStandardSettings(out _) &&
       CalibrationTaskContext.ReferencedStandardName == "温湿度巡检仪" &&
       CalibrationTaskContext.ReferencedCertificateNumber == "CERT-001",
    "current standard settings can be explicitly snapshotted into task");
SystemSettingsContext.ValidityDate = DateTime.Today.AddDays(-1);
Assert(!CalibrationTaskContext.TrySnapshotCurrentStandardSettings(out string expiredStandardError) &&
       expiredStandardError.Contains("到期"),
    "expired standard certificate blocks task snapshot");
SystemSettingsContext.ValidityDate = DateTime.Today.AddDays(1);
SystemSettingsContext.TemperatureResolution = 0.02;
Assert(!CalibrationTaskContext.TrySnapshotCurrentStandardSettings(0, false, out string lowResolutionError) &&
       lowResolutionError.Contains("0.01"),
    "JJF1101 rejects a temperature standard with insufficient resolution");
SystemSettingsContext.TemperatureResolution = 0.01;
SystemSettingsContext.MeasuringInstrumentClass = 0.1;
Assert(!CalibrationTaskContext.TrySnapshotCurrentStandardSettings(1, false, out string furnaceClassError) &&
       furnaceClassError.Contains("0.02"),
    "JJF1376 rejects an insufficient measuring-instrument class");
SystemSettingsContext.MeasuringInstrumentClass = 0.02;
SystemSettingsContext.ThermocoupleGrade = "廉金属1级";
CalibrationTaskContext.StandardIndex = 1;
Assert(CalibrationTaskContext.TrySnapshotCurrentStandardSettings(1, false, out _) &&
       CalibrationTaskContext.ReferencedMeasuringInstrumentClass == 0.02 &&
       CalibrationTaskContext.ReferencedThermocoupleGrade == "廉金属1级" &&
       CalibrationTaskContext.TryValidateReferencedStandardSettings(out _),
    "JJF1376 instrument class and thermocouple grade are frozen into the task snapshot");

Assert(ChannelCorrectionService.TryParse("1:0.02,2:-0.01", 50, out Dictionary<int, double> corrections, out _), "correction parse");
Assert(corrections.Count == 2 && corrections[2] == -0.01, "correction values");
Assert(!ChannelCorrectionService.TryParse("51:0.1", 50, out _, out _), "correction bounds");

Assert(Math.Abs(InspectionMeterService.DecodeFloatBigEndian(new byte[] { 0x42, 0xF6, 0xCC, 0xCD }) - 123.4) < 0.0001,
    "protocol float byte order");
Assert(InspectionMeterService.DecodeSignedHundredths(0x9CFF) == -1.0, "little-endian signed humidity-probe value");
Assert(InspectionMeterService.DecodeSignedHundredths(0xAC02) == 6.84, "field humidity register byte order");

var primaryTemperature = new InspectionChannelData
{
    Channel = 1, Type = ChannelType.Temperature, Role = ChannelRole.PrimaryTemperature, Value = 20, IsValid = true
};
var probeTemperature = new InspectionChannelData
{
    Channel = 1, Type = ChannelType.Temperature, Role = ChannelRole.HumidityProbeTemperature, Value = 0, IsValid = true
};
var humidityChannel = new InspectionChannelData
{
    Channel = 1, Type = ChannelType.Humidity, Role = ChannelRole.Humidity, Value = 50, IsValid = true
};
var mixedChannels = new List<InspectionChannelData> { primaryTemperature, probeTemperature, humidityChannel };
ChannelCorrectionService.Apply(mixedChannels, "1:0.2", "1:-0.5");
Assert(primaryTemperature.Value == 20.2 && probeTemperature.Value == 0 && humidityChannel.Value == 49.5,
    "corrections exclude humidity-probe companion temperature");
List<InspectionChannelData> requiredChannels = MeasurementChannelSelectionService.SelectRequired(mixedChannels, 1, 1);
Assert(requiredChannels.Count == 2 && requiredChannels.Contains(primaryTemperature) && requiredChannels.Contains(humidityChannel),
    "matrix channels exclude humidity-probe companion temperature");

Assert(TemperatureSensorCatalog.GetLegacyCode(4) == "TC_S" &&
       TemperatureSensorCatalog.GetLegacyCode(5) == "OTHER" &&
       TemperatureSensorCatalog.GetLegacyCode(6) == "TC_E",
    "legacy sensor index migration");
Assert(TemperatureSensorCatalog.GetIndex("TC_E") == 5, "stable sensor code lookup after display reorder");
Assert(TemperatureSensorCatalog.DisplayNames.Count == TemperatureSensorCatalog.Options.Count, "sensor display catalog alignment");

static MeasurementSnapshot Snapshot(params double[] temperatures) => new()
{
    Timestamp = DateTime.Now,
    Channels = temperatures.Select((value, index) => new InspectionChannelData
    {
        Channel = index + 1,
        Type = ChannelType.Temperature,
        Value = value,
        IsValid = true
    }).ToList(),
    ValidChannelCount = temperatures.Length
};

static MeasurementSnapshot SnapshotWithHumidity(double[] temperatures, double[] humidities) => new()
{
    Timestamp = DateTime.Now,
    Channels = temperatures.Select((value, index) => new InspectionChannelData
    {
        Channel = index + 1,
        Type = ChannelType.Temperature,
        Role = ChannelRole.PrimaryTemperature,
        Value = value,
        IsValid = true
    }).Concat(humidities.Select((value, index) => new InspectionChannelData
    {
        Channel = index + 1,
        Type = ChannelType.Humidity,
        Role = ChannelRole.Humidity,
        Value = value,
        IsValid = true
    })).ToList(),
    ValidChannelCount = temperatures.Length + humidities.Length
};

CalibrationTaskContext.StandardIndex = 0;
CalibrationTaskContext.CalibrationTypeIndex = 0;
CalibrationTaskContext.TemperaturePointCount = 2;
CalibrationTaskContext.PlannedCount = 2;
CalibrationTaskContext.SetTemperature = 20;
CalibrationTaskContext.ReferencedTemperatureResolution = 0.01;
CalibrationTaskContext.ReferencedTemperatureUncertainty = 0.04;
CalibrationTaskContext.ReferencedTemperatureCoverage = 2;
CalibrationTaskContext.ReferencedTemperatureStabilityChange = 0.1;
CalibrationRunContext.Begin();
CalibrationRunContext.Add(Snapshot(19, 21), 20, null);
CalibrationRunContext.Add(Snapshot(20, 22), 20, null);
CalibrationResultSummary environmentResult = CalibrationResultCalculator.Calculate();
Assert(environmentResult.IsValid, "JJF1101 result valid");
Assert(environmentResult.TemperatureUpperDeviation == 2 && environmentResult.TemperatureLowerDeviation == -1, "JJF1101 deviation formula");
Assert(environmentResult.TemperatureUniformity == 2 && environmentResult.TemperatureFluctuation == 0.5, "JJF1101 uniformity/fluctuation formula");
Assert(environmentResult.UncertaintyBudgets.Count == 1 && environmentResult.UncertaintyBudgets[0].Components.Count == 4,
    "JJF1101 uncertainty budget exposes four Appendix C components");
UncertaintyBudgetSummary environmentBudget = environmentResult.UncertaintyBudgets[0];
Assert(environmentBudget.Components.Select(component => component.Symbol).SequenceEqual(new[] { "u1", "u2", "u3", "u4" }) &&
       Math.Abs(environmentBudget.Components[1].Divisor - 2 * Math.Sqrt(3)) < 0.000001 &&
       Math.Abs(environmentBudget.ExpandedUncertainty - environmentResult.TemperatureExpandedUncertainty) < 0.000001,
    "JJF1101 uncertainty component divisors and final U remain traceable");
double expectedEnvironmentU = 2 * Math.Sqrt(
    Math.Pow(Math.Sqrt(0.5), 2) +
    Math.Pow(0.01 / (2 * Math.Sqrt(3)), 2) +
    Math.Pow(0.04 / 2, 2) +
    Math.Pow(0.1 / Math.Sqrt(3), 2));
Assert(Math.Abs(environmentResult.TemperatureExpandedUncertainty - expectedEnvironmentU) < 0.000001,
    "JJF1101 Appendix C numeric uncertainty formula");

CalibrationTaskContext.CalibrationTypeIndex = 1;
CalibrationTaskContext.HumidityPointCount = 2;
CalibrationTaskContext.SetHumidity = 50;
CalibrationTaskContext.ReferencedHumidityResolution = 0.1;
CalibrationTaskContext.ReferencedHumidityUncertainty = 1;
CalibrationTaskContext.ReferencedHumidityCoverage = 2;
CalibrationTaskContext.ReferencedHumidityStabilityChange = 0.5;
CalibrationRunContext.Begin();
CalibrationRunContext.Add(SnapshotWithHumidity(new[] { 19d, 21d }, new[] { 49d, 52d }), 20, 50);
CalibrationRunContext.Add(SnapshotWithHumidity(new[] { 20d, 22d }, new[] { 50d, 51d }), 20, 50);
CalibrationResultSummary humidityResult = CalibrationResultCalculator.Calculate();
Assert(humidityResult.IsValid && humidityResult.HumidityUpperDeviation == 2 && humidityResult.HumidityLowerDeviation == -1 &&
       humidityResult.HumidityUniformity == 2 && humidityResult.HumidityFluctuation == 0.5,
    "JJF1101 humidity deviation, uniformity and fluctuation formulas");
double expectedHumidityU = 2 * Math.Sqrt(
    Math.Pow(Math.Sqrt(0.5), 2) +
    Math.Pow(0.1 / (2 * Math.Sqrt(3)), 2) +
    Math.Pow(1d / 2, 2) +
    Math.Pow(0.5 / Math.Sqrt(3), 2));
Assert(Math.Abs(humidityResult.HumidityExpandedUncertainty - expectedHumidityU) < 0.000001,
    "JJF1101 humidity Appendix C numeric uncertainty formula");

CalibrationTaskContext.StandardIndex = 1;
CalibrationTaskContext.TemperaturePointCount = 3;
CalibrationTaskContext.TemperatureCenterPoint = 1;
CalibrationTaskContext.PlannedCount = 2;
CalibrationTaskContext.SetTemperature = 100;
CalibrationTaskContext.ReferencedTemperatureUncertainty = 0.84;
CalibrationTaskContext.ReferencedTemperatureCoverage = 2;
CalibrationRunContext.Begin();
CalibrationRunContext.Add(Snapshot(101, 102, 100), null, null);
CalibrationRunContext.Add(Snapshot(102, 103, 101), null, null);
CalibrationResultSummary furnaceResult = CalibrationResultCalculator.Calculate();
Assert(furnaceResult.IsValid, "JJF1376 result valid");
Assert(furnaceResult.FurnaceUniformityUpper == 1 && furnaceResult.FurnaceUniformityLower == -1, "JJF1376 uniformity formula");
Assert(furnaceResult.FurnaceStabilityUpper == 0.5 && furnaceResult.FurnaceStabilityLower == -0.5, "JJF1376 stability formula");
Assert(furnaceResult.FurnaceDeviationUpper == 2.5 && furnaceResult.FurnaceDeviationLower == 0.5, "JJF1376 deviation formula uses point means and nominal temperature");
Assert(furnaceResult.FurnaceMaximumDifference == 2, "JJF1376 maximum difference formula");
Assert(furnaceResult.UncertaintyBudgets.Count == 2 && furnaceResult.UncertaintyBudgets.All(budget => budget.Components.Count == 4),
    "JJF1376 upper/lower uniformity budgets expose extreme and center point components");
Assert(furnaceResult.UncertaintyBudgets[0].Components.Count(component => component.Category == "A类") == 2 &&
       furnaceResult.UncertaintyBudgets[0].Components.Count(component => component.SensitivityCoefficient == -1) == 2 &&
       Math.Abs(furnaceResult.UncertaintyBudgets[0].ExpandedUncertainty - furnaceResult.FurnaceUniformityUpperUncertainty) < 0.000001,
    "JJF1376 Appendix D mean repeatability, certificate and sensitivity coefficients remain traceable");
double expectedFurnaceU = 2 * Math.Sqrt(
    Math.Pow(Math.Sqrt(0.5) / Math.Sqrt(2), 2) +
    Math.Pow(0.84 / 2, 2) +
    Math.Pow(Math.Sqrt(0.5) / Math.Sqrt(2), 2) +
    Math.Pow(0.84 / 2, 2));
Assert(Math.Abs(furnaceResult.FurnaceUniformityUpperUncertainty - expectedFurnaceU) < 0.000001 &&
       Math.Abs(furnaceResult.FurnaceUniformityLowerUncertainty - expectedFurnaceU) < 0.000001,
    "JJF1376 Appendix D numeric uncertainty formula");

CalibrationTaskContext.TemperaturePointCount = 2;
CalibrationTaskContext.TemperatureCenterPoint = 1;
CalibrationRunContext.Begin();
CalibrationRunContext.Add(Snapshot(100, 102), null, null);
CalibrationRunContext.Add(Snapshot(101, 103), null, null);
CalibrationResultSummary centerIsMinimumResult = CalibrationResultCalculator.Calculate();
Assert(centerIsMinimumResult.FurnaceUniformityLower == 0 && centerIsMinimumResult.FurnaceUniformityLowerUncertainty == 0 &&
       centerIsMinimumResult.UncertaintyBudgets[1].Components.Count == 1 &&
       centerIsMinimumResult.UncertaintyBudgets[1].Components[0].Distribution.Contains("抵消"),
    "JJF1376 center-as-extreme identity cancels value and uncertainty instead of treating one point as independent inputs");

string storageTestRoot = Path.Combine(Path.GetTempPath(), "UpperComInspectionInstrument2022-storage-test", Guid.NewGuid().ToString("N"));
CalibrationTaskContext.StandardIndex = 0;
CalibrationTaskContext.CalibrationTypeIndex = 0;
CalibrationTaskContext.TemperaturePointCount = 2;
CalibrationTaskContext.HumidityPointCount = 0;
CalibrationTaskContext.TemperatureCenterPoint = 1;
CalibrationTaskContext.PlannedCount = 2;
CalibrationTaskContext.SamplingIntervalSeconds = 120;
CalibrationTaskContext.SetTemperature = 20;
CalibrationTaskContext.EquipmentName = "测试设备,一号";
CalibrationTaskContext.EquipmentSerialNumber = "TEST-001";
CalibrationTaskContext.ReferencedCertificateNumber = "CERT-001";
CalibrationTaskContext.ReferencedMeasuringInstrumentClass = 0.02;
CalibrationTaskContext.ReferencedThermocoupleGrade = "廉金属1级";
CalibrationRunContext.Begin();
CalibrationFileStorageService testStorage = new(storageTestRoot);
Assert(testStorage.TryBeginJob(out string beginStorageError), "storage begin: " + beginStorageError);
CalibrationSampleRecord storageRecord1 = CalibrationRunContext.Add(Snapshot(19, 21), 20, null);
CalibrationSampleRecord storageRecord2 = CalibrationRunContext.Add(Snapshot(20, 22), 20, null);
Assert(testStorage.TryAppendSample(storageRecord1, out string appendStorageError1), "storage append 1: " + appendStorageError1);
Assert(testStorage.TryAppendSample(storageRecord2, out string appendStorageError2), "storage append 2: " + appendStorageError2);
CalibrationResultSummary storageResult = CalibrationResultCalculator.Calculate();
Assert(testStorage.TryCompleteJob(storageResult, out string completeStorageError), "storage complete: " + completeStorageError);
string storageJobDirectory = testStorage.CurrentJobDirectory!;
foreach (string expectedFile in new[] { "作业摘要.csv", "任务信息.csv", "正式采样.csv", "正式采样原始通道.csv", "校准结果.csv", "不确定度分量.csv" })
{
    string expectedPath = Path.Combine(storageJobDirectory, expectedFile);
    Assert(File.Exists(expectedPath), "storage file exists: " + expectedFile);
    byte[] prefix = File.ReadAllBytes(expectedPath).Take(3).ToArray();
    Assert(prefix.SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }), "storage file UTF-8 BOM: " + expectedFile);
}
string sampleCsv = File.ReadAllText(Path.Combine(storageJobDirectory, "正式采样.csv"));
Assert(sampleCsv.Contains("\"温度1(℃)\"") && sampleCsv.Contains("\"19\"") && sampleCsv.Contains("\"22\""),
    "wide sample CSV contains dynamic channel matrix");
string rawCsv = File.ReadAllText(Path.Combine(storageJobDirectory, "正式采样原始通道.csv"));
Assert(rawCsv.Contains("\"修正前原始值\"") && rawCsv.Split("\"温度\"").Length - 1 == 4,
    "raw channel CSV contains one row per formal channel");
string uncertaintyCsv = File.ReadAllText(Path.Combine(storageJobDirectory, "不确定度分量.csv"));
Assert(uncertaintyCsv.Contains("\"标准不确定度ui\"") && uncertaintyCsv.Contains("\"合成标准不确定度uc\"") &&
       uncertaintyCsv.Contains("\"u1\"") && uncertaintyCsv.Contains("JJF 1101-2019 附录C"),
    "uncertainty CSV contains auditable components, synthesis and standard basis");
string taskCsv = File.ReadAllText(Path.Combine(storageJobDirectory, "任务信息.csv"));
Assert(taskCsv.Contains("\"测温仪器级别\"") && taskCsv.Contains("\"热电偶等级\"") && taskCsv.Contains("\"廉金属1级\""),
    "task snapshot CSV preserves JJF1376 standard capability fields for traceability");
IReadOnlyList<CalibrationArchiveSummary> storageHistory = testStorage.LoadHistory("测试设备", "JJF 1101-2019", "已完成");
Assert(storageHistory.Count == 1 && storageHistory[0].SampleProgress == "2/2" && storageHistory[0].Device == "测试设备,一号",
    "history scans quoted CSV summaries without a database");
Assert(CalibrationExcelReportService.Default.TryGenerate(storageJobDirectory, out string generatedExcelPath, out string generatedExcelError),
    "Excel report generation: " + generatedExcelError);
Assert(File.Exists(generatedExcelPath), "Excel report file exists");
using (ZipArchive excelArchive = ZipFile.OpenRead(generatedExcelPath))
{
    Assert(excelArchive.GetEntry("xl/workbook.xml") != null &&
           excelArchive.Entries.Count(entry => entry.FullName.StartsWith("xl/worksheets/sheet", StringComparison.Ordinal)) == 5,
        "Excel report contains workbook and five worksheets");
    using StreamReader workbookReader = new(excelArchive.GetEntry("xl/workbook.xml")!.Open());
    string workbookXml = workbookReader.ReadToEnd();
    Assert(workbookXml.Contains("不确定度分量") && workbookXml.Contains("任务与结果") && workbookXml.Contains("任务快照"),
        "Excel workbook exposes uncertainty budget alongside result and task snapshot");
}
Assert(CalibrationWordCertificateService.Default.TryGenerate(storageJobDirectory, out string generatedWordPath, out string generatedWordError),
    "Word certificate generation: " + generatedWordError);
Assert(File.Exists(generatedWordPath), "Word certificate file exists");
using (ZipArchive wordArchive = ZipFile.OpenRead(generatedWordPath))
{
    Assert(wordArchive.GetEntry("word/document.xml") != null &&
           wordArchive.GetEntry("word/styles.xml") != null &&
           wordArchive.Entries.Any(entry => entry.FullName.StartsWith("word/header", StringComparison.Ordinal)) &&
           wordArchive.Entries.Any(entry => entry.FullName.StartsWith("word/footer", StringComparison.Ordinal)),
        "Word certificate contains document, styles, header and footer parts");
    using StreamReader documentReader = new(wordArchive.GetEntry("word/document.xml")!.Open());
    string documentXml = documentReader.ReadToEnd();
    Assert(documentXml.Contains("校准证书") && documentXml.Contains("校准结果") &&
           documentXml.Contains("TEST-001") && documentXml.Contains("待审核签发"),
        "Word certificate contains archived task identity, result section and review status");
}
Assert(CalibrationPdfArchiveService.Default.TryGenerate(storageJobDirectory, out string generatedPdfPath, out string generatedPdfError),
    "PDF archive generation: " + generatedPdfError);
Assert(File.Exists(generatedPdfPath) && new FileInfo(generatedPdfPath).Length > 5000,
    "PDF archive file exists and contains nontrivial content");
Assert(File.ReadAllBytes(generatedPdfPath).Take(5).SequenceEqual(System.Text.Encoding.ASCII.GetBytes("%PDF-")),
    "PDF archive has a valid PDF file signature");
Assert(storageHistory[0].ExcelReportStatus == "已生成" &&
       storageHistory[0].WordCertificateStatus == "已生成" &&
       storageHistory[0].PdfArchiveStatus == "已生成",
    "history report status reflects the generated files on disk");

string legacyJobDirectory = Path.Combine(storageTestRoot, "legacy-format-1.0");
Directory.CreateDirectory(legacyJobDirectory);
foreach (string sourceFile in Directory.GetFiles(storageJobDirectory, "*.csv"))
    File.Copy(sourceFile, Path.Combine(legacyJobDirectory, Path.GetFileName(sourceFile)), overwrite: true);
File.Delete(Path.Combine(legacyJobDirectory, "不确定度分量.csv"));
Assert(!CalibrationExcelReportService.Default.TryGenerate(legacyJobDirectory, out _, out string incomplete11Error) &&
       incomplete11Error.Contains("必须包含文件"),
    "format 1.1 archive rejects a missing uncertainty component file");
Assert(!CalibrationPdfArchiveService.Default.TryGenerate(legacyJobDirectory, out _, out string incomplete11PdfError) &&
       incomplete11PdfError.Contains("必须包含文件"),
    "format 1.1 archive rejects PDF generation when uncertainty components are missing");
string legacySummaryPath = Path.Combine(legacyJobDirectory, "作业摘要.csv");
string legacySummary = File.ReadAllText(legacySummaryPath).Replace("\"1.1\"", "\"1.0\"", StringComparison.Ordinal);
File.WriteAllText(legacySummaryPath, legacySummary, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
Assert(CalibrationExcelReportService.Default.TryGenerate(legacyJobDirectory, out string legacyExcelPath, out string legacyExcelError),
    "legacy Excel compatibility: " + legacyExcelError);
using (ZipArchive legacyExcelArchive = ZipFile.OpenRead(legacyExcelPath))
{
    Assert(legacyExcelArchive.Entries.Count(entry => entry.FullName.StartsWith("xl/worksheets/sheet", StringComparison.Ordinal)) == 5,
        "legacy archive receives an explicit uncertainty limitation worksheet");
}
Assert(CalibrationWordCertificateService.Default.TryGenerate(legacyJobDirectory, out string legacyWordPath, out string legacyWordError),
    "legacy Word compatibility: " + legacyWordError);
Assert(File.Exists(legacyWordPath), "legacy archive receives a Word certificate with an uncertainty traceability limitation");
Assert(CalibrationPdfArchiveService.Default.TryGenerate(legacyJobDirectory, out string legacyPdfPath, out string legacyPdfError),
    "legacy PDF compatibility: " + legacyPdfError);
Assert(File.Exists(legacyPdfPath), "legacy archive receives a PDF report with an uncertainty traceability limitation");

CalibrationTaskContext.TemperaturePointCount = 3;
CalibrationTaskContext.PlannedCount = 2;
CalibrationRunContext.Begin();
Assert(testStorage.TryBeginJob(out string interruptedBeginError), "interrupted storage begin: " + interruptedBeginError);
CalibrationSampleRecord incompleteRecord = CalibrationRunContext.Add(Snapshot(20, 21), 20, null);
Assert(testStorage.TryAppendSample(incompleteRecord, out string incompleteAppendError), "incomplete storage append: " + incompleteAppendError);
Assert(testStorage.TryMarkInterrupted("自动检查中断状态", out string interruptedStateError), "interrupted state: " + interruptedStateError);
string incompleteSampleCsv = File.ReadAllText(Path.Combine(testStorage.CurrentJobDirectory!, "正式采样.csv"));
string incompleteRawCsv = File.ReadAllText(Path.Combine(testStorage.CurrentJobDirectory!, "正式采样原始通道.csv"));
Assert(incompleteSampleCsv.Contains("\"T3\"") && incompleteRawCsv.Contains("\"Missing\"") && incompleteRawCsv.Contains("\"任务要求通道未返回\""),
    "missing required channel is explicit in matrix and raw detail");
Assert(testStorage.LoadHistory(status: "已中断").Count == 1, "history identifies interrupted local job");

string realtimeTestRoot = storageTestRoot + "-realtime";
RealtimeMeasurementFileStorageService realtimeStorage = new(realtimeTestRoot);
RealtimeMeasurementSessionInfo realtimeInfo = new()
{
    PortName = "COM-TEST",
    BaudRate = 115200,
    SlaveAddress = 1,
    IntervalMilliseconds = 2000,
    CalibrationType = "温度",
    SensorType = "四线制 Pt100",
    TemperaturePointCount = 2,
    HumidityPointCount = 0,
    HasCalibrationTask = false,
    Standard = "未建立任务（设备联调）",
    EquipmentName = "实时联调设备",
    EquipmentSerialNumber = "RT-001"
};
Assert(realtimeStorage.TryBeginSession(realtimeInfo, out string realtimeBeginError),
    "realtime storage begin: " + realtimeBeginError);
MeasurementSnapshot realtimeSnapshot1 = Snapshot(19, 21);
realtimeSnapshot1.Sequence = 1001;
MeasurementSnapshot realtimeSnapshot2 = Snapshot(20);
realtimeSnapshot2.Sequence = 1002;
Assert(realtimeStorage.TryAppendSnapshot(realtimeSnapshot1, out string realtimeAppendError1),
    "realtime storage append 1: " + realtimeAppendError1);
Assert(realtimeStorage.TryAppendSnapshot(realtimeSnapshot2, out string realtimeAppendError2),
    "realtime storage append 2: " + realtimeAppendError2);
Assert(realtimeStorage.TryEndSession("已停止", "自动检查结束实时测量", out string realtimeEndError),
    "realtime storage end: " + realtimeEndError);
string realtimeSessionDirectory = realtimeStorage.CurrentSessionDirectory!;
Assert(realtimeStorage.SavedSnapshotCount == 2 && !realtimeStorage.IsActive,
    "realtime session count and final state");
foreach (string expectedFile in new[] { "实时测量摘要.csv", "实时测量.csv", "实时测量原始通道.csv" })
{
    string expectedPath = Path.Combine(realtimeSessionDirectory, expectedFile);
    Assert(File.Exists(expectedPath), "realtime storage file exists: " + expectedFile);
    byte[] prefix = File.ReadAllBytes(expectedPath).Take(3).ToArray();
    Assert(prefix.SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }), "realtime storage UTF-8 BOM: " + expectedFile);
}
string realtimeCsv = File.ReadAllText(Path.Combine(realtimeSessionDirectory, "实时测量.csv"));
string realtimeRawCsv = File.ReadAllText(Path.Combine(realtimeSessionDirectory, "实时测量原始通道.csv"));
string realtimeSummaryCsv = File.ReadAllText(Path.Combine(realtimeSessionDirectory, "实时测量摘要.csv"));
Assert(realtimeCsv.Contains("\"温度1(℃)\"") && realtimeCsv.Contains("\"温度2(℃)\"") &&
       realtimeCsv.Contains("\"T2\"") && realtimeCsv.Contains("\"1002\""),
    "realtime CSV keeps dynamic columns and flags a missing configured channel");
Assert(realtimeRawCsv.Contains("\"Missing\"") && realtimeRawCsv.Contains("\"实时测量要求通道未返回\""),
    "realtime raw CSV keeps missing-channel evidence");
Assert(realtimeSummaryCsv.Contains("\"普通实时测量\"") && realtimeSummaryCsv.Contains("\"已停止\"") &&
       realtimeSummaryCsv.Contains("\"2\""),
    "realtime summary keeps independent session status and count");
Assert(!File.Exists(Path.Combine(realtimeSessionDirectory, "正式采样.csv")) &&
       !string.Equals(realtimeSessionDirectory, storageJobDirectory, StringComparison.OrdinalIgnoreCase),
    "ordinary realtime records stay separate from formal calibration archives");

// 采集生命周期回归：停止只发出取消信号时，旧同步读尚未返回，第二次启动必须被拒绝。
BlockingMeasurementReader blockingReader = new();
InspectionDataAcquisitionService lifecycleService = new(blockingReader);
Assert(lifecycleService.Start(1, 200, "温度"), "first acquisition loop starts");
Assert(blockingReader.ReadEntered.Wait(TimeSpan.FromSeconds(2)), "blocking reader receives first request");
lifecycleService.Stop();
Assert(lifecycleService.IsRunning && !lifecycleService.Start(1, 200, "温度"),
    "rapid restart is rejected until the previous device read exits");
blockingReader.AllowReadToReturn.Set();
await lifecycleService.StopAsync();
Assert(!lifecycleService.IsRunning, "stopped acquisition loop fully exits");

TaskCompletionSource<bool> restartedData = new(TaskCreationOptions.RunContinuationsAsynchronously);
lifecycleService.DataAcquired += (_, _) => restartedData.TrySetResult(true);
Assert(lifecycleService.Start(1, 200, "温度"), "acquisition can restart after the old loop exits");
await restartedData.Task.WaitAsync(TimeSpan.FromSeconds(2));
await lifecycleService.StopAsync();

// 通信退避回归：连续错误按 1 s、2 s、3 s 递增，并在第三次后自动停止，不能无限轰击设备。
AlwaysFailMeasurementReader failingReader = new();
InspectionDataAcquisitionService retryService = new(failingReader);
List<(int Count, int Delay)> observedFailures = new();
TaskCompletionSource<bool> failureLimitReached = new(TaskCreationOptions.RunContinuationsAsynchronously);
retryService.AcquisitionError += _ =>
{
    observedFailures.Add((retryService.ConsecutiveFailureCount, retryService.NextRetryDelayMilliseconds));
    if (retryService.ConsecutiveFailureCount >= retryService.MaxConsecutiveFailures)
        failureLimitReached.TrySetResult(true);
};
Assert(retryService.Start(1, 200, "温度"), "retry acquisition loop starts");
await failureLimitReached.Task.WaitAsync(TimeSpan.FromSeconds(6));
await retryService.StopAsync();
Assert(observedFailures.SequenceEqual(new[] { (1, 1000), (2, 2000), (3, 3000) }),
    "communication failures use bounded backoff and stop at the configured limit");
Assert(!retryService.IsRunning && failingReader.ReadCount == 3,
    "failure limit stops the request loop without an unbounded fourth read");

// 本地追溯回归：运行日志和业务操作记录都必须是带 BOM、可由办公软件直接打开的 CSV。
string traceRoot = Path.Combine(storageTestRoot, "trace-check");
LocalTraceService traceService = new(traceRoot);
Assert(traceService.TryWriteRuntime("警告", "通信", "模拟超时", "第 1 次失败", "JOB-TRACE", "COM-TEST", out string runtimeLogError),
    "runtime CSV log write: " + runtimeLogError);
Assert(traceService.TryWriteOperation("启动正式校准采样", "成功", "JOB-TRACE", "自动检查", traceRoot, out string operationLogError),
    "operation CSV log write: " + operationLogError);
string runtimeLogPath = Path.Combine(traceService.RuntimeLogDirectory, $"运行日志-{DateTime.Now:yyyyMMdd}.csv");
foreach (string tracePath in new[] { runtimeLogPath, traceService.OperationLogPath })
{
    Assert(File.Exists(tracePath) && File.ReadAllBytes(tracePath).Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }),
        "trace CSV exists with UTF-8 BOM: " + Path.GetFileName(tracePath));
}
Assert(File.ReadAllText(runtimeLogPath).Contains("模拟超时") &&
       File.ReadAllText(traceService.OperationLogPath).Contains("启动正式校准采样"),
    "trace CSV preserves runtime and business action text");

// 异常退出恢复回归：新进程启动后只改摘要状态，已经落盘的样本必须继续保留。
string abandonedJobRoot = Path.Combine(storageTestRoot, "abandoned-job-check");
CalibrationFileStorageService abandonedJob = new(abandonedJobRoot);
CalibrationRunContext.Begin();
Assert(abandonedJob.TryBeginJob(out string abandonedJobBeginError), "abandoned job begin: " + abandonedJobBeginError);
CalibrationSampleRecord abandonedSample = CalibrationRunContext.Add(Snapshot(20, 21, 22), 20, null);
Assert(abandonedJob.TryAppendSample(abandonedSample, out string abandonedSampleError), "abandoned sample append: " + abandonedSampleError);
string abandonedSamplePath = Path.Combine(abandonedJob.CurrentJobDirectory!, "正式采样.csv");
long abandonedSampleLength = new FileInfo(abandonedSamplePath).Length;
CalibrationFileStorageService jobRecoveryScanner = new(abandonedJobRoot);
Assert(jobRecoveryScanner.RecoverAbandonedJobs(out string jobRecoveryError) == 1 && string.IsNullOrEmpty(jobRecoveryError),
    "startup recovers one abandoned formal job: " + jobRecoveryError);
CalibrationArchiveSummary recoveredJob = jobRecoveryScanner.LoadHistory().Single();
Assert(recoveredJob.Status == "已中断" && recoveredJob.StatusMessage.Contains("上次未正常结束") &&
       new FileInfo(abandonedSamplePath).Length == abandonedSampleLength,
    "formal recovery marks summary interrupted without changing saved sample data");

string abandonedRealtimeRoot = Path.Combine(storageTestRoot, "abandoned-realtime-check");
RealtimeMeasurementFileStorageService abandonedRealtime = new(abandonedRealtimeRoot);
Assert(abandonedRealtime.TryBeginSession(realtimeInfo, out string abandonedRealtimeBeginError),
    "abandoned realtime begin: " + abandonedRealtimeBeginError);
Assert(abandonedRealtime.TryAppendSnapshot(realtimeSnapshot1, out string abandonedRealtimeAppendError),
    "abandoned realtime append: " + abandonedRealtimeAppendError);
string abandonedRealtimeDirectory = abandonedRealtime.CurrentSessionDirectory!;
RealtimeMeasurementFileStorageService realtimeRecoveryScanner = new(abandonedRealtimeRoot);
Assert(realtimeRecoveryScanner.RecoverAbandonedSessions(out string realtimeRecoveryError) == 1 && string.IsNullOrEmpty(realtimeRecoveryError),
    "startup recovers one abandoned realtime session: " + realtimeRecoveryError);
string recoveredRealtimeSummary = File.ReadAllText(Path.Combine(abandonedRealtimeDirectory, "实时测量摘要.csv"));
Assert(recoveredRealtimeSummary.Contains("\"已中断\"") && recoveredRealtimeSummary.Contains("上次未正常结束"),
    "realtime recovery marks summary interrupted and keeps recorded data");

Console.WriteLine($"PASS: protocol, standards, formulas, formal/realtime archives, reports, acquisition lifecycle, CSV traces and startup recovery; test archive: {storageJobDirectory}");

/// <summary>用于验证“同步设备读尚未返回”场景的可控读取器。</summary>
sealed class BlockingMeasurementReader : IInspectionMeasurementReader
{
    public ManualResetEventSlim ReadEntered { get; } = new(false);
    public ManualResetEventSlim AllowReadToReturn { get; } = new(false);

    public List<InspectionChannelData> ReadMeasurements(string calibrationType, byte slaveAddress, long acquisitionId)
    {
        ReadEntered.Set();
        if (!AllowReadToReturn.Wait(TimeSpan.FromSeconds(5)))
            throw new TimeoutException("自动检查未释放模拟设备读操作。");
        return new List<InspectionChannelData>
        {
            new() { Channel = 1, Type = ChannelType.Temperature, Value = 20, IsValid = true }
        };
    }
}

/// <summary>用于验证有限重试和退避节拍的固定失败读取器。</summary>
sealed class AlwaysFailMeasurementReader : IInspectionMeasurementReader
{
    public int ReadCount { get; private set; }

    public List<InspectionChannelData> ReadMeasurements(string calibrationType, byte slaveAddress, long acquisitionId)
    {
        ReadCount++;
        throw new TimeoutException("模拟巡检仪无响应。");
    }
}
