using System;
using System.Collections.Generic;
using System.Linq;
using UpperComInspectionInstrument2022.Models;

namespace UpperComInspectionInstrument2022.Services
{
    /// <summary>
    /// 不确定度预算中的一个标准不确定度分量。输入量、除数和灵敏系数均保留，便于从最终 U 值反查计算过程。
    /// </summary>
    public sealed class UncertaintyComponentDetail
    {
        public string Symbol { get; init; } = string.Empty;
        public string Source { get; init; } = string.Empty;
        public string Category { get; init; } = string.Empty;
        public string Distribution { get; init; } = string.Empty;
        public double InputValue { get; init; }
        public string Unit { get; init; } = string.Empty;
        public double Divisor { get; init; } = 1;
        public string DivisorExpression { get; init; } = "1";
        public double StandardUncertainty { get; init; }
        public double SensitivityCoefficient { get; init; } = 1;
        public double Contribution { get; init; }
        public string Basis { get; init; } = string.Empty;
    }

    /// <summary>
    /// 一个结果项目对应的完整不确定度预算，包含评定点、各分量、合成标准不确定度和扩展不确定度。
    /// </summary>
    public sealed class UncertaintyBudgetSummary
    {
        public string ResultItem { get; init; } = string.Empty;
        public string EvaluationPoint { get; init; } = string.Empty;
        public string Unit { get; init; } = string.Empty;
        public IReadOnlyList<UncertaintyComponentDetail> Components { get; init; } = Array.Empty<UncertaintyComponentDetail>();
        public double CombinedStandardUncertainty { get; init; }
        public double CoverageFactor { get; init; } = 2;
        public double ExpandedUncertainty { get; init; }
        public string Basis { get; init; } = string.Empty;
    }

    /// <summary>
    /// 一轮正式校准的计算结果。环境试验设备与箱式电阻炉使用不同字段组。
    /// <see cref="IsValid"/> 为 false 时应先向用户显示 <see cref="Message"/>，不要使用数值字段。
    /// </summary>
    public sealed class CalibrationResultSummary
    {
        public bool IsValid { get; init; }
        public string Message { get; init; } = string.Empty;
        public double TemperatureUpperDeviation { get; init; }
        public double TemperatureLowerDeviation { get; init; }
        public double TemperatureUniformity { get; init; }
        public double TemperatureFluctuation { get; init; }
        public double HumidityUpperDeviation { get; init; }
        public double HumidityLowerDeviation { get; init; }
        public double HumidityUniformity { get; init; }
        public double HumidityFluctuation { get; init; }
        public double TemperatureExpandedUncertainty { get; init; }
        public double HumidityExpandedUncertainty { get; init; }
        public double FurnaceUniformityUpper { get; init; }
        public double FurnaceUniformityLower { get; init; }
        public double FurnaceStabilityUpper { get; init; }
        public double FurnaceStabilityLower { get; init; }
        public double FurnaceDeviationUpper { get; init; }
        public double FurnaceDeviationLower { get; init; }
        public double FurnaceMaximumDifference { get; init; }
        public double FurnaceUniformityUpperUncertainty { get; init; }
        public double FurnaceUniformityLowerUncertainty { get; init; }
        public IReadOnlyList<UncertaintyBudgetSummary> UncertaintyBudgets { get; init; } = Array.Empty<UncertaintyBudgetSummary>();
    }

    /// <summary>
    /// 将正式样本矩阵按 JJF 1101-2019 或 JJF 1376-2012 的计算路径转换为结果摘要。
    /// 该类不负责“合格/不合格”判定，因为规范中的参考指标不能直接替代被校设备技术要求。
    /// </summary>
    public static class CalibrationResultCalculator
    {
        /// <summary>
        /// 校验正式样本数量后，根据任务规范选择环境设备或箱式炉计算流程。
        /// </summary>
        public static CalibrationResultSummary Calculate()
        {
            IReadOnlyList<CalibrationSampleRecord> records = CalibrationRunContext.Samples;
            if (records.Count < CalibrationTaskContext.PlannedCount)
                return new CalibrationResultSummary { Message = $"正式样本不足：{records.Count}/{CalibrationTaskContext.PlannedCount} 组。" };

            return CalibrationTaskContext.StandardIndex == 1
                ? CalculateFurnace(records)
                : CalculateEnvironmentEquipment(records);
        }

