using System;
using System.Collections.Generic;
using System.Linq;
using UpperComInspectionInstrument2022.Models;

namespace UpperComInspectionInstrument2022.Services
{
    /// <summary>
    /// 管理校准逻辑测点与巡检仪物理接口之间的映射。
    /// 物理通道先完成协议解析和证书修正，再由本服务投影为 T1、T2、H1 等逻辑测点，
    /// 从而让现有矩阵、公式和报告继续只处理稳定的逻辑点序号。
    /// </summary>
    public static class MeasurementChannelMappingService
    {
        /// <summary>生成或修复指定点数的映射；缺失、越界或重复项自动分配为尚未占用的物理接口。</summary>
        public static List<int> Normalize(IEnumerable<int>? source, int pointCount, int physicalChannelCount)
        {
            if (pointCount < 0 || pointCount > physicalChannelCount)
                throw new ArgumentOutOfRangeException(nameof(pointCount));

            List<int> input = source?.ToList() ?? new List<int>();
            List<int> result = new(pointCount);
            HashSet<int> used = new();
            for (int logicalPoint = 0; logicalPoint < pointCount; logicalPoint++)
            {
                int requested = logicalPoint < input.Count ? input[logicalPoint] : 0;
                if (requested >= 1 && requested <= physicalChannelCount && used.Add(requested))
                {
                    result.Add(requested);
                    continue;
                }

                int fallback = Enumerable.Range(1, physicalChannelCount).First(channel => !used.Contains(channel));
                used.Add(fallback);
                result.Add(fallback);
            }
            return result;
        }

        /// <summary>返回完成当前映射所需读取的最大物理通道，避免按每个离散通道分别发送请求。</summary>
        public static int GetRequiredReadChannelCount(IEnumerable<int>? mapping, int logicalPointCount, int maximum)
        {
            List<int> normalized = Normalize(mapping, logicalPointCount, maximum);
            return normalized.Count == 0 ? 0 : normalized.Max();
        }

        /// <summary>
        /// 把一组物理通道数据转换为任务逻辑测点。
        /// 未映射的中间物理通道不会进入后续矩阵和报告；湿度探头伴随温度跟随相同湿度接口映射。
        /// </summary>
        public static List<InspectionChannelData> ApplyTaskMapping(
            IEnumerable<InspectionChannelData> physicalChannels,
            IReadOnlyList<int> temperatureMapping,
            IReadOnlyList<int> humidityMapping)
        {
            Dictionary<int, int> temperatureLogicalByPhysical = temperatureMapping
                .Select((physical, index) => new { physical, logical = index + 1 })
                .ToDictionary(item => item.physical, item => item.logical);
            Dictionary<int, int> humidityLogicalByPhysical = humidityMapping
                .Select((physical, index) => new { physical, logical = index + 1 })
                .ToDictionary(item => item.physical, item => item.logical);

            List<InspectionChannelData> result = new();
            foreach (InspectionChannelData source in physicalChannels)
            {
                int physical = source.PhysicalChannel > 0 ? source.PhysicalChannel : source.Channel;
                int logical;
                if (source.Role == ChannelRole.PrimaryTemperature)
                {
                    if (!temperatureLogicalByPhysical.TryGetValue(physical, out logical)) continue;
                }
                else
                {
                    if (!humidityLogicalByPhysical.TryGetValue(physical, out logical)) continue;
                }

                result.Add(CloneWithLogicalPoint(source, logical, physical));
            }

            return result
                .OrderBy(channel => channel.Role)
                .ThenBy(channel => channel.Channel)
                .ToList();
        }

        /// <summary>生成便于任务页面和归档查看的映射摘要。</summary>
        public static string FormatSummary(IReadOnlyList<int> temperatureMapping, IReadOnlyList<int> humidityMapping)
        {
            IEnumerable<string> temperature = temperatureMapping.Select((physical, index) => $"T{index + 1}→CH{physical}");
            IEnumerable<string> humidity = humidityMapping.Select((physical, index) => $"H{index + 1}→H{physical}");
            return string.Join("；", temperature.Concat(humidity));
        }

        private static InspectionChannelData CloneWithLogicalPoint(InspectionChannelData source, int logicalPoint, int physicalChannel)
        {
            return new InspectionChannelData
            {
                Channel = logicalPoint,
                PhysicalChannel = physicalChannel,
                Type = source.Type,
                Role = source.Role,
                Value = source.Value,
                RawValue = source.RawValue,
                CorrectionValue = source.CorrectionValue,
                HasAppliedCorrection = source.HasAppliedCorrection,
                Unit = source.Unit,
                RegisterAddress1 = source.RegisterAddress1,
                RegisterAddress2 = source.RegisterAddress2,
                Register1 = source.Register1,
                Register2 = source.Register2,
                RawBytes = source.RawBytes.ToArray(),
                RawHex = source.RawHex,
                DataStatus = source.DataStatus,
                Status = source.Status,
                Timestamp = source.Timestamp,
                AcquisitionId = source.AcquisitionId,
                IsValid = source.IsValid
            };
        }
    }
}
