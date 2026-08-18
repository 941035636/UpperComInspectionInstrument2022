using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UpperComInspectionInstrument2022.Models;

namespace UpperComInspectionInstrument2022.Services
{
    /// <summary>
    /// 巡检仪实时数据采集服务。负责单一采集循环的生命周期，不负责页面显示。
    /// </summary>
    public class InspectionDataAcquisitionService
    {
        private readonly InspectionMeterService _meterService;
        private readonly object _stateLock = new object();
        private CancellationTokenSource? _cts;
        private long _acquisitionId;
        private string _calibrationType = "温度";

        public bool IsRunning { get; private set; }
        public event Action<long, List<InspectionChannelData>>? DataAcquired;
        public event Action<Exception>? AcquisitionError;

        public InspectionDataAcquisitionService(InspectionMeterService meterService)
        {
            _meterService = meterService ?? throw new ArgumentNullException(nameof(meterService));
        }

        public void Start(byte slaveAddress, int intervalMilliseconds) => Start(slaveAddress, intervalMilliseconds, "温度");

        public void Start(byte slaveAddress, int intervalMilliseconds, string calibrationType)
        {
            if (intervalMilliseconds < 200) intervalMilliseconds = 200;
            CancellationTokenSource cts;
            lock (_stateLock)
            {
                if (IsRunning) return;
                cts = new CancellationTokenSource();
                _cts = cts;
                _calibrationType = string.IsNullOrWhiteSpace(calibrationType) ? "温度" : calibrationType;
                IsRunning = true;
            }
            _ = Task.Run(() => AcquisitionLoop(slaveAddress, intervalMilliseconds, cts), CancellationToken.None);
        }

        public void Stop()
        {
            CancellationTokenSource? cts;
            lock (_stateLock)
            {
                cts = _cts;
                _cts = null;
                IsRunning = false;
            }
            cts?.Cancel();
        }

        private async Task AcquisitionLoop(byte slaveAddress, int intervalMilliseconds, CancellationTokenSource owner)
        {
            CancellationToken token = owner.Token;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    long acquisitionId = Interlocked.Increment(ref _acquisitionId);
                    try
                    {
                        List<InspectionChannelData> data = _meterService.ReadMeasurements(_calibrationType, slaveAddress, acquisitionId);
                        if (!token.IsCancellationRequested) DataAcquired?.Invoke(acquisitionId, data);
                    }
                    catch (Exception ex) when (!token.IsCancellationRequested)
                    {
                        AcquisitionError?.Invoke(ex);
                    }
                    await Task.Delay(intervalMilliseconds, token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            finally
            {
                lock (_stateLock)
                {
                    if (ReferenceEquals(_cts, owner))
                    {
                        _cts = null;
                        IsRunning = false;
                    }
                }
                owner.Dispose();
            }
        }
    }
}
