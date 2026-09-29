using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using UpperComInspectionInstrument2022.Communication;
using UpperComInspectionInstrument2022.Models;
using UpperComInspectionInstrument2022.Services;

namespace UpperComInspectionInstrument2022.Views
{
    /// <summary>
    /// 校准任务配置页。
    /// 它把“规范—容积—校准点方案—空间布点—采样计划”联动成一份可执行任务，
    /// 并在进入工作台前完成范围、环境、标准器和偏离说明校验。
    /// </summary>
    public partial class SchemeView : Page
    {
        private bool _loading;
        private List<int> _temperatureChannelMapping = new();
        private List<int> _humidityChannelMapping = new();
        private readonly Dictionary<Control, InputVisualState> _invalidInputStates = new();

        /// <summary>任务配置页当前选择的规范索引，供系统设置页自动绑定使用。</summary>
        public int SelectedStandardIndex => Math.Clamp(StandardComboBox.SelectedIndex, 0, 1);

        /// <summary>初始化下拉选项，恢复上次任务，并应用当前规范规则。</summary>
        public SchemeView()
        {
            InitializeComponent();
            _loading = true;
            SensorTypeComboBox.ItemsSource = TemperatureSensorCatalog.DisplayNames;
            TemperaturePointCountComboBox.ItemsSource = Enumerable.Range(
                1, InspectionInstrumentProtocol.PhysicalTemperatureChannelCount).ToList();
            HumidityPointCountComboBox.ItemsSource = Enumerable.Range(
                1, InspectionInstrumentProtocol.PhysicalHumidityChannelCount).ToList();
            StandardComboBox.SelectedIndex = Math.Clamp(CalibrationTaskContext.StandardIndex, 0, 1);
            ConfigureStandardChoices(StandardComboBox.SelectedIndex, false);
            VolumeComboBox.SelectedIndex = CalibrationTaskContext.IsConfigured && CalibrationTaskContext.VolumeIndex is >= 0 and <= 1
                ? CalibrationTaskContext.VolumeIndex
                : -1;
            CalibrationTypeComboBox.SelectedIndex = StandardComboBox.SelectedIndex == 1
                ? 0
                : Math.Clamp(CalibrationTaskContext.CalibrationTypeIndex, 0, 1);
            PointSelectionComboBox.SelectedIndex = Math.Clamp(CalibrationTaskContext.PointSelectionIndex, 0, 1);
            PointLayoutModeComboBox.SelectedIndex = Math.Clamp(
                CalibrationTaskContext.PointLayoutModeIndex, 0, Math.Max(0, PointLayoutModeComboBox.Items.Count - 1));
            SamplingPlanModeComboBox.SelectedIndex = Math.Clamp(CalibrationTaskContext.SamplingPlanModeIndex, 0, 1);
            LoadConditionComboBox.SelectedIndex = Math.Clamp(CalibrationTaskContext.LoadConditionIndex, 0, 1);
            StabilityBasisComboBox.SelectedIndex = StandardComboBox.SelectedIndex == 1
                ? 0
                : Math.Clamp(CalibrationTaskContext.StabilityBasisIndex, 0, 2);
            AppearanceCheckComboBox.SelectedIndex = Math.Clamp(CalibrationTaskContext.AppearanceCheckIndex, 0, 2);
            SensorTypeComboBox.SelectedIndex = CalibrationTaskContext.IsConfigured &&
                                               CalibrationTaskContext.SensorTypeIndex >= 0 &&
                                               CalibrationTaskContext.SensorTypeIndex < SensorTypeComboBox.Items.Count
                ? CalibrationTaskContext.SensorTypeIndex
                : -1;
            _temperatureChannelMapping = new List<int>(CalibrationTaskContext.TemperatureChannelMapping);
            _humidityChannelMapping = new List<int>(CalibrationTaskContext.HumidityChannelMapping);
            LoadTaskValues();
            UpdateChannelMappingSummary();
            _loading = false;
            // 已保存任务必须保留任务快照；只有新建任务才应用规范默认值。
            ApplyRule(!CalibrationTaskContext.IsConfigured);
            UpdateReferencedSettings();
        }

        /// <summary>把 <see cref="CalibrationTaskContext"/> 中已保存的任务值回填到页面控件。</summary>
        private void LoadTaskValues()
        {
            CustomerNameTextBox.Text = CalibrationTaskContext.CustomerName;
            CustomerAddressTextBox.Text = CalibrationTaskContext.CustomerAddress;
            EquipmentNameTextBox.Text = CalibrationTaskContext.EquipmentName;
            ManufacturerTextBox.Text = CalibrationTaskContext.Manufacturer;
            ModelSpecificationTextBox.Text = CalibrationTaskContext.ModelSpecification;
            EquipmentSerialNumberTextBox.Text = CalibrationTaskContext.EquipmentSerialNumber;
            MeasurementRangeTextBox.Text = CalibrationTaskContext.MeasurementRange;
            CalibrationLocationTextBox.Text = CalibrationTaskContext.CalibrationLocation;
            CalibratorTextBox.Text = CalibrationTaskContext.Calibrator;
            VerifierTextBox.Text = CalibrationTaskContext.Verifier;
            FurnaceChamberLengthTextBox.Text = FormatOptional(CalibrationTaskContext.FurnaceChamberLengthMm);
            FurnaceChamberWidthTextBox.Text = FormatOptional(CalibrationTaskContext.FurnaceChamberWidthMm);
            FurnaceChamberHeightTextBox.Text = FormatOptional(CalibrationTaskContext.FurnaceChamberHeightMm);
            WorkZoneLengthTextBox.Text = FormatOptional(CalibrationTaskContext.WorkZoneLengthMm);
            WorkZoneWidthTextBox.Text = FormatOptional(CalibrationTaskContext.WorkZoneWidthMm);
            WorkZoneHeightTextBox.Text = FormatOptional(CalibrationTaskContext.WorkZoneHeightMm);
            SetTemperatureTextBox.Text = CalibrationTaskContext.SetTemperature?.ToString("0.###") ?? string.Empty;
            SetHumidityTextBox.Text = CalibrationTaskContext.SetHumidity?.ToString("0.###") ?? string.Empty;
            SelectPointCount(TemperaturePointCountComboBox, CalibrationTaskContext.TemperaturePointCount);
            SelectPointCount(HumidityPointCountComboBox, CalibrationTaskContext.HumidityPointCount);
            TemperatureCenterPointTextBox.Text = CalibrationTaskContext.TemperatureCenterPoint.ToString();
            HumidityCenterPointTextBox.Text = CalibrationTaskContext.HumidityCenterPoint.ToString();
            PointLayoutDescriptionTextBox.Text = CalibrationTaskContext.PointLayoutDescription;
            DutTemperatureResolutionTextBox.Text = FormatPositive(CalibrationTaskContext.DutTemperatureResolution);
            DutHumidityResolutionTextBox.Text = FormatPositive(CalibrationTaskContext.DutHumidityResolution);
            AmbientTemperatureTextBox.Text = FormatOptional(CalibrationTaskContext.AmbientTemperature);
            AmbientHumidityTextBox.Text = FormatOptional(CalibrationTaskContext.AmbientHumidity);
            AmbientPressureTextBox.Text = FormatOptional(CalibrationTaskContext.AmbientPressure);
            LoadDescriptionTextBox.Text = CalibrationTaskContext.LoadDescription;
            PlannedCountTextBox.Text = CalibrationTaskContext.PlannedCount.ToString();
            SamplingIntervalTextBox.Text = CalibrationTaskContext.SamplingIntervalSeconds.ToString();
            StableWaitTextBox.Text = CalibrationTaskContext.StableWaitMinutes.ToString();
            DeviationDescriptionTextBox.Text = CalibrationTaskContext.DeviationDescription;
            EnvironmentConfirmedCheckBox.IsChecked = CalibrationTaskContext.EnvironmentInterferenceConfirmed;
            TaskStatusTextBlock.Text = CalibrationTaskContext.IsConfigured ? "已加载已保存任务，修改后请重新保存" : "任务尚未保存";
        }

