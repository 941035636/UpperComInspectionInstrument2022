using System;
using System.IO;
using System.Text.Json;

namespace UpperComInspectionInstrument2022.Models
{
    /// <summary>
    /// 当前校准任务。任务保存时同时固化标准器资料，避免后续修改系统设置影响已建立任务。
    /// </summary>
    public static class CalibrationTaskContext
    {
        private static readonly string TaskPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IndustrialEquipmentCalibration",
            "calibration-task.json");

        public static int StandardIndex { get; set; }
        public static int DeviceTypeIndex { get; set; }
        /// <summary>本次被校设备的容积分类；-1 表示尚未选择。</summary>
        public static int VolumeIndex { get; set; } = -1;
        /// <summary>0=温度参数/炉温参数，1=温湿度参数。</summary>
        public static int CalibrationTypeIndex { get; set; }
        /// <summary>本次任务实际使用的传感器类型；-1 表示尚未确认。</summary>
        public static int SensorTypeIndex { get; set; } = -1;
        public static string SensorTypeCode { get; set; } = string.Empty;
        public static int PointSelectionIndex { get; set; }
        public static int PointLayoutModeIndex { get; set; }
        public static int LoadConditionIndex { get; set; }
        public static int StabilityBasisIndex { get; set; } = 1;
        public static int AppearanceCheckIndex { get; set; }

        public static int TemperaturePointCount { get; set; } = 9;
        public static int HumidityPointCount { get; set; }
        public static int TemperatureCenterPoint { get; set; } = 5;
        public static int HumidityCenterPoint { get; set; } = 1;
        public static int PlannedCount { get; set; } = 16;
        public static int SamplingIntervalSeconds { get; set; } = 120;
        public static int StableWaitMinutes { get; set; } = 30;

        public static double? SetTemperature { get; set; }
        public static double? SetHumidity { get; set; }
        public static double? DutDisplayTemperature { get; set; }
        public static double? DutDisplayHumidity { get; set; }
        /// <summary>被校设备实际显示分辨力；0 表示尚未填写，不能作为校准计算输入。</summary>
        public static double DutTemperatureResolution { get; set; }
        public static double DutHumidityResolution { get; set; }
        public static double? AmbientTemperature { get; set; }
        public static double? AmbientHumidity { get; set; }
        public static double? AmbientPressure { get; set; }
        public static double? WorkZoneLengthMm { get; set; }
        public static double? WorkZoneWidthMm { get; set; }
        public static double? WorkZoneHeightMm { get; set; }

        public static string CustomerName { get; set; } = string.Empty;
        public static string CustomerAddress { get; set; } = string.Empty;
        public static string EquipmentName { get; set; } = string.Empty;
        public static string Manufacturer { get; set; } = string.Empty;
        public static string ModelSpecification { get; set; } = string.Empty;
        public static string EquipmentSerialNumber { get; set; } = string.Empty;
        public static string MeasurementRange { get; set; } = string.Empty;
        public static string CalibrationLocation { get; set; } = string.Empty;
        public static string LoadDescription { get; set; } = string.Empty;
        public static string PointLayoutDescription { get; set; } = string.Empty;
        public static string DeviationDescription { get; set; } = string.Empty;
        public static string Calibrator { get; set; } = string.Empty;
        public static string Verifier { get; set; } = string.Empty;
        public static DateTime CalibrationDate { get; set; } = DateTime.Today;
        public static bool EnvironmentInterferenceConfirmed { get; set; }

        public static string ReferencedStandardName { get; set; } = string.Empty;
        public static string ReferencedCertificateNumber { get; set; } = string.Empty;
        public static DateTime? ReferencedValidityDate { get; set; }
        public static string ReferencedModel { get; set; } = string.Empty;
        public static string ReferencedSerialNumber { get; set; } = string.Empty;
        public static string ReferencedOrganization { get; set; } = string.Empty;
        public static string ReferencedTemperatureRange { get; set; } = string.Empty;
        public static string ReferencedHumidityRange { get; set; } = string.Empty;
        public static double ReferencedTemperatureResolution { get; set; }
        public static double ReferencedHumidityResolution { get; set; }
        public static string ReferencedAccuracySpecification { get; set; } = string.Empty;
        public static string ReferencedTemperatureCorrections { get; set; } = string.Empty;
        public static string ReferencedHumidityCorrections { get; set; } = string.Empty;
        public static double ReferencedTemperatureStabilityChange { get; set; }
        public static double ReferencedHumidityStabilityChange { get; set; }
        public static double ReferencedTemperatureUncertainty { get; set; }
        public static double ReferencedTemperatureCoverage { get; set; }
        public static double ReferencedHumidityUncertainty { get; set; }
        public static double ReferencedHumidityCoverage { get; set; }
        public static bool IsConfigured { get; set; }
        public static bool HasCompletedCalibration { get; set; }

