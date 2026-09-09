using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using UpperComInspectionInstrument2022.Models;
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
        TextBox temperatureCount = Find<TextBox>(taskPage, "TemperaturePointCountTextBox");
        TextBox humidityCount = Find<TextBox>(taskPage, "HumidityPointCountTextBox");
        TextBox temperatureCenter = Find<TextBox>(taskPage, "TemperatureCenterPointTextBox");
        TextBox humidityCenter = Find<TextBox>(taskPage, "HumidityCenterPointTextBox");
        TextBox plannedCount = Find<TextBox>(taskPage, "PlannedCountTextBox");
        TextBox samplingInterval = Find<TextBox>(taskPage, "SamplingIntervalTextBox");
        TextBlock plannedCountLabel = Find<TextBlock>(taskPage, "PlannedCountLabel");
        TextBlock samplingIntervalLabel = Find<TextBlock>(taskPage, "SamplingIntervalLabel");
        Button figureButton = Find<Button>(taskPage, "ViewLayoutFigureButton");
        Button saveTaskButton = Find<Button>(taskPage, "SaveTaskButton");
        Expander optionalArchive = Find<Expander>(taskPage, "OptionalArchiveExpander");

        Assert(!optionalArchive.IsExpanded, "optional device/customer archive should be collapsed by default");
        Assert(saveTaskButton.Width == 260 && DockPanel.GetDock(saveTaskButton) == Dock.Right &&
               saveTaskButton.Parent is DockPanel { LastChildFill: false },
            "task footer should use a normal-width right-aligned primary action");
        Assert(standard.Parent is Grid standardGrid && standardGrid.ColumnDefinitions[1].MaxWidth == 380,
            "paired task selectors should share the same responsive maximum width");
        TextBox measurementRange = Find<TextBox>(taskPage, "MeasurementRangeTextBox");
        Assert(measurementRange.Parent is Grid rangeGrid && rangeGrid.ColumnDefinitions[1].MaxWidth == 420,
            "measurement range should use a bounded field width instead of stretching across the card");
        Assert(volume.SelectedIndex == -1 && temperatureCount.Text.Length == 0 && temperatureCount.IsReadOnly,
            "task page should require an explicit volume before deriving point layout");
        Assert(figureButton.Visibility == Visibility.Visible && Equals(figureButton.Content, "查看布点图"),
            "JJF1101 should expose its layout figures before volume selection");

        volume.SelectedIndex = 0;
        Assert(temperatureCount.Text == "9" && temperatureCenter.Text == "5" && plannedCount.Text == "16" && samplingInterval.Text == "120",
            "JJF1101 <=2m3 linkage");
        Assert(Equals(figureButton.Content, "查看图 1"), "JJF1101 <=2m3 should link to figure 1");
        calibrationType.SelectedIndex = 1;
        Assert(humidityCount.Visibility == Visibility.Visible && humidityCount.Text == "3" && humidityCenter.Text == "3",
            "JJF1101 humidity linkage and O-channel default");
        volume.SelectedIndex = 1;
        Assert(temperatureCount.Text == "15" && humidityCount.Text == "4" && temperatureCenter.Text == "15" && humidityCenter.Text == "4",
            "JJF1101 >2m3 linkage");
        Assert(Equals(figureButton.Content, "查看图 2"), "JJF1101 >2m3 should link to figure 2");
        VerifyJjf1101LayoutFigureDialog();
        layout.SelectedIndex = 1;
        Assert(!temperatureCount.IsReadOnly && !humidityCount.IsReadOnly && !temperatureCenter.IsReadOnly,
            "JJF1101 actual-work-position mode exposes the user-requested point customization");

        standard.SelectedIndex = 1;
        Assert(volume.SelectedIndex == -1 && humidityCount.Visibility == Visibility.Collapsed && figureButton.Visibility == Visibility.Visible &&
               plannedCount.Text == "20" && samplingInterval.Text == "180" && !plannedCount.IsReadOnly && !samplingInterval.IsReadOnly,
            "JJF1376 exposes both sample count and interval while retaining normative defaults");
        Assert(plannedCountLabel.Text.Contains("≥20") && samplingIntervalLabel.Text.Contains("(s)") &&
               taskPage.FindName("SamplingPlanHintTextBlock") == null,
            "JJF1376 task page keeps editable plan fields without a redundant inline explanation");
        samplingInterval.Text = "120";
        Assert(samplingInterval.Text == "120", "JJF1376 custom interval should remain editable");
        volume.SelectedIndex = 0;
        Assert(temperatureCount.Text == "5" && temperatureCenter.Text == "3" && temperatureCount.IsReadOnly,
            "JJF1376 <=0.15m3 five-point linkage");
        volume.SelectedIndex = 1;
        Assert(temperatureCount.Text == "9" && temperatureCenter.Text == "9", "JJF1376 >0.15m3 nine-point linkage");
        layout.SelectedIndex = 2;
        Assert(!temperatureCount.IsReadOnly && !temperatureCenter.IsReadOnly, "JJF1376 custom work-position mode is editable");
        Assert(Find<TextBlock>(taskPage, "StandardCapabilityTextBlock").Text.Contains("0.02") &&
               Find<TextBlock>(taskPage, "StandardCapabilityTextBlock").Text.Contains("热电偶"),
            "furnace task page exposes measuring-instrument class and thermocouple grade");

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
        CalibrationTaskContext.TemperaturePointCount = 9;
        CalibrationTaskContext.HumidityPointCount = 0;
        CalibrationTaskContext.TemperatureCenterPoint = 5;
        CalibrationTaskContext.HumidityCenterPoint = 1;
        CalibrationTaskContext.PlannedCount = 16;
        CalibrationTaskContext.SamplingIntervalSeconds = 120;
        CalibrationTaskContext.SetTemperature = null;
        CalibrationTaskContext.SetHumidity = null;
        CalibrationTaskContext.EnvironmentInterferenceConfirmed = false;
    }

    /// <summary>防止系统设置页的普通输入框和底部保存按钮再次随窗口无限拉伸。</summary>
    private static void VerifySystemSettingsControlProportions()
    {
        DeviceView settingsPage = new();
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
        object frozenColumns = matrix.ReadLocalValue(DataGrid.FrozenColumnCountProperty);
        if (frozenColumns is not int frozenColumnCount || frozenColumnCount != 2)
            throw new InvalidOperationException("measurement matrix should keep sequence and time visible while horizontally scrolling");
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
