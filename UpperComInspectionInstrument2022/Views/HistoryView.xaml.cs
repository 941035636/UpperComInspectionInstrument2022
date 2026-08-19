using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using UpperComInspectionInstrument2022.Services;

namespace UpperComInspectionInstrument2022.Views
{
    public partial class HistoryView : Page
    {
        public HistoryView()
        {
            InitializeComponent();
            RefreshHistory();
        }

        private void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshHistory();
        }

        private void RefreshHistory()
        {
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
        }

        private static string GetFilterValue(ComboBox comboBox, string allValue)
        {
            string value = (comboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? string.Empty;
            return value == allValue ? string.Empty : value;
        }

        private void OpenDataRootButton_Click(object sender, RoutedEventArgs e)
        {
            Directory.CreateDirectory(CalibrationFileStorageService.Default.DataRootPath);
            OpenPath(CalibrationFileStorageService.Default.DataRootPath);
        }

        private void OpenSelectedJobButton_Click(object sender, RoutedEventArgs e) => OpenSelectedPath(openSample: false);

        private void OpenSelectedSampleButton_Click(object sender, RoutedEventArgs e) => OpenSelectedPath(openSample: true);

        private void OpenSelectedResultButton_Click(object sender, RoutedEventArgs e) => OpenSelectedResult();

        private void HistoryDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) => OpenSelectedPath(openSample: false);

        private void OpenSelectedResult()
        {
            if (HistoryDataGrid.SelectedItem is not CalibrationArchiveSummary selected)
            {
                MessageBox.Show("请先选择一条本地校准作业。", "历史记录", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (!File.Exists(selected.ResultFilePath))
            {
                MessageBox.Show("该作业尚未生成校准结果，可能仍在采样或已经中断。", "结果文件不存在", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            OpenPath(selected.ResultFilePath);
        }

        private void OpenSelectedPath(bool openSample)
        {
            if (HistoryDataGrid.SelectedItem is not CalibrationArchiveSummary selected)
            {
                MessageBox.Show("请先选择一条本地校准作业。", "历史记录", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string path = openSample ? selected.SampleFilePath : selected.DirectoryPath;
            if (!Directory.Exists(path) && !File.Exists(path))
            {
                MessageBox.Show($"本地文件已被移动或删除：\n{path}", "无法打开", MessageBoxButton.OK, MessageBoxImage.Warning);
                RefreshHistory();
                return;
            }
            OpenPath(path);
        }

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

        private void OpenCalibrationJobButton_Click(object sender, RoutedEventArgs e)
        {
            if (Application.Current.MainWindow is MainWindow mainWindow)
                mainWindow.ShowCalibrationJobPage();
        }
    }
}
