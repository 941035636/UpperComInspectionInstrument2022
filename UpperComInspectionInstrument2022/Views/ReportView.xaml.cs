using System.Windows;
using System.Windows.Controls;
using UpperComInspectionInstrument2022.Models;

namespace UpperComInspectionInstrument2022.Views
{
    /// <summary>
    /// 旧版报告占位页。当前正式 Excel 原始记录由结果页和历史页直接生成，本页仅保留导航兼容。
    /// </summary>
    public partial class ReportView : Page
    {
        /// <summary>初始化旧版报告占位状态。</summary>
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
        /// <summary>返回当前校准结果页。</summary>
        private void BackResultButton_Click(object sender, RoutedEventArgs e) => NavigationService?.Navigate(new ResultView());
        /// <summary>兼容旧按钮并提示当前报告入口位置。</summary>
        private void GenerateReportButton_Click(object sender, RoutedEventArgs e) => MessageBox.Show("量值结果已经计算；校准记录、证书模板和文件导出模块尚未接入。", "报告", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
