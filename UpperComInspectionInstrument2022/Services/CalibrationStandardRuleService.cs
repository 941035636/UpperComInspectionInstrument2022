using System;

namespace UpperComInspectionInstrument2022.Services
{
    /// <summary>
    /// 两份校准规范中会直接约束任务配置和执行过程的规则。
    /// 这里保存“规范默认值和边界”，不把表 1 的参考技术指标当作合格判据。
    /// </summary>
    public sealed class CalibrationStandardRule
    {
        public string Code { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string ScopeText { get; init; } = string.Empty;
        public string VolumeOptionText { get; init; } = string.Empty;
        public int TemperaturePointCount { get; init; }
        public int HumidityPointCount { get; init; }
        public int TemperatureCenterPoint { get; init; }
        public int HumidityCenterPoint { get; init; }
        public int SampleCount { get; init; }
        public int SampleIntervalSeconds { get; init; }
        public int StableWaitMinutes { get; init; }
        public int MaximumStableWaitMinutes { get; init; }
        public double MinimumAmbientTemperature { get; init; }
        public double MaximumAmbientTemperature { get; init; }
        public double MaximumAmbientHumidity { get; init; }
        public double? MinimumAmbientPressure { get; init; }
        public double? MaximumAmbientPressure { get; init; }
        public double MinimumSetTemperature { get; init; }
        public double MaximumSetTemperature { get; init; }
        public bool SupportsHumidity { get; init; }
        public string[] CalibrationPointOptions { get; init; } = Array.Empty<string>();
        public string[] CalibrationPointRuleTexts { get; init; } = Array.Empty<string>();
        public string[] PointLayoutModeOptions { get; init; } = Array.Empty<string>();
        public string AlternatePointLayoutText { get; init; } = string.Empty;
        public string CustomPointCountLayoutText { get; init; } = string.Empty;
        public int CustomPointCountModeIndex { get; init; } = -1;
        public int[] EditablePointCountModeIndexes { get; init; } = Array.Empty<int>();
        public bool RequiresExtremeVolumeForCustomPointCount { get; init; }
        public bool SupportsCustomPointLayout { get; init; }
        public string PointLayoutText { get; init; } = string.Empty;
        public string SamplingRuleText { get; init; } = string.Empty;
        public string StabilityRuleText { get; init; } = string.Empty;
        public string EnvironmentRuleText { get; init; } = string.Empty;
        public string StandardEquipmentRuleText { get; init; } = string.Empty;
        public string ResultItemsText { get; init; } = string.Empty;
    }

    public static class CalibrationStandardRuleService
    {
        public const int Jjf1101Index = 0;
        public const int Jjf1376Index = 1;

        public static CalibrationStandardRule GetRule(int standardIndex, int volumeIndex, bool includesHumidity)
        {
            return standardIndex == Jjf1376Index
                ? CreateJjf1376(volumeIndex)
                : CreateJjf1101(volumeIndex, includesHumidity);
        }

