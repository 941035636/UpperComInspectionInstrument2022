using System.Windows;

namespace UpperComInspectionInstrument2022.Views
{
    /// <summary>
    /// JJF 1376-2012 图 1、图 2 布点示意查看窗口，辅助用户确认测温区定义。
    /// </summary>
    public partial class Jjf1376LayoutFigureWindow : Window
    {
        /// <summary>打开窗口并按 <paramref name="preferredFigure"/> 预选图 1 或图 2。</summary>
        public Jjf1376LayoutFigureWindow(int preferredFigure)
        {
            InitializeComponent();
            FigureTabControl.SelectedIndex = preferredFigure == 2 ? 1 : 0;
        }

        /// <summary>关闭布点示意窗口。</summary>
        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}
