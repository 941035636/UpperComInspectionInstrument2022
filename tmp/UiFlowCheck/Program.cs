using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using UpperComInspectionInstrument2022.Communication;
using UpperComInspectionInstrument2022.Models;
using UpperComInspectionInstrument2022.Services;
using UpperComInspectionInstrument2022.Views;

namespace UiFlowCheck;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        _ = new Application();
        ResetTask();
        SchemeView taskPage = new();

        ComboBox standard = Find<ComboBox>(taskPage, "StandardComboBox");
        ComboBox calibrationType = Find<ComboBox>(taskPage, "CalibrationTypeComboBox");
        ComboBox volume = Find<ComboBox>(taskPage, "VolumeComboBox");
        ComboBox layout = Find<ComboBox>(taskPage, "PointLayoutModeComboBox");
        ComboBox samplingPlanMode = Find<ComboBox>(taskPage, "SamplingPlanModeComboBox");
        ComboBox temperatureCount = Find<ComboBox>(taskPage, "TemperaturePointCountComboBox");
        ComboBox humidityCount = Find<ComboBox>(taskPage, "HumidityPointCountComboBox");
        TextBox temperatureCenter = Find<TextBox>(taskPage, "TemperatureCenterPointTextBox");
        TextBox humidityCenter = Find<TextBox>(taskPage, "HumidityCenterPointTextBox");
        TextBox plannedCount = Find<TextBox>(taskPage, "PlannedCountTextBox");
        TextBox samplingInterval = Find<TextBox>(taskPage, "SamplingIntervalTextBox");
        TextBlock plannedCountLabel = Find<TextBlock>(taskPage, "PlannedCountLabel");
        TextBlock samplingIntervalLabel = Find<TextBlock>(taskPage, "SamplingIntervalLabel");
        Button figureButton = Find<Button>(taskPage, "ViewLayoutFigureButton");
        Button saveTaskButton = Find<Button>(taskPage, "SaveTaskButton");
        TextBlock channelMappingSummary = Find<TextBlock>(taskPage, "ChannelMappingSummaryTextBlock");
        Expander optionalArchive = Find<Expander>(taskPage, "OptionalArchiveExpander");
        ScrollViewer taskScrollViewer = Find<ScrollViewer>(taskPage, "TaskScrollViewer");

        Assert(!optionalArchive.IsExpanded, "optional device/customer archive should be collapsed by default");
        Assert(taskScrollViewer.VerticalScrollBarVisibility == ScrollBarVisibility.Auto,
            "task validation should have a named scroll host for error-field navigation");
        string taskPageCode = File.ReadAllText(Path.Combine("UpperComInspectionInstrument2022", "Views", "SchemeView.xaml.cs"));
        Assert(taskPageCode.Contains("ShowInputErrors(", StringComparison.Ordinal) &&
               taskPageCode.Contains("first.BringIntoView", StringComparison.Ordinal) &&
               taskPageCode.Contains("Color.FromRgb(220, 38, 38)", StringComparison.Ordinal) &&
               taskPageCode.Contains("ClearAllInputErrors();", StringComparison.Ordinal) &&
               taskPageCode.Contains("invalidTaskFields.Select(item => item.Control)", StringComparison.Ordinal),
            "incomplete task parameters should be marked together, scrolled into view and cleared after editing");
        Assert(taskPage.FindName("CalibrationDateTextBox") == null && taskPage.FindName("CalibrationDatePicker") == null,
            "task page should not expose the system-generated calibration date");
        Assert(saveTaskButton.Width == 260 && DockPanel.GetDock(saveTaskButton) == Dock.Right &&
               saveTaskButton.Parent is DockPanel { LastChildFill: false },
            "task footer should use a normal-width right-aligned primary action");
        Assert(standard.Parent is Grid standardGrid && standardGrid.ColumnDefinitions[1].MaxWidth == 380,
            "paired task selectors should share the same responsive maximum width");
        TextBox measurementRange = Find<TextBox>(taskPage, "MeasurementRangeTextBox");
        Assert(measurementRange.Parent is Grid rangeGrid && rangeGrid.ColumnDefinitions[1].MaxWidth == 420,
            "measurement range should use a bounded field width instead of stretching across the card");
        Assert(volume.SelectedIndex == -1 && temperatureCount.SelectedIndex == -1 && !temperatureCount.IsEnabled &&
               temperatureCount.Items.Count == InspectionInstrumentProtocol.PhysicalTemperatureChannelCount &&
               humidityCount.Items.Count == InspectionInstrumentProtocol.PhysicalHumidityChannelCount,
            "task page should require an explicit volume before deriving point layout");
        Assert(samplingPlanMode.SelectedIndex == 0 && plannedCount.IsReadOnly && samplingInterval.IsReadOnly,
            "new tasks should use a locked normative sampling plan");
        Assert(figureButton.Visibility == Visibility.Visible && Equals(figureButton.Content, "查看布点图"),
            "JJF1101 should expose its layout figures before volume selection");

        volume.SelectedIndex = 0;
        Assert(Equals(temperatureCount.SelectedItem, 9) && !temperatureCount.IsEnabled &&
               temperatureCenter.Text == "5" && plannedCount.Text == "16" && samplingInterval.Text == "120",
            "JJF1101 <=2m3 linkage");
        Assert(channelMappingSummary.Text.Contains("T1→CH1", StringComparison.Ordinal) &&
               channelMappingSummary.Text.Contains("校准工作台", StringComparison.Ordinal),
            "task page should show the default mapping and direct physical-interface editing to the workbench");
        Assert(Equals(figureButton.Content, "查看图 1"), "JJF1101 <=2m3 should link to figure 1");
        calibrationType.SelectedIndex = 1;
        Assert(humidityCount.Visibility == Visibility.Visible && Equals(humidityCount.SelectedItem, 3) &&
               !humidityCount.IsEnabled && humidityCenter.Text == "3",
            "JJF1101 humidity linkage and O-channel default");
        volume.SelectedIndex = 1;
        Assert(Equals(temperatureCount.SelectedItem, 15) && Equals(humidityCount.SelectedItem, 4) &&
               temperatureCenter.Text == "15" && humidityCenter.Text == "4",
            "JJF1101 >2m3 linkage");
        Assert(Equals(figureButton.Content, "查看图 2"), "JJF1101 >2m3 should link to figure 2");
        VerifyJjf1101LayoutFigureDialog();
        layout.SelectedIndex = 1;
        Assert(temperatureCount.IsEnabled && humidityCount.IsEnabled && !temperatureCenter.IsReadOnly,
            "JJF1101 actual-work-position mode exposes the user-requested point customization");
        temperatureCount.SelectedItem = 5;
        humidityCount.SelectedItem = 2;
        Assert(temperatureCenter.Text == "5" && humidityCenter.Text == "2" &&
               channelMappingSummary.Text.Contains("T5→CH5", StringComparison.Ordinal) &&
               !channelMappingSummary.Text.Contains("T6→", StringComparison.Ordinal),
            "custom point-count dropdowns clamp center points and refresh logical channel mappings");

        standard.SelectedIndex = 1;
        Assert(volume.SelectedIndex == -1 && humidityCount.Visibility == Visibility.Collapsed && figureButton.Visibility == Visibility.Visible &&
               plannedCount.Text == "20" && samplingInterval.Text == "180" && plannedCount.IsReadOnly && samplingInterval.IsReadOnly,
            "JJF1376 normative plan should expose locked 20-group and 180-second defaults");
        Assert(plannedCountLabel.Text.Contains("≥20") && samplingIntervalLabel.Text.Contains("(s)") &&
               Find<TextBlock>(taskPage, "SamplingPlanHintTextBlock").Text.Contains("3 min"),
            "JJF1376 task page should explain its normative sampling plan");
        samplingPlanMode.SelectedIndex = 1;
        Assert(!plannedCount.IsReadOnly && !samplingInterval.IsReadOnly,
            "custom sampling plan mode should unlock both execution parameters");
        samplingInterval.Text = "120";
        Assert(samplingInterval.Text == "120", "JJF1376 custom interval should remain editable");
        volume.SelectedIndex = 0;
        Assert(Equals(temperatureCount.SelectedItem, 5) && temperatureCenter.Text == "3" && !temperatureCount.IsEnabled,
            "JJF1376 <=0.15m3 five-point linkage");
        volume.SelectedIndex = 1;
        Assert(Equals(temperatureCount.SelectedItem, 9) && temperatureCenter.Text == "9", "JJF1376 >0.15m3 nine-point linkage");
        layout.SelectedIndex = 2;
        Assert(temperatureCount.IsEnabled && !temperatureCenter.IsReadOnly, "JJF1376 custom work-position mode is editable");
        Assert(Find<TextBlock>(taskPage, "StandardCapabilityTextBlock").Text.Contains("0.02") &&
               Find<TextBlock>(taskPage, "StandardCapabilityTextBlock").Text.Contains("廉金属不低于1级"),
            "furnace task page should reference the normative instrument class and thermocouple grade automatically");

        VerifySavedCustomSamplingPlanIsRestored();

        VerifyLoginLayoutAndSessionGate();
        VerifySystemSettingsControlProportions();
        VerifyWorkbenchNavigationKeepsPageInstance();
        VerifySignedResultPresentation();
        VerifyHistoryActionsRequireSelection();
        Console.WriteLine("PASS: login gate, WPF task/settings layout, persistent workbench, status semantics, safe history actions and result presentation assertions");
    }

    private static T Find<T>(FrameworkElement root, string name) where T : FrameworkElement =>
        root.FindName(name) as T ?? throw new InvalidOperationException($"WPF control not found: {name}");

    /// <summary>确认应用不再绕过登录，并检查登录/注册控件及密码输入类型。</summary>
    private static void VerifyLoginLayoutAndSessionGate()
    {
        string appXaml = File.ReadAllText(Path.Combine("UpperComInspectionInstrument2022", "App.xaml"));
        string appCode = File.ReadAllText(Path.Combine("UpperComInspectionInstrument2022", "App.xaml.cs"));
        if (appXaml.Contains("StartupUri", StringComparison.Ordinal) ||
            !appCode.Contains("ShowLoginWindow()", StringComparison.Ordinal))
            throw new InvalidOperationException("application startup should be gated by the login window");

        LoginWindow loginWindow = new();
        if (Find<TextBox>(loginWindow, "LoginUserNameTextBox") == null ||
            Find<PasswordBox>(loginWindow, "LoginPasswordBox") == null ||
            Find<PasswordBox>(loginWindow, "RegisterPasswordBox") == null ||
            Find<PasswordBox>(loginWindow, "RegisterConfirmPasswordBox") == null ||
            Find<Button>(loginWindow, "ForgotPasswordButton") == null ||
            Find<CheckBox>(loginWindow, "RememberPasswordCheckBox") == null ||
            Find<Button>(loginWindow, "RegisterButton") == null)
            throw new InvalidOperationException("login and re-registration should expose complete credential controls");
        if (loginWindow.FindName("ResetPasswordPanel") != null ||
            loginWindow.FindName("RecoveryCodePanel") != null)
            throw new InvalidOperationException("login should not retain recovery-code UI");

        Find<Button>(loginWindow, "ForgotPasswordButton").RaiseEvent(
            new RoutedEventArgs(Button.ClickEvent));
        if (Find<Grid>(loginWindow, "RegisterPanel").Visibility != Visibility.Visible ||
            Find<Grid>(loginWindow, "LoginPanel").Visibility != Visibility.Collapsed ||
            !Find<TextBlock>(loginWindow, "RegisterInfoTextBlock").Text.Contains("替换原账户") ||
            !Equals(Find<Button>(loginWindow, "RegisterButton").Content, "重新注册账户") ||
            Find<Button>(loginWindow, "BackToLoginButton").Visibility != Visibility.Visible)
            throw new InvalidOperationException("forgot-password action should switch directly to re-registration");
        Find<Button>(loginWindow, "BackToLoginButton").RaiseEvent(
            new RoutedEventArgs(Button.ClickEvent));
        if (Find<Grid>(loginWindow, "LoginPanel").Visibility != Visibility.Visible)
            throw new InvalidOperationException("re-registration should return to the login form");
    }

    /// <summary>确认 JJF 1101 两张规范布点图都能加载，并可按设备容积预选图 2。</summary>
    private static void VerifyJjf1101LayoutFigureDialog()
    {
        Jjf1101LayoutFigureWindow window = new(2);
        if (Find<TabControl>(window, "FigureTabControl").SelectedIndex != 1)
            throw new InvalidOperationException("JJF1101 layout dialog should preselect the figure matching equipment volume");
        if (Find<Image>(window, "Figure1Image").Source == null || Find<Image>(window, "Figure2Image").Source == null)
            throw new InvalidOperationException("JJF1101 layout dialog should load both embedded standard figures");
    }

    private static void ResetTask()
    {
        CalibrationTaskContext.IsConfigured = false;
        CalibrationTaskContext.HasCompletedCalibration = false;
        CalibrationTaskContext.StandardIndex = 0;
        CalibrationTaskContext.CalibrationTypeIndex = 0;
        CalibrationTaskContext.VolumeIndex = -1;
        CalibrationTaskContext.PointSelectionIndex = 0;
        CalibrationTaskContext.PointLayoutModeIndex = 0;
        CalibrationTaskContext.SamplingPlanModeIndex = 0;
        CalibrationTaskContext.TemperaturePointCount = 9;
        CalibrationTaskContext.HumidityPointCount = 0;
        CalibrationTaskContext.TemperatureCenterPoint = 5;
        CalibrationTaskContext.HumidityCenterPoint = 1;
        CalibrationTaskContext.PlannedCount = 16;
        CalibrationTaskContext.SamplingIntervalSeconds = 120;
        CalibrationTaskContext.SetTemperature = null;
        CalibrationTaskContext.SetHumidity = null;
        CalibrationTaskContext.TemperatureChannelMapping = new List<int>();
        CalibrationTaskContext.HumidityChannelMapping = new List<int>();
        CalibrationTaskContext.EnvironmentInterferenceConfirmed = false;
    }

    /// <summary>回归检查：重新创建任务配置页时，不得用空间布点默认值覆盖已保存的自定义采样计划。</summary>
    private static void VerifySavedCustomSamplingPlanIsRestored()
    {
        ResetTask();
        CalibrationTaskContext.IsConfigured = true;
        CalibrationTaskContext.VolumeIndex = 0;
        CalibrationTaskContext.PointLayoutModeIndex = 0;
        CalibrationTaskContext.SamplingPlanModeIndex = 1;
        CalibrationTaskContext.PlannedCount = 8;
        CalibrationTaskContext.SamplingIntervalSeconds = 12;

        SchemeView restoredPage = new();
        ComboBox samplingPlanMode = Find<ComboBox>(restoredPage, "SamplingPlanModeComboBox");
        TextBox plannedCount = Find<TextBox>(restoredPage, "PlannedCountTextBox");
        TextBox samplingInterval = Find<TextBox>(restoredPage, "SamplingIntervalTextBox");
        if (samplingPlanMode.SelectedIndex != 1 || plannedCount.Text != "8" || samplingInterval.Text != "12" ||
            plannedCount.IsReadOnly || samplingInterval.IsReadOnly)
            throw new InvalidOperationException("saved custom sampling plan must survive task-page reconstruction");
    }

    /// <summary>防止系统设置页的普通输入框和底部保存按钮再次随窗口无限拉伸。</summary>
    private static void VerifySystemSettingsControlProportions()
    {
        DeviceView settingsPage = new(0);
        TextBox standardName = Find<TextBox>(settingsPage, "StandardNameTextBox");
        if (standardName.MinHeight != 32 ||
            standardName.Parent is not Grid identityGrid ||
            identityGrid.ColumnDefinitions[1].MaxWidth != 380)
            throw new InvalidOperationException("system settings identity fields should share the bounded 32px form-control scale");

        TextBox temperatureUncertainty = Find<TextBox>(settingsPage, "TemperatureUncertaintyTextBox");
        if (temperatureUncertainty.Parent is not Grid uncertaintyGrid ||
            uncertaintyGrid.ColumnDefinitions[1].MaxWidth != 150)
            throw new InvalidOperationException("certificate numeric inputs should remain compact instead of stretching across the card");

        TextBox temperatureCorrections = Find<TextBox>(settingsPage, "TemperatureCorrectionsTextBox");
        if (temperatureCorrections.Parent is not Grid correctionsGrid ||
            correctionsGrid.ColumnDefinitions[1].MaxWidth != 820)
            throw new InvalidOperationException("channel correction input should be the only wide data field in its section");

        Button saveSettings = Find<Button>(settingsPage, "SaveSettingsButton");
        if (saveSettings.Width != 220 || DockPanel.GetDock(saveSettings) != Dock.Right ||
            saveSettings.Parent is not DockPanel { LastChildFill: false })
            throw new InvalidOperationException("system settings footer should use a normal-width right-aligned primary action");

        TextBox accuracySpecification = Find<TextBox>(settingsPage, "AccuracySpecificationTextBox");
        if (settingsPage.FindName("CapabilityStandardComboBox") != null ||
            settingsPage.FindName("Jjf1101RequirementPanel") != null ||
            settingsPage.FindName("Jjf1376RequirementPanel") != null ||
            Find<Grid>(settingsPage, "Jjf1101AccuracyPanel").Visibility != Visibility.Visible ||
            Find<Grid>(settingsPage, "Jjf1376InstrumentPanel").Visibility != Visibility.Collapsed ||
            Find<TextBox>(settingsPage, "HumidityResolutionTextBox").Visibility != Visibility.Visible ||
            !accuracySpecification.IsReadOnly ||
            accuracySpecification.Text != SystemSettingsContext.Jjf1101AccuracyRequirement)
            throw new InvalidOperationException("JJF1101 settings should bind the task standard, auto-fill read-only MPE and omit a second standard selector");

        DeviceView furnaceSettingsPage = new(1);
        TextBox instrumentClass = Find<TextBox>(furnaceSettingsPage, "MeasuringInstrumentClassTextBox");
        TextBox thermocoupleGrade = Find<TextBox>(furnaceSettingsPage, "ThermocoupleGradeTextBox");
        if (Find<Grid>(furnaceSettingsPage, "Jjf1101AccuracyPanel").Visibility != Visibility.Collapsed ||
            Find<Grid>(furnaceSettingsPage, "Jjf1376InstrumentPanel").Visibility != Visibility.Visible ||
            Find<TextBox>(furnaceSettingsPage, "HumidityResolutionTextBox").Visibility != Visibility.Collapsed ||
            Find<Border>(furnaceSettingsPage, "HumidityCertificatePanel").Visibility != Visibility.Collapsed ||
            Find<Grid>(furnaceSettingsPage, "TemperatureStabilityChangeRow").Visibility != Visibility.Collapsed ||
            !instrumentClass.IsReadOnly || instrumentClass.Text != "0.02" ||
            !thermocoupleGrade.IsReadOnly || thermocoupleGrade.Text != SystemSettingsContext.Jjf1376ThermocoupleGradeRequirement)
            throw new InvalidOperationException("JJF1376 settings should auto-fill fixed normative values and hide JJF1101-only fields");
    }

    private static void VerifySignedResultPresentation()
    {
        CalibrationTaskContext.StandardIndex = 0;
        CalibrationTaskContext.CalibrationTypeIndex = 0;
        CalibrationTaskContext.IsConfigured = true;
        CalibrationTaskContext.HasCompletedCalibration = true;
        CalibrationTaskContext.TemperaturePointCount = 2;
        CalibrationTaskContext.HumidityPointCount = 0;
        CalibrationTaskContext.PlannedCount = 2;
        CalibrationTaskContext.SetTemperature = 20;
        CalibrationTaskContext.ReferencedTemperatureResolution = 0.01;
        CalibrationTaskContext.ReferencedTemperatureUncertainty = 0.04;
        CalibrationTaskContext.ReferencedTemperatureCoverage = 2;
        CalibrationTaskContext.ReferencedTemperatureStabilityChange = 0.1;
        CalibrationRunContext.Begin();
        CalibrationRunContext.Add(Snapshot(18, 19), null, null);
        CalibrationRunContext.Add(Snapshot(17, 18), null, null);
        CalibrationTaskContext.HasCompletedCalibration = true;

        ResultView resultPage = new();
        string deviation = Find<TextBlock>(resultPage, "Metric1Value").Text;
        if (deviation.Contains("+-", StringComparison.Ordinal) || deviation != "上 -1.000 / 下 -3.000 ℃")
            throw new InvalidOperationException("negative deviation presentation is ambiguous: " + deviation);
        if (!Find<Button>(resultPage, "GenerateWordCertificateButton").IsEnabled)
            throw new InvalidOperationException("completed calibration should enable the Word certificate entry");
        Border statusPanel = Find<Border>(resultPage, "ResultStatusPanel");
        AssertBrush(statusPanel.Background, Color.FromRgb(240, 253, 244), "completed result should use success background");
        if (Find<Border>(resultPage, "Metric6Card").Visibility != Visibility.Collapsed)
            throw new InvalidOperationException("unused sixth metric should collapse instead of reserving an empty cell");
        string reportStatus = Find<TextBlock>(resultPage, "ReportFilesStatusTextBlock").Text;
        if (!reportStatus.Contains("原始记录", StringComparison.Ordinal) ||
            !reportStatus.Contains("校准报告", StringComparison.Ordinal) ||
            reportStatus.Contains("PDF", StringComparison.Ordinal))
            throw new InvalidOperationException("result page should expose only Excel record and Word report states: " + reportStatus);
    }

    private static void VerifyWorkbenchNavigationKeepsPageInstance()
    {
        UserSessionContext.SignIn("ui-test", "界面测试员");
        UpperComInspectionInstrument2022.MainWindow shell = new();
        if (Find<TextBlock>(shell, "CurrentUserNameTextBlock").Text != "界面测试员" ||
            Find<Button>(shell, "LogoutButton").Visibility != Visibility.Visible)
            throw new InvalidOperationException("main window should display the authenticated user and logout entry");
        DrainDispatcher();
        CalibrationTaskContext.IsConfigured = true;
        shell.ShowRealTimeMeasurementPage();
        DrainDispatcher();
        Frame frame = Find<Frame>(shell, "MainFrame");
        FrameworkElement workbench = frame.Content as FrameworkElement ??
            throw new InvalidOperationException("workbench navigation did not produce page content");
        DataGrid matrix = Find<DataGrid>(workbench, "MeasurementMatrixDataGrid");
        ComboBox temperaturePointCount = Find<ComboBox>(workbench, "TemperaturePointCountComboBox");
        ComboBox humidityPointCount = Find<ComboBox>(workbench, "HumidityPointCountComboBox");
        Button channelConfigurationButton = Find<Button>(workbench, "ChannelConfigurationButton");
        Button backToTaskButton = Find<Button>(workbench, "BackToTaskButton");
        Button editTaskButton = Find<Button>(workbench, "EditTaskButton");
        TextBlock batteryLevel = Find<TextBlock>(workbench, "BatteryLevelTextBlock");
        if (temperaturePointCount.Items.Count != InspectionInstrumentProtocol.PhysicalTemperatureChannelCount + 1 ||
            humidityPointCount.Items.Count != InspectionInstrumentProtocol.PhysicalHumidityChannelCount + 1 ||
            !Equals(temperaturePointCount.Items[temperaturePointCount.Items.Count - 1], InspectionInstrumentProtocol.PhysicalTemperatureChannelCount.ToString()) ||
            !Equals(humidityPointCount.Items[humidityPointCount.Items.Count - 1], InspectionInstrumentProtocol.PhysicalHumidityChannelCount.ToString()))
            throw new InvalidOperationException("workbench point selectors should expose only the current 24 temperature and 9 humidity interfaces");
        if (channelConfigurationButton.IsEnabled)
            throw new InvalidOperationException("device channel configuration must stay disabled before the serial port is connected");
        if (!batteryLevel.Text.Contains("剩余电量", StringComparison.Ordinal))
            throw new InvalidOperationException("workbench device card should expose the remaining battery status");
        if (!Equals(backToTaskButton.Content, "← 返回任务配置") ||
            !Equals(editTaskButton.Content, "修改任务配置") ||
            backToTaskButton.Width < 130 ||
            string.IsNullOrWhiteSpace(backToTaskButton.ToolTip?.ToString()) ||
            string.IsNullOrWhiteSpace(editTaskButton.ToolTip?.ToString()))
            throw new InvalidOperationException(
                "task configuration navigation should be prominent in both the page header and current-task card");
        object frozenColumns = matrix.ReadLocalValue(DataGrid.FrozenColumnCountProperty);
        if (frozenColumns is not int frozenColumnCount || frozenColumnCount != 2)
            throw new InvalidOperationException("measurement matrix should keep sequence and time visible while horizontally scrolling");
        Canvas trendChart = Find<Canvas>(workbench, "MeasurementChartCanvas");
        if (trendChart.Cursor != Cursors.Cross ||
            Find<TextBlock>(workbench, "TemperatureLegendTextBlock").Text != "中心温度" ||
            Find<TextBlock>(workbench, "HumidityLegendTextBlock").Text != "湿度 O点")
            throw new InvalidOperationException("trend chart should expose center-point legends and interactive reading cursor");
        string workbenchCode = File.ReadAllText(Path.Combine("UpperComInspectionInstrument2022", "Views", "RealTimeMeasurementPage.xaml.cs"));
        if (!workbenchCode.Contains("CreateAxisScale(temperatures", StringComparison.Ordinal) ||
            !workbenchCode.Contains("CreateAxisScale(humidities", StringComparison.Ordinal) ||
            !workbenchCode.Contains("DrawTimeAxis(_visibleTrendSamples)", StringComparison.Ordinal) ||
            !workbenchCode.Contains("segment = null", StringComparison.Ordinal))
            throw new InvalidOperationException("trend chart should retain independent unit axes, time ticks and invalid-data gaps");
        if (!workbenchCode.Contains("FormatMeasurementMatrixValue(channel)", StringComparison.Ordinal) ||
            !workbenchCode.Contains("湿度探头伴随温度（诊断，不参与校准）", StringComparison.Ordinal) ||
            !workbenchCode.Contains("ToString(\"F2\", CultureInfo.InvariantCulture)", StringComparison.Ordinal))
            throw new InvalidOperationException("negative humidity and probe-temperature diagnostics should remain visible to the operator");
        if (!workbenchCode.Contains("DisplaySequence = _viewModel.AcquisitionCount + 1", StringComparison.Ordinal) ||
            !workbenchCode.Contains("row[\"采集序号\"] = snapshot.DisplaySequence", StringComparison.Ordinal) ||
            !workbenchCode.Contains("CurrentSequenceTextBlock.Text = snapshot.DisplaySequence.ToString()", StringComparison.Ordinal) ||
            !workbenchCode.Contains("请先暂停实时测量，再清空数据", StringComparison.Ordinal) ||
            !workbenchCode.Contains("本次采集序号重置", StringComparison.Ordinal) ||
            !workbenchCode.Contains("已经写入本地的 CSV 文件不会删除", StringComparison.Ordinal) ||
            !workbenchCode.Contains("ClearDataButton.IsEnabled = false", StringComparison.Ordinal) ||
            !workbenchCode.Contains("ClearDataButton.IsEnabled = true", StringComparison.Ordinal))
            throw new InvalidOperationException(
                "clearing paused realtime data must reset only the user-facing sequence while preserving trace files");
        if (!workbenchCode.Contains("ChannelCorrectionService.ApplyForMeasurement", StringComparison.Ordinal) ||
            !workbenchCode.Contains("MeasurementChannelEnableService.Apply(data, _activeChannelEnabled)", StringComparison.Ordinal) ||
            !workbenchCode.Contains("_channelProfileService.TryLoad", StringComparison.Ordinal) ||
            !workbenchCode.Contains("DataStatus.Disabled) return \"关闭\"", StringComparison.Ordinal) ||
            !workbenchCode.Contains("MeasurementChannelMappingService.ApplyTaskMapping", StringComparison.Ordinal) ||
            !workbenchCode.Contains("MeasurementChannelMappingService.GetRequiredReadChannelCount", StringComparison.Ordinal) ||
            !workbenchCode.Contains("_runStateService.EnsureStarted(slaveAddress)", StringComparison.Ordinal) ||
            !workbenchCode.Contains("TryRefreshRemainingBattery(_activeSlaveAddress)", StringComparison.Ordinal) ||
            !workbenchCode.Contains("TryCalculateRange", StringComparison.Ordinal) ||
            !workbenchCode.Contains("数据不足（", StringComparison.Ordinal) ||
            workbenchCode.Contains("return double.PositiveInfinity", StringComparison.Ordinal) ||
            !workbenchCode.Contains("_deviceTransitionInProgress", StringComparison.Ordinal) ||
            !workbenchCode.Contains("CaptureExpectedDeviceError(() => _runStateService.EnsureStarted(slaveAddress))", StringComparison.Ordinal) ||
            !workbenchCode.Contains("在后台线程内部把可预期的设备通信异常转换为错误文本", StringComparison.Ordinal) ||
            !workbenchCode.Contains("SystemSettingsContext.TemperatureChannelCorrections", StringComparison.Ordinal) ||
            !workbenchCode.Contains("CalibrationTaskContext.ReferencedTemperatureCorrections", StringComparison.Ordinal) ||
            !workbenchCode.Contains("CalibrationTaskContext.CalibrationDate = DateTime.Today", StringComparison.Ordinal))
            throw new InvalidOperationException("workbench should serialize connect/start transitions, reject incomplete stability windows and apply staged corrections");
        int enableFilterIndex = workbenchCode.IndexOf("MeasurementChannelEnableService.Apply(data, _activeChannelEnabled)", StringComparison.Ordinal);
        int correctionIndex = workbenchCode.IndexOf("ChannelCorrectionService.ApplyForMeasurement", StringComparison.Ordinal);
        if (enableFilterIndex < 0 || correctionIndex < 0 || enableFilterIndex > correctionIndex)
            throw new InvalidOperationException("disabled physical channels must be rejected before corrections and logical point mapping");
        if (!workbenchCode.Contains("TryAlignRealtimeIntervalForFormalCalibration", StringComparison.Ordinal) ||
            !workbenchCode.Contains("RestoreRealtimeIntervalAfterFormalCalibration", StringComparison.Ordinal) ||
            !workbenchCode.Contains("AdvanceNextCalibrationSampleAt(snapshot.Timestamp", StringComparison.Ordinal))
            throw new InvalidOperationException("formal sampling should align device polling, restore the realtime interval and avoid cumulative schedule drift");
        if (!workbenchCode.Contains("启动前通道使能一致性检查暂时停用", StringComparison.Ordinal) ||
            !workbenchCode.Contains("硬件端修复并完成联调后", StringComparison.Ordinal) ||
            !workbenchCode.Contains("EnsureSavedChannelConfigurationConsistentAsync", StringComparison.Ordinal))
            throw new InvalidOperationException(
                "the channel-enable preflight must remain explicitly disabled while retaining the recoverable fallback implementation");
        int errorHandlerStart = workbenchCode.IndexOf("private void OnAcquisitionError", StringComparison.Ordinal);
        int errorHandlerEnd = workbenchCode.IndexOf("private void BackHomeButton_Click", errorHandlerStart, StringComparison.Ordinal);
        string errorHandlerCode = workbenchCode[errorHandlerStart..errorHandlerEnd];
        if (!errorHandlerCode.Contains("PersistentWarningFailureCount", StringComparison.Ordinal) ||
            !errorHandlerCode.Contains("串口保持打开", StringComparison.Ordinal) ||
            errorHandlerCode.Contains("_modbusClient.Close()", StringComparison.Ordinal) ||
            errorHandlerCode.Contains("TryMarkInterrupted", StringComparison.Ordinal) ||
            errorHandlerCode.Contains("TryEndSession", StringComparison.Ordinal))
            throw new InvalidOperationException("communication errors should keep the serial session alive and pause formal sampling without interrupting the job");
        string meterServiceCode = File.ReadAllText(Path.Combine("UpperComInspectionInstrument2022", "Services", "InSpectionMeterService.cs"));
        if (!meterServiceCode.Contains("ReadWithCompatibleBlockFallback", StringComparison.Ordinal) ||
            !meterServiceCode.Contains("CompatibleTemperatureBlockRegisterCount", StringComparison.Ordinal) ||
            !meterServiceCode.Contains("CompatibleHumidityBlockRegisterCount", StringComparison.Ordinal))
            throw new InvalidOperationException("dynamic channel reads should retain the Qt-compatible fixed-block fallback");
        string configurationServiceCode = File.ReadAllText(Path.Combine(
            "UpperComInspectionInstrument2022", "Services", "InspectionInstrumentConfigurationService.cs"));
        if (configurationServiceCode.Contains("InspectionInstrumentConfiguration current = Read(slaveAddress)", StringComparison.Ordinal) ||
            configurationServiceCode.Contains("ModbusResponse blockResponse", StringComparison.Ordinal) ||
            !configurationServiceCode.Contains("WriteChangedRegistersAndVerify", StringComparison.Ordinal) ||
            !configurationServiceCode.Contains("TryWriteAndVerifyRegister", StringComparison.Ordinal) ||
            !configurationServiceCode.Contains("ValidateChangedRegistersAgainstSnapshot", StringComparison.Ordinal) ||
            !configurationServiceCode.Contains("IsExplicitModbusRejection", StringComparison.Ordinal) ||
            !configurationServiceCode.Contains("程序未进入配置模式", StringComparison.Ordinal) ||
            !configurationServiceCode.Contains("失败后设备也未确认恢复采集", StringComparison.Ordinal) ||
            !configurationServiceCode.Contains("ConfigurationReadBlock", StringComparison.Ordinal) ||
            !configurationServiceCode.Contains("TryValidateConfigurationRegisters", StringComparison.Ordinal) ||
            !configurationServiceCode.Contains("MatchingConfirmationCount", StringComparison.Ordinal) ||
            !configurationServiceCode.Contains("疑似迟到测量帧", StringComparison.Ordinal) ||
            !configurationServiceCode.Contains("HasInvalidRegisters", StringComparison.Ordinal) ||
            !configurationServiceCode.Contains("HasNonCanonicalRegisterOrder", StringComparison.Ordinal) ||
            !configurationServiceCode.Contains("EncodeConfigurationWriteValue(expectedValue)", StringComparison.Ordinal) ||
            !configurationServiceCode.Contains("ConfigurationPreflightCommunicationException", StringComparison.Ordinal) ||
            !configurationServiceCode.Contains("ConfigurationSnapshotChangedException", StringComparison.Ordinal) ||
            !configurationServiceCode.Contains("writePhaseStarted", StringComparison.Ordinal) ||
            !configurationServiceCode.Contains("CanRetryWithCurrentSnapshot", StringComparison.Ordinal) ||
            !configurationServiceCode.Contains("forceRepair", StringComparison.Ordinal) ||
            !configurationServiceCode.Contains("稳定异常配置", StringComparison.Ordinal) ||
            !configurationServiceCode.Contains("TryReadConfigurationBlockRegisterByRegister", StringComparison.Ordinal) ||
            !configurationServiceCode.Contains("单寄存器降级仍未确认", StringComparison.Ordinal) ||
            !configurationServiceCode.Contains("blocks.Where(item => !item.Completed)", StringComparison.Ordinal) ||
            !configurationServiceCode.Contains("UsedCompatibilityConfigurationMode = configurationModeEntered", StringComparison.Ordinal) ||
            !configurationServiceCode.Contains("InspectionInstrumentRunStateService(_client).EnsureStarted", StringComparison.Ordinal) ||
            !configurationServiceCode.Contains("public InspectionInstrumentEnableState ReadChannelEnabled", StringComparison.Ordinal) ||
            !configurationServiceCode.Contains("ConfigurationFallbackChunkRegisterCount", StringComparison.Ordinal) ||
            configurationServiceCode.Contains("WriteControlCoilWithRetry", StringComparison.Ordinal) ||
            configurationServiceCode.Contains("WriteMultipleRegisters(slaveAddress", StringComparison.Ordinal) ||
            configurationServiceCode.Contains("RefreshConfigurationMode", StringComparison.Ordinal))
            throw new InvalidOperationException(
                "device configuration must resume failed read blocks and verify idempotent single-register writes before using configuration-mode fallback");
        string modbusClientCode = File.ReadAllText(Path.Combine(
            "UpperComInspectionInstrument2022", "Communication", "ModbusRtuClient.cs"));
        if (!modbusClientCode.Contains("ReadAttemptCount = 3", StringComparison.Ordinal) ||
            !modbusClientCode.Contains("DrainInputUntilQuiet", StringComparison.Ordinal) ||
            !modbusClientCode.Contains("public ModbusResponse ReadCoils", StringComparison.Ordinal) ||
            !modbusClientCode.Contains("List<byte> buffer = new List<byte>()", StringComparison.Ordinal) ||
            modbusClientCode.Contains("List<byte> _receiveBuffer", StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Modbus reads must use three bounded attempts and a transaction-local receive buffer");
        string configurationWindowCode = File.ReadAllText(Path.Combine(
            "UpperComInspectionInstrument2022", "Views", "InstrumentChannelConfigurationWindow.xaml.cs"));
        string configurationWindowXaml = File.ReadAllText(Path.Combine(
            "UpperComInspectionInstrument2022", "Views", "InstrumentChannelConfigurationWindow.xaml"));
        if (configurationWindowXaml.Contains("设备使能通道", StringComparison.Ordinal) ||
            configurationWindowXaml.Contains("DataGridCheckBoxColumn", StringComparison.Ordinal))
            throw new InvalidOperationException("humidity configuration should expose H1-H9 directly without an internal channel-number column");
        ModbusRtuClient configurationClient = new();
        InspectionDataAcquisitionService configurationAcquisition = new(new InspectionMeterService(configurationClient));
        InstrumentChannelConfigurationWindow unifiedConfigurationWindow = new(
            configurationClient,
            configurationAcquisition,
            1,
            5,
            2,
            new[] { 16, 17, 18, 19, 20 },
            new[] { 6, 7 });
        DataGrid unifiedTemperatureMapping = Find<DataGrid>(unifiedConfigurationWindow, "TemperatureMappingGrid");
        DataGrid unifiedHumidityMapping = Find<DataGrid>(unifiedConfigurationWindow, "HumidityMappingGrid");
        DataGridComboBoxColumn unifiedTemperaturePhysicalColumn =
            (DataGridComboBoxColumn)unifiedTemperatureMapping.Columns[1];
        if (unifiedTemperatureMapping.Items.Count != 5 || unifiedHumidityMapping.Items.Count != 2 ||
            unifiedTemperaturePhysicalColumn.ItemsSource.Cast<object>().Count() !=
            InspectionInstrumentProtocol.PhysicalTemperatureChannelCount ||
            !Find<TextBlock>(unifiedConfigurationWindow, "BindingSummaryTextBlock").Text.Contains("CH16", StringComparison.Ordinal) ||
            !Find<TextBlock>(unifiedConfigurationWindow, "BindingSummaryTextBlock").Text.Contains("H6", StringComparison.Ordinal))
            throw new InvalidOperationException(
                "the unified device configuration should preserve task mappings and preview their derived enable state");
        if (!configurationWindowCode.Contains("_loadedConfiguration", StringComparison.Ordinal) ||
            !configurationWindowCode.Contains("loadedConfiguration.ChannelEnabled.SequenceEqual", StringComparison.Ordinal) ||
            !configurationWindowCode.Contains("ReadChannelConfiguration", StringComparison.Ordinal) ||
            !configurationWindowCode.Contains("WriteChannelEnabledAndSave", StringComparison.Ordinal) ||
            configurationWindowCode.Contains("SensorTypeCode", StringComparison.Ordinal) ||
            configurationWindowXaml.Contains("SensorTypeColumn", StringComparison.Ordinal) ||
            !configurationWindowXaml.Contains("当前设备", StringComparison.Ordinal) ||
            !configurationWindowXaml.Contains("配置诊断", StringComparison.Ordinal) ||
            !configurationWindowCode.Contains("protected override void OnClosing", StringComparison.Ordinal) ||
            !configurationWindowCode.Contains("if (_isBusy)", StringComparison.Ordinal) ||
            !configurationWindowCode.Contains("ConfigurationOperation.Reading", StringComparison.Ordinal) ||
            !configurationWindowCode.Contains("CloseButton.IsEnabled = !busy || _activeOperation == ConfigurationOperation.Reading", StringComparison.Ordinal) ||
            !configurationWindowCode.Contains("_readCancellation?.Cancel()", StringComparison.Ordinal) ||
            !configurationWindowCode.Contains("_closeAfterReadCancellation = true", StringComparison.Ordinal) ||
            !configurationWindowCode.Contains("Dispatcher.BeginInvoke(new Action(Close))", StringComparison.Ordinal) ||
            !configurationWindowCode.Contains("TraceConfiguration", StringComparison.Ordinal) ||
            !configurationWindowCode.Contains("最后请求=", StringComparison.Ordinal) ||
            !configurationWindowCode.Contains("保存后修复", StringComparison.Ordinal) ||
            !configurationWindowCode.Contains("loadedConfiguration.HasEnableRegistersNeedingRepair", StringComparison.Ordinal) ||
            !configurationWindowCode.Contains("可疑旧字节序，保存后修复", StringComparison.Ordinal) ||
            !configurationWindowCode.Contains("TryBuildDesiredChannelState", StringComparison.Ordinal) ||
            !configurationWindowCode.Contains("CompleteJointSave", StringComparison.Ordinal) ||
            !configurationWindowCode.Contains("TaskMappingApplied?.Invoke", StringComparison.Ordinal) ||
            !configurationWindowCode.Contains("ShowSaveResult", StringComparison.Ordinal) ||
            configurationWindowCode.Contains("ShowSaveResultAndClose", StringComparison.Ordinal) ||
            configurationWindowCode.Contains("DialogResult = true", StringComparison.Ordinal) ||
            !configurationWindowCode.Contains("配置窗口将保留", StringComparison.Ordinal) ||
            !configurationWindowCode.Contains("保存成功。", StringComparison.Ordinal) ||
            !configurationWindowCode.Contains("保存完成（有提示）", StringComparison.Ordinal) ||
            !configurationWindowCode.Contains("其余物理通道自动关闭", StringComparison.Ordinal) ||
            !configurationWindowXaml.Contains("TemperatureMappingGrid", StringComparison.Ordinal) ||
            !configurationWindowXaml.Contains("HumidityMappingGrid", StringComparison.Ordinal) ||
            !configurationWindowXaml.Contains("保存绑定并应用设备", StringComparison.Ordinal) ||
            !configurationWindowCode.Contains("CanRetryWithCurrentSnapshot: true", StringComparison.Ordinal) ||
            !configurationWindowCode.Contains("写入前核对暂时无响应", StringComparison.Ordinal) ||
            !configurationWindowCode.Contains("MessageBoxButton.YesNo", StringComparison.Ordinal) ||
            !configurationWindowCode.Contains("当前快照和编辑内容已保留", StringComparison.Ordinal) ||
            !configurationWindowCode.Contains("配置写入可能已经开始", StringComparison.Ordinal) ||
            !configurationWindowCode.Contains("_profileService.TrySave", StringComparison.Ordinal))
            throw new InvalidOperationException(
                "task mapping must drive device enables in the unified configuration window while reads remain cancellable and writes protected");
        if (!workbenchCode.Contains("window.TaskMappingApplied +=", StringComparison.Ordinal) ||
            !workbenchCode.Contains("CalibrationTaskContext.Save();", StringComparison.Ordinal) ||
            !workbenchCode.Contains("window.ShowDialog();", StringComparison.Ordinal) ||
            workbenchCode.Contains("window.ShowDialog() == true", StringComparison.Ordinal))
            throw new InvalidOperationException(
                "successful channel mappings must be persisted immediately while the configuration window remains open");
        string channelProfileServiceCode = File.ReadAllText(Path.Combine(
            "UpperComInspectionInstrument2022", "Services", "InspectionInstrumentChannelProfileService.cs"));
        if (!channelProfileServiceCode.Contains("instrument-channel-profiles.json", StringComparison.Ordinal) ||
            !channelProfileServiceCode.Contains("PortName", StringComparison.Ordinal) ||
            !channelProfileServiceCode.Contains("SlaveAddress", StringComparison.Ordinal) ||
            !channelProfileServiceCode.Contains("File.Move(temporaryPath, ProfileFilePath, overwrite: true)", StringComparison.Ordinal))
            throw new InvalidOperationException(
                "the last verified channel-enable profile must be persisted atomically per serial port and slave address");
        Button startAcquisition = Find<Button>(workbench, "StartAcquisitionButton");
        startAcquisition.IsEnabled = true;
        AssertBrush(startAcquisition.Foreground, Colors.White, "primary workbench action should keep readable white text");

        shell.SetGlobalRunStatus("COM7 · 正式采样 2/16", UpperComInspectionInstrument2022.GlobalRunStatusTone.Active, true);
        if (Find<TextBlock>(shell, "GlobalRunStatusTextBlock").Text != "COM7 · 正式采样 2/16" ||
            Find<Border>(shell, "GlobalRunStatusBorder").Cursor != Cursors.Hand)
            throw new InvalidOperationException("global acquisition status should be visible and return to the workbench");
        shell.ShowTaskConfigurationPage();
        DrainDispatcher();
        shell.ShowCalibrationJobPage();
        DrainDispatcher();
        if (!ReferenceEquals(workbench, frame.Content))
            throw new InvalidOperationException("returning from another page should reuse the existing calibration workbench");
        UserSessionContext.SignOut();
    }

    private static void DrainDispatcher()
    {
        DispatcherFrame frame = new();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static void VerifyHistoryActionsRequireSelection()
    {
        HistoryView historyPage = new();
        if (historyPage.FindName("GeneratePdfArchiveButton") != null ||
            historyPage.FindName("OpenPdfArchiveButton") != null ||
            historyPage.FindName("OpenSelectedSampleButton") != null ||
            historyPage.FindName("OpenSelectedResultButton") != null)
            throw new InvalidOperationException("history should expose Excel records and Word reports instead of duplicate CSV/PDF actions");
        Find<DataGrid>(historyPage, "HistoryDataGrid").SelectedItem = null;
        string[] actionNames =
        {
            "OpenSelectedJobButton", "GenerateExcelReportButton", "GenerateWordCertificateButton"
        };
        foreach (string actionName in actionNames)
        {
            if (Find<Button>(historyPage, actionName).IsEnabled)
                throw new InvalidOperationException($"history action should be disabled before selecting a job: {actionName}");
        }
    }

    private static void AssertBrush(Brush brush, Color expected, string message)
    {
        if (brush is not SolidColorBrush solid || solid.Color != expected)
            throw new InvalidOperationException(message);
    }

    private static MeasurementSnapshot Snapshot(params double[] temperatures) => new()
    {
        Timestamp = DateTime.Now,
        Channels = temperatures.Select((value, index) => new InspectionChannelData
        {
            Channel = index + 1,
            Type = ChannelType.Temperature,
            Role = ChannelRole.PrimaryTemperature,
            Value = value,
            IsValid = true
        }).ToList(),
        ValidChannelCount = temperatures.Length
    };
}
