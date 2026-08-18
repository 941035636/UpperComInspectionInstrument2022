using System.Windows;
using System.Windows.Controls;
using UpperComInspectionInstrument2022.Models;

namespace UpperComInspectionInstrument2022.Views
{
    public partial class ReportView : Page
    {
        public ReportView()
        {
            InitializeComponent();
            bool completed = CalibrationTaskContext.HasCompletedCalibration;
            GenerateReportButton.IsEnabled = false;
            if (completed)
            {
                ReportStatusTextBlock.Text = "正式采样和规范量值计算已完成；报告文件模板与导出模块尚未接入。";
                ReportStatusTextBlock.Foreground = System.Windows.Media.Brushes.DarkGreen;
            }
        }
        private void BackResultButton_Click(object sender, RoutedEventArgs e) => NavigationService?.Navigate(new ResultView());
        private void GenerateReportButton_Click(object sender, RoutedEventArgs e) => MessageBox.Show("量值结果已经计算；校准记录、证书模板和文件导出模块尚未接入。", "报告", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
