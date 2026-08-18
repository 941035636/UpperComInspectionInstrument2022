using System;
using System.Collections.Generic;
using System.Linq;
using UpperComInspectionInstrument2022.Models;

namespace UpperComInspectionInstrument2022.Services
{
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
    }

    public static class CalibrationResultCalculator
    {
        public static CalibrationResultSummary Calculate()
        {
            IReadOnlyList<CalibrationSampleRecord> records = CalibrationRunContext.Samples;
            if (records.Count < CalibrationTaskContext.PlannedCount)
                return new CalibrationResultSummary { Message = $"正式样本不足：{records.Count}/{CalibrationTaskContext.PlannedCount} 组。" };

            return CalibrationTaskContext.StandardIndex == 1
                ? CalculateFurnace(records)
                : CalculateEnvironmentEquipment(records);
        }

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
            int temperatureMaximumPoint = Enumerable.Range(0, temperatures[0].Length)
                .OrderByDescending(point => temperatures.Max(row => row[point])).First();
            CalibrationResultSummary temperatureResult = new()
            {
                IsValid = true,
                TemperatureUpperDeviation = temperatureMaximum - setTemperature,
                TemperatureLowerDeviation = temperatureMinimum - setTemperature,
                TemperatureUniformity = temperatures.Average(x => x.Max() - x.Min()),
                TemperatureFluctuation = CalculateMaximumHalfRange(temperatures),
                TemperatureExpandedUncertainty = CalculateEnvironmentExpandedUncertainty(
                    temperatures.Select(row => row[temperatureMaximumPoint]),
                    CalibrationTaskContext.ReferencedTemperatureResolution,
                    CalibrationTaskContext.ReferencedTemperatureUncertainty,
                    CalibrationTaskContext.ReferencedTemperatureCoverage,
                    CalibrationTaskContext.ReferencedTemperatureStabilityChange)
            };
            if (!CalibrationTaskContext.IncludesHumidity) return temperatureResult;

            double setHumidity = CalibrationTaskContext.SetHumidity ?? 0;
            int humidityMaximumPoint = Enumerable.Range(0, humidities[0].Length)
                .OrderByDescending(point => humidities.Max(row => row[point])).First();
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
                HumidityExpandedUncertainty = CalculateEnvironmentExpandedUncertainty(
                    humidities.Select(row => row[humidityMaximumPoint]),
                    CalibrationTaskContext.ReferencedHumidityResolution,
                    CalibrationTaskContext.ReferencedHumidityUncertainty,
                    CalibrationTaskContext.ReferencedHumidityCoverage,
                    CalibrationTaskContext.ReferencedHumidityStabilityChange)
            };
        }

        private static CalibrationResultSummary CalculateFurnace(IReadOnlyList<CalibrationSampleRecord> records)
        {
            if (!TryGetMatrix(records, ChannelType.Temperature, CalibrationTaskContext.TemperaturePointCount, out List<double[]> temperatures, out string error))
                return new CalibrationResultSummary { Message = error };
            int centerIndex = CalibrationTaskContext.TemperatureCenterPoint - 1;
            if (centerIndex < 0 || centerIndex >= CalibrationTaskContext.TemperaturePointCount)
                return new CalibrationResultSummary { Message = "中心（监控）点不在已配置温度测点范围内。" };

            double[] pointAverages = Enumerable.Range(0, CalibrationTaskContext.TemperaturePointCount)
                .Select(i => temperatures.Average(row => row[i])).ToArray();
            double centerActual = pointAverages[centerIndex];
            double[] centerValues = temperatures.Select(row => row[centerIndex]).ToArray();
            double centerAverage = centerValues.Average();
            double nominal = CalibrationTaskContext.SetTemperature ?? 0;
            int maximumPoint = Array.IndexOf(pointAverages, pointAverages.Max());
            int minimumPoint = Array.IndexOf(pointAverages, pointAverages.Min());
            double centerInputUncertainty = CalculateFurnaceInputUncertainty(temperatures, centerIndex);
            double maximumInputUncertainty = CalculateFurnaceInputUncertainty(temperatures, maximumPoint);
            double minimumInputUncertainty = CalculateFurnaceInputUncertainty(temperatures, minimumPoint);
            double coverage = CalibrationTaskContext.ReferencedTemperatureCoverage > 0 ? CalibrationTaskContext.ReferencedTemperatureCoverage : 2;
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
                FurnaceUniformityUpperUncertainty = coverage * Math.Sqrt(maximumInputUncertainty * maximumInputUncertainty + centerInputUncertainty * centerInputUncertainty),
                FurnaceUniformityLowerUncertainty = coverage * Math.Sqrt(minimumInputUncertainty * minimumInputUncertainty + centerInputUncertainty * centerInputUncertainty)
            };
        }

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

        private static double CalculateEnvironmentExpandedUncertainty(IEnumerable<double> repeatedValues, double resolution,
            double certificateUncertainty, double coverageFactor, double stabilityChange)
        {
            double[] values = repeatedValues.ToArray();
            double repeatability = SampleStandardDeviation(values);
            double resolutionComponent = resolution > 0 ? resolution / 2.0 / Math.Sqrt(3) : 0;
            double correctionComponent = certificateUncertainty > 0 && coverageFactor > 0 ? certificateUncertainty / coverageFactor : 0;
            double stabilityComponent = stabilityChange > 0 ? stabilityChange / Math.Sqrt(3) : 0;
            double combined = Math.Sqrt(repeatability * repeatability + resolutionComponent * resolutionComponent +
                                        correctionComponent * correctionComponent + stabilityComponent * stabilityComponent);
            return (coverageFactor > 0 ? coverageFactor : 2) * combined;
        }

        private static double CalculateFurnaceInputUncertainty(IReadOnlyList<double[]> matrix, int pointIndex)
        {
            double[] values = matrix.Select(row => row[pointIndex]).ToArray();
            double repeatabilityOfMean = SampleStandardDeviation(values) / Math.Sqrt(values.Length);
            double coverage = CalibrationTaskContext.ReferencedTemperatureCoverage;
            double correction = CalibrationTaskContext.ReferencedTemperatureUncertainty > 0 && coverage > 0
                ? CalibrationTaskContext.ReferencedTemperatureUncertainty / coverage
                : 0;
            return Math.Sqrt(repeatabilityOfMean * repeatabilityOfMean + correction * correction);
        }

        private static double SampleStandardDeviation(IReadOnlyList<double> values)
        {
            if (values.Count < 2) return 0;
            double average = values.Average();
            double sum = values.Sum(value => (value - average) * (value - average));
            return Math.Sqrt(sum / (values.Count - 1));
        }
    }
}
