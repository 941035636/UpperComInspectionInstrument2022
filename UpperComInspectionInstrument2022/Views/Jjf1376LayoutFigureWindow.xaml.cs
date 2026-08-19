using System.Windows;

namespace UpperComInspectionInstrument2022.Views
{
    public partial class Jjf1376LayoutFigureWindow : Window
    {
        public Jjf1376LayoutFigureWindow(int preferredFigure)
        {
            InitializeComponent();
            FigureTabControl.SelectedIndex = preferredFigure == 2 ? 1 : 0;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}