        private static CalibrationStandardRule CreateJjf1101(int volumeIndex, bool includesHumidity)
        {
            bool large = volumeIndex == 1;
            return new CalibrationStandardRule
            {
                Code = "JJF 1101-2019",
                Title = "环境试验设备温度、湿度参数校准规范",
                ScopeText = "适用范围：温度 -80 ℃～300 ℃；相对湿度 10 %RH～100 %RH。",
                VolumeOptionText = large ? "设备容积大于 2 m³" : "设备容积小于等于 2 m³",
                TemperaturePointCount = large ? 15 : 9,
                HumidityPointCount = includesHumidity ? (large ? 4 : 3) : 0,
                TemperatureCenterPoint = large ? 15 : 5,
                HumidityCenterPoint = includesHumidity ? (large ? 4 : 3) : 0,
                SampleCount = 16,
                SampleIntervalSeconds = 120,
                StableWaitMinutes = 30,
                MaximumStableWaitMinutes = 60,
                MinimumAmbientTemperature = 15,
                MaximumAmbientTemperature = 35,
                MaximumAmbientHumidity = 85,
                MinimumAmbientPressure = 80,
                MaximumAmbientPressure = 106,
                MinimumSetTemperature = -80,
                MaximumSetTemperature = 300,
                SupportsHumidity = true,
                CalibrationPointOptions = new[]
                {
                    "用户常用温湿度点（当前工况）",
                    "使用范围下限/上限/中间点（逐工况）",
                    "客户指定校准点（当前工况）"
                },
                CalibrationPointRuleTexts = new[]
                {
                    includesHumidity
                        ? "按用户实际需要选择常用温度、湿度点；当前执行引擎一次采集一个温湿度工况。"
                        : "按用户实际需要选择常用温度点；当前执行引擎一次采集一个温度工况。",
                    "规范方案包含使用范围下限、上限和中间点；当前任务按其中一个设定值执行，三个工况需分别完成。",
                    "按客户指定值建立当前校准工况，并在偏离/自定义说明中记录依据。"
                },
                PointLayoutModeOptions = new[]
                {
                    "按规范默认空间布点",
                    "按实际工作位置调整（点数可自定义）",
                    "极端容积按需调整点数（<0.05 / >50 m³）"
                },
                SupportsCustomPointLayout = true,
                CustomPointCountModeIndex = 2,
                EditablePointCountModeIndexes = new[] { 1, 2 },
                RequiresExtremeVolumeForCustomPointCount = true,
                PointLayoutText = large
                    ? "上、中、下三层布点：温度 15 点、湿度 4 点；温度 15 点和湿度 O 点位于中层几何中心。各点距内壁为对应边长的 1/10，受风道影响时可加大但不超过 500 mm。湿度通道建议按 A/B/C/O 映射，O 默认 CH4，须按实际接线确认。"
                    : "上、中、下三层布点：温度 9 点、湿度 3 点；温度 5 点和湿度 O 点位于中层几何中心。各点距内壁为对应边长的 1/10，受风道影响时可加大但不超过 500 mm。湿度通道建议按 A/B/O 映射，O 默认 CH3，须按实际接线确认。",
                AlternatePointLayoutText = large
                    ? "可按用户实际工作需要自定义温度/湿度点数、中心点和空间位置；须完整记录点数、各点位置、与内壁距离及调整原因。"
                    : "可按用户实际工作需要自定义温度/湿度点数、中心点和空间位置；有样品架或样品车时，下层点可布置在其上方 10 mm 处，并完整记录调整原因。",
                CustomPointCountLayoutText = large
                    ? "仅当实际工作空间容积大于 50 m³ 时，可按实际需要或用户需求增加测点；必须填写工作区尺寸，并完整记录点数、中心位置和调整原因。"
                    : "仅当实际工作空间容积小于 0.05 m³ 时，可按实际需要或用户需求减少测点；必须填写工作区尺寸，并完整记录点数、中心位置和调整原因。",
                SamplingRuleText = "设备稳定后每 2 min 记录 1 组，30 min 内共 16 组。按设备运行或客户要求调整时，必须在原始记录和证书中说明。",
                StabilityRuleText = "优先按设备说明书判定。说明书未规定时，到达设定值后等待 30 min；仍不稳定可再延长 30 min，总等待不超过 60 min；能够确认已稳定时可提前记录。",
                EnvironmentRuleText = "环境 15 ℃～35 ℃、湿度不大于 85 %RH、气压 80 kPa～106 kPa；应无强烈振动和腐蚀性气体，并避免其他冷热源影响。通常空载，负载校准需说明。",
                StandardEquipmentRuleText = "温度标准不少于 9 通道，宜用四线制铂电阻，分辨力不低于 0.01 ℃；湿度标准不少于 3 通道，分辨力不低于 0.1 %RH；各通道结果应含修正值。",
                ResultItemsText = includesHumidity
                    ? "温度偏差、湿度偏差、温度均匀度、湿度均匀度、温度波动度、湿度波动度及测量不确定度"
                    : "温度偏差、温度均匀度、温度波动度及测量不确定度"
            };
        }

