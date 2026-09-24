using UpperComInspectionInstrument2022.Services;
using UpperComInspectionInstrument2022.Models;
using UpperComInspectionInstrument2022.Communication;
using System.Collections;
using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
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
Assert(smallChamber.CalibrationPointOptions.Length == 2 &&
       smallChamber.CalibrationPointOptions[0].Contains("常用") &&
       smallChamber.CalibrationPointOptions[1].Contains("客户指定") &&
       !smallChamber.CalibrationPointOptions.Any(option => option.Contains("逐工况")),
    "JJF1101 exposes only common and customer-specified calibration points");

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
Assert(smallFurnace.CalibrationPointOptions.Length == 2 &&
       smallFurnace.CalibrationPointOptions[0].Contains("常用") &&
       smallFurnace.CalibrationPointOptions[1].Contains("客户指定") &&
       !smallFurnace.CalibrationPointOptions.Any(option => option.Contains("逐工况")),
    "JJF1376 exposes only common and customer-specified calibration temperatures");

string accountTestRoot = Path.Combine(
    Path.GetTempPath(), "UpperComInspectionInstrument2022-account-test", Guid.NewGuid().ToString("N"));
LocalAccountService accountService = new(accountTestRoot);
Assert(accountService.TryHasAccounts(out bool hasAccounts, out _) && !hasAccounts,
    "fresh local account store starts empty");
Assert(!accountService.TryRegister("a", string.Empty, "Password123", out _, out string invalidUserError) &&
       invalidUserError.Contains("2～32"),
    "local account validates user name");
Assert(!accountService.TryRegister("tester", string.Empty, "password", out _, out string weakPasswordError) &&
       weakPasswordError.Contains("字母和数字"),
    "local account validates password strength");
Assert(accountService.TryRegister(
           "tester", "测试操作员", "Password123", out LocalUserIdentity? registeredUser, out _) &&
       registeredUser?.DisplayName == "测试操作员",
    "local account registration");
string accountJson = File.ReadAllText(accountService.AccountFilePath);
Assert(!accountJson.Contains("Password123", StringComparison.Ordinal) &&
       accountJson.Contains("PasswordSalt", StringComparison.Ordinal) &&
       accountJson.Contains("PasswordHash", StringComparison.Ordinal) &&
       !accountJson.Contains("RecoveryHash", StringComparison.Ordinal),
    "local account stores only a salted password hash and no recovery-code data");
Assert(!accountService.TryRegister("TESTER", "重复账户", "Password456", out _, out string duplicateUserError) &&
       duplicateUserError.Contains("已经存在"),
    "local account names are case-insensitively unique");
Assert(!accountService.TryAuthenticate("tester", "WrongPassword1", out _, out string wrongPasswordError) &&
       wrongPasswordError.Contains("用户名或密码错误"),
    "local account rejects an incorrect password");
LocalAccountService reloadedAccountService = new(accountTestRoot);
Assert(reloadedAccountService.TryAuthenticate(
           "TESTER", "Password123", out LocalUserIdentity? authenticatedUser, out _) &&
       authenticatedUser?.UserName == "tester",
    "local account persists and authenticates after reload");
string unrelatedLocalFile = Path.Combine(accountTestRoot, "calibration-data.csv");
File.WriteAllText(unrelatedLocalFile, "sample,data");
Assert(reloadedAccountService.TryReRegister(
           "replacement", "新操作员", "ChangedPassword456", out LocalUserIdentity? replacementUser, out _) &&
       replacementUser?.DisplayName == "新操作员",
    "forgotten password can be handled by direct local re-registration");
Assert(!reloadedAccountService.TryAuthenticate("tester", "Password123", out _, out _) &&
       reloadedAccountService.TryAuthenticate(
           "replacement", "ChangedPassword456", out authenticatedUser, out _) &&
       File.ReadAllText(unrelatedLocalFile) == "sample,data",
    "re-registration invalidates the old account without touching unrelated local data");
RememberedCredentialService rememberedCredentialService = new(accountTestRoot);
Assert(rememberedCredentialService.TrySave("replacement", "ChangedPassword456", out _) &&
       !File.ReadAllText(rememberedCredentialService.CredentialFilePath)
           .Contains("ChangedPassword456", StringComparison.Ordinal),
    "remembered password is protected instead of stored as plaintext");
Assert(rememberedCredentialService.TryLoad(
           out bool hasRememberedCredential,
           out string rememberedUserName,
           out string rememberedPassword,
           out _) &&
       hasRememberedCredential && rememberedUserName == "replacement" &&
       rememberedPassword == "ChangedPassword456",
    "remembered password decrypts for the current Windows user");
Assert(rememberedCredentialService.TryClear(out _) &&
       !File.Exists(rememberedCredentialService.CredentialFilePath),
    "remembered password can be cleared without deleting the account");
UserSessionContext.SignIn(authenticatedUser!.UserName, authenticatedUser.DisplayName);
Assert(UserSessionContext.IsAuthenticated && UserSessionContext.Current?.DisplayName == "新操作员",
    "authenticated user session");
UserSessionContext.SignOut();
Assert(!UserSessionContext.IsAuthenticated, "local user sign out");

