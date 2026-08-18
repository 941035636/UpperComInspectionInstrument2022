using System;
using System.IO;
using System.Text.Json;

namespace UpperComInspectionInstrument2022.Models
{
    /// <summary>
    /// 长期有效的实验室和标准器资料。现场条件与被校设备资料属于任务，不放在这里。
    /// </summary>
    public static class SystemSettingsContext
    {
        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IndustrialEquipmentCalibration",
            "system-settings.json");

        public static string StandardName { get; set; } = "温湿度巡检仪";
        public static string CertificateNumber { get; set; } = string.Empty;
        public static DateTime? ValidityDate { get; set; }
        public static string Model { get; set; } = string.Empty;
        public static string SerialNumber { get; set; } = string.Empty;
        public static string Organization { get; set; } = string.Empty;
        public static string OrganizationAddress { get; set; } = string.Empty;
        public static string TemperatureRange { get; set; } = "-80 ℃～300 ℃";
        public static string HumidityRange { get; set; } = "10 %RH～100 %RH";
        public static double TemperatureResolution { get; set; } = 0.01;
        public static double HumidityResolution { get; set; } = 0.1;
        public static string AccuracySpecification { get; set; } = "温度 MPE ±(0.15 ℃+0.002|t|)；湿度 MPE ±2.0 %RH";
        public static string ThermocoupleGrade { get; set; } = "廉金属不低于1级；贵金属不低于2级";
        public static double MeasuringInstrumentClass { get; set; } = 0.02;
        /// <summary>格式示例：1:0.02,2:-0.01；空白表示当前数据已按证书修正或尚未录入。</summary>
        public static string TemperatureChannelCorrections { get; set; } = string.Empty;
        public static string HumidityChannelCorrections { get; set; } = string.Empty;
        public static double TemperatureStabilityChange { get; set; } = 0.10;
        public static double HumidityStabilityChange { get; set; } = 0.5;
        /// <summary>标准器证书给出的扩展不确定度 U。</summary>
        public static double TemperatureUncertainty { get; set; } = 0.04;
        public static double TemperatureCoverage { get; set; } = 2;
        public static double HumidityUncertainty { get; set; } = 1;
        public static double HumidityCoverage { get; set; } = 2;

        public static void Load()
        {
            try
            {
                if (!File.Exists(SettingsPath)) return;
                SettingsSnapshot? snapshot = JsonSerializer.Deserialize<SettingsSnapshot>(File.ReadAllText(SettingsPath));
                if (snapshot == null) return;
                StandardName = snapshot.StandardName ?? StandardName;
                CertificateNumber = snapshot.CertificateNumber ?? string.Empty;
                ValidityDate = snapshot.ValidityDate;
                Model = snapshot.Model ?? string.Empty;
                SerialNumber = snapshot.SerialNumber ?? string.Empty;
                Organization = snapshot.Organization ?? string.Empty;
                OrganizationAddress = snapshot.OrganizationAddress ?? string.Empty;
                TemperatureRange = snapshot.TemperatureRange ?? TemperatureRange;
                HumidityRange = snapshot.HumidityRange ?? HumidityRange;
                TemperatureResolution = snapshot.TemperatureResolution > 0 ? snapshot.TemperatureResolution : TemperatureResolution;
                HumidityResolution = snapshot.HumidityResolution > 0 ? snapshot.HumidityResolution : HumidityResolution;
                AccuracySpecification = snapshot.AccuracySpecification ?? AccuracySpecification;
                ThermocoupleGrade = snapshot.ThermocoupleGrade ?? ThermocoupleGrade;
                MeasuringInstrumentClass = snapshot.MeasuringInstrumentClass > 0 ? snapshot.MeasuringInstrumentClass : MeasuringInstrumentClass;
                TemperatureChannelCorrections = snapshot.TemperatureChannelCorrections ?? string.Empty;
                HumidityChannelCorrections = snapshot.HumidityChannelCorrections ?? string.Empty;
                TemperatureStabilityChange = snapshot.TemperatureStabilityChange;
                HumidityStabilityChange = snapshot.HumidityStabilityChange;
                TemperatureUncertainty = snapshot.TemperatureUncertainty;
                TemperatureCoverage = snapshot.TemperatureCoverage;
                HumidityUncertainty = snapshot.HumidityUncertainty;
                HumidityCoverage = snapshot.HumidityCoverage;
            }
            catch (IOException) { }
            catch (JsonException) { }
        }

        public static void Save()
        {
            string? directory = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            SettingsSnapshot snapshot = new()
            {
                StandardName = StandardName,
                CertificateNumber = CertificateNumber,
                ValidityDate = ValidityDate,
                Model = Model,
                SerialNumber = SerialNumber,
                Organization = Organization,
                OrganizationAddress = OrganizationAddress,
                TemperatureRange = TemperatureRange,
                HumidityRange = HumidityRange,
                TemperatureResolution = TemperatureResolution,
                HumidityResolution = HumidityResolution,
                AccuracySpecification = AccuracySpecification,
                ThermocoupleGrade = ThermocoupleGrade,
                MeasuringInstrumentClass = MeasuringInstrumentClass,
                TemperatureChannelCorrections = TemperatureChannelCorrections,
                HumidityChannelCorrections = HumidityChannelCorrections,
                TemperatureStabilityChange = TemperatureStabilityChange,
                HumidityStabilityChange = HumidityStabilityChange,
                TemperatureUncertainty = TemperatureUncertainty,
                TemperatureCoverage = TemperatureCoverage,
                HumidityUncertainty = HumidityUncertainty,
                HumidityCoverage = HumidityCoverage
            };
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
        }

        private sealed class SettingsSnapshot
        {
            public string? StandardName { get; set; }
            public string? CertificateNumber { get; set; }
            public DateTime? ValidityDate { get; set; }
            public string? Model { get; set; }
            public string? SerialNumber { get; set; }
            public string? Organization { get; set; }
            public string? OrganizationAddress { get; set; }
            public string? TemperatureRange { get; set; }
            public string? HumidityRange { get; set; }
            public double TemperatureResolution { get; set; } = 0.01;
            public double HumidityResolution { get; set; } = 0.1;
            public string? AccuracySpecification { get; set; }
            public string? ThermocoupleGrade { get; set; }
            public double MeasuringInstrumentClass { get; set; } = 0.02;
            public string? TemperatureChannelCorrections { get; set; }
            public string? HumidityChannelCorrections { get; set; }
            public double TemperatureStabilityChange { get; set; } = 0.10;
            public double HumidityStabilityChange { get; set; } = 0.5;
            public double TemperatureUncertainty { get; set; } = 0.04;
            public double TemperatureCoverage { get; set; } = 2;
            public double HumidityUncertainty { get; set; } = 1;
            public double HumidityCoverage { get; set; } = 2;
        }
    }
}
