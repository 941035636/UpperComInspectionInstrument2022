using System.Windows;
using System.Windows.Controls;
using UpperComInspectionInstrument2022.Models;
using UpperComInspectionInstrument2022.Services;

namespace UpperComInspectionInstrument2022.Views
{
    /// <summary>
    /// 系统级资料设置页，用于维护标准器、证书、修正值和不确定度等跨任务复用的信息。
    /// 单次被校设备与委托信息不在本页维护。
    /// </summary>
    public partial class DeviceView : Page
    {
        private readonly int _standardIndex;

        /// <summary>
        /// 初始化页面并从 <see cref="SystemSettingsContext"/> 回填当前设置。
        /// <paramref name="preferredStandardIndex"/> 来自任务配置；本页不再提供第二套规范选择，避免任务与设置不一致。
        /// </summary>
        public DeviceView(int? preferredStandardIndex = null)
        {
            _standardIndex = Math.Clamp(
                preferredStandardIndex ?? CalibrationTaskContext.StandardIndex, 0, 1);
            InitializeComponent();
            LoadSettings();
            ApplyStandardDefaults();
            UpdateCapabilityMode();
        }

        /// <summary>校验所有数值与通道修正格式，通过后保存到本地系统设置文件。</summary>
        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            bool isFurnace = IsFurnaceMode;
            if (!TryReadRequiredText(LaboratoryNameTextBox, "实验室名称") ||
                !TryReadRequiredText(LaboratoryAddressTextBox, "实验室地址") ||
                !TryReadRequiredText(StandardNameTextBox, "标准器名称") ||
                !TryReadRequiredText(CertificateNumberTextBox, "标准器证书编号")) return;
            if (!ValidityDatePicker.SelectedDate.HasValue)
            {
                MessageBox.Show("请选择标准器证书有效期。", "输入检查", MessageBoxButton.OK, MessageBoxImage.Warning);
                ValidityDatePicker.Focus();
                return;
            }
            if (!TryReadPositiveDouble(TemperatureResolutionTextBox, "温度分辨力", out double temperatureResolution) ||
                !TryReadPositiveDouble(TemperatureUncertaintyTextBox, "温度扩展不确定度", out double temperatureUncertainty) ||
                !TryReadPositiveDouble(TemperatureCoverageTextBox, "温度包含因子", out double temperatureCoverage)) return;

            double humidityResolution = SystemSettingsContext.HumidityResolution;
            double humidityUncertainty = SystemSettingsContext.HumidityUncertainty;
            double humidityCoverage = SystemSettingsContext.HumidityCoverage;
            double temperatureStabilityChange = SystemSettingsContext.TemperatureStabilityChange;
            double humidityStabilityChange = SystemSettingsContext.HumidityStabilityChange;
            if (!isFurnace &&
                (!TryReadPositiveDouble(HumidityResolutionTextBox, "湿度分辨力", out humidityResolution) ||
                     !TryReadPositiveDouble(HumidityUncertaintyTextBox, "湿度扩展不确定度", out humidityUncertainty) ||
                     !TryReadPositiveDouble(HumidityCoverageTextBox, "湿度包含因子", out humidityCoverage) ||
                     !TryReadNonNegativeDouble(TemperatureStabilityChangeTextBox, "温度修正值最大变化", out temperatureStabilityChange) ||
                     !TryReadNonNegativeDouble(HumidityStabilityChangeTextBox, "湿度修正值最大变化", out humidityStabilityChange))) return;

            if (!ChannelCorrectionService.TryParse(TemperatureCorrectionsTextBox.Text, 50, out _, out string temperatureCorrectionError))
            {
                MessageBox.Show(temperatureCorrectionError, "温度通道修正", MessageBoxButton.OK, MessageBoxImage.Warning);
                TemperatureCorrectionsTextBox.Focus();
                return;
            }
            if (!isFurnace &&
                !ChannelCorrectionService.TryParse(HumidityCorrectionsTextBox.Text, 10, out _, out string humidityCorrectionError))
            {
                MessageBox.Show(humidityCorrectionError, "湿度通道修正", MessageBoxButton.OK, MessageBoxImage.Warning);
                HumidityCorrectionsTextBox.Focus();
                return;
            }