        /// <summary>
        /// 计算环境试验设备的偏差、均匀度、波动度和扩展不确定度。
        /// </summary>
        private static CalibrationResultSummary CalculateEnvironmentEquipment(IReadOnlyList<CalibrationSampleRecord> records)
        {
            if (!TryGetMatrix(records, ChannelType.Temperature, CalibrationTaskContext.TemperaturePointCount, out List<double[]> temperatures, out string error))
                return new CalibrationResultSummary { Message = error };

            List<double[]> humidities = new();
            if (CalibrationTaskContext.IncludesHumidity &&
                !TryGetMatrix(records, ChannelType.Humidity, CalibrationTaskContext.HumidityPointCount, out humidities, out error))
                return new CalibrationResultSummary { Message = error };

            double setTemperature = CalibrationTaskContext.SetTemperature ?? 0;
            double temperatureMaximum = temperatures.SelectMany(x => x).Max();
            double temperatureMinimum = temperatures.SelectMany(x => x).Min();
            // 选择所有采样中出现过最高值的空间点，作为温度不确定度的保守评定点。
            int temperatureMaximumPoint = Enumerable.Range(0, temperatures[0].Length)
                .OrderByDescending(point => temperatures.Max(row => row[point])).First();
            UncertaintyBudgetSummary temperatureBudget = CalculateEnvironmentUncertaintyBudget(
                temperatures.Select(row => row[temperatureMaximumPoint]),
                CalibrationTaskContext.ReferencedTemperatureResolution,
                CalibrationTaskContext.ReferencedTemperatureUncertainty,
                CalibrationTaskContext.ReferencedTemperatureCoverage,
                CalibrationTaskContext.ReferencedTemperatureStabilityChange,
                "温度偏差（上、下偏差同源）",
                $"温度{temperatureMaximumPoint + 1}",
                "℃");
            CalibrationResultSummary temperatureResult = new()
            {
                IsValid = true,
                TemperatureUpperDeviation = temperatureMaximum - setTemperature,
                TemperatureLowerDeviation = temperatureMinimum - setTemperature,
                TemperatureUniformity = temperatures.Average(x => x.Max() - x.Min()),
                TemperatureFluctuation = CalculateMaximumHalfRange(temperatures),
                TemperatureExpandedUncertainty = temperatureBudget.ExpandedUncertainty,
                UncertaintyBudgets = new[] { temperatureBudget }
            };
            if (!CalibrationTaskContext.IncludesHumidity) return temperatureResult;

            double setHumidity = CalibrationTaskContext.SetHumidity ?? 0;
            int humidityMaximumPoint = Enumerable.Range(0, humidities[0].Length)
                .OrderByDescending(point => humidities.Max(row => row[point])).First();
            UncertaintyBudgetSummary humidityBudget = CalculateEnvironmentUncertaintyBudget(
                humidities.Select(row => row[humidityMaximumPoint]),
                CalibrationTaskContext.ReferencedHumidityResolution,
                CalibrationTaskContext.ReferencedHumidityUncertainty,
                CalibrationTaskContext.ReferencedHumidityCoverage,
                CalibrationTaskContext.ReferencedHumidityStabilityChange,
                "相对湿度偏差（上、下偏差同源）",
                $"湿度{humidityMaximumPoint + 1}",
                "%RH");
            return new CalibrationResultSummary
            {
                IsValid = true,
                TemperatureUpperDeviation = temperatureResult.TemperatureUpperDeviation,
                TemperatureLowerDeviation = temperatureResult.TemperatureLowerDeviation,
                TemperatureUniformity = temperatureResult.TemperatureUniformity,
                TemperatureFluctuation = temperatureResult.TemperatureFluctuation,
                TemperatureExpandedUncertainty = temperatureResult.TemperatureExpandedUncertainty,
                HumidityUpperDeviation = humidities.SelectMany(x => x).Max() - setHumidity,
                HumidityLowerDeviation = humidities.SelectMany(x => x).Min() - setHumidity,
                HumidityUniformity = humidities.Average(x => x.Max() - x.Min()),
                HumidityFluctuation = CalculateMaximumHalfRange(humidities),
                HumidityExpandedUncertainty = humidityBudget.ExpandedUncertainty,
                UncertaintyBudgets = new[] { temperatureBudget, humidityBudget }
            };
        }