        /// <summary>切换规范时重建容积、校准类型、稳定依据和布点方案选项。</summary>
        private void StandardComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || StandardComboBox.SelectedIndex < 0) return;
            _loading = true;
            ConfigureStandardChoices(StandardComboBox.SelectedIndex, true);
            SamplingPlanModeComboBox.SelectedIndex = 0;
            _loading = false;
            ApplyRule(true);
        }

        /// <summary>按 JJF 1101 或 JJF 1376 配置所有规范相关下拉选项。</summary>
        private void ConfigureStandardChoices(int standardIndex, bool resetSelection)
        {
            int oldVolume = resetSelection
                ? -1
                : CalibrationTaskContext.IsConfigured && CalibrationTaskContext.VolumeIndex is >= 0 and <= 1
                    ? CalibrationTaskContext.VolumeIndex
                    : -1;
            VolumeComboBox.Items.Clear();
            CalibrationTypeComboBox.Items.Clear();
            StabilityBasisComboBox.Items.Clear();
            PointSelectionComboBox.Items.Clear();
            PointLayoutModeComboBox.Items.Clear();
            if (standardIndex == CalibrationStandardRuleService.Jjf1376Index)
            {
                VolumeLabel.Text = "测温区容积 *";
                VolumeComboBox.Items.Add("≤ 0.15 m³");
                VolumeComboBox.Items.Add("> 0.15 m³");
                CalibrationTypeComboBox.Items.Add("炉温参数（均匀度、稳定度、偏差、最大温差）");
                StabilityBasisComboBox.Items.Add("人工确认达到热稳定状态");
            }
            else
            {
                VolumeLabel.Text = "设备容积 *";
                VolumeComboBox.Items.Add("≤ 2 m³");
                VolumeComboBox.Items.Add("> 2 m³");
                CalibrationTypeComboBox.Items.Add("温度参数");
                CalibrationTypeComboBox.Items.Add("温湿度参数");
                StabilityBasisComboBox.Items.Add("按设备说明书/工艺规定确认稳定");
                StabilityBasisComboBox.Items.Add("按规范默认等待（到达设定值后 30～60 min）");
                StabilityBasisComboBox.Items.Add("人工确认已稳定并提前开始（记录说明）");
            }
            CalibrationStandardRule optionRule = CalibrationStandardRuleService.GetRule(standardIndex, 0, standardIndex == 0);
            foreach (string option in optionRule.CalibrationPointOptions) PointSelectionComboBox.Items.Add(option);
            foreach (string option in optionRule.PointLayoutModeOptions) PointLayoutModeComboBox.Items.Add(option);
            VolumeComboBox.SelectedIndex = oldVolume;
            CalibrationTypeComboBox.SelectedIndex = 0;
            StabilityBasisComboBox.SelectedIndex = standardIndex == 1 ? 0 : 1;
            if (resetSelection)
            {
                PointSelectionComboBox.SelectedIndex = 0;
                PointLayoutModeComboBox.SelectedIndex = 0;
            }
        }

        /// <summary>容积档位变化后重新生成默认测点数、中心点和布点说明。</summary>
        private void VolumeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_loading && VolumeComboBox.SelectedIndex >= 0) ApplyRule(true);
        }

        /// <summary>温度/温湿度类型变化后更新湿度参数可见性和规范默认值。</summary>
        private void CalibrationTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_loading && CalibrationTypeComboBox.SelectedIndex >= 0) ApplyRule(true);
        }

        /// <summary>布点模式变化后更新是否允许自定义点数及对应说明。</summary>
        private void PointLayoutModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_loading) ApplyRule(true);
        }

        /// <summary>
        /// 自定义测点数变化时同步收敛中心点和通道映射，避免保留大于新点数的旧中心点或旧绑定。
        /// </summary>
        private void PointCountComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            if (ReferenceEquals(sender, TemperaturePointCountComboBox) &&
                TryGetPointCount(TemperaturePointCountComboBox, 1, InspectionInstrumentProtocol.PhysicalTemperatureChannelCount, out int temperatureCount))
            {
                ClampCenterPoint(TemperatureCenterPointTextBox, temperatureCount);
            }
            else if (ReferenceEquals(sender, HumidityPointCountComboBox) &&
                     TryGetPointCount(HumidityPointCountComboBox, 1, InspectionInstrumentProtocol.PhysicalHumidityChannelCount, out int humidityCount))
            {
                ClampCenterPoint(HumidityCenterPointTextBox, humidityCount);
            }

            UpdateChannelMappingSummary();
        }

        /// <summary>
        /// 切换正式采样计划。规范模式立即恢复规范默认值并锁定输入；自定义模式保留当前值供用户调整。
        /// </summary>
        private void SamplingPlanModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || StandardComboBox.SelectedIndex < 0) return;
            bool includesHumidity = StandardComboBox.SelectedIndex == 0 && CalibrationTypeComboBox.SelectedIndex == 1;
            int volumeIndex = VolumeComboBox.SelectedIndex >= 0 ? VolumeComboBox.SelectedIndex : 0;
            CalibrationStandardRule rule = CalibrationStandardRuleService.GetRule(
                StandardComboBox.SelectedIndex, volumeIndex, includesHumidity);
            UpdateSamplingPlanControls(rule, SamplingPlanModeComboBox.SelectedIndex == 0);
        }

        /// <summary>校准点来源变化后只更新方案说明，不覆盖用户已经填写的执行参数。</summary>
        private void PointSelectionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_loading) ApplyRule(false);
        }

        /// <summary>只有负载校准时才允许填写负载说明。</summary>
        private void LoadConditionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LoadDescriptionTextBox != null) LoadDescriptionTextBox.IsEnabled = LoadConditionComboBox.SelectedIndex == 1;
        }

        /// <summary>稳定依据变化时切换等待时间输入框。</summary>
        private void StabilityBasisComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_loading) UpdateStabilityControls();
        }

        /// <summary>
        /// 应用规范联动规则。<paramref name="applyDefaults"/> 为 true 时重新写入规范默认执行参数，
        /// 为 false 时只刷新说明和控件状态，避免覆盖用户输入。
        /// </summary>
        private void ApplyRule(bool applyDefaults)
        {
            if (StandardComboBox.SelectedIndex < 0 || CalibrationTypeComboBox.SelectedIndex < 0) return;
            bool includesHumidity = StandardComboBox.SelectedIndex == 0 && CalibrationTypeComboBox.SelectedIndex == 1;
            bool isJjf1101 = StandardComboBox.SelectedIndex == 0;
            MeasurementRangeLabel.Text = "使用/测量范围（可选）";
            WorkZoneDimensionLabel.Text = !isJjf1101
                ? "测温区尺寸 (mm) *"
                : PointLayoutModeComboBox.SelectedIndex == 2
                    ? "工作区尺寸 (mm) *"
                    : "工作区尺寸 (mm)（可选）";
            FurnaceChamberDimensionGrid.Visibility = isJjf1101 ? Visibility.Collapsed : Visibility.Visible;
            ViewLayoutFigureButton.Visibility = Visibility.Visible;
            ViewLayoutFigureButton.Content = isJjf1101
                ? VolumeComboBox.SelectedIndex switch
                {
                    0 => "查看图 1",
                    1 => "查看图 2",
                    _ => "查看布点图"
                }
                : PointLayoutModeComboBox.SelectedIndex switch
                {
                    0 => "查看图 1",
                    1 => "查看图 2",
                    _ => "查看图 1 / 图 2"
                };
            ViewLayoutFigureButton.ToolTip = isJjf1101
                ? "查看 JJF 1101-2019 图 1、图 2 的温度和湿度空间布点"
                : "查看 JJF 1376-2012 图 1、图 2 的测温区与测温点位置";
            ApplyStandardVisibility(includesHumidity, isJjf1101);

            if (VolumeComboBox.SelectedIndex < 0)
            {
                CalibrationStandardRule pendingRule = CalibrationStandardRuleService.GetRule(
                    StandardComboBox.SelectedIndex, 0, includesHumidity);
                if (applyDefaults)
                {
                    TemperaturePointCountComboBox.SelectedIndex = -1;
                    HumidityPointCountComboBox.SelectedIndex = -1;
                    TemperatureCenterPointTextBox.Text = string.Empty;
                    HumidityCenterPointTextBox.Text = includesHumidity ? string.Empty : "0";
                    StableWaitTextBox.Text = pendingRule.StableWaitMinutes.ToString();
                    PointLayoutDescriptionTextBox.Text = string.Empty;
                }
                UpdateSamplingPlanControls(pendingRule, applyDefaults && SamplingPlanModeComboBox.SelectedIndex == 0);
                RuleScopeTextBlock.Text = $"{pendingRule.ScopeText}\n{pendingRule.EnvironmentRuleText}";
                RulePlanTextBlock.Text = $"校准点方案：{CalibrationStandardRuleService.GetCalibrationPointRuleText(pendingRule, PointSelectionComboBox.SelectedIndex)}\n请先选择实际容积，软件再生成空间测点数量、中心点和布点说明。";
                RuleStabilityTextBlock.Text = $"{pendingRule.StabilityRuleText}\n输出：{pendingRule.ResultItemsText}";
                TemperaturePointCountComboBox.IsEnabled = false;
                HumidityPointCountComboBox.IsEnabled = false;
                TemperatureCenterPointTextBox.IsReadOnly = true;
                HumidityCenterPointTextBox.IsReadOnly = true;
                PointLayoutDescriptionTextBox.IsReadOnly = true;
                UpdateStabilityControls();
                UpdateReferencedSettings();
                UpdateChannelMappingSummary();
                return;
            }

            CalibrationStandardRule rule = CalibrationStandardRuleService.GetRule(StandardComboBox.SelectedIndex, VolumeComboBox.SelectedIndex, includesHumidity);
            string selectedLayoutText = CalibrationStandardRuleService.GetPointLayoutText(rule, PointLayoutModeComboBox.SelectedIndex);

            if (applyDefaults)
            {
                SelectPointCount(TemperaturePointCountComboBox, rule.TemperaturePointCount);
                SelectPointCount(HumidityPointCountComboBox, rule.HumidityPointCount);
                TemperatureCenterPointTextBox.Text = rule.TemperatureCenterPoint.ToString();
                HumidityCenterPointTextBox.Text = Math.Max(1, rule.HumidityCenterPoint).ToString();
                StableWaitTextBox.Text = rule.StableWaitMinutes.ToString();
            }
            UpdateSamplingPlanControls(rule, applyDefaults && SamplingPlanModeComboBox.SelectedIndex == 0);
            if (applyDefaults || !rule.SupportsCustomPointLayout)
                PointLayoutDescriptionTextBox.Text = selectedLayoutText;

            RuleScopeTextBlock.Text = $"{rule.ScopeText}\n{rule.EnvironmentRuleText}";
            RulePlanTextBlock.Text = $"校准点方案：{CalibrationStandardRuleService.GetCalibrationPointRuleText(rule, PointSelectionComboBox.SelectedIndex)}\n空间布点：{selectedLayoutText}\n{rule.SamplingRuleText}";
            RuleStabilityTextBlock.Text = $"{rule.StabilityRuleText}\n输出：{rule.ResultItemsText}";

            bool customPointInput = CalibrationStandardRuleService.AllowsCustomPointInput(
                rule, PointLayoutModeComboBox.SelectedIndex);
            TemperaturePointCountComboBox.IsEnabled = customPointInput;
            HumidityPointCountComboBox.IsEnabled = customPointInput;
            TemperatureCenterPointTextBox.IsReadOnly = !customPointInput;
            // O 是规范中的空间位置；它映射到巡检仪哪个湿度通道取决于现场接线，始终允许确认/修改。
            HumidityCenterPointTextBox.IsReadOnly = false;
            PointLayoutDescriptionTextBox.IsReadOnly = !customPointInput;
            LoadDescriptionTextBox.IsEnabled = LoadConditionComboBox.SelectedIndex == 1;
            UpdateStabilityControls();
            UpdateReferencedSettings();
            UpdateChannelMappingSummary();
        }

        /// <summary>按页面当前点数修复映射并显示紧凑摘要。</summary>
        private void UpdateChannelMappingSummary()
        {
            if (ChannelMappingSummaryTextBlock == null) return;
            int temperatureCount = TryGetPointCount(
                TemperaturePointCountComboBox, 1, InspectionInstrumentProtocol.PhysicalTemperatureChannelCount, out int parsedTemperature)
                ? parsedTemperature : 0;
            bool includesHumidity = StandardComboBox.SelectedIndex == 0 && CalibrationTypeComboBox.SelectedIndex == 1;
            int humidityCount = includesHumidity && TryGetPointCount(
                HumidityPointCountComboBox, 1, InspectionInstrumentProtocol.PhysicalHumidityChannelCount, out int parsedHumidity)
                ? parsedHumidity : 0;
            _temperatureChannelMapping = MeasurementChannelMappingService.Normalize(
                _temperatureChannelMapping, temperatureCount, InspectionInstrumentProtocol.PhysicalTemperatureChannelCount);
            _humidityChannelMapping = MeasurementChannelMappingService.Normalize(
                _humidityChannelMapping, humidityCount, InspectionInstrumentProtocol.PhysicalHumidityChannelCount);
            string summary = MeasurementChannelMappingService.FormatSummary(_temperatureChannelMapping, _humidityChannelMapping);
            ChannelMappingSummaryTextBlock.Text = string.IsNullOrWhiteSpace(summary)
                ? "请先选择规范、容积和测点数；保存任务后到校准工作台配置实际设备接口。"
                : $"当前默认：{summary}。保存任务后，到校准工作台“设备通道配置”中按实际接线调整并应用。";
        }

        /// <summary>
        /// 根据所选规范说明正式采样计划的默认值和可调整方式。
        /// </summary>
        private void UpdateSamplingPlanControls(CalibrationStandardRule rule, bool applyDefaults)
        {
            bool isJjf1101 = StandardComboBox.SelectedIndex == CalibrationStandardRuleService.Jjf1101Index;
            bool usesNormativePlan = SamplingPlanModeComboBox.SelectedIndex != 1;
            if (applyDefaults)
            {
                PlannedCountTextBox.Text = rule.SampleCount.ToString();
                SamplingIntervalTextBox.Text = rule.SampleIntervalSeconds.ToString();
            }

            PlannedCountTextBox.IsReadOnly = usesNormativePlan;
            SamplingIntervalTextBox.IsReadOnly = usesNormativePlan;
            if (isJjf1101)
            {
                PlannedCountLabel.Text = "正式样本数 *";
                SamplingIntervalLabel.Text = "正式采样间隔 (s) *";
                PlannedCountTextBox.ToolTip = usesNormativePlan ? "JJF 1101 规范计划为 16 组。" : "按设备运行状况或用户需求填写正式样本组数。";
                SamplingIntervalTextBox.ToolTip = usesNormativePlan ? "JJF 1101 规范计划为每 120 秒记录一组。" : "按设备运行状况或用户需求填写，单位为秒。";
                SamplingPlanHintTextBlock.Text = usesNormativePlan
                    ? "JJF 1101：每 2 min 记录一次，30 min 共 16 组。"
                    : "调整时间间隔或记录次数后，须在偏离/自定义说明中记录设备运行状况或用户需求。";
                return;
            }

            PlannedCountLabel.Text = "正式样本数（≥20）*";
            SamplingIntervalLabel.Text = "正式采样间隔 (s) *";
            PlannedCountTextBox.ToolTip = usesNormativePlan ? "JJF 1376 规范计划为 20 组。" : "自定义计划仍不得少于 20 组。";
            SamplingIntervalTextBox.ToolTip = usesNormativePlan ? "JJF 1376 规范计划为每 180 秒记录一组。" : "自定义间隔必须在偏离/自定义说明中记录。";
            SamplingPlanHintTextBlock.Text = usesNormativePlan
                ? "JJF 1376：60 min 内每隔 3 min 记录一次，至少 20 次。"
                : "自定义计划不得少于 20 组，并须记录现场要求、原因和依据。";
        }

        /// <summary>按当前规范、设备容积或箱式炉布点模式打开对应的规范布点图。</summary>
        private void ViewLayoutFigureButton_Click(object sender, RoutedEventArgs e)
        {
            if (StandardComboBox.SelectedIndex == CalibrationStandardRuleService.Jjf1101Index)
            {
                int preferredEnvironmentFigure = VolumeComboBox.SelectedIndex == 1 ? 2 : 1;
                Jjf1101LayoutFigureWindow environmentWindow = new(preferredEnvironmentFigure)
                {
                    Owner = Window.GetWindow(this)
                };
                environmentWindow.ShowDialog();
                return;
            }

            int preferredFigure = PointLayoutModeComboBox.SelectedIndex == 1 ? 2 : 1;
            Jjf1376LayoutFigureWindow window = new(preferredFigure)
            {
                Owner = Window.GetWindow(this)
            };
            window.ShowDialog();
        }

        /// <summary>根据任务是否含湿度及规范类型控制条件字段的显示。</summary>
        private void ApplyStandardVisibility(bool includesHumidity, bool isJjf1101)
        {
            Visibility humidityVisibility = includesHumidity ? Visibility.Visible : Visibility.Collapsed;
            SetHumidityLabel.Visibility = humidityVisibility;
            SetHumidityTextBox.Visibility = humidityVisibility;
            HumidityPointCountLabel.Visibility = humidityVisibility;
            HumidityPointCountComboBox.Visibility = humidityVisibility;
            HumidityCenterPointLabel.Visibility = humidityVisibility;
            HumidityCenterPointTextBox.Visibility = humidityVisibility;
            DutHumidityResolutionLabel.Visibility = humidityVisibility;
            DutHumidityResolutionTextBox.Visibility = humidityVisibility;
            AmbientPressureLabel.Visibility = isJjf1101 ? Visibility.Visible : Visibility.Collapsed;
            AmbientPressureTextBox.Visibility = isJjf1101 ? Visibility.Visible : Visibility.Collapsed;
            AppearanceCheckLabel.Visibility = isJjf1101 ? Visibility.Collapsed : Visibility.Visible;
            AppearanceCheckComboBox.Visibility = isJjf1101 ? Visibility.Collapsed : Visibility.Visible;
        }

        /// <summary>只在 JJF 1101 选择规范计时等待时显示等待分钟数。</summary>
        private void UpdateStabilityControls()
        {
            bool timedWait = StandardComboBox.SelectedIndex == 0 && StabilityBasisComboBox.SelectedIndex == 1;
            StableWaitLabel.Visibility = timedWait ? Visibility.Visible : Visibility.Collapsed;
            StableWaitTextBox.Visibility = timedWait ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>在任务页显示即将固化到任务中的标准器身份和主要能力。</summary>
        private void UpdateReferencedSettings()
        {
            string standardName = string.IsNullOrWhiteSpace(SystemSettingsContext.StandardName) ? "未配置标准器名称" : SystemSettingsContext.StandardName;
            string certificate = string.IsNullOrWhiteSpace(SystemSettingsContext.CertificateNumber) ? "证书编号未填写" : $"证书 {SystemSettingsContext.CertificateNumber}";
            string validity = SystemSettingsContext.ValidityDate?.ToString("yyyy-MM-dd") ?? "有效期未填写";
            StandardReferenceTextBlock.Text = $"{standardName} · {certificate} · {validity}";
            bool isFurnace = StandardComboBox.SelectedIndex == CalibrationStandardRuleService.Jjf1376Index;
            bool includesHumidity = !isFurnace && CalibrationTypeComboBox.SelectedIndex == 1;
            string measuringInstrumentClass = SystemSettingsContext.MeasuringInstrumentClass > 0
                ? $"{SystemSettingsContext.MeasuringInstrumentClass:0.###} 级"
                : "级别未填写";
            string thermocoupleGrade = string.IsNullOrWhiteSpace(SystemSettingsContext.ThermocoupleGrade)
                ? "未填写"
                : SystemSettingsContext.ThermocoupleGrade;
            StandardCapabilityTextBlock.Text = isFurnace
                ? $"温度 {SystemSettingsContext.TemperatureRange} / {SystemSettingsContext.TemperatureResolution:0.###} ℃ / 测温仪器{measuringInstrumentClass}；热电偶 {thermocoupleGrade}；U={SystemSettingsContext.TemperatureUncertainty:0.###}, k={SystemSettingsContext.TemperatureCoverage:0.###}"
                : includesHumidity
                    ? $"温度 {SystemSettingsContext.TemperatureRange} / {SystemSettingsContext.TemperatureResolution:0.###} ℃ / U={SystemSettingsContext.TemperatureUncertainty:0.###}, k={SystemSettingsContext.TemperatureCoverage:0.###}；湿度 {SystemSettingsContext.HumidityRange} / {SystemSettingsContext.HumidityResolution:0.###} %RH / U={SystemSettingsContext.HumidityUncertainty:0.###}, k={SystemSettingsContext.HumidityCoverage:0.###}"
                    : $"温度 {SystemSettingsContext.TemperatureRange} / {SystemSettingsContext.TemperatureResolution:0.###} ℃ / U={SystemSettingsContext.TemperatureUncertainty:0.###}, k={SystemSettingsContext.TemperatureCoverage:0.###}";
        }

        /// <summary>跳转到系统设置维护标准器和证书资料。</summary>
        private void OpenSystemSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            if (Application.Current.MainWindow is MainWindow mainWindow)
                mainWindow.ShowSettingsPage(StandardComboBox.SelectedIndex);
        }

        /// <summary>
        /// 按从基础选择到现场条件的顺序校验任务；全部通过后固化标准器快照、保存任务并进入工作台。
        /// </summary>
        private void StartCalibrationButton_Click(object sender, RoutedEventArgs e)
        {
            ClearAllInputErrors();
            if (StandardComboBox.SelectedIndex < 0 || CalibrationTypeComboBox.SelectedIndex < 0 || PointSelectionComboBox.SelectedIndex < 0)
            {
                ShowInputError("请选择校准规范、校准项目和校准点方案。", StandardComboBox);
                return;
            }
            if (VolumeComboBox.SelectedIndex < 0)
            {
                ShowInputError("请选择本次被校设备的实际容积分类，软件不能据默认值推断布点。", VolumeComboBox);
                return;
            }

            bool includesHumidity = StandardComboBox.SelectedIndex == 0 && CalibrationTypeComboBox.SelectedIndex == 1;
            CalibrationStandardRule rule = CalibrationStandardRuleService.GetRule(StandardComboBox.SelectedIndex, VolumeComboBox.SelectedIndex, includesHumidity);

            double? workZoneVolume = TryGetWorkZoneVolume(out bool hasAnyWorkZoneDimension);
            bool furnaceChamberComplete = ParseOptionalPositiveDouble(FurnaceChamberLengthTextBox.Text).HasValue &&
                                          ParseOptionalPositiveDouble(FurnaceChamberWidthTextBox.Text).HasValue &&
                                          ParseOptionalPositiveDouble(FurnaceChamberHeightTextBox.Text).HasValue;
            if (StandardComboBox.SelectedIndex == 1 && !furnaceChamberComplete)
            {
                ShowInputError("JJF 1376 附录 A 需要炉膛长度 L、宽度 W 和高度 H，请完整填写正数。", FurnaceChamberLengthTextBox);
                return;
            }
            if (StandardComboBox.SelectedIndex == 1 && !workZoneVolume.HasValue)
            {
                ShowInputError("JJF 1376 原始记录需要测温区长度、宽度和高度，请完整填写正数。", WorkZoneLengthTextBox);
                return;
            }
            if (StandardComboBox.SelectedIndex == 0 && hasAnyWorkZoneDimension && !workZoneVolume.HasValue)
            {
                ShowInputError("工作区尺寸应同时填写长度、宽度和高度，或全部留空。", WorkZoneLengthTextBox);
                return;
            }
            if (workZoneVolume.HasValue && !CalibrationStandardRuleService.MatchesVolumeClass(
                    StandardComboBox.SelectedIndex, VolumeComboBox.SelectedIndex, workZoneVolume.Value))
            {
                double threshold = CalibrationStandardRuleService.GetVolumeThreshold(StandardComboBox.SelectedIndex);
                ShowInputError($"按工作区尺寸计算的容积为 {workZoneVolume.Value:0.######} m³，与所选“{(VolumeComboBox.SelectedIndex == 0 ? "≤" : ">")} {threshold:0.##} m³”档位不一致。", WorkZoneLengthTextBox);
                return;
            }

            if (!TryParseDouble(SetTemperatureTextBox.Text, rule.MinimumSetTemperature, rule.MaximumSetTemperature, out double setTemperature))
            {
                ShowInputError($"设定温度必须在 {rule.MinimumSetTemperature:0.###} ℃～{rule.MaximumSetTemperature:0.###} ℃ 范围内。", SetTemperatureTextBox);
                return;
            }
            double? setHumidity = null;
            if (includesHumidity)
            {
                if (!TryParseDouble(SetHumidityTextBox.Text, 10, 100, out double humidity))
                {
                    ShowInputError("设定湿度必须在 10 %RH～100 %RH 范围内。", SetHumidityTextBox);
                    return;
                }
                setHumidity = humidity;
            }

            if (SensorTypeComboBox.SelectedIndex < 0)
            {
                ShowInputError("请选择本次实际接入的温度传感器类型。", SensorTypeComboBox);
                return;
            }

            int temperatureCount = 0;
            int humidityCount = 0;
            int temperatureCenter = 0;
            int humidityCenter = 0;
            int plannedCount = 0;
            int samplingInterval = 0;
            double dutTemperatureResolution = 0;
            double dutHumidityResolution = 0;
            var invalidTaskFields = new List<(Control Control, string Name)>();
            bool temperatureCountValid = TryGetPointCount(
                TemperaturePointCountComboBox, 1, InspectionInstrumentProtocol.PhysicalTemperatureChannelCount, out temperatureCount);
            if (!temperatureCountValid) invalidTaskFields.Add((TemperaturePointCountComboBox, "温度空间测点数"));

            bool humidityCountValid = !includesHumidity || TryGetPointCount(
                HumidityPointCountComboBox, 1, InspectionInstrumentProtocol.PhysicalHumidityChannelCount, out humidityCount);
            if (!includesHumidity) humidityCount = 0;
            if (!humidityCountValid) invalidTaskFields.Add((HumidityPointCountComboBox, "湿度空间测点数"));

            bool temperatureCenterValid = temperatureCountValid &&
                                          TryParseInt(TemperatureCenterPointTextBox.Text, 1, temperatureCount, out temperatureCenter);
            if (!temperatureCenterValid) invalidTaskFields.Add((TemperatureCenterPointTextBox, "温度中心点"));

            bool humidityCenterValid = !includesHumidity ||
                                       (humidityCountValid &&
                                        TryParseInt(HumidityCenterPointTextBox.Text, 1, humidityCount, out humidityCenter));
            if (!humidityCenterValid) invalidTaskFields.Add((HumidityCenterPointTextBox, "湿度 O 对应通道"));

            if (!TryParseInt(PlannedCountTextBox.Text, 1, 10000, out plannedCount))
                invalidTaskFields.Add((PlannedCountTextBox, "正式样本数"));
            if (!TryParseInt(SamplingIntervalTextBox.Text, 1, 86400, out samplingInterval))
                invalidTaskFields.Add((SamplingIntervalTextBox, "正式采样间隔"));
            if (!TryParseDouble(DutTemperatureResolutionTextBox.Text, 0.000001, 1000, out dutTemperatureResolution))
                invalidTaskFields.Add((DutTemperatureResolutionTextBox, "被校温度分辨力"));
            if (includesHumidity &&
                !TryParseDouble(DutHumidityResolutionTextBox.Text, 0.000001, 100, out dutHumidityResolution))
                invalidTaskFields.Add((DutHumidityResolutionTextBox, "被校湿度分辨力"));

            if (invalidTaskFields.Count > 0)
            {
                string fieldNames = string.Join("、", invalidTaskFields.Select(item => item.Name));
                ShowInputErrors(
                    $"以下参数缺失或超出允许范围：\n{fieldNames}\n\n关闭提示后将定位到第一个错误项，红色边框字段需要修改。",
                    "任务参数不完整",
                    invalidTaskFields.Select(item => item.Control));
                return;
            }

            bool ambientTemperatureValid = TryParseDouble(
                AmbientTemperatureTextBox.Text, rule.MinimumAmbientTemperature, rule.MaximumAmbientTemperature, out double ambientTemperature);
            bool ambientHumidityValid = TryParseDouble(
                AmbientHumidityTextBox.Text, 0, rule.MaximumAmbientHumidity, out double ambientHumidity);
            if (!ambientTemperatureValid || !ambientHumidityValid)
            {
                var controls = new List<Control>();
                if (!ambientTemperatureValid) controls.Add(AmbientTemperatureTextBox);
                if (!ambientHumidityValid) controls.Add(AmbientHumidityTextBox);
                ShowInputErrors(
                    $"现场环境必须满足：温度 {rule.MinimumAmbientTemperature:0} ℃～{rule.MaximumAmbientTemperature:0} ℃，湿度不大于 {rule.MaximumAmbientHumidity:0} %RH。",
                    "环境条件不满足规范",
                    controls);
                return;
            }
            double? ambientPressure = null;
            if (rule.MinimumAmbientPressure.HasValue &&
                !TryParseDouble(AmbientPressureTextBox.Text, rule.MinimumAmbientPressure.Value, rule.MaximumAmbientPressure!.Value, out double pressure))
            {
                ShowInputError($"JJF 1101 要求环境气压为 {rule.MinimumAmbientPressure:0} kPa～{rule.MaximumAmbientPressure:0} kPa。", AmbientPressureTextBox);
                return;
            }
            else if (rule.MinimumAmbientPressure.HasValue)
            {
                ambientPressure = double.Parse(AmbientPressureTextBox.Text.Trim());
            }

            bool customPointInput = CalibrationStandardRuleService.AllowsCustomPointInput(
                rule, PointLayoutModeComboBox.SelectedIndex);
            if (!customPointInput &&
                (temperatureCount != rule.TemperaturePointCount || humidityCount != rule.HumidityPointCount))
            {
                ShowInputErrors(
                    "当前布点模式使用规范自动生成的测点数量；如需修改，请选择带“点数可自定义”的布点方式，并填写布点说明。",
                    "布点不符合选择",
                    new Control[] { TemperaturePointCountComboBox, HumidityPointCountComboBox });
                return;
            }
            if (!customPointInput && temperatureCenter != rule.TemperatureCenterPoint)
            {
                ShowInputError("规范默认布点的温度中心/监控点不能改变；如需调整空间位置，请选择对应的调整方式并填写说明。", TemperatureCenterPointTextBox, "中心点不符合选择");
                return;
            }
            if (customPointInput && string.IsNullOrWhiteSpace(PointLayoutDescriptionTextBox.Text))
            {
                ShowInputError("调整布点必须说明测点位置及原因。", PointLayoutDescriptionTextBox);
                return;
            }
            bool changedNormativePointCount = CalibrationStandardRuleService.RequiresDeviationForPointCountChange(
                rule, PointLayoutModeComboBox.SelectedIndex, temperatureCount, humidityCount);
            if (changedNormativePointCount && string.IsNullOrWhiteSpace(DeviationDescriptionTextBox.Text))
            {
                ShowInputError("自定义测点数改变了规范默认方案，必须在偏离/自定义说明中记录原因、依据和实际点数。", DeviationDescriptionTextBox);
                return;
            }
            bool extremePointCountMode = rule.RequiresExtremeVolumeForCustomPointCount &&
                                         rule.CustomPointCountModeIndex >= 0 &&
                                         PointLayoutModeComboBox.SelectedIndex == rule.CustomPointCountModeIndex;
            if (extremePointCountMode && (!workZoneVolume.HasValue ||
                                          !CalibrationStandardRuleService.AllowsJjf1101PointCountAdjustment(workZoneVolume.Value)))
            {
                ShowInputError("JJF 1101 只有工作空间容积小于 0.05 m³ 或大于 50 m³ 时才允许调整测点数量；请填写真实工作区尺寸并选择正确容积档位。", WorkZoneLengthTextBox);
                return;
            }
            if (StandardComboBox.SelectedIndex == CalibrationStandardRuleService.Jjf1376Index &&
                customPointInput && string.IsNullOrWhiteSpace(DeviationDescriptionTextBox.Text))
            {
                ShowInputError("箱式电阻炉自定义测点数属于现场调整，必须在偏离/自定义说明中记录原因和依据。", DeviationDescriptionTextBox);
                return;
            }
            if (PointSelectionComboBox.SelectedIndex == 1 && string.IsNullOrWhiteSpace(DeviationDescriptionTextBox.Text))
            {
                ShowInputError("客户指定校准点必须在偏离/自定义说明中记录客户要求。", DeviationDescriptionTextBox);
                return;
            }
            if (LoadConditionComboBox.SelectedIndex == 1 && string.IsNullOrWhiteSpace(LoadDescriptionTextBox.Text))
            {
                ShowInputError("负载校准必须说明负载情况。", LoadDescriptionTextBox);
                return;
            }
            if (SamplingPlanModeComboBox.SelectedIndex == 0 &&
                (plannedCount != rule.SampleCount || samplingInterval != rule.SampleIntervalSeconds))
            {
                ShowInputErrors(
                    "按规范采样计划时，样本数和采样间隔必须使用规范默认值；如需调整，请切换为自定义采样计划。",
                    "采样计划不一致",
                    new Control[] { PlannedCountTextBox, SamplingIntervalTextBox });
                return;
            }
            if (StandardComboBox.SelectedIndex == CalibrationStandardRuleService.Jjf1376Index && plannedCount < 20)
            {
                ShowInputError("JJF 1376 正式样本数不得少于 20 组。", PlannedCountTextBox);
                return;
            }
            long jjf1376DurationSeconds = (long)(plannedCount - 1) * samplingInterval;
            bool changedJjf1376Plan = StandardComboBox.SelectedIndex == CalibrationStandardRuleService.Jjf1376Index &&
                                      (samplingInterval != 180 || jjf1376DurationSeconds > 3600);
            if (changedJjf1376Plan && string.IsNullOrWhiteSpace(DeviationDescriptionTextBox.Text))
            {
                ShowInputError(
                    "当前箱式炉采样间隔或计划总时长已偏离 JJF 1376 的默认执行要求，请在偏离/自定义说明中填写现场要求、原因和依据。",
                    DeviationDescriptionTextBox);
                return;
            }
            bool changedJjf1101Plan = StandardComboBox.SelectedIndex == 0 && (plannedCount != 16 || samplingInterval != 120);
            if (changedJjf1101Plan && string.IsNullOrWhiteSpace(DeviationDescriptionTextBox.Text))
            {
                ShowInputError("调整 JJF 1101 默认采样间隔或次数时，必须填写客户要求/设备运行情况说明。", DeviationDescriptionTextBox);
                return;
            }
            int stableWait = 0;
            if (StandardComboBox.SelectedIndex == 0 && StabilityBasisComboBox.SelectedIndex == 1 &&
                !TryParseInt(StableWaitTextBox.Text, 30, 60, out stableWait))
            {
                ShowInputError("按 JJF 1101 默认等待时，稳定等待应为 30 min～60 min。", StableWaitTextBox);
                return;
            }
            if (EnvironmentConfirmedCheckBox.IsChecked != true)
            {
                MessageBox.Show("请先确认现场不存在规范禁止的环境干扰。", "现场条件未确认", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            SaveTask(rule, includesHumidity, setTemperature, setHumidity, temperatureCount, humidityCount, temperatureCenter,
                humidityCenter, plannedCount, samplingInterval, stableWait, dutTemperatureResolution, dutHumidityResolution,
                ambientTemperature, ambientHumidity, ambientPressure);
            // 任务配置与标准器维护解耦：先保存任务，标准器资料允许稍后补充。
            // 完整资料仍会在正式校准启动前强制校验，避免不完整信息进入正式报告。
            bool standardSettingsCaptured = CalibrationTaskContext.TrySnapshotCurrentStandardSettings(
                StandardComboBox.SelectedIndex, includesHumidity, out string standardSettingsError);
            if (!standardSettingsCaptured)
                CalibrationTaskContext.ClearReferencedStandardSettings();
            CalibrationTaskContext.Save();
            string standard = StandardComboBox.SelectedIndex == 1 ? "JJF 1376-2012" : "JJF 1101-2019";
            LocalTraceService.Default.TryWriteOperation(
                "保存校准任务",
                "成功",
                "当前任务",
                $"{standard}；设定温度 {setTemperature:0.###} ℃；温度 {temperatureCount} 点；湿度 {humidityCount} 点；正式样本 {plannedCount} 组",
                string.Empty,
                out _);
            TaskStatusTextBlock.Text = "任务已保存，标准器资料和规范规则已固化";
            if (!standardSettingsCaptured)
                TaskStatusTextBlock.Text = "任务已保存，标准器资料暂未完整；正式校准前请到系统设置补充。";
            TaskStatusTextBlock.Foreground = standardSettingsCaptured ? Brushes.DarkGreen : Brushes.DarkOrange;
            if (Application.Current.MainWindow is MainWindow mainWindow) mainWindow.ShowRealTimeMeasurementPage();
        }

        /// <summary>把已经通过校验的页面值写入任务上下文并持久化，不在本方法重复做输入判断。</summary>
        private void SaveTask(CalibrationStandardRule rule, bool includesHumidity, double setTemperature, double? setHumidity,
            int temperatureCount, int humidityCount, int temperatureCenter, int humidityCenter, int plannedCount,
            int samplingInterval, int stableWait, double dutTemperatureResolution, double dutHumidityResolution,
            double ambientTemperature, double ambientHumidity, double? ambientPressure)
        {
            CalibrationTaskContext.StandardIndex = StandardComboBox.SelectedIndex;
            CalibrationTaskContext.DeviceTypeIndex = StandardComboBox.SelectedIndex;
            CalibrationTaskContext.VolumeIndex = VolumeComboBox.SelectedIndex;
            CalibrationTaskContext.CalibrationTypeIndex = StandardComboBox.SelectedIndex == 1 ? 0 : CalibrationTypeComboBox.SelectedIndex;
            CalibrationTaskContext.SensorTypeIndex = SensorTypeComboBox.SelectedIndex;
            CalibrationTaskContext.SensorTypeCode = TemperatureSensorCatalog.GetCode(SensorTypeComboBox.SelectedIndex);
            CalibrationTaskContext.PointSelectionIndex = PointSelectionComboBox.SelectedIndex;
            CalibrationTaskContext.PointLayoutModeIndex = PointLayoutModeComboBox.SelectedIndex;
            CalibrationTaskContext.SamplingPlanModeIndex = SamplingPlanModeComboBox.SelectedIndex;
            CalibrationTaskContext.LoadConditionIndex = LoadConditionComboBox.SelectedIndex;
            CalibrationTaskContext.StabilityBasisIndex = StandardComboBox.SelectedIndex == 1 ? 0 : StabilityBasisComboBox.SelectedIndex;
            CalibrationTaskContext.AppearanceCheckIndex = AppearanceCheckComboBox.SelectedIndex;
            CalibrationTaskContext.TemperaturePointCount = temperatureCount;
            CalibrationTaskContext.HumidityPointCount = includesHumidity ? humidityCount : 0;
            CalibrationTaskContext.TemperatureCenterPoint = temperatureCenter;
            CalibrationTaskContext.HumidityCenterPoint = includesHumidity ? humidityCenter : 0;
            CalibrationTaskContext.TemperatureChannelMapping = MeasurementChannelMappingService.Normalize(
                _temperatureChannelMapping, temperatureCount, InspectionInstrumentProtocol.PhysicalTemperatureChannelCount);
            CalibrationTaskContext.HumidityChannelMapping = MeasurementChannelMappingService.Normalize(
                _humidityChannelMapping, includesHumidity ? humidityCount : 0, InspectionInstrumentProtocol.PhysicalHumidityChannelCount);
            CalibrationTaskContext.PlannedCount = plannedCount;
            CalibrationTaskContext.SamplingIntervalSeconds = samplingInterval;
            CalibrationTaskContext.StableWaitMinutes = stableWait;
            CalibrationTaskContext.SetTemperature = setTemperature;
            CalibrationTaskContext.SetHumidity = setHumidity;
            CalibrationTaskContext.DutTemperatureResolution = dutTemperatureResolution;
            CalibrationTaskContext.DutHumidityResolution = dutHumidityResolution;
            CalibrationTaskContext.AmbientTemperature = ambientTemperature;
            CalibrationTaskContext.AmbientHumidity = ambientHumidity;
            CalibrationTaskContext.AmbientPressure = ambientPressure;
            CalibrationTaskContext.FurnaceChamberLengthMm = ParseOptionalPositiveDouble(FurnaceChamberLengthTextBox.Text);
            CalibrationTaskContext.FurnaceChamberWidthMm = ParseOptionalPositiveDouble(FurnaceChamberWidthTextBox.Text);
            CalibrationTaskContext.FurnaceChamberHeightMm = ParseOptionalPositiveDouble(FurnaceChamberHeightTextBox.Text);
            CalibrationTaskContext.WorkZoneLengthMm = ParseOptionalPositiveDouble(WorkZoneLengthTextBox.Text);
            CalibrationTaskContext.WorkZoneWidthMm = ParseOptionalPositiveDouble(WorkZoneWidthTextBox.Text);
            CalibrationTaskContext.WorkZoneHeightMm = ParseOptionalPositiveDouble(WorkZoneHeightTextBox.Text);
            CalibrationTaskContext.CustomerName = CustomerNameTextBox.Text.Trim();
            CalibrationTaskContext.CustomerAddress = CustomerAddressTextBox.Text.Trim();
            CalibrationTaskContext.EquipmentName = EquipmentNameTextBox.Text.Trim();
            CalibrationTaskContext.Manufacturer = ManufacturerTextBox.Text.Trim();
            CalibrationTaskContext.ModelSpecification = ModelSpecificationTextBox.Text.Trim();
            CalibrationTaskContext.EquipmentSerialNumber = EquipmentSerialNumberTextBox.Text.Trim();
            CalibrationTaskContext.MeasurementRange = MeasurementRangeTextBox.Text.Trim();
            CalibrationTaskContext.CalibrationLocation = CalibrationLocationTextBox.Text.Trim();
            CalibrationTaskContext.LoadDescription = LoadDescriptionTextBox.Text.Trim();
            CalibrationTaskContext.PointLayoutDescription = string.IsNullOrWhiteSpace(PointLayoutDescriptionTextBox.Text) ? rule.PointLayoutText : PointLayoutDescriptionTextBox.Text.Trim();
            CalibrationTaskContext.DeviationDescription = DeviationDescriptionTextBox.Text.Trim();
            CalibrationTaskContext.Calibrator = CalibratorTextBox.Text.Trim();
            CalibrationTaskContext.Verifier = VerifierTextBox.Text.Trim();
            CalibrationTaskContext.CalibrationDate = DateTime.Today;
            CalibrationTaskContext.EnvironmentInterferenceConfirmed = true;
            CalibrationTaskContext.IsConfigured = true;
            CalibrationTaskContext.HasCompletedCalibration = false;
            CalibrationTaskContext.Save();
        }

        /// <summary>显示单项输入警告，并在关闭弹窗后定位、聚焦和红框标记对应控件。</summary>
        private void ShowInputError(string message, Control control, string title = "输入检查") =>
            ShowInputErrors(message, title, new[] { control });

        /// <summary>
        /// 同时标记一组错误字段；弹窗关闭后把滚动区域定位到第一个可见字段。
        /// 字段内容发生变化时立即恢复原边框，避免用户修正后仍看到过期错误状态。
        /// </summary>
        private void ShowInputErrors(string message, string title, IEnumerable<Control> controls)
        {
            List<Control> invalidControls = controls
                .Where(control => control != null && control.IsVisible)
                .Distinct()
                .ToList();
            if (invalidControls.Count == 0)
                invalidControls = controls.Where(control => control != null).Distinct().ToList();

            foreach (Control control in invalidControls)
                MarkInputError(control);

            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
            Control? first = invalidControls.FirstOrDefault();
            if (first == null) return;

            TaskScrollViewer.UpdateLayout();
            first.BringIntoView(new Rect(0, 0, Math.Max(1, first.ActualWidth), Math.Max(1, first.ActualHeight)));
            TaskScrollViewer.UpdateLayout();
            first.Focus();
            if (first is TextBox textBox)
                textBox.SelectAll();
            else if (first is ComboBox comboBox)
                comboBox.IsDropDownOpen = false;
        }

        /// <summary>保存控件原视觉状态并应用醒目的错误边框。</summary>
        private void MarkInputError(Control control)
        {
            if (!_invalidInputStates.ContainsKey(control))
            {
                _invalidInputStates.Add(control, new InputVisualState(control.BorderBrush, control.BorderThickness));
                if (control is TextBox textBox)
                    textBox.TextChanged += InvalidTextBox_TextChanged;
                else if (control is ComboBox comboBox)
                    comboBox.SelectionChanged += InvalidComboBox_SelectionChanged;
            }

            control.BorderBrush = new SolidColorBrush(Color.FromRgb(220, 38, 38));
            control.BorderThickness = new Thickness(2);
        }

        private void InvalidTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is Control control) ClearInputError(control);
        }

        private void InvalidComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is Control control) ClearInputError(control);
        }

        /// <summary>字段被编辑后恢复进入错误状态前的边框，并解除临时事件订阅。</summary>
        private void ClearInputError(Control control)
        {
            if (!_invalidInputStates.Remove(control, out InputVisualState? state)) return;
            control.BorderBrush = state.BorderBrush;
            control.BorderThickness = state.BorderThickness;
            if (control is TextBox textBox)
                textBox.TextChanged -= InvalidTextBox_TextChanged;
            else if (control is ComboBox comboBox)
                comboBox.SelectionChanged -= InvalidComboBox_SelectionChanged;
        }

        /// <summary>每次重新提交前清理上一次校验留下的视觉状态，再按当前值重新标记。</summary>
        private void ClearAllInputErrors()
        {
            foreach (Control control in _invalidInputStates.Keys.ToList())
                ClearInputError(control);
        }

        private sealed record InputVisualState(Brush? BorderBrush, Thickness BorderThickness);

        /// <summary>把可空数显示为简洁文本，空值保持空白。</summary>
        private static string FormatOptional(double? value) => value?.ToString("0.###") ?? string.Empty;
        /// <summary>只显示正数；未填写的 0 显示为空白。</summary>
        private static string FormatPositive(double value) => value > 0 ? value.ToString("0.###") : string.Empty;
        /// <summary>读取可选正数，空白或非法输入返回空值。</summary>
        private static double? ParseOptionalPositiveDouble(string text) =>
            double.TryParse(text.Trim(), out double value) && double.IsFinite(value) && value > 0 ? value : null;

        /// <summary>从有限选项中恢复测点数；0 表示当前任务不使用该参数。</summary>
        private static void SelectPointCount(ComboBox comboBox, int value)
        {
            comboBox.SelectedItem = value > 0 ? value : null;
        }

        /// <summary>读取并校验测点数下拉框，避免自由文本产生空值、非数字或越界数据。</summary>
        private static bool TryGetPointCount(ComboBox comboBox, int min, int max, out int value)
        {
            value = comboBox.SelectedItem is int selected ? selected : 0;
            return value >= min && value <= max;
        }

        /// <summary>缩小测点数时同步收敛原中心点，避免留下超出新点数的无效序号。</summary>
        private static void ClampCenterPoint(TextBox centerPointTextBox, int pointCount)
        {
            if (int.TryParse(centerPointTextBox.Text.Trim(), out int centerPoint) && centerPoint > pointCount)
                centerPointTextBox.Text = pointCount.ToString();
        }

        /// <summary>
        /// 用长度×宽度×高度计算工作区体积并从 mm³ 换算为 m³；尺寸不完整时返回空值。
        /// </summary>
        private double? TryGetWorkZoneVolume(out bool hasAnyDimension)
        {
            hasAnyDimension = !string.IsNullOrWhiteSpace(WorkZoneLengthTextBox.Text) ||
                              !string.IsNullOrWhiteSpace(WorkZoneWidthTextBox.Text) ||
                              !string.IsNullOrWhiteSpace(WorkZoneHeightTextBox.Text);
            double? length = ParseOptionalPositiveDouble(WorkZoneLengthTextBox.Text);
            double? width = ParseOptionalPositiveDouble(WorkZoneWidthTextBox.Text);
            double? height = ParseOptionalPositiveDouble(WorkZoneHeightTextBox.Text);
            if (!length.HasValue || !width.HasValue || !height.HasValue) return null;
            return length.Value * width.Value * height.Value / 1_000_000_000d;
        }

        /// <summary>读取位于闭区间内的整数。</summary>
        private static bool TryParseInt(string text, int min, int max, out int value) =>
            int.TryParse(text.Trim(), out value) && value >= min && value <= max;
        /// <summary>读取位于闭区间内的有限浮点数。</summary>
        private static bool TryParseDouble(string text, double min, double max, out double value) =>
            double.TryParse(text.Trim(), out value) && double.IsFinite(value) && value >= min && value <= max;

    }
}
