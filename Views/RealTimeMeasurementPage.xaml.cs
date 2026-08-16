using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using UpperComInspectionInstrument2022.Models;
using UpperComInspectionInstrument2022.Services;
using UpperComInspectionInstrument2022.ViewModels;

namespace UpperComInspectionInstrument2022.Views
{
    public partial class RealTimeMeasurementPage : Page
    {
        private readonly RealTimeMeasurementViewModel _viewModel;

        private readonly InspectionDataAcquisitionService
            _acquisitionService;

        public RealTimeMeasurementPage(
            InspectionDataAcquisitionService acquisitionService)
        {
            InitializeComponent();

            _acquisitionService =
                acquisitionService;

            _viewModel =
                new RealTimeMeasurementViewModel();

            DataContext = _viewModel;

            _acquisitionService.DataAcquired +=
                OnDataAcquired;

            _acquisitionService.AcquisitionError +=
                OnAcquisitionError;

            Unloaded += RealTimeMeasurementPage_Unloaded;
        }


        /// <summary>
        /// 开始实时采集
        /// </summary>
        private void StartAcquisitionButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                if (!byte.TryParse(SlaveAddressTextBox.Text.Trim(), out byte slaveAddress) || slaveAddress == 0)
                {
                    MessageBox.Show("从站地址必须是 1～247。", "参数错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (!int.TryParse(IntervalTextBox.Text.Trim(), out int interval) || interval < 200)
                {
                    MessageBox.Show("采集周期必须是不小于 200 毫秒的整数。", "参数错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                _acquisitionService.Start(slaveAddress, interval);
                _viewModel.IsAcquiring = true;
                StatusTextBlock.Text = "正在采集";
                StartAcquisitionButton.IsEnabled = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "启动采集失败",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }


        /// <summary>
        /// 停止采集
        /// </summary>
        private void StopAcquisitionButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                _acquisitionService.Stop();

                _viewModel.IsAcquiring = false;

                StatusTextBlock.Text =
                    "已停止采集";
                StartAcquisitionButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "停止采集失败",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }


        /// <summary>
        /// 清空采集记录
        /// </summary>
        private void ClearDataButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            MessageBoxResult result =
                MessageBox.Show(
                    "确定要清空所有采集记录吗？",
                    "确认",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            _viewModel.ClearSnapshots();

            ChannelDataGrid.ItemsSource =
                null;

            AcquisitionCountTextBlock.Text =
                "0";

            StatusTextBlock.Text =
                "等待采集";
        }

        private void SnapshotDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SnapshotDataGrid.SelectedItem is MeasurementSnapshot snapshot)
            {
                _viewModel.SelectedSnapshot = snapshot;
                ChannelDataGrid.ItemsSource = snapshot.Channels;
                StatusTextBlock.Text = $"查看第 {snapshot.Sequence} 次采集";
            }
        }


        /// <summary>
        /// 接收到一次完整的50路采集数据
        /// </summary>
        private void OnDataAcquired(
            long acquisitionId,
            List<InspectionChannelData> data)
        {
            Dispatcher.Invoke(
                () =>
                {
                    int validCount = 0;

                    int invalidCount = 0;

                    foreach (
                        InspectionChannelData item
                        in data)
                    {
                        // 这里暂时根据你的现有 Status 判断。
                        //
                        // 后面我们会把“通信状态”和
                        // “测量值有效性”进一步分离。

                        if (item.Status == "有效" ||
                            item.Status == "正常")
                        {
                            validCount++;
                        }
                        else
                        {
                            invalidCount++;
                        }
                    }


                    MeasurementSnapshot snapshot =
                        new MeasurementSnapshot
                        {
                            Sequence =
                                acquisitionId,

                            Timestamp =
                                DateTime.Now,

                            Channels =
                                new List<InspectionChannelData>(
                                    data),

                            ValidChannelCount =
                                validCount,

                            InvalidChannelCount =
                                invalidCount
                        };


                    // 关键：
                    //
                    // 不是 ItemsSource = data
                    //
                    // 而是追加一条完整采集快照。

                    _viewModel.AddSnapshot(
                        snapshot);


                    // 当前选中的一次采集
                    ChannelDataGrid.ItemsSource =
                        snapshot.Channels;


                    AcquisitionCountTextBlock.Text =
                        _viewModel.AcquisitionCount
                            .ToString();


                    StatusTextBlock.Text =
                        $"正在采集，已完成 " +
                        $"{_viewModel.AcquisitionCount} 次";


                    SnapshotDataGrid.ScrollIntoView(
                        snapshot);
                });
        }


        /// <summary>
        /// 自动采集异常
        /// </summary>
        private void OnAcquisitionError(
            Exception ex)
        {
            Dispatcher.Invoke(
                () =>
                {
                    StatusTextBlock.Text =
                        "采集异常";

                    MessageBox.Show(
                        ex.Message,
                        "采集异常",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                });
        }


        /// <summary>
        /// 点击开始校准
        /// </summary>
        private void StartCalibrationButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_viewModel.Snapshots.Count == 0)
            {
                MessageBox.Show(
                    "目前还没有采集数据。\n\n" +
                    "请先进行实时采集。",
                    "无法开始校准",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }


            MessageBox.Show(
                "开始校准功能下一阶段实现。\n\n" +
                "这里将根据对应校准规范，" +
                "使用已经采集的数据计算校准结果。",
                "开始校准");
        }


        /// <summary>
        /// 返回首页
        /// </summary>
        private void BackHomeButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (NavigationService != null &&
                NavigationService.CanGoBack)
            {
                NavigationService.GoBack();
            }
        }


        /// <summary>
        /// 页面离开时解除事件
        /// </summary>
        private void RealTimeMeasurementPage_Unloaded(object sender, RoutedEventArgs e)
        {
            _acquisitionService.Stop();
            _acquisitionService.DataAcquired -= OnDataAcquired;
            _acquisitionService.AcquisitionError -= OnAcquisitionError;
            Unloaded -= RealTimeMeasurementPage_Unloaded;
        }
    }
}

