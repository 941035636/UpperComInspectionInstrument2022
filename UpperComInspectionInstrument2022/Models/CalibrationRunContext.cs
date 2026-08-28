using System;
using System.Collections.Generic;

namespace UpperComInspectionInstrument2022.Models
{
    /// <summary>
    /// 正式校准中的一组样本，包含标准器全部通道和同一时刻录入的被校设备示值。
    /// </summary>
    public sealed class CalibrationSampleRecord
    {
        /// <summary>本次正式校准内从 1 开始递增的样本序号。</summary>
        public int SampleNumber { get; init; }
        /// <summary>标准器快照的采集时间。</summary>
        public DateTime Timestamp { get; init; }
        /// <summary>巡检仪在该时刻返回的完整通道快照。</summary>
        public MeasurementSnapshot Snapshot { get; init; } = new();
        /// <summary>被校设备温度显示值；未录入时为空。</summary>
        public double? DutDisplayTemperature { get; init; }
        /// <summary>被校设备湿度显示值；未录入或非湿度任务时为空。</summary>
        public double? DutDisplayHumidity { get; init; }
    }

    /// <summary>
    /// 正式校准样本与实时趋势数据分开保存，防止把快速刷新数据误当成规范样本。
    /// </summary>
    public static class CalibrationRunContext
    {
        private static readonly List<CalibrationSampleRecord> SamplesInternal = new();
        /// <summary>当前正式校准已经记录的只读样本列表。</summary>
        public static IReadOnlyList<CalibrationSampleRecord> Samples => SamplesInternal;
        /// <summary>当前正式校准的开始时间；尚未开始时为空。</summary>
        public static DateTime? StartedAt { get; private set; }

        /// <summary>开始一轮新的正式校准，清除上轮样本并记录开始时间。</summary>
        public static void Begin()
        {
            SamplesInternal.Clear();
            StartedAt = DateTime.Now;
            CalibrationTaskContext.HasCompletedCalibration = false;
        }

        /// <summary>把一组实时快照转换为正式样本并追加到当前校准。</summary>
        public static CalibrationSampleRecord Add(MeasurementSnapshot snapshot, double? dutTemperature, double? dutHumidity)
        {
            CalibrationSampleRecord record = new()
            {
                SampleNumber = SamplesInternal.Count + 1,
                Timestamp = snapshot.Timestamp,
                Snapshot = snapshot,
                DutDisplayTemperature = dutTemperature,
                DutDisplayHumidity = dutHumidity
            };
            SamplesInternal.Add(record);
            return record;
        }

        /// <summary>清除当前正式校准状态，通常用于重新配置任务或手动清空数据。</summary>
        public static void Clear()
        {
            SamplesInternal.Clear();
            StartedAt = null;
            CalibrationTaskContext.HasCompletedCalibration = false;
        }
    }
}
