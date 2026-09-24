using System.Windows;
using System.Windows.Controls;
using UpperComInspectionInstrument2022.Communication;
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
            // 标准器资料允许先留空。用户可以先保存任务，正式校准前再补齐并由工作台进行完整性校验。
            if (!TryReadOptionalPositiveDouble(TemperatureResolutionTextBox, "温度分辨力", out double temperatureResolution) ||
                !TryReadOptionalPositiveDouble(TemperatureUncertaintyTextBox, "温度扩展不确定度", out double temperatureUncertainty) ||
                !TryReadOptionalPositiveDouble(TemperatureCoverageTextBox, "温度包含因子", out double temperatureCoverage)) return;

            double humidityResolution = SystemSettingsContext.HumidityResolution;
            double humidityUncertainty = SystemSettingsContext.HumidityUncertainty;
            double humidityCoverage = SystemSettingsContext.HumidityCoverage;
            double temperatureStabilityChange = SystemSettingsContext.TemperatureStabilityChange;
            double humidityStabilityChange = SystemSettingsContext.HumidityStabilityChange;
            if (!isFurnace &&
                (!TryReadOptionalPositiveDouble(HumidityResolutionTextBox, "湿度分辨力", out humidityResolution) ||
                     !TryReadOptionalPositiveDouble(HumidityUncertaintyTextBox, "湿度扩展不确定度", out humidityUncertainty) ||
                     !TryReadOptionalPositiveDouble(HumidityCoverageTextBox, "湿度包含因子", out humidityCoverage) ||
                     !TryReadOptionalNonNegativeDouble(TemperatureStabilityChangeTextBox, "温度修正值最大变化", out temperatureStabilityChange) ||
                     !TryReadOptionalNonNegativeDouble(HumidityStabilityChangeTextBox, "湿度修正值最大变化", out humidityStabilityChange))) return;

            if (!ChannelCorrectionService.TryParse(
                    TemperatureCorrectionsTextBox.Text,
                    InspectionInstrumentProtocol.PhysicalTemperatureChannelCount,
                    out _,
                    out string temperatureCorrectionError))
            {
                MessageBox.Show(temperatureCorrectionError, "温度通道修正", MessageBoxButton.OK, MessageBoxImage.Warning);
                TemperatureCorrectionsTextBox.Focus();
                return;
            }
            if (!isFurnace &&
                !ChannelCorrectionService.TryParse(
                    HumidityCorrectionsTextBox.Text,
                    InspectionInstrumentProtocol.PhysicalHumidityChannelCount,
                    out _,
                    out string humidityCorrectionError))
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

            bool formalCalibrationRunning = Application.Current.MainWindow is MainWindow mainWindow &&
                                            mainWindow.IsFormalCalibrationRunning;
            bool currentTaskUpdated = false;
            string taskSyncWarning = string.Empty;
            if (CalibrationTaskContext.IsConfigured &&
                CalibrationTaskContext.StandardIndex == _standardIndex &&
                !CalibrationTaskContext.HasCompletedCalibration &&
                !formalCalibrationRunning)
            {
                if (CalibrationTaskContext.TrySnapshotCurrentStandardSettings(
                        _standardIndex,
                        CalibrationTaskContext.IncludesHumidity,
                        out taskSyncWarning))
                {
                    CalibrationTaskContext.Save();
                    currentTaskUpdated = true;
                }
            }

            if (Application.Current.MainWindow is MainWindow owner)
                owner.NotifySystemSettingsSaved();

            bool standardProfileComplete = CalibrationTaskContext.TryValidateSystemStandardSettings(
                _standardIndex,
                CalibrationTaskContext.StandardIndex == 0 && CalibrationTaskContext.CalibrationTypeIndex == 1,
                out _);
            StatusTextBlock.Text = formalCalibrationRunning
                ? "系统资料已保存；当前正式校准仍使用启动时冻结的修正值，新值用于后续实时测量和任务。"
                : currentTaskUpdated
                    ? "系统资料已保存并同步当前任务；通道修正从下一组实时数据生效。"
                    : !standardProfileComplete
                        ? "系统资料已保存；标准器资料暂未完整，正式校准前请补充。"
                        : string.IsNullOrWhiteSpace(taskSyncWarning)
                        ? "系统资料已保存；通道修正从下一组实时数据生效。"
                        : $"系统资料已保存；当前任务未同步：{taskSyncWarning}";
            StatusTextBlock.Foreground = !standardProfileComplete || !string.IsNullOrWhiteSpace(taskSyncWarning)
                ? System.Windows.Media.Brushes.DarkOrange
                : System.Windows.Media.Brushes.DarkGreen;
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

        /// <summary>读取可选的正数；留空表示该标准器参数暂未维护，正式校准前再补充。</summary>
        private static bool TryReadOptionalPositiveDouble(TextBox textBox, string name, out double value)
        {
            if (string.IsNullOrWhiteSpace(textBox.Text))
            {
                value = 0;
                return true;
            }
            if (double.TryParse(textBox.Text, out value) && double.IsFinite(value) && value > 0) return true;
            MessageBox.Show($"{name}如果填写，必须是大于 0 的有效数字。", "输入检查", MessageBoxButton.OK, MessageBoxImage.Warning);
            textBox.Focus();
            return false;
        }

        /// <summary>读取可选的非负数；留空表示该标准器参数暂未维护。</summary>
        private static bool TryReadOptionalNonNegativeDouble(TextBox textBox, string name, out double value)
        {
            if (string.IsNullOrWhiteSpace(textBox.Text))
            {
                value = 0;
                return true;
            }
            if (double.TryParse(textBox.Text, out value) && double.IsFinite(value) && value >= 0) return true;
            MessageBox.Show($"{name}如果填写，必须是大于等于 0 的有效数字。", "输入检查", MessageBoxButton.OK, MessageBoxImage.Warning);
            textBox.Focus();
            return false;
        }
    }
}
