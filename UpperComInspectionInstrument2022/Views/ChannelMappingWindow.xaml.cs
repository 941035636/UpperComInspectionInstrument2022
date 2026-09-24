using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using UpperComInspectionInstrument2022.Communication;
using UpperComInspectionInstrument2022.Services;

namespace UpperComInspectionInstrument2022.Views
{
    /// <summary>编辑一次校准任务中逻辑测点与巡检仪物理接口的对应关系。</summary>
    public partial class ChannelMappingWindow : Window
    {
        private readonly ObservableCollection<ChannelMappingRow> _temperatureRows;
        private readonly ObservableCollection<ChannelMappingRow> _humidityRows;

        public IReadOnlyList<int> TemperatureMapping => _temperatureRows.Select(row => row.PhysicalChannel).ToList();
        public IReadOnlyList<int> HumidityMapping => _humidityRows.Select(row => row.PhysicalChannel).ToList();

        public ChannelMappingWindow(
            int temperaturePointCount,
            int humidityPointCount,
            IEnumerable<int>? temperatureMapping,
            IEnumerable<int>? humidityMapping)
        {
            InitializeComponent();

            List<int> normalizedTemperature = MeasurementChannelMappingService.Normalize(
                temperatureMapping,
                temperaturePointCount,
                InspectionInstrumentProtocol.PhysicalTemperatureChannelCount);
            List<int> normalizedHumidity = MeasurementChannelMappingService.Normalize(
                humidityMapping,
                humidityPointCount,
                InspectionInstrumentProtocol.PhysicalHumidityChannelCount);

            _temperatureRows = new ObservableCollection<ChannelMappingRow>(normalizedTemperature
                .Select((physical, index) => new ChannelMappingRow($"T{index + 1}", physical)));
            _humidityRows = new ObservableCollection<ChannelMappingRow>(normalizedHumidity
                .Select((physical, index) => new ChannelMappingRow($"H{index + 1}", physical)));

            TemperaturePhysicalColumn.ItemsSource = Enumerable.Range(
                1, InspectionInstrumentProtocol.PhysicalTemperatureChannelCount).ToList();
            HumidityPhysicalColumn.ItemsSource = Enumerable.Range(
                1, InspectionInstrumentProtocol.PhysicalHumidityChannelCount).ToList();
            TemperatureMappingGrid.ItemsSource = _temperatureRows;
            HumidityMappingGrid.ItemsSource = _humidityRows;
            HumidityMappingPanel.Visibility = humidityPointCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            TemperatureMappingGrid.CommitEdit();
            HumidityMappingGrid.CommitEdit();
            if (_temperatureRows.Select(row => row.PhysicalChannel).Distinct().Count() != _temperatureRows.Count)
            {
                MessageBox.Show("温度测点不能重复绑定同一个物理接口。", "通道绑定", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (_humidityRows.Select(row => row.PhysicalChannel).Distinct().Count() != _humidityRows.Count)
            {
                MessageBox.Show("湿度测点不能重复绑定同一个物理接口。", "通道绑定", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
        }

        /// <summary>DataGrid 的单行编辑模型。</summary>
        private sealed class ChannelMappingRow
        {
            public ChannelMappingRow(string logicalPointLabel, int physicalChannel)
            {
                LogicalPointLabel = logicalPointLabel;
                PhysicalChannel = physicalChannel;
            }

            public string LogicalPointLabel { get; }
            public int PhysicalChannel { get; set; }
        }
    }
}
