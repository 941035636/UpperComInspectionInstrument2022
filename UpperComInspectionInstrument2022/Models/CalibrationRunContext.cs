using System;
using System.Collections.Generic;

namespace UpperComInspectionInstrument2022.Models
{
    public sealed class CalibrationSampleRecord
    {
        public int SampleNumber { get; init; }
        public DateTime Timestamp { get; init; }
        public MeasurementSnapshot Snapshot { get; init; } = new();
        public double? DutDisplayTemperature { get; init; }
        public double? DutDisplayHumidity { get; init; }
    }

    /// <summary>
    /// 正式校准样本与实时趋势数据分开保存，防止把快速刷新数据误当成规范样本。
    /// </summary>
    public static class CalibrationRunContext
    {
        private static readonly List<CalibrationSampleRecord> SamplesInternal = new();
        public static IReadOnlyList<CalibrationSampleRecord> Samples => SamplesInternal;
        public static DateTime? StartedAt { get; private set; }

        public static void Begin()
        {
            SamplesInternal.Clear();
            StartedAt = DateTime.Now;
            CalibrationTaskContext.HasCompletedCalibration = false;
        }

        public static void Add(MeasurementSnapshot snapshot, double? dutTemperature, double? dutHumidity)
        {
            SamplesInternal.Add(new CalibrationSampleRecord
            {
                SampleNumber = SamplesInternal.Count + 1,
                Timestamp = snapshot.Timestamp,
                Snapshot = snapshot,
                DutDisplayTemperature = dutTemperature,
                DutDisplayHumidity = dutHumidity
            });
        }

        public static void Clear()
        {
            SamplesInternal.Clear();
            StartedAt = null;
            CalibrationTaskContext.HasCompletedCalibration = false;
        }
    }
}
