using System.Collections.Generic;
using UpperComInspectionInstrument2022.Communication;
using UpperComInspectionInstrument2022.Models;

namespace UpperComInspectionInstrument2022.Services
{
    /// <summary>
    /// 把巡检仪33路物理通道使能快照应用到测量结果。
    /// 巡检仪关闭通道后仍可能在测量寄存器中保留最后一次值，因此不能仅凭寄存器可读就认定为实时数据。
    /// </summary>
    public static class MeasurementChannelEnableService
    {
        /// <summary>
        /// 将关闭通道标记为不可用。原始寄存器和值继续保留用于故障追溯，但不会参与后续计算。
        /// 温度 CH1～CH24 对应快照索引0～23；湿度 H1～H9及其伴随温度对应索引24～32。
        /// </summary>
        public static void Apply(
            IList<InspectionChannelData> channels,
            IReadOnlyList<bool>? channelEnabled)
        {
            if (channelEnabled == null ||
                channelEnabled.Count != InspectionInstrumentProtocol.ConfigurableEnableChannelCount)
                return;

            foreach (InspectionChannelData channel in channels)
            {
                int physicalChannel = channel.PhysicalChannel > 0
                    ? channel.PhysicalChannel
                    : channel.Channel;
                int enableIndex = channel.Role == ChannelRole.PrimaryTemperature
                    ? physicalChannel - 1
                    : InspectionInstrumentProtocol.PhysicalTemperatureChannelCount + physicalChannel - 1;
                if (enableIndex < 0 || enableIndex >= channelEnabled.Count || channelEnabled[enableIndex])
                    continue;

                channel.IsValid = false;
                channel.DataStatus = DataStatus.Disabled;
                channel.Status = "通道已关闭（寄存器为关闭前保留值）";
                channel.HasAppliedCorrection = false;
                channel.CorrectionValue = 0;
            }
        }

        /// <summary>生成已关闭物理通道的简短列表，用于运行日志和工作台提示。</summary>
        public static string FormatDisabledChannels(IReadOnlyList<bool>? channelEnabled)
        {
            if (channelEnabled == null ||
                channelEnabled.Count != InspectionInstrumentProtocol.ConfigurableEnableChannelCount)
                return string.Empty;

            var names = new List<string>();
            for (int index = 0; index < channelEnabled.Count; index++)
            {
                if (channelEnabled[index]) continue;
                names.Add(index < InspectionInstrumentProtocol.PhysicalTemperatureChannelCount
                    ? $"CH{index + 1}"
                    : $"H{index - InspectionInstrumentProtocol.PhysicalTemperatureChannelCount + 1}");
            }
            return string.Join("、", names);
        }
    }
}