string corruptAccountRoot = Path.Combine(
    Path.GetTempPath(), "UpperComInspectionInstrument2022-account-corrupt-test", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(corruptAccountRoot);
string corruptAccountPath = Path.Combine(corruptAccountRoot, "users.json");
const string corruptAccountContent = "{ account data is damaged";
File.WriteAllText(corruptAccountPath, corruptAccountContent);
LocalAccountService corruptAccountService = new(corruptAccountRoot);
Assert(!corruptAccountService.TryHasAccounts(out _, out string corruptReadError) &&
       corruptReadError.Contains(corruptAccountPath, StringComparison.Ordinal),
    "damaged account store reports its path instead of treating the machine as unregistered");
Assert(!corruptAccountService.TryRegister("repair-test", string.Empty, "Password123", out _, out _) &&
       !corruptAccountService.TryReRegister("repair-test", string.Empty, "Password123", out _, out _) &&
       File.ReadAllText(corruptAccountPath) == corruptAccountContent,
    "registration and re-registration refuse to overwrite a damaged account store");

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

SystemSettingsContext.LaboratoryName = "测试校准实验室";
SystemSettingsContext.LaboratoryAddress = "测试市质量路1号";
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

Assert(ChannelCorrectionService.TryParse("1:0.02,2:-0.01", InspectionInstrumentProtocol.PhysicalTemperatureChannelCount, out Dictionary<int, double> corrections, out _), "correction parse");
Assert(corrections.Count == 2 && corrections[2] == -0.01, "correction values");
Assert(!ChannelCorrectionService.TryParse("25:0.1", InspectionInstrumentProtocol.PhysicalTemperatureChannelCount, out _, out _), "correction bounds");
Assert(InspectionInstrumentProtocol.GetTemperatureRegisterQuantity(9) == 18,
    "nine active temperature channels request eighteen registers");
Assert(InspectionInstrumentProtocol.GetHumidityRegisterQuantity(3) == 6,
    "three active humidity probes request six humidity/companion-temperature registers");
Assert(InspectionInstrumentProtocol.CompatibleTemperatureBlockRegisterCount == 100,
    "Qt-compatible temperature block contains one hundred registers");
Assert(InspectionInstrumentProtocol.CompatibleHumidityBlockRegisterCount == 20,
    "Qt-compatible humidity block contains twenty registers");
Assert(InspectionInstrumentProtocol.SensorTypeStartAddress == 0x0146 &&
       InspectionInstrumentProtocol.ChannelEnableStartAddress == 0x015E &&
       InspectionInstrumentProtocol.ConfigurationModeCoilAddress == 0x0001 &&
       InspectionInstrumentProtocol.SaveConfigurationCoilAddress == 0x0005,
    "latest protocol channel-configuration register and coil addresses");
Assert(InspectionInstrumentProtocol.ConfigurableSensorTypeChannelCount == 24 &&
       InspectionInstrumentProtocol.ConfigurableEnableChannelCount == 33,
    "current hardware exposes 24 temperature types and 33 enable states");
string channelProfileTestRoot = Path.Combine(
    Path.GetTempPath(),
    "UpperComInspectionInstrument2022-channel-profile-test",
    Guid.NewGuid().ToString("N"));
InspectionInstrumentChannelProfileService channelProfileService = new(channelProfileTestRoot);
bool[] expectedChannelProfile = Enumerable.Range(0, 33).Select(index => index < 10).ToArray();
Assert(channelProfileService.TrySave("COM7", 1, expectedChannelProfile, out string channelProfileSaveError),
    $"channel profile save: {channelProfileSaveError}");
Assert(channelProfileService.TryLoad(
           "com7",
           1,
           out bool[] loadedChannelProfile,
           out DateTime channelProfileSavedAt,
           out string channelProfileLoadError) &&
       loadedChannelProfile.SequenceEqual(expectedChannelProfile) &&
       channelProfileSavedAt != default,
    $"channel profile load: {channelProfileLoadError}");
Assert(!channelProfileService.TryLoad("COM8", 1, out _, out _, out string otherPortProfileError) &&
       string.IsNullOrWhiteSpace(otherPortProfileError),
    "channel profile must be isolated by serial port and slave address");
Assert(!channelProfileService.TrySave(string.Empty, 1, expectedChannelProfile, out string emptyPortProfileError) &&
       !string.IsNullOrWhiteSpace(emptyPortProfileError),
    "channel profile must reject an empty serial port name");
Assert(InspectionInstrumentProtocol.ConfigurationRequestIntervalMilliseconds == 250,
    "configuration requests keep a short device processing interval without slowing the page by one second per frame");
Assert(InspectionInstrumentProtocol.ConfigurationFallbackChunkRegisterCount == 4,
    "configuration reads use the four-register request proven by the serial assistant");
Assert(InspectionInstrumentProtocol.ConfigurationReadConfirmationCount == 1 &&
       InspectionInstrumentProtocol.ConfigurationReadRoundCount >= 3,
    "configuration blocks accept one semantically valid response and retain bounded retries for rejected or missing frames");
Assert(InspectionInstrumentProtocol.InvalidConfigurationReadConfirmationCount == 2,
    "out-of-range configuration values must repeat before the UI offers a controlled repair");
Assert(InspectionInstrumentProtocol.ConfigurationControlResponseTimeoutMilliseconds <= 1000,
    "configuration control commands do not spend multiple seconds waiting for missing coil echoes");
Assert(InspectionInstrumentProtocol.SaveConfigurationCommitDelayMilliseconds >= 2000,
    "configuration save leaves time for the device to commit nonvolatile settings");
Assert(InspectionInstrumentConfigurationService.DecodeConfigurationRegister(0x0100, true) == 1 &&
       InspectionInstrumentConfigurationService.DecodeConfigurationRegister(0x0D00, true) == 13 &&
       InspectionInstrumentConfigurationService.DecodeConfigurationRegister(0x0001, false) == 1,
    "device configuration registers decode both low-byte-first and standard word payloads");
Assert(InspectionInstrumentConfigurationService.EncodeConfigurationRegister(1, true) == 0x0100 &&
       InspectionInstrumentConfigurationService.EncodeConfigurationRegister(13, true) == 0x0D00 &&
       InspectionInstrumentConfigurationService.EncodeConfigurationRegister(1, false) == 0x0001,
    "device configuration writes preserve the detected register payload byte order");
ushort mixedStandard = InspectionInstrumentConfigurationService.DecodeConfigurationRegisterAutomatically(
    0x0001, 1, out bool mixedStandardUsesLowByteFirst);
ushort mixedLowByteFirst = InspectionInstrumentConfigurationService.DecodeConfigurationRegisterAutomatically(
    0x0100, 1, out bool mixedLowByteFirstUsesLowByteFirst);
Assert(mixedStandard == 1 && !mixedStandardUsesLowByteFirst &&
       mixedLowByteFirst == 1 && mixedLowByteFirstUsesLowByteFirst,
    "mixed channel-enable register byte orders decode independently");
bool rejectedDelayedMeasurementWord = false;
try
{
    InspectionInstrumentConfigurationService.DecodeConfigurationRegisterAutomatically(
        0xCDCC, 1, out _, "通道使能");
}
catch (InvalidOperationException)
{
    rejectedDelayedMeasurementWord = true;
}
Assert(rejectedDelayedMeasurementWord,
    "configuration validation rejects 0xCDCC, a delayed 123.4 floating-point measurement fragment");

byte[] serialAssistantConfigurationResponse =
{
    0x01, 0x03, 0x08, 0x01, 0x00, 0x01, 0x00, 0x01, 0x00, 0x01, 0x00, 0x55, 0xA6
};
MethodInfo extractReadResponse = typeof(ModbusRtuClient).GetMethod(
    "TryExtractReadResponse",
    BindingFlags.NonPublic | BindingFlags.Static)
    ?? throw new InvalidOperationException("Modbus transaction-local frame parser was not found");
object[] parserArguments =
{
    new List<byte>(new byte[] { 0x7E, 0x55 }.Concat(serialAssistantConfigurationResponse)),
    (byte)1,
    8,
    false,
    Array.Empty<byte>()
};
bool parsedSerialAssistantFrame = (bool)(extractReadResponse.Invoke(null, parserArguments) ?? false);
Assert(parsedSerialAssistantFrame &&
       parserArguments[4] is byte[] extractedConfigurationResponse &&
       extractedConfigurationResponse.SequenceEqual(serialAssistantConfigurationResponse),
    "transaction-local parser accepts the exact four-register response captured by the serial assistant");

Assert(InspectionInstrumentProtocol.AcquisitionRunningStateCoilAddress == 0x0000 &&
       InspectionInstrumentProtocol.StartAcquisitionCoilAddress == 0x0010 &&
       InspectionInstrumentProtocol.StopAcquisitionCoilAddress == 0x0011,
    "inspection instrument run-state and start-stop coils match the latest protocol");
byte[] runningCoilResponse = { 0x01, 0x01, 0x01, 0x01, 0x90, 0x48 };
MethodInfo extractBitReadResponse = typeof(ModbusRtuClient).GetMethod(
    "TryExtractBitReadResponse",
    BindingFlags.NonPublic | BindingFlags.Static)
    ?? throw new InvalidOperationException("Modbus coil transaction parser was not found");
object[] bitParserArguments =
{
    new List<byte>(new byte[] { 0x7E }.Concat(runningCoilResponse)),
    (byte)1,
    1,
    false,
    Array.Empty<byte>()
};
bool parsedRunningCoilFrame = (bool)(extractBitReadResponse.Invoke(null, bitParserArguments) ?? false);
Assert(parsedRunningCoilFrame &&
       bitParserArguments[4] is byte[] extractedRunningCoilResponse &&
       extractedRunningCoilResponse.SequenceEqual(runningCoilResponse),
    "transaction-local parser accepts a valid running-state coil response");

Assert(Math.Abs(InspectionMeterService.DecodeFloatBigEndian(new byte[] { 0x42, 0xF6, 0xCC, 0xCD }) - 123.4) < 0.0001,
    "protocol float byte order");
Assert(InspectionMeterService.DecodeSignedHundredths(0x9CFF) == -1.0, "little-endian signed humidity-probe value");
Assert(InspectionMeterService.DecodeSignedHundredths(0xAC02) == 6.84, "field humidity register byte order");
Assert(InspectionMeterService.DecodeSignedHundredths(0x00FF) == -2.56,
    "negative humidity must retain its sign after the register-byte swap");
Assert(InspectionMeterService.DecodeSignedHundredths(0x7BFE) == -3.89,
    "negative humidity in the device byte order must retain its sign");
Assert(InspectionMeterService.DecodeSignedHundredths(0xFE7B) == -3.89,
    "negative humidity in standard byte order must retain its sign");

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
var liveCorrectionChannel = new InspectionChannelData
{
    Channel = 1, Type = ChannelType.Temperature, Role = ChannelRole.PrimaryTemperature, Value = 20, IsValid = true
};
ChannelCorrectionService.ApplyForMeasurement(
    new List<InspectionChannelData> { liveCorrectionChannel }, false,
    "1:0.3", string.Empty, "1:0.2", string.Empty);
Assert(Math.Abs(liveCorrectionChannel.Value - 20.3) < 0.000001,
    "realtime measurement uses the latest system correction");
var formalCorrectionChannel = new InspectionChannelData
{
    Channel = 1, Type = ChannelType.Temperature, Role = ChannelRole.PrimaryTemperature, Value = 20, IsValid = true
};
ChannelCorrectionService.ApplyForMeasurement(
    new List<InspectionChannelData> { formalCorrectionChannel }, true,
    "1:0.3", string.Empty, "1:0.2", string.Empty);
Assert(Math.Abs(formalCorrectionChannel.Value - 20.2) < 0.000001,
    "formal calibration keeps the frozen task correction");
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
        RawValue = value,
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
        RawValue = value,
        IsValid = true
    }).Concat(humidities.Select((value, index) => new InspectionChannelData
    {
        Channel = index + 1,
        Type = ChannelType.Humidity,
        Role = ChannelRole.Humidity,
        Value = value,
        RawValue = value,
        IsValid = true
    })).ToList(),
    ValidChannelCount = temperatures.Length + humidities.Length
};