        public static bool IncludesHumidity => StandardIndex == 0 && CalibrationTypeIndex == 1;

        public static void Load()
        {
            try
            {
                if (!File.Exists(TaskPath)) return;
                TaskSnapshot? snapshot = JsonSerializer.Deserialize<TaskSnapshot>(File.ReadAllText(TaskPath));
                if (snapshot == null) return;

                StandardIndex = snapshot.StandardIndex;
                DeviceTypeIndex = snapshot.DeviceTypeIndex;
                VolumeIndex = snapshot.IsConfigured && snapshot.VolumeIndex is >= 0 and <= 1 ? snapshot.VolumeIndex : -1;
                CalibrationTypeIndex = snapshot.CalibrationTypeIndex > 1 ? 1 : snapshot.CalibrationTypeIndex;
                SensorTypeCode = snapshot.IsConfigured
                    ? string.IsNullOrWhiteSpace(snapshot.SensorTypeCode)
                        ? TemperatureSensorCatalog.GetCode(snapshot.SensorTypeIndex)
                        : snapshot.SensorTypeCode
                    : string.Empty;
                SensorTypeIndex = TemperatureSensorCatalog.GetIndex(SensorTypeCode);
                PointSelectionIndex = snapshot.PointSelectionIndex;
                PointLayoutModeIndex = snapshot.PointLayoutModeIndex;
                LoadConditionIndex = snapshot.LoadConditionIndex;
                StabilityBasisIndex = snapshot.StabilityBasisIndex;
                AppearanceCheckIndex = snapshot.AppearanceCheckIndex;
                TemperaturePointCount = snapshot.TemperaturePointCount > 0 ? snapshot.TemperaturePointCount : 9;
                HumidityPointCount = snapshot.HumidityPointCount;
                TemperatureCenterPoint = snapshot.TemperatureCenterPoint > 0
                    ? snapshot.TemperatureCenterPoint
                    : snapshot.CenterPoint > 0 ? snapshot.CenterPoint : 5;
                HumidityCenterPoint = snapshot.HumidityCenterPoint > 0 ? snapshot.HumidityCenterPoint : 1;
                PlannedCount = snapshot.PlannedCount > 0 ? snapshot.PlannedCount : StandardIndex == 1 ? 20 : 16;
                SamplingIntervalSeconds = snapshot.SamplingIntervalSeconds > 0 ? snapshot.SamplingIntervalSeconds : StandardIndex == 1 ? 180 : 120;
                StableWaitMinutes = snapshot.StableWaitMinutes >= 0 ? snapshot.StableWaitMinutes : StandardIndex == 1 ? 0 : 30;
                SetTemperature = snapshot.SetTemperature;
                SetHumidity = snapshot.SetHumidity;
                DutDisplayTemperature = snapshot.DutDisplayTemperature;
                DutDisplayHumidity = snapshot.DutDisplayHumidity;
                DutTemperatureResolution = snapshot.DutTemperatureResolution > 0 ? snapshot.DutTemperatureResolution : 0;
                DutHumidityResolution = snapshot.DutHumidityResolution > 0 ? snapshot.DutHumidityResolution : 0;
                AmbientTemperature = snapshot.AmbientTemperature;
                AmbientHumidity = snapshot.AmbientHumidity;
                AmbientPressure = snapshot.AmbientPressure;
                WorkZoneLengthMm = snapshot.WorkZoneLengthMm;
                WorkZoneWidthMm = snapshot.WorkZoneWidthMm;
                WorkZoneHeightMm = snapshot.WorkZoneHeightMm;
                CustomerName = snapshot.CustomerName ?? string.Empty;
                CustomerAddress = snapshot.CustomerAddress ?? string.Empty;
                EquipmentName = snapshot.EquipmentName ?? string.Empty;
                Manufacturer = snapshot.Manufacturer ?? string.Empty;
                ModelSpecification = snapshot.ModelSpecification ?? string.Empty;
                EquipmentSerialNumber = snapshot.EquipmentSerialNumber ?? string.Empty;
                MeasurementRange = snapshot.MeasurementRange ?? string.Empty;
                CalibrationLocation = snapshot.CalibrationLocation ?? string.Empty;
                LoadDescription = snapshot.LoadDescription ?? string.Empty;
                PointLayoutDescription = snapshot.PointLayoutDescription ?? string.Empty;
                DeviationDescription = snapshot.DeviationDescription ?? string.Empty;
                Calibrator = snapshot.Calibrator ?? string.Empty;
                Verifier = snapshot.Verifier ?? string.Empty;
                CalibrationDate = snapshot.CalibrationDate == default ? DateTime.Today : snapshot.CalibrationDate;
                EnvironmentInterferenceConfirmed = snapshot.EnvironmentInterferenceConfirmed;
                CopyReferencedSettings(snapshot);
                IsConfigured = snapshot.IsConfigured;
                HasCompletedCalibration = false;
            }
            catch (IOException) { }
            catch (JsonException) { }
        }

