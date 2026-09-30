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
        /// <summary>底层采集请求追溯号。跨暂停和清空保持递增，用于日志及原始文件关联。</summary>
        public long Sequence { get; set; }

        /// <summary>当前工作台内的显示序号。清空数据后归零，下一次成功采集从1重新开始。</summary>
        public long DisplaySequence { get; set; }

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

        /// <summary>
        /// 创建空快照，并初始化通道集合，避免调用方在添加通道前进行空值判断。
        /// </summary>
        public MeasurementSnapshot()
        {
            Channels =
                new List<InspectionChannelData>();
        }
    }
}
