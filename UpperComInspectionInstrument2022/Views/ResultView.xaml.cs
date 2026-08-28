using DocumentFormat.OpenXml.Drawing.Diagrams;
using System.Diagnostics;
using System.IO;
using System.Reflection.Emit;
using System.Windows;
using System.Windows.Controls;
using UpperComInspectionInstrument2022.Models;
using UpperComInspectionInstrument2022.Services;

namespace UpperComInspectionInstrument2022.Views
{
    /// <summary>
    /// 当前校准结果页：从正式样本重新计算并按所选规范展示对应指标，同时提供 Excel 原始记录和 Word 证书入口。
    /// </summary>
    public partial class ResultView : Page
    {
        /// <summary>初始化任务摘要、环境信息和标准器快照，然后显示计算结果。</summary>
        public ResultView()
        {
            InitializeComponent();
            string equipmentName = string.IsNullOrWhiteSpace(CalibrationTaskContext.EquipmentName)
                ? "设备档案未填写"
                : CalibrationTaskContext.EquipmentName;
            TaskReferenceTextBlock.Text = CalibrationTaskContext.IsConfigured
                ? $"{GetStandardName()} · {equipmentName} · 温度 {CalibrationTaskContext.TemperaturePointCount} 点 · 湿度 {CalibrationTaskContext.HumidityPointCount} 点 · 正式样本 {CalibrationRunContext.Samples.Count}/{CalibrationTaskContext.PlannedCount} 组"
                : "尚未建立任务";
            string pressure = CalibrationTaskContext.AmbientPressure.HasValue ? $" / {CalibrationTaskContext.AmbientPressure:0.###} kPa" : string.Empty;
            EnvironmentTextBlock.Text = $"{CalibrationTaskContext.AmbientTemperature:0.###} ℃ / {CalibrationTaskContext.AmbientHumidity:0.###} %RH{pressure}";
            StandardSnapshotTextBlock.Text = string.IsNullOrWhiteSpace(CalibrationTaskContext.ReferencedStandardName)
                ? "尚未保存标准器快照"
                : $"{CalibrationTaskContext.ReferencedStandardName} · {CalibrationTaskContext.ReferencedCertificateNumber} · 有效期 {CalibrationTaskContext.ReferencedValidityDate:yyyy-MM-dd}";
            OpenReportButton.IsEnabled = CalibrationTaskContext.HasCompletedCalibration;
            GenerateWordCertificateButton.IsEnabled = CalibrationTaskContext.HasCompletedCalibration;
            ShowResults();
        }


        /// <summary>检查正式采样完成状态，执行规范计算并映射到结果卡片。</summary>
        private void ShowResults()
        {
            Metric6Value.Text = $"{CalibrationRunContext.Samples.Count} / {CalibrationTaskContext.PlannedCount} 组";
            if (!CalibrationTaskContext.HasCompletedCalibration)
            {
                ResultStatusTextBlock.Text = "当前任务尚未完成正式采样";
                ResultHintTextBlock.Text = "请返回校准工作台完成稳定确认和计划样本采集。";
                return;
            }


            CalibrationResultSummary result = CalibrationResultCalculator.Calculate();
            if (!result.IsValid)
            {
                ResultStatusTextBlock.Text = "正式样本不能完成规范计算";
                ResultHintTextBlock.Text = result.Message;
                return;
            }
            /**/
            else if (result.IsValid == false)
            {
                ResultHintTextBlock.Text = result.Message;
                return;
            }

            ResultStatusTextBlock.Text = "正式样本已按规范公式完成计算";
            ResultHintTextBlock.Text = "量值按规范公式计算，不确定度按附录示例的重复性、分辨力、证书修正值和稳定性等分量计算；表中参考技术指标不直接作为合格判据，出证前仍需核验原始记录和分量来源。";
            ResultStatusTextBlock.Foreground = System.Windows.Media.Brushes.DarkGreen;

            if (CalibrationTaskContext.StandardIndex == 1)
            {
                SetMetric(Metric1Label, Metric1Value, Metric1Hint, "炉温均匀度", FormatUpperLower(result.FurnaceUniformityUpper, result.FurnaceUniformityLower, "℃"), "各点实际温度相对中心监控点");
                SetMetric(Metric2Label, Metric2Value, Metric2Hint, "炉温稳定度", FormatUpperLower(result.FurnaceStabilityUpper, result.FurnaceStabilityLower, "℃"), "中心点最大、最小值相对平均值");
                SetMetric(Metric3Label, Metric3Value, Metric3Hint, "炉温偏差", FormatUpperLower(result.FurnaceDeviationUpper, result.FurnaceDeviationLower, "℃"), "最高、最低实际温度相对标称温度");
                SetMetric(Metric4Label, Metric4Value, Metric4Hint, "炉内最大温差", $"{result.FurnaceMaximumDifference:F2} ℃", "各测量周期最大温差中的最大值");
                SetMetric(Metric5Label, Metric5Value, Metric5Hint, "炉温均匀度扩展不确定度", $"U+={result.FurnaceUniformityUpperUncertainty:F2} / U-={result.FurnaceUniformityLowerUncertainty:F2} ℃", $"k={CalibrationTaskContext.ReferencedTemperatureCoverage:0.###}；重复性与装置修正值分量");
                return;
            }

            SetMetric(Metric1Label, Metric1Value, Metric1Hint, "温度偏差", FormatUpperLower(result.TemperatureUpperDeviation, result.TemperatureLowerDeviation, "℃"), $"扩展不确定度 U={result.TemperatureExpandedUncertainty:F2} ℃，k={CalibrationTaskContext.ReferencedTemperatureCoverage:0.###}");
            SetMetric(Metric2Label, Metric2Value, Metric2Hint, "温度均匀度", $"{result.TemperatureUniformity:F2} ℃", "各组最大与最小温差的算术平均");
            SetMetric(Metric3Label, Metric3Value, Metric3Hint, "温度波动度", $"±{result.TemperatureFluctuation:F2} ℃", "各测点规定时间内极差一半的最大值");
            if (CalibrationTaskContext.IncludesHumidity)
            {
                SetMetric(Metric4Label, Metric4Value, Metric4Hint, "相对湿度偏差", FormatUpperLower(result.HumidityUpperDeviation, result.HumidityLowerDeviation, "%RH"), $"扩展不确定度 U={result.HumidityExpandedUncertainty:F2} %RH，k={CalibrationTaskContext.ReferencedHumidityCoverage:0.###}");
                SetMetric(Metric5Label, Metric5Value, Metric5Hint, "相对湿度均匀度", $"{result.HumidityUniformity:F2} %RH", "各组最大与最小湿度差的算术平均");
                SetMetric(Metric6Label, Metric6Value, Metric6Hint, "相对湿度波动度", $"±{result.HumidityFluctuation:F2} %RH", "各湿度测点极差一半的最大值");
            }
            else
            {
                SetMetric(Metric4Label, Metric4Value, Metric4Hint, "温度偏差扩展不确定度", $"U={result.TemperatureExpandedUncertainty:F2} ℃", $"k={CalibrationTaskContext.ReferencedTemperatureCoverage:0.###}；重复性、分辨力、修正值和标准器稳定性");
                SetMetric(Metric5Label, Metric5Value, Metric5Hint, "正式样本", $"{CalibrationRunContext.Samples.Count} / {CalibrationTaskContext.PlannedCount} 组", "与快速实时趋势数据分离");
                Metric6Card.Visibility = Visibility.Hidden;
            }
        }

