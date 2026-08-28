using CommunityToolkit.Mvvm.ComponentModel;
using DocumentFormat.OpenXml.Office2013.WebExtension;
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
        private MeasurementSnapshot? selectedSnapshot;

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



        /// <summary>
        /// 添加一次采集结果，并将其设为当前快照。
        /// 内存只保留最近 600 组实时快照；正式校准样本由 <see cref="CalibrationRunContext"/> 单独保存。
        /// </summary>
        public void AddSnapshot(
            MeasurementSnapshot snapshot)
        {
            Snapshots.Add(snapshot);

            // 实时趋势和矩阵只使用近期数据，正式校准样本由 CalibrationRunContext 独立保存。
            // 限制内存中的快速快照数量，避免长时间巡检后 UI 持续增长。
            while (Snapshots.Count > 600) Snapshots.RemoveAt(0);

            SelectedSnapshot = snapshot;

            AcquisitionCount++;

            StatusText =
                $"已采集 {AcquisitionCount} 次";
        }

    }
}