        private static CalibrationStandardRule CreateJjf1376(int volumeIndex)
        {
            bool large = volumeIndex == 1;
            return new CalibrationStandardRule
            {
                Code = "JJF 1376-2012",
                Title = "箱式电阻炉校准规范",
                ScopeText = "适用范围：工作温度不高于 1300 ℃的箱式电阻炉。",
                VolumeOptionText = large ? "测温区容积大于 0.15 m³" : "测温区容积小于等于 0.15 m³",
                TemperaturePointCount = large ? 9 : 5,
                HumidityPointCount = 0,
                TemperatureCenterPoint = large ? 9 : 3,
                HumidityCenterPoint = 0,
                SampleCount = 20,
                SampleIntervalSeconds = 180,
                StableWaitMinutes = 0,
                MaximumStableWaitMinutes = 0,
                MinimumAmbientTemperature = 15,
                MaximumAmbientTemperature = 35,
                MaximumAmbientHumidity = 85,
                MinimumSetTemperature = 0,
                MaximumSetTemperature = 1300,
                SupportsHumidity = false,
                CalibrationPointOptions = new[]
                {
                    "实际常用温度（当前工况）",
                    "最低和最高工作温度（逐工况）",
                    "客户指定温度（当前工况）"
                },
                CalibrationPointRuleTexts = new[]
                {
                    "根据客户要求选择箱式炉实际常用温度；当前执行引擎一次采集一个温度工况。",
                    "规范方案包含箱式炉最低和最高工作温度；当前任务按其中一个设定值执行，两个工况需分别完成。",
                    "按客户指定温度建立当前校准工况，并在偏离/自定义说明中记录依据。"
                },
                PointLayoutModeOptions = new[]
                {
                    "按工作区尺寸确定测温区（图 1）",
                    "按炉膛尺寸确定测温区（图 2）",
                    "按实际测温架/工作位置调整（点数可自定义）"
                },
                SupportsCustomPointLayout = true,
                CustomPointCountModeIndex = 2,
                EditablePointCountModeIndexes = new[] { 2 },
                PointLayoutText = large
                    ? "按生产厂或客户提供的工作区尺寸作为测温区，布置 9 点：8 个端角点，监控点 9 位于距控温热电偶测量端延伸方向不超过 150 mm 处。"
                    : "按生产厂或客户提供的工作区尺寸作为测温区，布置 5 点：几何中心监控点 3，以及前下左、前上右、后上左、后下右四个端角点。",
                AlternatePointLayoutText = large
                    ? "以炉膛尺寸为设计参数，按规范图 2 确定测温区，布置 9 点：8 个端角点和监控点 9；监控点距控温热电偶测量端延伸方向不超过 150 mm。"
                    : "以炉膛尺寸为设计参数，按规范图 2 确定测温区，布置 5 点：中心监控点 3 和四个规定端角点。",
                CustomPointCountLayoutText = "按实际测温架、装载区域或客户工作位置自定义温度测点数、监控点编号及空间位置；必须逐点记录位置，并在偏离/自定义说明中写明不能采用规范 5 点/9 点布置的原因。",
                SamplingRuleText = "达到热稳定后，在 60 min 内每隔 3 min 记录 1 次，至少 20 次；每一轮所有测温点必须在 1 min 内记录完成。",
                StabilityRuleText = "规范要求炉温达到校准温度并处于热稳定状态后开始读数，不规定统一等待分钟数，需由操作人员依据设备状态确认。",
                EnvironmentRuleText = "环境 15 ℃～35 ℃、湿度不大于 85 %RH；应无影响校准的外磁场、强烈振动、强烈气流、高浓度粉尘和腐蚀性物质。通常空载校准。",
                StandardEquipmentRuleText = "测温仪器范围应覆盖 0 ℃～1300 ℃且不低于 0.02 级；廉金属热电偶不低于 1 级、贵金属热电偶不低于 2 级；转换开关寄生电势不大于 1 μV。",
                ResultItemsText = "外观检查、炉温均匀度、炉温稳定度、炉温偏差、炉内最大温差及测量不确定度"
            };
        }

        public static bool IsSetTemperatureInScope(CalibrationStandardRule rule, double value) =>
            double.IsFinite(value) && value >= rule.MinimumSetTemperature && value <= rule.MaximumSetTemperature;

        public static double GetVolumeThreshold(int standardIndex) =>
            standardIndex == Jjf1376Index ? 0.15 : 2;

        public static bool MatchesVolumeClass(int standardIndex, int volumeIndex, double volumeM3)
        {
            if (!double.IsFinite(volumeM3) || volumeM3 <= 0 || volumeIndex is < 0 or > 1) return false;
            double threshold = GetVolumeThreshold(standardIndex);
            return volumeIndex == 0 ? volumeM3 <= threshold : volumeM3 > threshold;
        }

        public static bool AllowsJjf1101PointCountAdjustment(double volumeM3) =>
            double.IsFinite(volumeM3) && volumeM3 > 0 && (volumeM3 < 0.05 || volumeM3 > 50);

        public static bool AllowsCustomPointInput(CalibrationStandardRule rule, int layoutModeIndex) =>
            rule.SupportsCustomPointLayout && Array.IndexOf(rule.EditablePointCountModeIndexes, layoutModeIndex) >= 0;

        public static string GetCalibrationPointRuleText(CalibrationStandardRule rule, int selectionIndex)
        {
            return selectionIndex >= 0 && selectionIndex < rule.CalibrationPointRuleTexts.Length
                ? rule.CalibrationPointRuleTexts[selectionIndex]
                : "请选择本次当前校准点的来源。";
        }

        public static string GetPointLayoutText(CalibrationStandardRule rule, int layoutModeIndex)
        {
            if (layoutModeIndex == rule.CustomPointCountModeIndex &&
                !string.IsNullOrWhiteSpace(rule.CustomPointCountLayoutText))
                return rule.CustomPointCountLayoutText;
            return layoutModeIndex == 1 && !string.IsNullOrWhiteSpace(rule.AlternatePointLayoutText)
                ? rule.AlternatePointLayoutText
                : rule.PointLayoutText;
        }
    }
}