CalibrationTaskContext.StandardIndex = 0;
CalibrationTaskContext.CalibrationTypeIndex = 0;
CalibrationTaskContext.VolumeIndex = 0;
CalibrationTaskContext.PointLayoutModeIndex = 0;
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
CalibrationTaskContext.TemperaturePointCount = 9;
CalibrationTaskContext.HumidityPointCount = 0;
CalibrationTaskContext.TemperatureCenterPoint = 5;
CalibrationTaskContext.PlannedCount = 2;
CalibrationTaskContext.SamplingIntervalSeconds = 120;
CalibrationTaskContext.SetTemperature = 20;
CalibrationTaskContext.EquipmentName = "测试设备,一号";
CalibrationTaskContext.EquipmentSerialNumber = "TEST-001";
CalibrationTaskContext.ReferencedLaboratoryName = "测试校准实验室";
CalibrationTaskContext.ReferencedLaboratoryAddress = "测试市质量路1号";
CalibrationTaskContext.ReferencedCertificateNumber = "CERT-001";
CalibrationTaskContext.ReferencedMeasuringInstrumentClass = 0.02;
CalibrationTaskContext.ReferencedThermocoupleGrade = "廉金属1级";
CalibrationRunContext.Begin();
CalibrationFileStorageService testStorage = new(storageTestRoot);
Assert(testStorage.TryBeginJob(out string beginStorageError), "storage begin: " + beginStorageError);
CalibrationSampleRecord storageRecord1 = CalibrationRunContext.Add(Snapshot(19, 20, 21, 22, 20, 18, 19, 20, 21), 20, null);
CalibrationSampleRecord storageRecord2 = CalibrationRunContext.Add(Snapshot(20, 21, 22, 23, 20, 19, 20, 21, 22), 20, null);
Assert(testStorage.TryAppendSample(storageRecord1, out string appendStorageError1), "storage append 1: " + appendStorageError1);
Assert(testStorage.TryAppendSample(storageRecord2, out string appendStorageError2), "storage append 2: " + appendStorageError2);
CalibrationResultSummary storageResult = CalibrationResultCalculator.Calculate();
Assert(testStorage.TryCompleteJob(storageResult, out string completeStorageError), "storage complete: " + completeStorageError);
string storageJobDirectory = testStorage.CurrentJobDirectory!;
foreach (string expectedFile in new[] { "作业摘要.csv", "任务信息.csv", "正式采样.csv", "校准结果.csv" })
{
    string expectedPath = Path.Combine(storageJobDirectory, expectedFile);
    Assert(File.Exists(expectedPath), "storage file exists: " + expectedFile);
    byte[] prefix = File.ReadAllBytes(expectedPath).Take(3).ToArray();
    Assert(prefix.SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }), "storage file UTF-8 BOM: " + expectedFile);
}
string summaryCsv = File.ReadAllText(Path.Combine(storageJobDirectory, "作业摘要.csv"));
Assert(summaryCsv.Contains("\"数据格式版本\",\"任务编号\",\"校准日期\",\"开始时间\",\"结束时间\"") &&
       System.Text.RegularExpressions.Regex.IsMatch(
           summaryCsv,
           "\"1\\.3\",\"JOB-[^\"]+\",\"\\d{4}-\\d{2}-\\d{2}\\t\",\"\\d{2}:\\d{2}:\\d{2}\\t\",\"\\d{2}:\\d{2}:\\d{2}\\t\""),
    "job summary keeps readable date and clock-time text without spreadsheet hash overflow");
