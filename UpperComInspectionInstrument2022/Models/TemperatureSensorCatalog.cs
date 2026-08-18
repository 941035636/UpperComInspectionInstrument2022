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
        public sealed record Option(string Code, string DisplayName);

        // 前六项保持旧任务配置页的历史顺序，用于迁移尚未保存 Code 的任务文件。
        public static IReadOnlyList<Option> Options { get; } =
        [
            new("PT100_4W", "四线制 Pt100"),
            new("CU50", "Cu50"),
            new("CU100", "Cu100"),
            new("TC_K", "K 型热电偶"),
            new("TC_S", "S 型热电偶"),
            new("OTHER", "其他/自定义"),
            new("TC_E", "E 型热电偶"),
            new("TC_J", "J 型热电偶"),
            new("TC_T", "T 型热电偶")
        ];

        public static IReadOnlyList<string> DisplayNames { get; } = Options.Select(option => option.DisplayName).ToArray();

        public static string GetCode(int index) => index >= 0 && index < Options.Count ? Options[index].Code : string.Empty;

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
