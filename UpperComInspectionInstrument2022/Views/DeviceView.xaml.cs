using System.Windows;
using System.Windows.Controls;
using UpperComInspectionInstrument2022.Models;
using UpperComInspectionInstrument2022.Services;

namespace UpperComInspectionInstrument2022.Views
{
    public partial class DeviceView : Page
    {
        public DeviceView()
        {
            InitializeComponent();
            LoadSettings();
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (!TryReadPositiveDouble(TemperatureResolutionTextBox, "温度分辨力", out double temperatureResolution) ||
                !TryReadPositiveDouble(HumidityResolutionTextBox, "湿度分辨力", out double humidityResolution) ||
                !TryReadPositiveDouble(MeasuringInstrumentClassTextBox, "测温仪器级别", out double instrumentClass) ||
                !TryReadPositiveDouble(TemperatureUncertaintyTextBox, "温度扩展不确定度", out double temperatureUncertainty) ||
                !TryReadPositiveDouble(TemperatureCoverageTextBox, "温度包含因子", out double temperatureCoverage) ||
                !TryReadPositiveDouble(HumidityUncertaintyTextBox, "湿度扩展不确定度", out double humidityUncertainty) ||
                !TryReadPositiveDouble(HumidityCoverageTextBox, "湿度包含因子", out double humidityCoverage) ||
                !TryReadNonNegativeDouble(TemperatureStabilityChangeTextBox, "温度修正值最大变化", out double temperatureStabilityChange) ||
                !TryReadNonNegativeDouble(HumidityStabilityChangeTextBox, "湿度修正值最大变化", out double humidityStabilityChange)) return;
            if (!ChannelCorrectionService.TryParse(TemperatureCorrectionsTextBox.Text, 50, out _, out string temperatureCorrectionError))
            {
                MessageBox.Show(temperatureCorrectionError, "温度通道修正", MessageBoxButton.OK, MessageBoxImage.Warning);
                TemperatureCorrectionsTextBox.Focus();
                return;
            }
            if (!ChannelCorrectionService.TryParse(HumidityCorrectionsTextBox.Text, 10, out _, out string humidityCorrectionError))
            {
                MessageBox.Show(humidityCorrectionError, "湿度通道修正", MessageBoxButton.OK, MessageBoxImage.Warning);
                HumidityCorrectionsTextBox.Focus();
                return;
            }

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
            SystemSettingsContext.AccuracySpecification = AccuracySpecificationTextBox.Text.Trim();
            SystemSettingsContext.ThermocoupleGrade = ThermocoupleGradeTextBox.Text.Trim();
            SystemSettingsContext.MeasuringInstrumentClass = instrumentClass;
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

        private void LoadSettings()
        {
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
            MeasuringInstrumentClassTextBox.Text = SystemSettingsContext.MeasuringInstrumentClass.ToString("0.###");
            TemperatureCorrectionsTextBox.Text = SystemSettingsContext.TemperatureChannelCorrections;
            HumidityCorrectionsTextBox.Text = SystemSettingsContext.HumidityChannelCorrections;
            TemperatureStabilityChangeTextBox.Text = SystemSettingsContext.TemperatureStabilityChange.ToString("0.###");
            HumidityStabilityChangeTextBox.Text = SystemSettingsContext.HumidityStabilityChange.ToString("0.###");
            TemperatureUncertaintyTextBox.Text = SystemSettingsContext.TemperatureUncertainty.ToString("0.###");
            TemperatureCoverageTextBox.Text = SystemSettingsContext.TemperatureCoverage.ToString("0.###");
            HumidityUncertaintyTextBox.Text = SystemSettingsContext.HumidityUncertainty.ToString("0.###");
            HumidityCoverageTextBox.Text = SystemSettingsContext.HumidityCoverage.ToString("0.###");
        }

        private static bool TryReadPositiveDouble(TextBox textBox, string name, out double value)
        {
            if (double.TryParse(textBox.Text, out value) && double.IsFinite(value) && value > 0) return true;
            MessageBox.Show($"{name}必须是大于 0 的有效数字。", "输入检查", MessageBoxButton.OK, MessageBoxImage.Warning);
            textBox.Focus();
            return false;
        }

        private static bool TryReadNonNegativeDouble(TextBox textBox, string name, out double value)
        {
            if (double.TryParse(textBox.Text, out value) && double.IsFinite(value) && value >= 0) return true;
            MessageBox.Show($"{name}必须是大于等于 0 的有效数字。", "输入检查", MessageBoxButton.OK, MessageBoxImage.Warning);
            textBox.Focus();
            return false;
        }
    }
}