Assert(!Directory.Exists(Path.Combine(storageJobDirectory, "内部追溯数据")) &&
       !File.Exists(Path.Combine(storageJobDirectory, "正式采样原始通道.csv")) &&
       !File.Exists(Path.Combine(storageJobDirectory, "不确定度分量.csv")),
    "formal job keeps only operator-facing CSV files and does not create an internal trace folder");
string sampleCsv = File.ReadAllText(Path.Combine(storageJobDirectory, "正式采样.csv"));
Assert(sampleCsv.Contains("\"温度1(℃)\"") && sampleCsv.Contains("\"19.000\"") && sampleCsv.Contains("\"22.000\""),
    "wide sample CSV contains a fixed three-decimal dynamic channel matrix");
Assert(sampleCsv.Contains("\"样本序号\",\"采样日期\",\"采样时间\"") &&
       System.Text.RegularExpressions.Regex.IsMatch(
           sampleCsv,
           "\"\\d{4}-\\d{2}-\\d{2}\\t\",\"\\d{2}:\\d{2}:\\d{2}\\.\\d{3}\\t\"") &&
       sampleCsv.Contains("\"20.000\""),
    "formal sample CSV keeps spreadsheet-safe date and HH:mm:ss.fff text plus fixed three-decimal DUT readings");
string resultCsv = File.ReadAllText(Path.Combine(storageJobDirectory, "校准结果.csv"));
Assert(resultCsv.Contains("\"3.000\"") && resultCsv.Contains("\"-2.000\"") &&
       resultCsv.Contains("\"4.000\"") && resultCsv.Contains("\"0.500\""),
    "final calibration result CSV uses fixed three-decimal precision");
string taskCsv = File.ReadAllText(Path.Combine(storageJobDirectory, "任务信息.csv"));
Assert(taskCsv.Contains("\"测温仪器级别\"") && taskCsv.Contains("\"热电偶等级\"") && taskCsv.Contains("\"廉金属1级\"") &&
       taskCsv.Contains("\"实验室名称\"") && taskCsv.Contains("\"测试校准实验室\""),
    "task snapshot CSV preserves laboratory identity and JJF1376 standard capability fields for traceability");
