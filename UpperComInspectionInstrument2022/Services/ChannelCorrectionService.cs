using System;
using System.Collections.Generic;
using UpperComInspectionInstrument2022.Models;

namespace UpperComInspectionInstrument2022.Services
{
    /// <summary>
    /// 解析并应用标准器证书给出的逐通道修正值。
    /// 修正值只作用于本次任务选用的主温度或湿度通道，不修改协议原始寄存器数据。
    /// </summary>
    public static class ChannelCorrectionService
    {
        /// <summary>
        /// 将“通道号:修正值”文本解析为字典，例如“1:0.02,2:-0.01”。
        /// </summary>
        public static bool TryParse(string text, int maximumChannel, out Dictionary<int, double> corrections, out string error)
        {
            corrections = new Dictionary<int, double>();
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(text)) return true;

            string[] entries = text.Replace('，', ',').Replace('；', ';')
                .Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (string entry in entries)
            {
                string[] pair = entry.Split(':', 2, StringSplitOptions.TrimEntries);
                if (pair.Length != 2 || !int.TryParse(pair[0], out int channel) || channel < 1 || channel > maximumChannel ||
                    !double.TryParse(pair[1], out double correction) || !double.IsFinite(correction))
                {
                    error = $"修正值“{entry}”格式不正确，应使用 通道号:修正值，例如 1:0.02。";
                    corrections.Clear();
                    return false;
                }
                if (!corrections.TryAdd(channel, correction))
                {
                    error = $"通道 {channel} 的修正值重复。";
                    corrections.Clear();
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// 保存每个通道的原始值，并把对应证书修正值叠加到有效测量值上。
        /// </summary>
        public static void Apply(List<InspectionChannelData> channels, string temperatureText, string humidityText)
        {
            TryParse(temperatureText, 50, out Dictionary<int, double> temperatureCorrections, out _);
            TryParse(humidityText, 10, out Dictionary<int, double> humidityCorrections, out _);
            foreach (InspectionChannelData channel in channels)
            {
                channel.RawValue = channel.Value;
                Dictionary<int, double>? source = channel.Role switch
                {
                    ChannelRole.PrimaryTemperature => temperatureCorrections,
                    ChannelRole.Humidity => humidityCorrections,
                    _ => null
                };
                if (source == null) continue;
                if (!channel.IsValid || !source.TryGetValue(channel.Channel, out double correction)) continue;
                channel.CorrectionValue = correction;
                channel.Value += correction;
                channel.HasAppliedCorrection = true;
            }
        }
    }
}
