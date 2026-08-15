using System;
using System.Collections.Generic;

namespace UpperComInspectionInstrument2022.Models
{
    /// <summary>
    /// 一次完整的巡检仪采集快照。
    ///
    /// 一次快照 = 某一个时间点，
    /// 巡检仪 CH01 ~ CH50 的完整数据。
    /// </summary>
    public class MeasurementSnapshot
    {
        /// <summary>
        /// 采集序号
        /// </summary>
        public long Sequence { get; set; }

        /// <summary>
        /// 本次采集时间
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// 本次采集的所有通道数据
        /// </summary>
        public List<InspectionChannelData> Channels { get; set; }

        /// <summary>
        /// 有效通道数量
        /// </summary>
        public int ValidChannelCount { get; set; }

        /// <summary>
        /// 异常通道数量
        /// </summary>
        public int InvalidChannelCount { get; set; }

        public MeasurementSnapshot()
        {
            Channels =
                new List<InspectionChannelData>();
        }
    }
}