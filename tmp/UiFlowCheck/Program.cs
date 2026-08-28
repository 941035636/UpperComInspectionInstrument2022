using System.Windows;
using System.Windows.Controls;
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
        Button figureButton = Find<Button>(taskPage, "ViewLayoutFigureButton");
        Expander optionalArchive = Find<Expander>(taskPage, "OptionalArchiveExpander");

        Assert(!optionalArchive.IsExpanded, "optional device/customer archive should be collapsed by default");
        Assert(volume.SelectedIndex == -1 && temperatureCount.Text.Length == 0 && temperatureCount.IsReadOnly,
            "task page should require an explicit volume before deriving point layout");

        volume.SelectedIndex = 0;
        Assert(temperatureCount.Text == "9" && temperatureCenter.Text == "5" && plannedCount.Text == "16" && samplingInterval.Text == "120",
            "JJF1101 <=2m3 linkage");
        calibrationType.SelectedIndex = 1;
        Assert(humidityCount.Visibility == Visibility.Visible && humidityCount.Text == "3" && humidityCenter.Text == "3",
            "JJF1101 humidity linkage and O-channel default");
        volume.SelectedIndex = 1;
        Assert(temperatureCount.Text == "15" && humidityCount.Text == "4" && temperatureCenter.Text == "15" && humidityCenter.Text == "4",
            "JJF1101 >2m3 linkage");
        layout.SelectedIndex = 1;
        Assert(!temperatureCount.IsReadOnly && !humidityCount.IsReadOnly && !temperatureCenter.IsReadOnly,
            "JJF1101 actual-work-position mode exposes the user-requested point customization");

        standard.SelectedIndex = 1;
        Assert(volume.SelectedIndex == -1 && humidityCount.Visibility == Visibility.Collapsed && figureButton.Visibility == Visibility.Visible &&
               plannedCount.Text == "20" && samplingInterval.Text == "180" && plannedCount.IsReadOnly && samplingInterval.IsReadOnly,
            "JJF1376 switches to furnace-only controls and normative sampling plan");
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

        VerifyWorkbenchNavigationKeepsPageInstance();
        VerifySignedResultPresentation();
        Console.WriteLine("PASS: WPF task linkage, visibility, optional-section layout and result presentation assertions");
    }

    private static T Find<T>(FrameworkElement root, string name) where T : FrameworkElement =>
        root.FindName(name) as T ?? throw new InvalidOperationException($"WPF control not found: {name}");

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
        if (deviation.Contains("+-", StringComparison.Ordinal) || deviation != "上 -1.00 / 下 -3.00 ℃")
            throw new InvalidOperationException("negative deviation presentation is ambiguous: " + deviation);
        if (!Find<Button>(resultPage, "GenerateWordCertificateButton").IsEnabled)
            throw new InvalidOperationException("completed calibration should enable the Word certificate entry");
        if (!Find<Button>(resultPage, "GeneratePdfArchiveButton").IsEnabled)
            throw new InvalidOperationException("completed calibration should enable the PDF archive entry");
        string reportStatus = Find<TextBlock>(resultPage, "ReportFilesStatusTextBlock").Text;
        if (!reportStatus.Contains("Excel", StringComparison.Ordinal) ||
            !reportStatus.Contains("Word", StringComparison.Ordinal) ||
            !reportStatus.Contains("PDF", StringComparison.Ordinal))
            throw new InvalidOperationException("result page should expose all report file states: " + reportStatus);
    }

    private static void VerifyWorkbenchNavigationKeepsPageInstance()
    {
        UpperComInspectionInstrument2022.MainWindow shell = new();
        CalibrationTaskContext.IsConfigured = true;
        shell.ShowRealTimeMeasurementPage();
        Frame frame = Find<Frame>(shell, "MainFrame");
        object workbench = frame.Content;
        shell.ShowTaskConfigurationPage();
        shell.ShowCalibrationJobPage();
        if (!ReferenceEquals(workbench, frame.Content))
            throw new InvalidOperationException("returning from another page should reuse the existing calibration workbench");
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
