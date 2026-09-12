using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using UpperComInspectionInstrument2022.Services;

namespace UpperComInspectionInstrument2022.Views
{
    /// <summary>
    /// 本地校准作业浏览页。数据来源是文件夹内的 CSV 摘要，不依赖数据库。
    /// </summary>
    public partial class HistoryView : Page
    {
        /// <summary>初始化页面并加载本地历史记录。</summary>
        public HistoryView()
        {
            InitializeComponent();
            RefreshHistory();
            UpdateActionState();
        }

        /// <summary>按当前关键字和下拉筛选条件重新加载历史记录。</summary>
        private void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshHistory();
        }

        /// <summary>刷新表格数据源，并在有/无记录状态之间切换页面显示。</summary>
        private void RefreshHistory()
        {
            string? selectedJobId = (HistoryDataGrid.SelectedItem as CalibrationArchiveSummary)?.JobId;
            string standard = GetFilterValue(StandardFilterComboBox, "全部规范");
            string status = GetFilterValue(StatusFilterComboBox, "全部状态");
            IReadOnlyList<CalibrationArchiveSummary> records = CalibrationFileStorageService.Default.LoadHistory(
                KeywordTextBox.Text,
                standard,
                status);
            HistoryDataGrid.ItemsSource = records;
            bool hasRecords = records.Count > 0;
            HistoryDataGrid.Visibility = hasRecords ? Visibility.Visible : Visibility.Collapsed;
            EmptyStatePanel.Visibility = hasRecords ? Visibility.Collapsed : Visibility.Visible;
            if (!string.IsNullOrWhiteSpace(selectedJobId))
                HistoryDataGrid.SelectedItem = records.FirstOrDefault(record => record.JobId == selectedJobId);
            UpdateActionState();
        }


        /// <summary>所选作业变化时，仅开放当前状态和本地文件确实支持的动作。</summary>
        private void HistoryDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateActionState();

        /// <summary>根据任务完成状态和文件存在情况更新工具栏，避免无选择或无文件时产生无效点击。</summary>
        private void UpdateActionState()
        {
            CalibrationArchiveSummary? selected = HistoryDataGrid.SelectedItem as CalibrationArchiveSummary;
            bool hasSelection = selected != null;
            bool isCompleted = string.Equals(selected?.Status, "已完成", StringComparison.Ordinal);
            bool canGenerate = isCompleted && Directory.Exists(selected!.DirectoryPath);

            OpenSelectedJobButton.IsEnabled = hasSelection && Directory.Exists(selected!.DirectoryPath);
            GenerateExcelReportButton.IsEnabled = canGenerate;
            GenerateWordCertificateButton.IsEnabled = canGenerate;
            GenerateExcelReportButton.Content = hasSelection && File.Exists(selected!.ExcelReportFilePath)
                ? "打开原始记录 Excel"
                : "生成原始记录 Excel";
            GenerateWordCertificateButton.Content = hasSelection && File.Exists(selected!.WordCertificateFilePath)
                ? "打开校准报告 Word"
                : "生成校准报告 Word";
        }