        /// <summary>统一设置一张结果卡片的名称、数值和口径说明。</summary>
        private static void SetMetric(TextBlock label, TextBlock value, TextBlock hint, string labelText, string valueText, string hintText)
        {
            label.Text = labelText;
            value.Text = valueText;
            hint.Text = hintText;
        }

        private static string GetMetric(TextBlock Label)
        {
            string labtxt = Label.Text;
            return labtxt;
        }

        /// <summary>将上、下偏差显式标注并按真实正负号显示，避免负上偏差出现“+-”等歧义。</summary>
        private static string FormatUpperLower(double upper, double lower, string unit) =>
            $"上 {FormatSigned(upper)} / 下 {FormatSigned(lower)} {unit}";

        /// <summary>正值带加号，负值保留负号，零值不附加符号。</summary>
        private static string FormatSigned(double value) => value switch
        {
            > 0 => $"+{value:F2}",
            < 0 => $"{value:F2}",
            _ => "0.00"
        };

        /// <summary>取得当前任务的规范代号。</summary>
        private static string GetStandardName() => CalibrationTaskContext.StandardIndex == 1 ? "JJF 1376-2012" : "JJF 1101-2019";

        /// <summary>返回保留实时数据的校准工作台。</summary>
        private void BackCalibrationButton_Click(object sender, RoutedEventArgs e)
        {
            if (Application.Current.MainWindow is MainWindow mainWindow) mainWindow.ShowRealTimeMeasurementPage();
        }

        /// <summary>从当前已完成作业的固化 CSV 生成 Excel 原始记录并调用默认办公软件打开。</summary>
        private void OpenReportButton_Click(object sender, RoutedEventArgs e)
        {
            string? jobDirectory = CalibrationFileStorageService.Default.CurrentJobDirectory;
            if (string.IsNullOrWhiteSpace(jobDirectory))
            {
                MessageBox.Show("当前会话没有可用的本地作业目录。请从“历史记录”选择已完成作业后生成 Excel 原始记录。", "找不到作业目录", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (!CalibrationExcelReportService.Default.TryGenerate(jobDirectory, out string reportPath, out string error))
            {
                MessageBox.Show(error, "Excel 原始记录生成失败", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            try
            {
                Process.Start(new ProcessStartInfo(reportPath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Excel 原始记录已生成，但无法调用系统默认办公软件打开：\n{reportPath}\n\n{ex.Message}", "打开失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>从当前已完成作业的固化 CSV 生成 Word 校准证书并调用默认办公软件打开。</summary>
        private void GenerateWordCertificateButton_Click(object sender, RoutedEventArgs e)
        {
            string? jobDirectory = CalibrationFileStorageService.Default.CurrentJobDirectory;
            if (string.IsNullOrWhiteSpace(jobDirectory))
            {
                MessageBox.Show("当前会话没有可用的本地作业目录。请从“历史记录”选择已完成作业后生成 Word 校准证书。", "找不到作业目录", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (!CalibrationWordCertificateService.Default.TryGenerate(jobDirectory, out string certificatePath, out string error))
            {
                MessageBox.Show(error, "Word 校准证书生成失败", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            try
            {
                Process.Start(new ProcessStartInfo(certificatePath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Word 校准证书已生成，但无法调用系统默认办公软件打开：\n{certificatePath}\n\n{ex.Message}", "打开失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