            SystemSettingsContext.LaboratoryName = LaboratoryNameTextBox.Text.Trim();
            SystemSettingsContext.LaboratoryAddress = LaboratoryAddressTextBox.Text.Trim();
            SystemSettingsContext.StandardName = StandardNameTextBox.Text.Trim();
            SystemSettingsContext.CertificateNumber = CertificateNumberTextBox.Text.Trim();
            SystemSettingsContext.ValidityDate = ValidityDatePicker.SelectedDate;
            SystemSettingsContext.Model = ModelTextBox.Text.Trim();
            SystemSettingsContext.SerialNumber = SerialNumberTextBox.Text.Trim();
            SystemSettingsContext.Organization = OrganizationTextBox.Text.Trim();
            SystemSettingsContext.OrganizationAddress = OrganizationAddressTextBox.Text.Trim();
            SystemSettingsContext.TemperatureRange = TemperatureRangeTextBox.Text.Trim();
            SystemSettingsContext.HumidityRange = HumidityRangeTextBox.Text.Trim();
            SystemSettingsContext.TemperatureResolution = temperatureResolution;
            SystemSettingsContext.HumidityResolution = humidityResolution;
            SystemSettingsContext.AccuracySpecification = SystemSettingsContext.Jjf1101AccuracyRequirement;
            SystemSettingsContext.ThermocoupleGrade = SystemSettingsContext.Jjf1376ThermocoupleGradeRequirement;
            SystemSettingsContext.MeasuringInstrumentClass = SystemSettingsContext.Jjf1376InstrumentClassRequirement;
            SystemSettingsContext.TemperatureChannelCorrections = TemperatureCorrectionsTextBox.Text.Trim();
            SystemSettingsContext.HumidityChannelCorrections = HumidityCorrectionsTextBox.Text.Trim();
            SystemSettingsContext.TemperatureStabilityChange = temperatureStabilityChange;
            SystemSettingsContext.HumidityStabilityChange = humidityStabilityChange;
            SystemSettingsContext.TemperatureUncertainty = temperatureUncertainty;
            SystemSettingsContext.TemperatureCoverage = temperatureCoverage;
            SystemSettingsContext.HumidityUncertainty = humidityUncertainty;
            SystemSettingsContext.HumidityCoverage = humidityCoverage;
            SystemSettingsContext.Save();
            StatusTextBlock.Text = "系统资料已保存，新建或重新保存任务时会引用最新快照。";
            StatusTextBlock.Foreground = System.Windows.Media.Brushes.DarkGreen;
        }

        /// <summary>
        /// 按任务配置页选择的规范显示对应字段，用户无需也不能在系统设置中再次选择规范。
        /// </summary>
        private void UpdateCapabilityMode()
        {
            bool isFurnace = IsFurnaceMode;
            Visibility environmentVisibility = isFurnace ? Visibility.Collapsed : Visibility.Visible;
            Visibility furnaceVisibility = isFurnace ? Visibility.Visible : Visibility.Collapsed;

            Jjf1101AccuracyPanel.Visibility = environmentVisibility;
            Jjf1376InstrumentPanel.Visibility = furnaceVisibility;
            HumidityRangeLabel.Visibility = environmentVisibility;
            HumidityRangeTextBox.Visibility = environmentVisibility;
            HumidityResolutionLabel.Visibility = environmentVisibility;
            HumidityResolutionTextBox.Visibility = environmentVisibility;
            HumidityCertificatePanel.Visibility = environmentVisibility;
            HumidityCorrectionsGrid.Visibility = environmentVisibility;
            TemperatureStabilityChangeRow.Visibility = environmentVisibility;
            CertificateParameterGapColumn.Width = isFurnace ? new GridLength(0) : new GridLength(18);
            HumidityCertificateColumn.Width = isFurnace ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        }

        /// <summary>当前任务是否采用 JJF 1376 箱式电阻炉规范。</summary>
        private bool IsFurnaceMode =>
            _standardIndex == CalibrationStandardRuleService.Jjf1376Index;

        /// <summary>把规范固定值写入只读字段，历史配置中的旧值不会覆盖当前规范要求。</summary>
        private void ApplyStandardDefaults()
        {
            AccuracySpecificationTextBox.Text = SystemSettingsContext.Jjf1101AccuracyRequirement;
            MeasuringInstrumentClassTextBox.Text =
                SystemSettingsContext.Jjf1376InstrumentClassRequirement.ToString("0.###");
            ThermocoupleGradeTextBox.Text = SystemSettingsContext.Jjf1376ThermocoupleGradeRequirement;
        }