        public static void Save()
        {
            string? directory = Path.GetDirectoryName(TaskPath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(TaskPath, JsonSerializer.Serialize(CreateSnapshot(), new JsonSerializerOptions { WriteIndented = true }));
        }

        /// <summary>
        /// 校验当前系统设置中的标准器身份与证书有效性，并将完整资料固化到当前任务。
        /// 已建立任务只有显式调用本方法才会更新快照，避免系统设置变化静默影响历史任务。
        /// </summary>
        public static bool TrySnapshotCurrentStandardSettings(out string error)
        {
            if (string.IsNullOrWhiteSpace(SystemSettingsContext.StandardName))
            {
                error = "请先在系统设置中填写标准器名称。";
                return false;
            }
            if (string.IsNullOrWhiteSpace(SystemSettingsContext.CertificateNumber))
            {
                error = "请先在系统设置中填写标准器证书编号。";
                return false;
            }
            if (!SystemSettingsContext.ValidityDate.HasValue)
            {
                error = "请先在系统设置中填写标准器证书有效期。";
                return false;
            }
            if (SystemSettingsContext.ValidityDate.Value.Date < DateTime.Today)
            {
                error = $"系统设置中的标准器证书已于 {SystemSettingsContext.ValidityDate:yyyy-MM-dd} 到期。";
                return false;
            }

            ReferencedStandardName = SystemSettingsContext.StandardName.Trim();
            ReferencedCertificateNumber = SystemSettingsContext.CertificateNumber.Trim();
            ReferencedValidityDate = SystemSettingsContext.ValidityDate;
            ReferencedModel = SystemSettingsContext.Model;
            ReferencedSerialNumber = SystemSettingsContext.SerialNumber;
            ReferencedOrganization = SystemSettingsContext.Organization;
            ReferencedTemperatureRange = SystemSettingsContext.TemperatureRange;
            ReferencedHumidityRange = SystemSettingsContext.HumidityRange;
            ReferencedTemperatureResolution = SystemSettingsContext.TemperatureResolution;
            ReferencedHumidityResolution = SystemSettingsContext.HumidityResolution;
            ReferencedAccuracySpecification = SystemSettingsContext.AccuracySpecification;
            ReferencedTemperatureCorrections = SystemSettingsContext.TemperatureChannelCorrections;
            ReferencedHumidityCorrections = SystemSettingsContext.HumidityChannelCorrections;
            ReferencedTemperatureStabilityChange = SystemSettingsContext.TemperatureStabilityChange;
            ReferencedHumidityStabilityChange = SystemSettingsContext.HumidityStabilityChange;
            ReferencedTemperatureUncertainty = SystemSettingsContext.TemperatureUncertainty;
            ReferencedTemperatureCoverage = SystemSettingsContext.TemperatureCoverage;
            ReferencedHumidityUncertainty = SystemSettingsContext.HumidityUncertainty;
            ReferencedHumidityCoverage = SystemSettingsContext.HumidityCoverage;
            error = string.Empty;
            return true;
        }

        private static TaskSnapshot CreateSnapshot() => new()
        {
            StandardIndex = StandardIndex,
            DeviceTypeIndex = DeviceTypeIndex,
            VolumeIndex = VolumeIndex,
            CalibrationTypeIndex = CalibrationTypeIndex,
            SensorTypeIndex = SensorTypeIndex,
            SensorTypeCode = SensorTypeCode,
            PointSelectionIndex = PointSelectionIndex,
            PointLayoutModeIndex = PointLayoutModeIndex,
            LoadConditionIndex = LoadConditionIndex,
            StabilityBasisIndex = StabilityBasisIndex,
            AppearanceCheckIndex = AppearanceCheckIndex,
            TemperaturePointCount = TemperaturePointCount,
            HumidityPointCount = HumidityPointCount,
            TemperatureCenterPoint = TemperatureCenterPoint,
            HumidityCenterPoint = HumidityCenterPoint,
            PlannedCount = PlannedCount,
            SamplingIntervalSeconds = SamplingIntervalSeconds,
            StableWaitMinutes = StableWaitMinutes,
            SetTemperature = SetTemperature,
            SetHumidity = SetHumidity,
            DutDisplayTemperature = DutDisplayTemperature,
            DutDisplayHumidity = DutDisplayHumidity,
            DutTemperatureResolution = DutTemperatureResolution,
            DutHumidityResolution = DutHumidityResolution,
            AmbientTemperature = AmbientTemperature,
            AmbientHumidity = AmbientHumidity,
            AmbientPressure = AmbientPressure,
            WorkZoneLengthMm = WorkZoneLengthMm,
            WorkZoneWidthMm = WorkZoneWidthMm,
            WorkZoneHeightMm = WorkZoneHeightMm,
            CustomerName = CustomerName,
            CustomerAddress = CustomerAddress,
            EquipmentName = EquipmentName,
            Manufacturer = Manufacturer,
            ModelSpecification = ModelSpecification,
            EquipmentSerialNumber = EquipmentSerialNumber,
            MeasurementRange = MeasurementRange,
            CalibrationLocation = CalibrationLocation,
            LoadDescription = LoadDescription,
            PointLayoutDescription = PointLayoutDescription,
            DeviationDescription = DeviationDescription,
            Calibrator = Calibrator,
            Verifier = Verifier,
            CalibrationDate = CalibrationDate,
            EnvironmentInterferenceConfirmed = EnvironmentInterferenceConfirmed,
            ReferencedStandardName = ReferencedStandardName,
            ReferencedCertificateNumber = ReferencedCertificateNumber,
            ReferencedValidityDate = ReferencedValidityDate,
            ReferencedModel = ReferencedModel,
            ReferencedSerialNumber = ReferencedSerialNumber,
            ReferencedOrganization = ReferencedOrganization,
            ReferencedTemperatureRange = ReferencedTemperatureRange,
            ReferencedHumidityRange = ReferencedHumidityRange,
            ReferencedTemperatureResolution = ReferencedTemperatureResolution,
            ReferencedHumidityResolution = ReferencedHumidityResolution,
            ReferencedAccuracySpecification = ReferencedAccuracySpecification,
            ReferencedTemperatureCorrections = ReferencedTemperatureCorrections,
            ReferencedHumidityCorrections = ReferencedHumidityCorrections,
            ReferencedTemperatureStabilityChange = ReferencedTemperatureStabilityChange,
            ReferencedHumidityStabilityChange = ReferencedHumidityStabilityChange,
            ReferencedTemperatureUncertainty = ReferencedTemperatureUncertainty,
            ReferencedTemperatureCoverage = ReferencedTemperatureCoverage,
            ReferencedHumidityUncertainty = ReferencedHumidityUncertainty,
            ReferencedHumidityCoverage = ReferencedHumidityCoverage,
            IsConfigured = IsConfigured
        };

        private static void CopyReferencedSettings(TaskSnapshot snapshot)
        {
            ReferencedStandardName = snapshot.ReferencedStandardName ?? string.Empty;
            ReferencedCertificateNumber = snapshot.ReferencedCertificateNumber ?? string.Empty;
            ReferencedValidityDate = snapshot.ReferencedValidityDate;
            ReferencedModel = snapshot.ReferencedModel ?? string.Empty;
            ReferencedSerialNumber = snapshot.ReferencedSerialNumber ?? string.Empty;
            ReferencedOrganization = snapshot.ReferencedOrganization ?? string.Empty;
            ReferencedTemperatureRange = snapshot.ReferencedTemperatureRange ?? string.Empty;
            ReferencedHumidityRange = snapshot.ReferencedHumidityRange ?? string.Empty;
            ReferencedTemperatureResolution = snapshot.ReferencedTemperatureResolution;
            ReferencedHumidityResolution = snapshot.ReferencedHumidityResolution;
            ReferencedAccuracySpecification = snapshot.ReferencedAccuracySpecification ?? string.Empty;
            ReferencedTemperatureCorrections = snapshot.ReferencedTemperatureCorrections ?? string.Empty;
            ReferencedHumidityCorrections = snapshot.ReferencedHumidityCorrections ?? string.Empty;
            ReferencedTemperatureStabilityChange = snapshot.ReferencedTemperatureStabilityChange;
            ReferencedHumidityStabilityChange = snapshot.ReferencedHumidityStabilityChange;
            ReferencedTemperatureUncertainty = snapshot.ReferencedTemperatureUncertainty;
            ReferencedTemperatureCoverage = snapshot.ReferencedTemperatureCoverage;
            ReferencedHumidityUncertainty = snapshot.ReferencedHumidityUncertainty;
            ReferencedHumidityCoverage = snapshot.ReferencedHumidityCoverage;
        }

        private sealed class TaskSnapshot
        {
            public int StandardIndex { get; set; }
            public int DeviceTypeIndex { get; set; }
            public int VolumeIndex { get; set; }
            public int CalibrationTypeIndex { get; set; }
            public int SensorTypeIndex { get; set; }
            public string? SensorTypeCode { get; set; }
            public int PointSelectionIndex { get; set; }
            public int PointLayoutModeIndex { get; set; }
            public int LoadConditionIndex { get; set; }
            public int StabilityBasisIndex { get; set; } = 1;
            public int AppearanceCheckIndex { get; set; }
            public int TemperaturePointCount { get; set; }
            public int HumidityPointCount { get; set; }
            public int CenterPoint { get; set; }
            public int TemperatureCenterPoint { get; set; }
            public int HumidityCenterPoint { get; set; }
            public int PlannedCount { get; set; }
            public int SamplingIntervalSeconds { get; set; }
            public int StableWaitMinutes { get; set; } = -1;
            public double? SetTemperature { get; set; }
            public double? SetHumidity { get; set; }
            public double? DutDisplayTemperature { get; set; }
            public double? DutDisplayHumidity { get; set; }
            public double DutTemperatureResolution { get; set; }
            public double DutHumidityResolution { get; set; }
            public double? AmbientTemperature { get; set; }
            public double? AmbientHumidity { get; set; }
            public double? AmbientPressure { get; set; }
            public double? WorkZoneLengthMm { get; set; }
            public double? WorkZoneWidthMm { get; set; }
            public double? WorkZoneHeightMm { get; set; }
            public string? CustomerName { get; set; }
            public string? CustomerAddress { get; set; }
            public string? EquipmentName { get; set; }
            public string? Manufacturer { get; set; }
            public string? ModelSpecification { get; set; }
            public string? EquipmentSerialNumber { get; set; }
            public string? MeasurementRange { get; set; }
            public string? CalibrationLocation { get; set; }
            public string? LoadDescription { get; set; }
            public string? PointLayoutDescription { get; set; }
            public string? DeviationDescription { get; set; }
            public string? Calibrator { get; set; }
            public string? Verifier { get; set; }
            public DateTime CalibrationDate { get; set; }
            public bool EnvironmentInterferenceConfirmed { get; set; }
            public string? ReferencedStandardName { get; set; }
            public string? ReferencedCertificateNumber { get; set; }
            public DateTime? ReferencedValidityDate { get; set; }
            public string? ReferencedModel { get; set; }
            public string? ReferencedSerialNumber { get; set; }
            public string? ReferencedOrganization { get; set; }
            public string? ReferencedTemperatureRange { get; set; }
            public string? ReferencedHumidityRange { get; set; }
            public double ReferencedTemperatureResolution { get; set; }
            public double ReferencedHumidityResolution { get; set; }
            public string? ReferencedAccuracySpecification { get; set; }
            public string? ReferencedTemperatureCorrections { get; set; }
            public string? ReferencedHumidityCorrections { get; set; }
            public double ReferencedTemperatureStabilityChange { get; set; }
            public double ReferencedHumidityStabilityChange { get; set; }
            public double ReferencedTemperatureUncertainty { get; set; }
            public double ReferencedTemperatureCoverage { get; set; }
            public double ReferencedHumidityUncertainty { get; set; }
            public double ReferencedHumidityCoverage { get; set; }
            public bool IsConfigured { get; set; }
        }
    }
}