        /// <summary>
        /// 计算箱式炉的炉温均匀度、稳定度、偏差、最大温差及均匀度不确定度。
        /// </summary>
        private static CalibrationResultSummary CalculateFurnace(IReadOnlyList<CalibrationSampleRecord> records)
        {
            if (!TryGetMatrix(records, ChannelType.Temperature, CalibrationTaskContext.TemperaturePointCount, out List<double[]> temperatures, out string error))
                return new CalibrationResultSummary { Message = error };
            int centerIndex = CalibrationTaskContext.TemperatureCenterPoint - 1;
            if (centerIndex < 0 || centerIndex >= CalibrationTaskContext.TemperaturePointCount)
                return new CalibrationResultSummary { Message = "中心（监控）点不在已配置温度测点范围内。" };

            // 先按空间点计算多次采样平均值，再与监控点平均值或名义温度比较。
            double[] pointAverages = Enumerable.Range(0, CalibrationTaskContext.TemperaturePointCount)
                .Select(i => temperatures.Average(row => row[i])).ToArray();
            double centerActual = pointAverages[centerIndex];
            double[] centerValues = temperatures.Select(row => row[centerIndex]).ToArray();
            double centerAverage = centerValues.Average();
            double nominal = CalibrationTaskContext.SetTemperature ?? 0;
            int maximumPoint = Array.IndexOf(pointAverages, pointAverages.Max());
            int minimumPoint = Array.IndexOf(pointAverages, pointAverages.Min());
            double coverage = CalibrationTaskContext.ReferencedTemperatureCoverage > 0 ? CalibrationTaskContext.ReferencedTemperatureCoverage : 2;
            UncertaintyBudgetSummary upperBudget = CalculateFurnaceUniformityBudget(
                temperatures, maximumPoint, centerIndex, coverage, "炉温均匀度上偏差", "最高点");
            UncertaintyBudgetSummary lowerBudget = CalculateFurnaceUniformityBudget(
                temperatures, minimumPoint, centerIndex, coverage, "炉温均匀度下偏差", "最低点");
            return new CalibrationResultSummary
            {
                IsValid = true,
                FurnaceUniformityUpper = pointAverages.Max() - centerActual,
                FurnaceUniformityLower = pointAverages.Min() - centerActual,
                FurnaceStabilityUpper = centerValues.Max() - centerAverage,
                FurnaceStabilityLower = centerValues.Min() - centerAverage,
                FurnaceDeviationUpper = pointAverages.Max() - nominal,
                FurnaceDeviationLower = pointAverages.Min() - nominal,
                FurnaceMaximumDifference = temperatures.Max(row => row.Max() - row.Min()),
                FurnaceUniformityUpperUncertainty = upperBudget.ExpandedUncertainty,
                FurnaceUniformityLowerUncertainty = lowerBudget.ExpandedUncertainty,
                UncertaintyBudgets = new[] { upperBudget, lowerBudget }
            };
        }

        /// <summary>
        /// 把样本集合整理成“每行一组采样、每列一个空间测点”的规则矩阵。
        /// 任意一组缺少必需通道都会失败，防止错位数据进入规范公式。
        /// </summary>
        private static bool TryGetMatrix(IReadOnlyList<CalibrationSampleRecord> records, ChannelType type, int pointCount,
            out List<double[]> matrix, out string error)
        {
            matrix = new List<double[]>();
            foreach (CalibrationSampleRecord record in records)
            {
                Dictionary<int, double> values = record.Snapshot.Channels
                    .Where(c => MeasurementChannelSelectionService.IsCalibrationChannel(c, type) &&
                                c.IsValid && c.Channel >= 1 && c.Channel <= pointCount)
                    .GroupBy(c => c.Channel)
                    .ToDictionary(g => g.Key, g => g.First().Value);
                if (values.Count != pointCount)
                {
                    string name = type == ChannelType.Temperature ? "温度" : "湿度";
                    error = $"第 {record.SampleNumber} 组{name}有效测点不足：{values.Count}/{pointCount}。";
                    matrix.Clear();
                    return false;
                }
                matrix.Add(Enumerable.Range(1, pointCount).Select(channel => values[channel]).ToArray());
            }
            error = string.Empty;
            return true;
        }

