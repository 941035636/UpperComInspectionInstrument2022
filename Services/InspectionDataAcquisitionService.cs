
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UpperComInspectionInstrument2022.Models;


namespace UpperComInspectionInstrument2022.Services
{
    /// <summary>
    /// 巡检仪实时数据采集服务
    /// </summary>
    public class InspectionDataAcquisitionService
    {
        private readonly InspectionMeterService _meterService;

        private CancellationTokenSource? _cts;
        private Task? _acquisitionTask;

        private long _acquisitionId;

        public bool IsRunning
        {
            get;
            private set;
        }

        /// <summary>
        /// 每次采集完成
        /// </summary>
        public event Action<
            long,
            List<InspectionChannelData>>
            ? DataAcquired;

        /// <summary>
        /// 采集异常
        /// </summary>
        public event Action<Exception>? AcquisitionError;

        public InspectionDataAcquisitionService(
            InspectionMeterService meterService)
        {
            _meterService = meterService;
        }

        /// <summary>
        /// 开始自动采集
        /// </summary>
        public void Start(
            byte slaveAddress,
            int intervalMilliseconds)
        {
            if (IsRunning)
                return;

            if (intervalMilliseconds < 200)
            {
                intervalMilliseconds = 200;
            }

            _cts =
                new CancellationTokenSource();

            IsRunning = true;

            _acquisitionTask = Task.Run(
                () => AcquisitionLoop(
                    slaveAddress,
                    intervalMilliseconds,
                    _cts.Token));
        }

        /// <summary>
        /// 停止自动采集
        /// </summary>
        public void Stop()
        {
            if (!IsRunning)
                return;

            try
            {
                _cts?.Cancel();
            }
            catch
            {
            }

            IsRunning = false;
        }

        /// <summary>
        /// 自动采集循环
        /// </summary>
        private async Task AcquisitionLoop(
            byte slaveAddress,
            int intervalMilliseconds,
            CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    long acquisitionId =
                        Interlocked.Increment(
                            ref _acquisitionId);

                    try
                    {
                        List<InspectionChannelData>
                            data =
                            _meterService
                                .ReadTemperatures(
                                    slaveAddress,
                                    acquisitionId);

                        DataAcquired?.Invoke(
                            acquisitionId,
                            data);
                    }
                    catch (Exception ex)
                    {
                        AcquisitionError?.Invoke(ex);
                    }

                    await Task.Delay(
                        intervalMilliseconds,
                        token);
                }
            }
            catch (TaskCanceledException)
            {
                // 正常停止
            }
            finally
            {
                IsRunning = false;
            }
        }
    }
}

