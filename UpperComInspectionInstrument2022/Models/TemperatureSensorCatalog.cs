using System;
using System.Collections.Generic;
using System.Linq;

namespace UpperComInspectionInstrument2022.Models
{
    /// <summary>
    /// 任务配置与校准工作台共用的温度传感器目录。
    /// Code 用于持久化，界面顺序变化时不会改变已保存任务的含义。
    /// </summary>
    public static class TemperatureSensorCatalog
    {
        // 旧版任务只保存下拉框索引；该映射必须永久保持旧顺序，不能跟随界面排序变化。
        private static IReadOnlyList<string> LegacyCodesByIndex { get; } =
        [
            "PT100_4W",
            "CU50",
            "CU100",
            "TC_K",
            "TC_S",
            "OTHER",
            "TC_E",
            "TC_J",
            "TC_T"
        ];

        /// <summary>传感器持久化代码与界面名称的对应关系。</summary>
        public sealed record Option(string Code, string DisplayName);

        // 前六项保持旧任务配置页的历史顺序，用于迁移尚未保存 Code 的任务文件。
        public static IReadOnlyList<Option> Options { get; } =
        [
            new("PT100_4W", "Pt100"),
            new("CU50", "Cu50"),
            new("CU100", "Cu100"),
            new("TC_K", "K 型热电偶"),
            new("TC_S", "S 型热电偶"),
            new("TC_E", "E 型热电偶"),
            new("TC_J", "J 型热电偶"),
            new("TC_T", "T 型热电偶"),
            new("OTHER", "其他/自定义")
        ];

        /// <summary>提供给下拉框直接绑定的传感器显示名称。</summary>
        public static IReadOnlyList<string> DisplayNames { get; } = Options.Select(option => option.DisplayName).ToArray();

        /// <summary>根据界面下拉框索引取得稳定的持久化代码；索引无效时返回空字符串。</summary>
        public static string GetCode(int index) => index >= 0 && index < Options.Count ? Options[index].Code : string.Empty;

        /// <summary>把尚未保存稳定代码的旧任务索引转换为原目录代码，避免界面重排改变历史任务含义。</summary>
        public static string GetLegacyCode(int index) =>
            index >= 0 && index < LegacyCodesByIndex.Count ? LegacyCodesByIndex[index] : string.Empty;

        /// <summary>根据持久化代码反查界面索引；未找到时返回 -1。</summary>
        public static int GetIndex(string? code)
        {
            if (string.IsNullOrWhiteSpace(code)) return -1;
            for (int index = 0; index < Options.Count; index++)
            {
                if (string.Equals(Options[index].Code, code, StringComparison.OrdinalIgnoreCase)) return index;
            }
            return -1;
        }
    }
}