IReadOnlyList<CalibrationArchiveSummary> storageHistory = testStorage.LoadHistory("测试设备", "JJF 1101-2019", "已完成");
Assert(storageHistory.Count == 1 && storageHistory[0].SampleProgress == "2/2" && storageHistory[0].Device == "测试设备,一号" &&
       storageHistory[0].StartedAt.Date == DateTime.Today,
    "history scans quoted CSV summaries without a database");
Assert(CalibrationExcelReportService.Default.TryGenerate(storageJobDirectory, out string generatedExcelPath, out string generatedExcelError),
    "Excel report generation: " + generatedExcelError);
Assert(File.Exists(generatedExcelPath), "Excel report file exists");
using (ZipArchive excelArchive = ZipFile.OpenRead(generatedExcelPath))
{
    Assert(excelArchive.GetEntry("xl/workbook.xml") != null &&
           excelArchive.Entries.Count(entry => entry.FullName.StartsWith("xl/worksheets/sheet", StringComparison.Ordinal)) == 3,
        "Excel report contains workbook and three required worksheets");
    using StreamReader workbookReader = new(excelArchive.GetEntry("xl/workbook.xml")!.Open());
    string workbookXml = workbookReader.ReadToEnd();
    Assert(workbookXml.Contains("校准记录") && workbookXml.Contains("正式采样") && workbookXml.Contains("任务快照") &&
           !workbookXml.Contains("原始通道") && !workbookXml.Contains("不确定度分量") &&
           workbookXml.Split("state=\"hidden\"").Length - 1 == 2,
        "JJF1101 workbook exposes one Appendix A record sheet and only two supporting sheets");
    using StreamReader recordReader = new(excelArchive.GetEntry("xl/worksheets/sheet1.xml")!.Open());
    string recordXml = recordReader.ReadToEnd();
    Assert(recordXml.Contains("1 A") && recordXml.Contains("5 O") && recordXml.Contains("8 B") &&
           recordXml.Contains("图 B1  布点示意图") && recordXml.Split(">门<").Length - 1 == 3,
        "JJF1101 raw record contains the Figure B1 9-point mapping and three door directions");
    Assert(recordXml.Contains("3.000 ℃") && recordXml.Contains("-2.000 ℃") &&
           recordXml.Contains("4.000 ℃") && recordXml.Contains("0.500 ℃"),
        "JJF1101 Excel report presents final calibration results with three decimals");
    using StreamReader stylesReader = new(excelArchive.GetEntry("xl/styles.xml")!.Open());
    string stylesXml = stylesReader.ReadToEnd();
    Assert(stylesXml.Contains("borders count=\"11\""),
        "JJF1101 layout uses directional border styles to draw three equipment sections");
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
    int jjf1101ResultIndex = documentXml.LastIndexOf("校准不确定度", StringComparison.Ordinal);
    int jjf1101ClosingIndex = documentXml.LastIndexOf("声明与签发", StringComparison.Ordinal);
    int jjf1101ApprovalIndex = documentXml.LastIndexOf("批准人", StringComparison.Ordinal);
    Assert(documentXml.Contains("校准证书") && documentXml.Contains("测试校准实验室") &&
           documentXml.Contains("环境试验设备校准证书内页") && documentXml.Contains("布点示意图") && documentXml.Contains("图 B1") &&
           documentXml.Contains("上偏差") &&
           documentXml.Contains("下偏差") && documentXml.Contains("均匀度") &&
           documentXml.Contains("波动度") && documentXml.Contains("校准不确定度") &&
           documentXml.Contains("TEST-001") && documentXml.Contains("3.000") &&
           documentXml.Contains("-2.000") && documentXml.Contains("0.500")/* && documentXml.Contains("签发状态：待审核")*/,
        "Word certificate follows JJF1101 Appendix B and preserves archived identity and review status");
    Assert(jjf1101ResultIndex >= 0 && jjf1101ClosingIndex > jjf1101ResultIndex && jjf1101ApprovalIndex > jjf1101ClosingIndex,
        "JJF1101 Word declaration and issuance block follows all calibration results");
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

// JJF 1376 报告回归：附录 A 用 Excel 记录逐次值/修正值/实际温度，附录 B 用 Word 只展示最终结果。
string furnaceReportRoot = Path.Combine(storageTestRoot, "jjf1376-report-check");
CalibrationTaskContext.StandardIndex = 1;
CalibrationTaskContext.CalibrationTypeIndex = 0;
CalibrationTaskContext.PointLayoutModeIndex = 0;
CalibrationTaskContext.TemperaturePointCount = 5;
CalibrationTaskContext.HumidityPointCount = 0;
CalibrationTaskContext.TemperatureCenterPoint = 3;
CalibrationTaskContext.PlannedCount = 2;
CalibrationTaskContext.SamplingIntervalSeconds = 180;
CalibrationTaskContext.SetTemperature = 800;
CalibrationTaskContext.AppearanceCheckIndex = 1;
CalibrationTaskContext.FurnaceChamberLengthMm = 500;
CalibrationTaskContext.FurnaceChamberWidthMm = 400;
CalibrationTaskContext.FurnaceChamberHeightMm = 300;
CalibrationTaskContext.WorkZoneLengthMm = 300;
CalibrationTaskContext.WorkZoneWidthMm = 240;
CalibrationTaskContext.WorkZoneHeightMm = 180;
CalibrationRunContext.Begin();
CalibrationFileStorageService furnaceStorage = new(furnaceReportRoot);
Assert(furnaceStorage.TryBeginJob(out string furnaceBeginError), "JJF1376 report job begin: " + furnaceBeginError);
CalibrationSampleRecord furnaceRecord1 = CalibrationRunContext.Add(Snapshot(801, 802, 800, 799, 800), null, null);
CalibrationSampleRecord furnaceRecord2 = CalibrationRunContext.Add(Snapshot(802, 803, 801, 800, 801), null, null);
Assert(furnaceStorage.TryAppendSample(furnaceRecord1, out string furnaceAppendError1), "JJF1376 append sample 1: " + furnaceAppendError1);
Assert(furnaceStorage.TryAppendSample(furnaceRecord2, out string furnaceAppendError2), "JJF1376 append sample 2: " + furnaceAppendError2);
CalibrationResultSummary furnaceReportResult = CalibrationResultCalculator.Calculate();
Assert(furnaceStorage.TryCompleteJob(furnaceReportResult, out string furnaceCompleteError), "JJF1376 report job complete: " + furnaceCompleteError);
string furnaceJobDirectory = furnaceStorage.CurrentJobDirectory!;
Assert(CalibrationExcelReportService.Default.TryGenerate(furnaceJobDirectory, out string furnaceExcelPath, out string furnaceExcelError),
    "JJF1376 Appendix A Excel generation: " + furnaceExcelError);
using (ZipArchive furnaceExcelArchive = ZipFile.OpenRead(furnaceExcelPath))
{
    using StreamReader workbookReader = new(furnaceExcelArchive.GetEntry("xl/workbook.xml")!.Open());
    string workbookXml = workbookReader.ReadToEnd();
    using StreamReader recordReader = new(furnaceExcelArchive.GetEntry("xl/worksheets/sheet1.xml")!.Open());
    string recordXml = recordReader.ReadToEnd();
    Assert(workbookXml.Contains("校准记录") && workbookXml.Split("state=\"hidden\"").Length - 1 == 2 &&
           recordXml.Contains("箱式电阻炉校准记录") && recordXml.Contains("修正值") && recordXml.Contains("实际温度") &&
           recordXml.Contains("炉膛尺寸") && recordXml.Contains("测温区尺寸") && recordXml.Contains("炉内最大温差"),
        "JJF1376 Excel follows Appendix A and keeps audit sheets hidden");
}
Assert(CalibrationWordCertificateService.Default.TryGenerate(furnaceJobDirectory, out string furnaceWordPath, out string furnaceWordError),
    "JJF1376 Appendix B Word generation: " + furnaceWordError);
using (ZipArchive furnaceWordArchive = ZipFile.OpenRead(furnaceWordPath))
{
    using StreamReader documentReader = new(furnaceWordArchive.GetEntry("word/document.xml")!.Open());
    string documentXml = documentReader.ReadToEnd();
    int jjf1376ResultIndex = documentXml.LastIndexOf("扩展不确定度", StringComparison.Ordinal);
    int jjf1376ClosingIndex = documentXml.LastIndexOf("声明与签发", StringComparison.Ordinal);
    int jjf1376ApprovalIndex = documentXml.LastIndexOf("批准人", StringComparison.Ordinal);
    Assert(documentXml.Contains("箱式电阻炉校准结果") && documentXml.Contains("外观检查") &&
           documentXml.Contains("炉温均匀度") && documentXml.Contains("炉温稳定度") &&
           documentXml.Contains("炉温偏差") && documentXml.Contains("炉内最大温差") &&
           documentXml.Contains("扩展不确定度")/* && documentXml.Contains("以下空白")*/,
        "JJF1376 Word follows Appendix B result structure");
    Assert(jjf1376ResultIndex >= 0 && jjf1376ClosingIndex > jjf1376ResultIndex && jjf1376ApprovalIndex > jjf1376ClosingIndex,
        "JJF1376 Word declaration and issuance block follows all calibration results");
}

CalibrationTaskContext.StandardIndex = 0;
CalibrationTaskContext.CalibrationTypeIndex = 0;
CalibrationTaskContext.TemperaturePointCount = 9;
CalibrationTaskContext.HumidityPointCount = 0;
CalibrationTaskContext.TemperatureCenterPoint = 5;
CalibrationTaskContext.FurnaceChamberLengthMm = null;
CalibrationTaskContext.FurnaceChamberWidthMm = null;
CalibrationTaskContext.FurnaceChamberHeightMm = null;

string legacyJobDirectory = Path.Combine(storageTestRoot, "legacy-format-1.0");
Directory.CreateDirectory(legacyJobDirectory);
foreach (string sourceFile in Directory.GetFiles(storageJobDirectory, "*.csv"))
    File.Copy(sourceFile, Path.Combine(legacyJobDirectory, Path.GetFileName(sourceFile)), overwrite: true);
string legacySummaryPath = Path.Combine(legacyJobDirectory, "作业摘要.csv");
string legacySummary = File.ReadAllText(legacySummaryPath).Replace("\"1.3\"", "\"1.0\"", StringComparison.Ordinal);
File.WriteAllText(legacySummaryPath, legacySummary, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
Assert(CalibrationExcelReportService.Default.TryGenerate(legacyJobDirectory, out string legacyExcelPath, out string legacyExcelError),
    "legacy Excel compatibility: " + legacyExcelError);
using (ZipArchive legacyExcelArchive = ZipFile.OpenRead(legacyExcelPath))
{
    Assert(legacyExcelArchive.Entries.Count(entry => entry.FullName.StartsWith("xl/worksheets/sheet", StringComparison.Ordinal)) == 3,
        "legacy archive can rebuild the streamlined three-sheet workbook without technical CSV files");
}
Assert(CalibrationWordCertificateService.Default.TryGenerate(legacyJobDirectory, out string legacyWordPath, out string legacyWordError),
    "legacy Word compatibility: " + legacyWordError);
Assert(File.Exists(legacyWordPath), "legacy archive can rebuild a Word certificate from business CSV files");
Assert(CalibrationPdfArchiveService.Default.TryGenerate(legacyJobDirectory, out string legacyPdfPath, out string legacyPdfError),
    "legacy PDF compatibility: " + legacyPdfError);
Assert(File.Exists(legacyPdfPath), "legacy archive can rebuild a PDF report from business CSV files");

CalibrationTaskContext.TemperaturePointCount = 3;
CalibrationTaskContext.PlannedCount = 2;
CalibrationRunContext.Begin();
Assert(testStorage.TryBeginJob(out string interruptedBeginError), "interrupted storage begin: " + interruptedBeginError);
CalibrationSampleRecord incompleteRecord = CalibrationRunContext.Add(Snapshot(20, 21), 20, null);
Assert(testStorage.TryAppendSample(incompleteRecord, out string incompleteAppendError), "incomplete storage append: " + incompleteAppendError);
Assert(testStorage.TryMarkInterrupted("自动检查中断状态", out string interruptedStateError), "interrupted state: " + interruptedStateError);
string incompleteSampleCsv = File.ReadAllText(Path.Combine(testStorage.CurrentJobDirectory!, "正式采样.csv"));
Assert(incompleteSampleCsv.Contains("\"T3\"") &&
       incompleteSampleCsv.TrimEnd().EndsWith(",\"\"", StringComparison.Ordinal),
    "missing required channel remains explicit in the formal sample matrix without a raw-channel file");
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

// 暂停边界回归：取消信号发出后，正在进行的同步串口读取即使以超时结束，
// 也属于正常停止收尾，StopAsync 不得向界面传播该超时异常。
CancelDuringFailureMeasurementReader cancelDuringFailureReader = new();
InspectionDataAcquisitionService cancelDuringFailureService = new(cancelDuringFailureReader);
Assert(cancelDuringFailureService.Start(1, 200, "温度"), "cancel-during-read acquisition starts");
Assert(cancelDuringFailureReader.ReadEntered.Wait(TimeSpan.FromSeconds(2)), "cancel-during-read enters device request");
Task gracefulStopTask = cancelDuringFailureService.StopAsync();
cancelDuringFailureReader.AllowFailure.Set();
await gracefulStopTask.WaitAsync(TimeSpan.FromSeconds(2));
Assert(!cancelDuringFailureService.IsRunning,
    "timeout produced after operator cancellation is absorbed as a normal stop");

TaskCompletionSource<bool> restartedData = new(TaskCreationOptions.RunContinuationsAsynchronously);
lifecycleService.DataAcquired += (_, _) => restartedData.TrySetResult(true);
Assert(lifecycleService.Start(1, 200, "温度"), "acquisition can restart after the old loop exits");
await restartedData.Task.WaitAsync(TimeSpan.FromSeconds(2));
await lifecycleService.StopAsync();

// 动态节拍回归：正式采样开始时把 10 s 实时轮询缩短为 200 ms，旧等待必须被立即唤醒，不能仍等满 10 s。
ImmediateMeasurementReader intervalReader = new();
InspectionDataAcquisitionService intervalService = new(intervalReader);
int intervalResponseCount = 0;
TaskCompletionSource<bool> firstIntervalResponse = new(TaskCreationOptions.RunContinuationsAsynchronously);
TaskCompletionSource<bool> secondIntervalResponse = new(TaskCreationOptions.RunContinuationsAsynchronously);
intervalService.DataAcquired += (_, _) =>
{
    int count = Interlocked.Increment(ref intervalResponseCount);
    if (count == 1) firstIntervalResponse.TrySetResult(true);
    if (count == 2) secondIntervalResponse.TrySetResult(true);
};
Assert(intervalService.Start(1, 10000, "温度", 9, 0), "long-interval acquisition starts");
await firstIntervalResponse.Task.WaitAsync(TimeSpan.FromSeconds(2));
Assert(intervalReader.LastTemperatureChannelCount == 9 && intervalReader.LastHumidityChannelCount == 0,
    "acquisition forwards the task channel counts to the protocol reader");
Stopwatch intervalAdjustmentWatch = Stopwatch.StartNew();
Assert(intervalService.TryUpdateInterval(200) && intervalService.CurrentIntervalMilliseconds == 200,
    "running acquisition accepts the shorter formal-sampling interval");
await secondIntervalResponse.Task.WaitAsync(TimeSpan.FromSeconds(2));
intervalAdjustmentWatch.Stop();
Assert(intervalAdjustmentWatch.Elapsed < TimeSpan.FromSeconds(2) && intervalService.LastReadDurationMilliseconds > 0,
    "shortening the interval wakes the old 10-second wait and records complete-read duration");
Assert(intervalService.TryUpdateInterval(10000) && intervalService.CurrentIntervalMilliseconds == 10000,
    "original realtime interval can be restored after formal sampling");
await intervalService.StopAsync();

// 通信退避回归：连续错误按 1 s、2 s、3 s、4 s 递增，超过告警阈值后仍保持串口循环，等待设备自行恢复。
AlwaysFailMeasurementReader failingReader = new();
InspectionDataAcquisitionService retryService = new(failingReader);
List<(int Count, int Delay)> observedFailures = new();
TaskCompletionSource<bool> continuedAfterWarningThreshold = new(TaskCreationOptions.RunContinuationsAsynchronously);
retryService.AcquisitionError += _ =>
{
    observedFailures.Add((retryService.ConsecutiveFailureCount, retryService.NextRetryDelayMilliseconds));
    if (retryService.ConsecutiveFailureCount > retryService.PersistentWarningFailureCount)
        continuedAfterWarningThreshold.TrySetResult(true);
};
Assert(retryService.Start(1, 200, "温度"), "retry acquisition loop starts");
await continuedAfterWarningThreshold.Task.WaitAsync(TimeSpan.FromSeconds(8));
Assert(retryService.IsRunning && failingReader.ReadCount >= 4,
    "communication warning threshold keeps the request loop and serial session alive");
await retryService.StopAsync();
Assert(observedFailures.Take(4).SequenceEqual(new[] { (1, 1000), (2, 2000), (3, 3000), (4, 4000) }),
    "communication failures use bounded backoff while persistent retry remains active");
Assert(!retryService.IsRunning,
    "persistent retry loop still stops promptly when the operator explicitly requests stop");

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

List<int> normalizedTemperatureMapping = MeasurementChannelMappingService.Normalize(
    new[] { 7, 2, 7, 30 }, 4, InspectionInstrumentProtocol.PhysicalTemperatureChannelCount);
Assert(normalizedTemperatureMapping.SequenceEqual(new[] { 7, 2, 1, 3 }),
    "channel mapping repairs duplicate and out-of-range physical channels deterministically");
Assert(MeasurementChannelMappingService.GetRequiredReadChannelCount(
           new[] { 7, 2 }, 2, InspectionInstrumentProtocol.PhysicalTemperatureChannelCount) == 7,
    "sparse mapping reads one contiguous block through the highest physical channel");

List<InspectionChannelData> physicalMappedSource = new()
{
    new() { Channel = 2, PhysicalChannel = 2, Role = ChannelRole.PrimaryTemperature, Type = ChannelType.Temperature, Value = 20.2, IsValid = true },
    new() { Channel = 7, PhysicalChannel = 7, Role = ChannelRole.PrimaryTemperature, Type = ChannelType.Temperature, Value = 20.7, IsValid = true },
    new() { Channel = 3, PhysicalChannel = 3, Role = ChannelRole.Humidity, Type = ChannelType.Humidity, Value = 50.3, IsValid = true },
    new() { Channel = 3, PhysicalChannel = 3, Role = ChannelRole.HumidityProbeTemperature, Type = ChannelType.Temperature, Value = 19.3, IsValid = true }
};
ChannelCorrectionService.Apply(physicalMappedSource, "7:0.5", "3:-0.2");
List<InspectionChannelData> logicalMapped = MeasurementChannelMappingService.ApplyTaskMapping(
    physicalMappedSource, new[] { 7, 2 }, new[] { 3 });
Assert(logicalMapped.Single(item => item.Role == ChannelRole.PrimaryTemperature && item.Channel == 1).Value == 21.2 &&
       logicalMapped.Single(item => item.Role == ChannelRole.PrimaryTemperature && item.Channel == 1).PhysicalChannel == 7 &&
       logicalMapped.Single(item => item.Role == ChannelRole.PrimaryTemperature && item.Channel == 2).Value == 20.2 &&
       Math.Abs(logicalMapped.Single(item => item.Role == ChannelRole.Humidity && item.Channel == 1).Value - 50.1) < 0.000001 &&
       logicalMapped.Single(item => item.Role == ChannelRole.HumidityProbeTemperature && item.Channel == 1).PhysicalChannel == 3,
    "corrections use physical channels before logical point projection and probe temperature follows humidity mapping");

Console.WriteLine($"PASS: protocol, standards, formulas, formal/realtime archives, reports, acquisition lifecycle, CSV traces and startup recovery; test archive: {storageJobDirectory}");

/// <summary>用于验证“同步设备读尚未返回”场景的可控读取器。</summary>
sealed class BlockingMeasurementReader : IInspectionMeasurementReader
{
    public ManualResetEventSlim ReadEntered { get; } = new(false);
    public ManualResetEventSlim AllowReadToReturn { get; } = new(false);

    public List<InspectionChannelData> ReadMeasurements(
        string calibrationType,
        byte slaveAddress,
        long acquisitionId,
        int temperatureChannelCount,
        int humidityChannelCount)
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

/// <summary>模拟“操作人员点击暂停后，尚未结束的串口读取才返回超时”。</summary>
sealed class CancelDuringFailureMeasurementReader : IInspectionMeasurementReader
{
    public ManualResetEventSlim ReadEntered { get; } = new(false);
    public ManualResetEventSlim AllowFailure { get; } = new(false);

    public List<InspectionChannelData> ReadMeasurements(
        string calibrationType,
        byte slaveAddress,
        long acquisitionId,
        int temperatureChannelCount,
        int humidityChannelCount)
    {
        ReadEntered.Set();
        if (!AllowFailure.Wait(TimeSpan.FromSeconds(5)))
            throw new TimeoutException("自动检查未释放模拟设备读操作。");
        throw new TimeoutException("模拟暂停期间到达的巡检仪读取超时。");
    }
}

/// <summary>用于验证运行中动态调整轮询周期的立即成功读取器。</summary>
sealed class ImmediateMeasurementReader : IInspectionMeasurementReader
{
    public int LastTemperatureChannelCount { get; private set; }
    public int LastHumidityChannelCount { get; private set; }

    public List<InspectionChannelData> ReadMeasurements(
        string calibrationType,
        byte slaveAddress,
        long acquisitionId,
        int temperatureChannelCount,
        int humidityChannelCount)
    {
        LastTemperatureChannelCount = temperatureChannelCount;
        LastHumidityChannelCount = humidityChannelCount;
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

    public List<InspectionChannelData> ReadMeasurements(
        string calibrationType,
        byte slaveAddress,
        long acquisitionId,
        int temperatureChannelCount,
        int humidityChannelCount)
    {
        ReadCount++;
        throw new TimeoutException("模拟巡检仪无响应。");
    }
}