        /// <summary>把当前系统上下文逐项显示到输入控件。</summary>
        private void LoadSettings()
        {
            LaboratoryNameTextBox.Text = SystemSettingsContext.LaboratoryName;
            LaboratoryAddressTextBox.Text = SystemSettingsContext.LaboratoryAddress;
            StandardNameTextBox.Text = SystemSettingsContext.StandardName;
            CertificateNumberTextBox.Text = SystemSettingsContext.CertificateNumber;
            ValidityDatePicker.SelectedDate = SystemSettingsContext.ValidityDate;
            ModelTextBox.Text = SystemSettingsContext.Model;
            SerialNumberTextBox.Text = SystemSettingsContext.SerialNumber;
            OrganizationTextBox.Text = SystemSettingsContext.Organization;
            OrganizationAddressTextBox.Text = SystemSettingsContext.OrganizationAddress;
            TemperatureRangeTextBox.Text = SystemSettingsContext.TemperatureRange;
            HumidityRangeTextBox.Text = SystemSettingsContext.HumidityRange;
            TemperatureResolutionTextBox.Text = SystemSettingsContext.TemperatureResolution.ToString("0.###");
            HumidityResolutionTextBox.Text = SystemSettingsContext.HumidityResolution.ToString("0.###");
            AccuracySpecificationTextBox.Text = SystemSettingsContext.AccuracySpecification;
            ThermocoupleGradeTextBox.Text = SystemSettingsContext.ThermocoupleGrade;
            MeasuringInstrumentClassTextBox.Text = SystemSettingsContext.MeasuringInstrumentClass > 0
                ? SystemSettingsContext.MeasuringInstrumentClass.ToString("0.###")
                : string.Empty;
            TemperatureCorrectionsTextBox.Text = SystemSettingsContext.TemperatureChannelCorrections;
            HumidityCorrectionsTextBox.Text = SystemSettingsContext.HumidityChannelCorrections;
            TemperatureStabilityChangeTextBox.Text = SystemSettingsContext.TemperatureStabilityChange.ToString("0.###");
            HumidityStabilityChangeTextBox.Text = SystemSettingsContext.HumidityStabilityChange.ToString("0.###");
            TemperatureUncertaintyTextBox.Text = SystemSettingsContext.TemperatureUncertainty.ToString("0.###");
            TemperatureCoverageTextBox.Text = SystemSettingsContext.TemperatureCoverage.ToString("0.###");
            HumidityUncertaintyTextBox.Text = SystemSettingsContext.HumidityUncertainty.ToString("0.###");
            HumidityCoverageTextBox.Text = SystemSettingsContext.HumidityCoverage.ToString("0.###");
        }

        /// <summary>检查校准报告和标准器追溯所需的短文本字段。</summary>
        private static bool TryReadRequiredText(TextBox textBox, string name)
        {
            if (!string.IsNullOrWhiteSpace(textBox.Text)) return true;
            MessageBox.Show($"{name}不能为空。", "输入检查", MessageBoxButton.OK, MessageBoxImage.Warning);
            textBox.Focus();
            return false;
        }

        /// <summary>读取必须大于 0 的有限数；失败时提示并聚焦对应输入框。</summary>
        private static bool TryReadPositiveDouble(TextBox textBox, string name, out double value)
        {
            if (double.TryParse(textBox.Text, out value) && double.IsFinite(value) && value > 0) return true;
            MessageBox.Show($"{name}必须是大于 0 的有效数字。", "输入检查", MessageBoxButton.OK, MessageBoxImage.Warning);
            textBox.Focus();
            return false;
        }

        /// <summary>读取允许为 0、但不能为负数的有限数。</summary>
        private static bool TryReadNonNegativeDouble(TextBox textBox, string name, out double value)
        {
            if (double.TryParse(textBox.Text, out value) && double.IsFinite(value) && value >= 0) return true;
            MessageBox.Show($"{name}必须是大于等于 0 的有效数字。", "输入检查", MessageBoxButton.OK, MessageBoxImage.Warning);
            textBox.Focus();
            return false;
        }
    }
}