        /// <summary>读取筛选下拉框；“全部”选项转换为空字符串表示不筛选。</summary>
        private static string GetFilterValue(ComboBox comboBox, string allValue)
        {
            string value = (comboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? string.Empty;
            return value == allValue ? string.Empty : value;
        }

        /// <summary>创建并打开本地校准数据根目录。</summary>
        private void OpenDataRootButton_Click(object sender, RoutedEventArgs e)
        {
            Directory.CreateDirectory(CalibrationFileStorageService.Default.DataRootPath);
            OpenPath(CalibrationFileStorageService.Default.DataRootPath);
        }

        /// <summary>打开所选作业文件夹。</summary>
        private void OpenSelectedJobButton_Click(object sender, RoutedEventArgs e) => OpenSelectedJobDirectory();

        /// <summary>为所选已完成作业生成或更新 Excel 原始记录。</summary>
        private void GenerateExcelReportButton_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetSelectedArchive(out CalibrationArchiveSummary? selected)) return;
            if (File.Exists(selected.ExcelReportFilePath))
            {
                OpenPath(selected.ExcelReportFilePath);
                return;
            }
            if (!CalibrationExcelReportService.Default.TryGenerate(selected.DirectoryPath, out string reportPath, out string error))
            {
                WriteReportOperation(selected, "生成 Excel 原始记录", "失败", error, selected.DirectoryPath);
                MessageBox.Show(error, "Excel 原始记录生成失败", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            WriteReportOperation(selected, "生成 Excel 原始记录", "成功", "已从冻结 CSV 生成", reportPath);
            RefreshHistory();
            OpenPath(reportPath);
        }

        /// <summary>为所选已完成作业生成 Word 校准报告，或直接打开已有报告。</summary>
        private void GenerateWordCertificateButton_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetSelectedArchive(out CalibrationArchiveSummary? selected)) return;
            if (File.Exists(selected.WordCertificateFilePath))
            {
                OpenPath(selected.WordCertificateFilePath);
                return;
            }
            if (!CalibrationWordCertificateService.Default.TryGenerate(selected.DirectoryPath, out string certificatePath, out string error))
            {
                WriteReportOperation(selected, "生成 Word 校准报告", "失败", error, selected.DirectoryPath);
                MessageBox.Show(error, "Word 校准报告生成失败", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            WriteReportOperation(selected, "生成 Word 校准报告", "成功", "已从冻结 CSV 生成，状态为待审核签发", certificatePath);
            RefreshHistory();
            OpenPath(certificatePath);
        }

        /// <summary>双击历史行时打开对应作业文件夹。</summary>
        private void HistoryDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) => OpenSelectedJobDirectory();

        /// <summary>取得当前选中的历史作业；未选择时向用户给出提示。</summary>
        private bool TryGetSelectedArchive(out CalibrationArchiveSummary selected)
        {
            if (HistoryDataGrid.SelectedItem is CalibrationArchiveSummary archive)
            {
                selected = archive;
                return true;
            }
            selected = null!;
            MessageBox.Show("请先选择一条本地校准作业。", "历史记录", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        /// <summary>打开所选作业目录，并处理目录被移动的情况。</summary>
        private void OpenSelectedJobDirectory()
        {
            if (!TryGetSelectedArchive(out CalibrationArchiveSummary? selected)) return;

            string path = selected.DirectoryPath;
            if (!Directory.Exists(path))
            {
                MessageBox.Show($"本地文件已被移动或删除：\n{path}", "无法打开", MessageBoxButton.OK, MessageBoxImage.Warning);
                RefreshHistory();
                return;
            }
            OpenPath(path);
        }

        /// <summary>通过 Windows Shell 用默认资源管理器或办公软件打开路径。</summary>
        private static void OpenPath(string path)
        {
            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"无法调用系统默认程序打开：\n{path}\n\n{ex.Message}", "打开失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>记录历史作业报告生成动作，不依赖当前内存中的任务状态。</summary>
        private static void WriteReportOperation(
            CalibrationArchiveSummary archive,
            string operation,
            string result,
            string description,
            string relatedPath)
        {
            LocalTraceService.Default.TryWriteOperation(operation, result, archive.JobId, description, relatedPath, out _);
            LocalTraceService.Default.TryWriteRuntime(
                result == "成功" ? "信息" : "错误",
                "报告",
                operation,
                description,
                archive.JobId,
                relatedPath,
                out _);
        }

        /// <summary>从空历史状态返回校准作业入口。</summary>
        private void OpenCalibrationJobButton_Click(object sender, RoutedEventArgs e)
        {
            if (Application.Current.MainWindow is MainWindow mainWindow)
                mainWindow.ShowCalibrationJobPage();
        }
    }
}
