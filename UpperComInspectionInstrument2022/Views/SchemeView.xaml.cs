using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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

        /// <summary>初始化下拉选项，恢复上次任务，并应用当前规范规则。</summary>
        public SchemeView()
        {
            InitializeComponent();
            _loading = true;
            SensorTypeComboBox.ItemsSource = TemperatureSensorCatalog.DisplayNames;
            StandardComboBox.SelectedIndex = Math.Clamp(CalibrationTaskContext.StandardIndex, 0, 1);
            ConfigureStandardChoices(StandardComboBox.SelectedIndex, false);
            VolumeComboBox.SelectedIndex = CalibrationTaskContext.IsConfigured && CalibrationTaskContext.VolumeIndex is >= 0 and <= 1
                ? CalibrationTaskContext.VolumeIndex
                : -1;
            CalibrationTypeComboBox.SelectedIndex = StandardComboBox.SelectedIndex == 1
                ? 0
                : Math.Clamp(CalibrationTaskContext.CalibrationTypeIndex, 0, 1);
            PointSelectionComboBox.SelectedIndex = Math.Clamp(CalibrationTaskContext.PointSelectionIndex, 0, 2);
            PointLayoutModeComboBox.SelectedIndex = Math.Clamp(
                CalibrationTaskContext.PointLayoutModeIndex, 0, Math.Max(0, PointLayoutModeComboBox.Items.Count - 1));
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
            LoadTaskValues();
            _loading = false;
            ApplyRule(PointLayoutModeComboBox.SelectedIndex == 0);
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
            CalibrationDatePicker.SelectedDate = CalibrationTaskContext.CalibrationDate;
            CalibratorTextBox.Text = CalibrationTaskContext.Calibrator;
            VerifierTextBox.Text = CalibrationTaskContext.Verifier;
            WorkZoneLengthTextBox.Text = FormatOptional(CalibrationTaskContext.WorkZoneLengthMm);
            WorkZoneWidthTextBox.Text = FormatOptional(CalibrationTaskContext.WorkZoneWidthMm);
            WorkZoneHeightTextBox.Text = FormatOptional(CalibrationTaskContext.WorkZoneHeightMm);
            SetTemperatureTextBox.Text = CalibrationTaskContext.SetTemperature?.ToString("0.###") ?? string.Empty;
            SetHumidityTextBox.Text = CalibrationTaskContext.SetHumidity?.ToString("0.###") ?? string.Empty;
            TemperaturePointCountTextBox.Text = CalibrationTaskContext.TemperaturePointCount.ToString();
            HumidityPointCountTextBox.Text = CalibrationTaskContext.HumidityPointCount.ToString();
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

        /// <summary>校准点来源变化后只更新方案说明，不覆盖用户已经填写的执行参数。</summary>
        private void PointSelectionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_loading) ApplyRule(false);
        }

        /// <summary>关键输入变化时同步刷新页面底部的任务联动摘要。</summary>
        private void LinkageInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_loading && TaskLinkageSummaryTextBlock != null) UpdateTaskLinkageSummary();
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
            MeasurementRangeLabel.Text = PointSelectionComboBox.SelectedIndex == 1
                ? "使用/测量范围 *"
                : "使用/测量范围（可选）";
            WorkZoneDimensionLabel.Text = !isJjf1101
                ? "测温区尺寸 (mm) *"
                : PointLayoutModeComboBox.SelectedIndex == 2
                    ? "工作区尺寸 (mm) *"
                    : "工作区尺寸 (mm)（可选）";
            ViewLayoutFigureButton.Visibility = isJjf1101 ? Visibility.Collapsed : Visibility.Visible;
            ViewLayoutFigureButton.Content = PointLayoutModeComboBox.SelectedIndex switch
            {
                0 => "查看图 1",
                1 => "查看图 2",
                _ => "查看图 1 / 图 2"
            };
            ApplyStandardVisibility(includesHumidity, isJjf1101);

            if (VolumeComboBox.SelectedIndex < 0)
            {
                CalibrationStandardRule pendingRule = CalibrationStandardRuleService.GetRule(
                    StandardComboBox.SelectedIndex, 0, includesHumidity);
                if (applyDefaults)
                {
                    TemperaturePointCountTextBox.Text = string.Empty;
                    HumidityPointCountTextBox.Text = includesHumidity ? string.Empty : "0";
                    TemperatureCenterPointTextBox.Text = string.Empty;
                    HumidityCenterPointTextBox.Text = includesHumidity ? string.Empty : "0";
                    PlannedCountTextBox.Text = pendingRule.SampleCount.ToString();
                    SamplingIntervalTextBox.Text = pendingRule.SampleIntervalSeconds.ToString();
                    StableWaitTextBox.Text = pendingRule.StableWaitMinutes.ToString();
                    PointLayoutDescriptionTextBox.Text = string.Empty;
                }
                RuleScopeTextBlock.Text = $"{pendingRule.ScopeText}\n{pendingRule.EnvironmentRuleText}";
                RulePlanTextBlock.Text = $"校准点方案：{CalibrationStandardRuleService.GetCalibrationPointRuleText(pendingRule, PointSelectionComboBox.SelectedIndex)}\n请先选择实际容积，软件再生成空间测点数量、中心点和布点说明。";
                RuleStabilityTextBlock.Text = $"{pendingRule.StabilityRuleText}\n输出：{pendingRule.ResultItemsText}";
                TemperaturePointCountTextBox.IsReadOnly = true;
                HumidityPointCountTextBox.IsReadOnly = true;
                TemperatureCenterPointTextBox.IsReadOnly = true;
                HumidityCenterPointTextBox.IsReadOnly = true;
                PointLayoutDescriptionTextBox.IsReadOnly = true;
                PlannedCountTextBox.IsReadOnly = !isJjf1101;
                SamplingIntervalTextBox.IsReadOnly = !isJjf1101;
                UpdateStabilityControls();
                UpdateTaskLinkageSummary();
                UpdateReferencedSettings();
                return;
            }

            CalibrationStandardRule rule = CalibrationStandardRuleService.GetRule(StandardComboBox.SelectedIndex, VolumeComboBox.SelectedIndex, includesHumidity);
            string selectedLayoutText = CalibrationStandardRuleService.GetPointLayoutText(rule, PointLayoutModeComboBox.SelectedIndex);

            if (applyDefaults)
            {
                TemperaturePointCountTextBox.Text = rule.TemperaturePointCount.ToString();
                HumidityPointCountTextBox.Text = rule.HumidityPointCount.ToString();
                TemperatureCenterPointTextBox.Text = rule.TemperatureCenterPoint.ToString();
                HumidityCenterPointTextBox.Text = Math.Max(1, rule.HumidityCenterPoint).ToString();
                PlannedCountTextBox.Text = rule.SampleCount.ToString();
                SamplingIntervalTextBox.Text = rule.SampleIntervalSeconds.ToString();
                StableWaitTextBox.Text = rule.StableWaitMinutes.ToString();
            }
            if (applyDefaults || !rule.SupportsCustomPointLayout)
                PointLayoutDescriptionTextBox.Text = selectedLayoutText;

            RuleScopeTextBlock.Text = $"{rule.ScopeText}\n{rule.EnvironmentRuleText}";
            RulePlanTextBlock.Text = $"校准点方案：{CalibrationStandardRuleService.GetCalibrationPointRuleText(rule, PointSelectionComboBox.SelectedIndex)}\n空间布点：{selectedLayoutText}\n{rule.SamplingRuleText}";
            RuleStabilityTextBlock.Text = $"{rule.StabilityRuleText}\n输出：{rule.ResultItemsText}";

            bool customPointInput = CalibrationStandardRuleService.AllowsCustomPointInput(
                rule, PointLayoutModeComboBox.SelectedIndex);
            TemperaturePointCountTextBox.IsReadOnly = !customPointInput;
            HumidityPointCountTextBox.IsReadOnly = !customPointInput;
            TemperatureCenterPointTextBox.IsReadOnly = !customPointInput;
            // O 是规范中的空间位置；它映射到巡检仪哪个湿度通道取决于现场接线，始终允许确认/修改。
            HumidityCenterPointTextBox.IsReadOnly = false;
            PointLayoutDescriptionTextBox.IsReadOnly = !customPointInput;
            PlannedCountTextBox.IsReadOnly = !isJjf1101;
            SamplingIntervalTextBox.IsReadOnly = !isJjf1101;
            LoadDescriptionTextBox.IsEnabled = LoadConditionComboBox.SelectedIndex == 1;
            UpdateStabilityControls();
            UpdateTaskLinkageSummary();
            UpdateReferencedSettings();
        }

        /// <summary>按当前箱式炉布点模式打开规范图 1 或图 2。</summary>
        private void ViewLayoutFigureButton_Click(object sender, RoutedEventArgs e)
        {
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
            HumidityPointCountTextBox.Visibility = humidityVisibility;
            HumidityCenterPointLabel.Visibility = humidityVisibility;
            HumidityCenterPointTextBox.Visibility = humidityVisibility;
            DutHumidityResolutionLabel.Visibility = humidityVisibility;
            DutHumidityResolutionTextBox.Visibility = humidityVisibility;
            AmbientPressureLabel.Visibility = isJjf1101 ? Visibility.Visible : Visibility.Collapsed;
            AmbientPressureTextBox.Visibility = isJjf1101 ? Visibility.Visible : Visibility.Collapsed;
            AppearanceCheckLabel.Visibility = isJjf1101 ? Visibility.Collapsed : Visibility.Visible;
            AppearanceCheckComboBox.Visibility = isJjf1101 ? Visibility.Collapsed : Visibility.Visible;
        }

        /// <summary>
        /// 生成面向操作人员的联动摘要，明确保存后工作台将使用的工况、点数和中心点。
        /// </summary>
        private void UpdateTaskLinkageSummary()
        {
            if (TaskLinkageSummaryTextBlock == null || StandardComboBox.SelectedIndex < 0 ||
                CalibrationTypeComboBox.SelectedIndex < 0) return;

            bool includesHumidity = StandardComboBox.SelectedIndex == 0 && CalibrationTypeComboBox.SelectedIndex == 1;
            if (VolumeComboBox.SelectedIndex < 0)
            {
                CalibrationStandardRule pendingRule = CalibrationStandardRuleService.GetRule(
                    StandardComboBox.SelectedIndex, 0, includesHumidity);
                TaskLinkageSummaryTextBlock.Text =
                    $"{pendingRule.Code} → {GetSelectedText(PointSelectionComboBox, "校准点方案未选择")}\n" +
                    "请选择实际容积分类；选择后才生成空间测点数、中心点和工作台矩阵列。";
                return;
            }
            CalibrationStandardRule rule = CalibrationStandardRuleService.GetRule(
                StandardComboBox.SelectedIndex, VolumeComboBox.SelectedIndex, includesHumidity);
            string calibrationType = StandardComboBox.SelectedIndex == 1 ? "炉温参数" : includesHumidity ? "温湿度参数" : "温度参数";
            string setPoint = string.IsNullOrWhiteSpace(SetTemperatureTextBox.Text)
                ? "当前温度未填写"
                : $"当前 {SetTemperatureTextBox.Text.Trim()} ℃";
            if (includesHumidity)
                setPoint += string.IsNullOrWhiteSpace(SetHumidityTextBox.Text) ? " / 湿度未填写" : $" / {SetHumidityTextBox.Text.Trim()} %RH";
            string temperaturePoints = string.IsNullOrWhiteSpace(TemperaturePointCountTextBox.Text)
                ? "温度点数未生成"
                : $"温度 {TemperaturePointCountTextBox.Text.Trim()} 点（中心 {TemperatureCenterPointTextBox.Text.Trim()}）";
            string humidityPoints = includesHumidity
                ? $" + 湿度 {HumidityPointCountTextBox.Text.Trim()} 点（O→CH{HumidityCenterPointTextBox.Text.Trim()}）"
                : string.Empty;
            string pointSource = GetSelectedText(PointSelectionComboBox, "校准点方案未选择");
            string layoutMode = GetSelectedText(PointLayoutModeComboBox, "布点方式未选择");

            TaskLinkageSummaryTextBlock.Text =
                $"{rule.Code} → {calibrationType} → {pointSource} → {setPoint}\n" +
                $"{rule.VolumeOptionText} → {layoutMode} → {temperaturePoints}{humidityPoints}\n" +
                "保存任务后，校准工作台将按上述空间测点数生成实时数据矩阵。";
        }

        /// <summary>从普通字符串项或 ComboBoxItem 中读取显示文本。</summary>
        private static string GetSelectedText(ComboBox comboBox, string fallback)
        {
            return comboBox.SelectedItem switch
            {
                ComboBoxItem item => item.Content?.ToString() ?? fallback,
                string text => text,
                _ => fallback
            };
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
            StandardCapabilityTextBlock.Text = isFurnace
                ? $"温度 {SystemSettingsContext.TemperatureRange} / {SystemSettingsContext.TemperatureResolution:0.###} ℃ / {SystemSettingsContext.MeasuringInstrumentClass:0.###} 级；热电偶 {SystemSettingsContext.ThermocoupleGrade}；U={SystemSettingsContext.TemperatureUncertainty:0.###}, k={SystemSettingsContext.TemperatureCoverage:0.###}"
                : includesHumidity
                    ? $"温度 {SystemSettingsContext.TemperatureRange} / {SystemSettingsContext.TemperatureResolution:0.###} ℃ / U={SystemSettingsContext.TemperatureUncertainty:0.###}, k={SystemSettingsContext.TemperatureCoverage:0.###}；湿度 {SystemSettingsContext.HumidityRange} / {SystemSettingsContext.HumidityResolution:0.###} %RH / U={SystemSettingsContext.HumidityUncertainty:0.###}, k={SystemSettingsContext.HumidityCoverage:0.###}"
                    : $"温度 {SystemSettingsContext.TemperatureRange} / {SystemSettingsContext.TemperatureResolution:0.###} ℃ / U={SystemSettingsContext.TemperatureUncertainty:0.###}, k={SystemSettingsContext.TemperatureCoverage:0.###}";
        }

        /// <summary>跳转到系统设置维护标准器和证书资料。</summary>
        private void OpenSystemSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            if (Application.Current.MainWindow is MainWindow mainWindow) mainWindow.ShowSettingsPage();
        }

        /// <summary>
        /// 按从基础选择到现场条件的顺序校验任务；全部通过后固化标准器快照、保存任务并进入工作台。
        /// </summary>
        private void StartCalibrationButton_Click(object sender, RoutedEventArgs e)
        {
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

            if (PointSelectionComboBox.SelectedIndex == 1 &&
                !ValidateRangeBasedCalibrationPoint(setTemperature, setHumidity, includesHumidity))
                return;

            if (SensorTypeComboBox.SelectedIndex < 0)
            {
                ShowInputError("请选择本次实际接入的温度传感器类型。", SensorTypeComboBox);
                return;
            }

            if (!TryParseInt(TemperaturePointCountTextBox.Text, 1, 50, out int temperatureCount) ||
                !TryParseInt(HumidityPointCountTextBox.Text, 0, 10, out int humidityCount) ||
                !TryParseInt(TemperatureCenterPointTextBox.Text, 1, temperatureCount, out int temperatureCenter) ||
                (includesHumidity && !TryParseInt(HumidityCenterPointTextBox.Text, 1, humidityCount, out _)) ||
                !TryParseInt(PlannedCountTextBox.Text, 1, 10000, out int plannedCount) ||
                !TryParseInt(SamplingIntervalTextBox.Text, 1, 86400, out int samplingInterval) ||
                !TryParseDouble(DutTemperatureResolutionTextBox.Text, 0.000001, 1000, out double dutTemperatureResolution) ||
                (includesHumidity && !TryParseDouble(DutHumidityResolutionTextBox.Text, 0.000001, 100, out _)))
            {
                MessageBox.Show("请检查测点数、中心点、被校设备分辨力和正式采样计划。", "任务参数不完整", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            int humidityCenter = includesHumidity ? int.Parse(HumidityCenterPointTextBox.Text.Trim()) : 0;
            double dutHumidityResolution = includesHumidity ? double.Parse(DutHumidityResolutionTextBox.Text.Trim()) : 0;

            if (!TryParseDouble(AmbientTemperatureTextBox.Text, rule.MinimumAmbientTemperature, rule.MaximumAmbientTemperature, out double ambientTemperature) ||
                !TryParseDouble(AmbientHumidityTextBox.Text, 0, rule.MaximumAmbientHumidity, out double ambientHumidity))
            {
                MessageBox.Show($"现场环境必须满足：温度 {rule.MinimumAmbientTemperature:0} ℃～{rule.MaximumAmbientTemperature:0} ℃，湿度不大于 {rule.MaximumAmbientHumidity:0} %RH。", "环境条件不满足规范", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                MessageBox.Show("当前布点模式使用规范自动生成的测点数量；如需修改，请选择带“点数可自定义”的布点方式，并填写布点说明。", "布点不符合选择", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!customPointInput && temperatureCenter != rule.TemperatureCenterPoint)
            {
                MessageBox.Show("规范默认布点的温度中心/监控点不能改变；如需调整空间位置，请选择对应的调整方式并填写说明。", "中心点不符合选择", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            if (PointSelectionComboBox.SelectedIndex == 2 && string.IsNullOrWhiteSpace(DeviationDescriptionTextBox.Text))
            {
                ShowInputError("客户指定校准点必须在偏离/自定义说明中记录客户要求。", DeviationDescriptionTextBox);
                return;
            }
            if (LoadConditionComboBox.SelectedIndex == 1 && string.IsNullOrWhiteSpace(LoadDescriptionTextBox.Text))
            {
                ShowInputError("负载校准必须说明负载情况。", LoadDescriptionTextBox);
                return;
            }
            if (StandardComboBox.SelectedIndex == 1 && (plannedCount < 20 || samplingInterval != 180))
            {
                MessageBox.Show("JJF 1376 要求每隔 3 min 记录一次且至少 20 次。", "采样计划不符合规范", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            if (!CalibrationTaskContext.TrySnapshotCurrentStandardSettings(
                    StandardComboBox.SelectedIndex, includesHumidity, out string standardSettingsError))
            {
                MessageBox.Show($"{standardSettingsError}\n\n请先维护标准器资料，再保存校准任务。", "标准器资料不完整", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SaveTask(rule, includesHumidity, setTemperature, setHumidity, temperatureCount, humidityCount, temperatureCenter,
                humidityCenter, plannedCount, samplingInterval, stableWait, dutTemperatureResolution, dutHumidityResolution,
                ambientTemperature, ambientHumidity, ambientPressure);
            TaskStatusTextBlock.Text = "任务已保存，标准器资料和规范规则已固化";
            TaskStatusTextBlock.Foreground = Brushes.DarkGreen;
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
            CalibrationTaskContext.LoadConditionIndex = LoadConditionComboBox.SelectedIndex;
            CalibrationTaskContext.StabilityBasisIndex = StandardComboBox.SelectedIndex == 1 ? 0 : StabilityBasisComboBox.SelectedIndex;
            CalibrationTaskContext.AppearanceCheckIndex = AppearanceCheckComboBox.SelectedIndex;
            CalibrationTaskContext.TemperaturePointCount = temperatureCount;
            CalibrationTaskContext.HumidityPointCount = includesHumidity ? humidityCount : 0;
            CalibrationTaskContext.TemperatureCenterPoint = temperatureCenter;
            CalibrationTaskContext.HumidityCenterPoint = includesHumidity ? humidityCenter : 0;
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
            CalibrationTaskContext.CalibrationDate = CalibrationDatePicker.SelectedDate ?? DateTime.Today;
            CalibrationTaskContext.EnvironmentInterferenceConfirmed = true;
            CalibrationTaskContext.IsConfigured = true;
            CalibrationTaskContext.HasCompletedCalibration = false;
            CalibrationTaskContext.Save();
        }

        /// <summary>显示统一输入警告，并把焦点移到需要修正的控件。</summary>
        private static void ShowInputError(string message, Control control)
        {
            MessageBox.Show(message, "输入检查", MessageBoxButton.OK, MessageBoxImage.Warning);
            control.Focus();
        }

        /// <summary>把可空数显示为简洁文本，空值保持空白。</summary>
        private static string FormatOptional(double? value) => value?.ToString("0.###") ?? string.Empty;
        /// <summary>只显示正数；未填写的 0 显示为空白。</summary>
        private static string FormatPositive(double value) => value > 0 ? value.ToString("0.###") : string.Empty;
        /// <summary>读取可选正数，空白或非法输入返回空值。</summary>
        private static double? ParseOptionalPositiveDouble(string text) =>
            double.TryParse(text.Trim(), out double value) && double.IsFinite(value) && value > 0 ? value : null;

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

        /// <summary>
        /// 当用户选择“范围下限/上限/中间点”方案时，检查当前工况是否与填写的使用范围相匹配。
        /// </summary>
        private bool ValidateRangeBasedCalibrationPoint(double setTemperature, double? setHumidity, bool includesHumidity)
        {
            List<double> bounds = ExtractRangeNumbers(MeasurementRangeTextBox.Text);
            int requiredNumberCount = includesHumidity ? 4 : 2;
            if (bounds.Count < requiredNumberCount)
            {
                string example = includesHumidity ? "-40～150 ℃；20～95 %RH" : "300～1200 ℃";
                ShowInputError($"当前校准点选择了使用范围联动，请按“下限～上限”填写测量范围，例如 {example}。", MeasurementRangeTextBox);
                return false;
            }

            bool allowMiddle = StandardComboBox.SelectedIndex == CalibrationStandardRuleService.Jjf1101Index;
            if (!MatchesRangePoint(setTemperature, bounds[0], bounds[1], allowMiddle))
            {
                double lower = Math.Min(bounds[0], bounds[1]);
                double upper = Math.Max(bounds[0], bounds[1]);
                string allowed = allowMiddle
                    ? $"{lower:0.###}、{(lower + upper) / 2:0.###} 或 {upper:0.###} ℃"
                    : $"{lower:0.###} 或 {upper:0.###} ℃";
                ShowInputError($"当前工况设定温度与所选校准点方案不一致，应为使用范围的{(allowMiddle ? "下限、中间点或上限" : "最低或最高工作温度")}：{allowed}。", SetTemperatureTextBox);
                return false;
            }

            if (includesHumidity && setHumidity.HasValue && !MatchesRangePoint(setHumidity.Value, bounds[2], bounds[3], true))
            {
                double lower = Math.Min(bounds[2], bounds[3]);
                double upper = Math.Max(bounds[2], bounds[3]);
                ShowInputError($"设定湿度应为使用范围下限、中间点或上限：{lower:0.###}、{(lower + upper) / 2:0.###} 或 {upper:0.###} %RH。", SetHumidityTextBox);
                return false;
            }
            return true;
        }

        /// <summary>从“下限～上限；下限～上限”一类自由文本中按出现顺序提取有限数。</summary>
        private static List<double> ExtractRangeNumbers(string text)
        {
            List<double> values = new List<double>();
            foreach (Match match in Regex.Matches(text ?? string.Empty, @"(?<!\d)[+-]?\d+(?:\.\d+)?"))
            {
                if (double.TryParse(match.Value, out double value) && double.IsFinite(value)) values.Add(value);
            }
            return values;
        }

        /// <summary>判断设定值是否等于范围下限、上限，或在允许时等于中间点。</summary>
        private static bool MatchesRangePoint(double value, double first, double second, bool allowMiddle)
        {
            double lower = Math.Min(first, second);
            double upper = Math.Max(first, second);
            double tolerance = Math.Max(0.000001, Math.Max(Math.Abs(lower), Math.Abs(upper)) * 0.000001);
            return Math.Abs(value - lower) <= tolerance || Math.Abs(value - upper) <= tolerance ||
                   (allowMiddle && Math.Abs(value - (lower + upper) / 2) <= tolerance);
        }
    }
}
