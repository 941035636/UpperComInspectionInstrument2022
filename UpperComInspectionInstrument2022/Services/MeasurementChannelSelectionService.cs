using System.Collections.Generic;
using System.Linq;
using UpperComInspectionInstrument2022.Models;

namespace UpperComInspectionInstrument2022.Services
{
    /// <summary>统一定义哪些物理通道属于本次校准任务，避免 UI、修正和结果计算各自筛选。</summary>
    public static class MeasurementChannelSelectionService
    {
        public static bool IsCalibrationChannel(InspectionChannelData channel, ChannelType type)
        {
            return type == ChannelType.Temperature
                ? channel.Role == ChannelRole.PrimaryTemperature
                : channel.Role == ChannelRole.Humidity;
        }

        public static List<InspectionChannelData> SelectRequired(
            IEnumerable<InspectionChannelData> channels,
            int temperaturePointCount,
            int humidityPointCount)
        {
            return channels
                .Where(channel =>
                    (channel.Role == ChannelRole.PrimaryTemperature && channel.Channel >= 1 && channel.Channel <= temperaturePointCount) ||
                    (channel.Role == ChannelRole.Humidity && channel.Channel >= 1 && channel.Channel <= humidityPointCount))
                .OrderBy(channel => channel.Role)
                .ThenBy(channel => channel.Channel)
                .ToList();
        }

        public static List<InspectionChannelData> SelectRequired(
            IEnumerable<InspectionChannelData> channels,
            ChannelType type,
            int pointCount,
            bool validOnly)
        {
            return channels
                .Where(channel => IsCalibrationChannel(channel, type) &&
                                  channel.Channel >= 1 && channel.Channel <= pointCount &&
                                  (!validOnly || channel.IsValid))
                .OrderBy(channel => channel.Channel)
                .ToList();
        }
    }
}
