using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using UpperComInspectionInstrument2022.Models;

namespace UpperComInspectionInstrument2022.ViewModels
{
    /// <summary>
    /// 实时测量页面 ViewModel
    /// </summary>
    public partial class RealTimeMeasurementViewModel
        : ObservableObject
    {
        /// <summary>
        /// 所有采集快照。
        ///
        /// 每采集一次，就增加一条。
        /// 不覆盖之前的数据。
        /// </summary>
        [ObservableProperty]
        private ObservableCollection<MeasurementSnapshot>
            snapshots =
            new ObservableCollection<MeasurementSnapshot>();

        /// <summary>
        /// 当前选中的采集快照
        /// </summary>
        [ObservableProperty]
        private MeasurementSnapshot selectedSnapshot;

        /// <summary>
        /// 是否正在实时采集
        /// </summary>
        [ObservableProperty]
        private bool isAcquiring;

        /// <summary>
        /// 当前采集次数
        /// </summary>
        [ObservableProperty]
        private long acquisitionCount;

        /// <summary>
        /// 当前状态
        /// </summary>
        [ObservableProperty]
        private string statusText = "等待采集";

        /// <summary>
        /// 清空所有采集记录
        /// </summary>
        public void ClearSnapshots()
        {
            Snapshots.Clear();

            SelectedSnapshot = null;

            AcquisitionCount = 0;

            StatusText = "等待采集";
        }

        ///清空所有的采集记录
        //private void ClearSnapShots()
        //{

        //    snapshots.Clear();
        //    selectedSnapshot = null;
        //    acquisitionCount = 0;
        //    statusText = "等待采集";

        //}

        /// <summary>
        /// 添加一次采集结果
        /// </summary>
        public void AddSnapshot(
            MeasurementSnapshot snapshot)
        {
            Snapshots.Add(snapshot);

            SelectedSnapshot = snapshot;

            AcquisitionCount =
                Snapshots.Count;

            StatusText =
                $"已采集 {AcquisitionCount} 次";
        }
    }
}