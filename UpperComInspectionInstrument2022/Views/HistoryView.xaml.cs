using System.Windows;
using System.Windows.Controls;

namespace UpperComInspectionInstrument2022.Views
{
    public partial class HistoryView : Page
    {
        public HistoryView()
        {
            InitializeComponent();
        }

        private void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            EmptyStatePanel.Visibility = Visibility.Visible;
            HistoryDataGrid.Visibility = Visibility.Collapsed;
        }

        private void OpenCalibrationJobButton_Click(object sender, RoutedEventArgs e)
        {
            if (Application.Current.MainWindow is MainWindow mainWindow)
                mainWindow.ShowCalibrationJobPage();
        }
    }
}
