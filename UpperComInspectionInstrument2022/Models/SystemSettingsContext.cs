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
        /// <summary>JJF 1101-2019 表2规定的测量标准最大允许误差。</summary>
        public const string Jjf1101AccuracyRequirement = "温度 MPE ±(0.15 ℃+0.002|t|)；湿度 MPE ±2.0 %RH";
        /// <summary>JJF 1376-2012 表2规定的最低测温仪器级别。</summary>
        public const double Jjf1376InstrumentClassRequirement = 0.02;
        /// <summary>JJF 1376-2012 表2规定的热电偶等级要求。</summary>
        public const string Jjf1376ThermocoupleGradeRequirement = "廉金属不低于1级；贵金属不低于2级";
        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IndustrialEquipmentCalibration",
            "system-settings.json");

        public static string LaboratoryName { get; set; } = string.Empty;
        public static string LaboratoryAddress { get; set; } = string.Empty;
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
        public static string AccuracySpecification { get; set; } = Jjf1101AccuracyRequirement;
        public static string ThermocoupleGrade { get; set; } = Jjf1376ThermocoupleGradeRequirement;
        public static double MeasuringInstrumentClass { get; set; } = Jjf1376InstrumentClassRequirement;
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

        /// <summary>
        /// 从当前 Windows 用户的本地配置目录读取系统设置。
        /// 文件不存在或内容损坏时保留代码中的安全默认值，让程序仍可启动并由用户重新填写。
        /// </summary>
        public static void Load()
        {
            try
            {
                if (!File.Exists(SettingsPath)) return;
                SettingsSnapshot? snapshot = JsonSerializer.Deserialize<SettingsSnapshot>(File.ReadAllText(SettingsPath));
                if (snapshot == null) return;
                LaboratoryName = snapshot.LaboratoryName ?? string.Empty;
                LaboratoryAddress = snapshot.LaboratoryAddress ?? string.Empty;
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
                // 这三项是规范固定值，不允许旧版本配置或用户文件覆盖。
                AccuracySpecification = Jjf1101AccuracyRequirement;
                ThermocoupleGrade = Jjf1376ThermocoupleGradeRequirement;
                MeasuringInstrumentClass = Jjf1376InstrumentClassRequirement;
                TemperatureChannelCorrections = snapshot.TemperatureChannelCorrections ?? string.Empty;
                HumidityChannelCorrections = snapshot.HumidityChannelCorrections ?? string.Empty;
                TemperatureStabilityChange = snapshot.TemperatureStabilityChange >= 0 ? snapshot.TemperatureStabilityChange : TemperatureStabilityChange;
                HumidityStabilityChange = snapshot.HumidityStabilityChange >= 0 ? snapshot.HumidityStabilityChange : HumidityStabilityChange;
                TemperatureUncertainty = snapshot.TemperatureUncertainty > 0 ? snapshot.TemperatureUncertainty : TemperatureUncertainty;
                TemperatureCoverage = snapshot.TemperatureCoverage > 0 ? snapshot.TemperatureCoverage : TemperatureCoverage;
                HumidityUncertainty = snapshot.HumidityUncertainty > 0 ? snapshot.HumidityUncertainty : HumidityUncertainty;
                HumidityCoverage = snapshot.HumidityCoverage > 0 ? snapshot.HumidityCoverage : HumidityCoverage;
            }
            catch (IOException) { }
            catch (JsonException) { }
        }

        /// <summary>
        /// 将当前系统设置序列化为 JSON 文件。该文件只保存长期资料，不保存单次校准样本。
        /// </summary>
        public static void Save()
        {
            string? directory = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            SettingsSnapshot snapshot = new()
            {
                LaboratoryName = LaboratoryName,
                LaboratoryAddress = LaboratoryAddress,
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

        /// <summary>
        /// JSON 序列化专用的数据传输对象，使持久化结构与界面使用的静态上下文分离。
        /// </summary>
        private sealed class SettingsSnapshot
        {
            public string? LaboratoryName { get; set; }
            public string? LaboratoryAddress { get; set; }
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
            public double MeasuringInstrumentClass { get; set; } = Jjf1376InstrumentClassRequirement;
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
