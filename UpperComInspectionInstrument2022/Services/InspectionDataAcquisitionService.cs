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

        /// <summary>后台采集循环是否正在运行。</summary>
        public bool IsRunning { get; private set; }
        /// <summary>每完成一组全部通道读取后触发；事件在线程池线程上发出。</summary>
        public event Action<long, List<InspectionChannelData>>? DataAcquired;
        /// <summary>单次读取失败时触发；服务会继续下一轮，不会因偶发超时永久停止。</summary>
        public event Action<Exception>? AcquisitionError;

        /// <summary>创建采集服务并注入负责设备协议解析的巡检仪服务。</summary>
        public InspectionDataAcquisitionService(InspectionMeterService meterService)
        {
            _meterService = meterService ?? throw new ArgumentNullException(nameof(meterService));
        }

        /// <summary>按纯温度模式启动采集，供旧调用代码兼容使用。</summary>
        public void Start(byte slaveAddress, int intervalMilliseconds) => Start(slaveAddress, intervalMilliseconds, "温度");

        /// <summary>
        /// 启动唯一后台采集循环。重复调用不会创建第二个循环，最小轮询周期限制为 200 ms。
        /// </summary>
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

        /// <summary>请求停止采集。取消是协作式的，当前串口读操作结束后循环退出。</summary>
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

        /// <summary>
        /// 循环读取完整测量数据、分配采集序号并发布事件；单次异常与循环生命周期相互隔离。
        /// </summary>
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
