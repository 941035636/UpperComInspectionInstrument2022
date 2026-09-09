using System.Windows;

namespace UpperComInspectionInstrument2022.Views
{
    /// <summary>
    /// 显示 JJF 1101-2019 图 1、图 2，帮助操作人员确认温度数字测点、
    /// 湿度字母测点以及上、中、下三层与门方向。
    /// </summary>
    public partial class Jjf1101LayoutFigureWindow : Window
    {
        /// <summary>初始化布点图窗口，并按当前设备容积预选图 1 或图 2。</summary>
        public Jjf1101LayoutFigureWindow(int preferredFigure)
        {
            InitializeComponent();
            FigureTabControl.SelectedIndex = preferredFigure == 2 ? 1 : 0;
        }

        /// <summary>关闭布点图窗口。</summary>
        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}