        /// <summary>计算所有测点中最大的半极差，用于环境设备波动度。</summary>
        private static double CalculateMaximumHalfRange(IReadOnlyList<double[]> matrix)
        {
            int points = matrix[0].Length;
            double maximum = 0;
            for (int point = 0; point < points; point++)
            {
                double max = matrix.Max(row => row[point]);
                double min = matrix.Min(row => row[point]);
                maximum = Math.Max(maximum, (max - min) / 2.0);
            }
            return maximum;
        }

        /// <summary>
        /// 按 JJF 1101-2019 附录 C 建立温度或湿度偏差预算，并计算合成及扩展不确定度。
        /// </summary>
        private static UncertaintyBudgetSummary CalculateEnvironmentUncertaintyBudget(
            IEnumerable<double> repeatedValues,
            double resolution,
            double certificateUncertainty,
            double coverageFactor,
            double stabilityChange,
            string resultItem,
            string evaluationPoint,
            string unit)
        {
            double[] values = repeatedValues.ToArray();
            double coverage = coverageFactor > 0 ? coverageFactor : 2;
            List<UncertaintyComponentDetail> components = new()
            {
                CreateComponent(
                    "u1", "测量重复性", "A类", "正态",
                    SampleStandardDeviation(values), unit, 1, "1", 1,
                    "JJF 1101-2019 附录C C.5.1：重复测量样本标准偏差 s"),
                CreateComponent(
                    "u2", "标准器分辨力", "B类", "矩形",
                    Math.Max(0, resolution), unit, 2 * Math.Sqrt(3), "2×√3", 1,
                    "JJF 1101-2019 附录C C.5.2：分辨力 d 的半宽 d/2 按矩形分布"),
                CreateComponent(
                    "u3", "标准器修正值", "B类", "证书给定",
                    Math.Max(0, certificateUncertainty), unit, coverage, "k", 1,
                    "JJF 1101-2019 附录C C.5.3：证书扩展不确定度 U 按包含因子 k 折算"),
                CreateComponent(
                    "u4", "标准器稳定性", "B类", "矩形",
                    Math.Max(0, stabilityChange), unit, Math.Sqrt(3), "√3", 1,
                    "JJF 1101-2019 附录C C.5.4：相邻两次修正值最大变化按矩形分布")
            };
            return CreateBudget(
                resultItem,
                evaluationPoint,
                unit,
                components,
                coverage,
                "JJF 1101-2019 附录C C.7、C.8：各独立分量方和根合成，U=k×uc");
        }

        /// <summary>
        /// 按 JJF 1376-2012 附录 D，把极值点与中心监控点的输入分量展开为炉温均匀度预算。
        /// </summary>
        private static UncertaintyBudgetSummary CalculateFurnaceUniformityBudget(
            IReadOnlyList<double[]> matrix,
            int extremePointIndex,
            int centerPointIndex,
            double coverage,
            string resultItem,
            string extremePointName)
        {
            if (extremePointIndex == centerPointIndex)
            {
                UncertaintyComponentDetail cancellation = CreateComponent(
                    $"u(T{centerPointIndex + 1}-T{centerPointIndex + 1})",
                    $"{extremePointName}与中心点为同一测点",
                    "计算恒等",
                    "完全相关抵消",
                    0,
                    "℃",
                    1,
                    "1",
                    1,
                    "极值点与中心监控点相同，被测量为同一输入量减自身，量值及其不确定度完全抵消");
                return CreateBudget(
                    resultItem,
                    $"中心点 T{centerPointIndex + 1} - 中心点 T{centerPointIndex + 1}",
                    "℃",
                    new[] { cancellation },
                    coverage,
                    "同一测点相减恒为 0，不适用不同测点独立分量的方和根合成");
            }

            List<UncertaintyComponentDetail> components = new();
            components.AddRange(CreateFurnacePointComponents(matrix, extremePointIndex, extremePointName, 1, coverage));
            components.AddRange(CreateFurnacePointComponents(matrix, centerPointIndex, "中心点", -1, coverage));
            return CreateBudget(
                resultItem,
                $"{extremePointName} T{extremePointIndex + 1} - 中心点 T{centerPointIndex + 1}",
                "℃",
                components,
                coverage,
                "JJF 1376-2012 附录D D.3～D.5：极值点与中心点输入量方和根合成，U=k×uc");
        }

        /// <summary>生成箱式炉某个空间点的重复性均值分量和证书修正分量。</summary>
        private static IEnumerable<UncertaintyComponentDetail> CreateFurnacePointComponents(
            IReadOnlyList<double[]> matrix,
            int pointIndex,
            string pointName,
            double sensitivityCoefficient,
            double coverage)
        {
            double[] values = matrix.Select(row => row[pointIndex]).ToArray();
            string point = $"{pointName} T{pointIndex + 1}";
            yield return CreateComponent(
                $"uA(T{pointIndex + 1})", $"{point}重复测量平均值", "A类", "正态",
                SampleStandardDeviation(values), "℃", Math.Sqrt(values.Length), "√n", sensitivityCoefficient,
                "JJF 1376-2012 附录D D.4/D.5：平均值标准不确定度 s/√n");
            yield return CreateComponent(
                $"uB(T{pointIndex + 1})", $"{point}标准器修正值", "B类", "证书给定",
                Math.Max(0, CalibrationTaskContext.ReferencedTemperatureUncertainty), "℃", coverage, "k", sensitivityCoefficient,
                "JJF 1376-2012 附录D D.4/D.5：证书扩展不确定度 U 按包含因子 k 折算");
        }

        /// <summary>按输入量和除数计算单个标准不确定度分量及其带符号贡献。</summary>
        private static UncertaintyComponentDetail CreateComponent(
            string symbol,
            string source,
            string category,
            string distribution,
            double inputValue,
            string unit,
            double divisor,
            string divisorExpression,
            double sensitivityCoefficient,
            string basis)
        {
            double safeDivisor = divisor > 0 ? divisor : 1;
            double standardUncertainty = inputValue / safeDivisor;
            return new UncertaintyComponentDetail
            {
                Symbol = symbol,
                Source = source,
                Category = category,
                Distribution = distribution,
                InputValue = inputValue,
                Unit = unit,
                Divisor = safeDivisor,
                DivisorExpression = divisorExpression,
                StandardUncertainty = standardUncertainty,
                SensitivityCoefficient = sensitivityCoefficient,
                Contribution = sensitivityCoefficient * standardUncertainty,
                Basis = basis
            };
        }

        /// <summary>把若干独立贡献按方和根合成，并乘包含因子得到扩展不确定度。</summary>
        private static UncertaintyBudgetSummary CreateBudget(
            string resultItem,
            string evaluationPoint,
            string unit,
            IReadOnlyList<UncertaintyComponentDetail> components,
            double coverage,
            string basis)
        {
            double combined = Math.Sqrt(components.Sum(component => component.Contribution * component.Contribution));
            return new UncertaintyBudgetSummary
            {
                ResultItem = resultItem,
                EvaluationPoint = evaluationPoint,
                Unit = unit,
                Components = components,
                CombinedStandardUncertainty = combined,
                CoverageFactor = coverage,
                ExpandedUncertainty = coverage * combined,
                Basis = basis
            };
        }

        /// <summary>使用 n-1 分母计算样本标准偏差；少于两次测量时返回 0。</summary>
        private static double SampleStandardDeviation(IReadOnlyList<double> values)
        {
            if (values.Count < 2) return 0;
            double average = values.Average();
            double sum = values.Sum(value => (value - average) * (value - average));
            return Math.Sqrt(sum / (values.Count - 1));
        }
    }
}
