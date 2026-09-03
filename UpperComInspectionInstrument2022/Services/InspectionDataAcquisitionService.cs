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
        private const int RetryBaseDelayMilliseconds = 1000;
        private const int RetryMaximumDelayMilliseconds = 5000;
        private readonly IInspectionMeasurementReader _measurementReader;
        private readonly object _stateLock = new object();
        private CancellationTokenSource? _cts;
        private Task? _loopTask;
        private long _acquisitionId;
        private string _calibrationType = "温度";
        private bool _isRunning;
        private int _consecutiveFailureCount;
        private int _nextRetryDelayMilliseconds;

        /// <summary>连续通信失败达到该次数后，页面才会把本次采集判定为不可继续。</summary>
        public int MaxConsecutiveFailures { get; } = 3;

        /// <summary>后台采集循环是否尚未完全退出。</summary>
        public bool IsRunning
        {
            get { lock (_stateLock) return _isRunning; }
        }

        /// <summary>当前连续通信失败次数；任意一次完整读取成功后自动清零。</summary>
        public int ConsecutiveFailureCount
        {
            get { lock (_stateLock) return _consecutiveFailureCount; }
        }

        /// <summary>失败后下一次请求前的等待时间，单位为毫秒。</summary>
        public int NextRetryDelayMilliseconds
        {
            get { lock (_stateLock) return _nextRetryDelayMilliseconds; }
        }

        /// <summary>每完成一组全部通道读取后触发；事件在线程池线程上发出。</summary>
        public event Action<long, List<InspectionChannelData>>? DataAcquired;
        /// <summary>单次读取失败时触发；达到连续失败上限前，服务会按退避时间继续尝试。</summary>
        public event Action<Exception>? AcquisitionError;

        /// <summary>创建采集服务并注入负责设备协议解析的巡检仪服务。</summary>
        public InspectionDataAcquisitionService(IInspectionMeasurementReader measurementReader)
        {
            _measurementReader = measurementReader ?? throw new ArgumentNullException(nameof(measurementReader));
        }

        /// <summary>按纯温度模式启动采集，供旧调用代码兼容使用。</summary>
        public bool Start(byte slaveAddress, int intervalMilliseconds) => Start(slaveAddress, intervalMilliseconds, "温度");

        /// <summary>
        /// 启动唯一后台采集循环。旧循环尚未退出时返回 false，避免两个循环交替访问同一串口。
        /// 最小轮询周期限制为 200 ms。
        /// </summary>
        public bool Start(byte slaveAddress, int intervalMilliseconds, string calibrationType)
        {
            if (intervalMilliseconds < 200) intervalMilliseconds = 200;
            CancellationTokenSource cts;
            lock (_stateLock)
            {
                if (_loopTask is { IsCompleted: false }) return false;
                cts = new CancellationTokenSource();
                _cts = cts;
                _calibrationType = string.IsNullOrWhiteSpace(calibrationType) ? "温度" : calibrationType;
                _consecutiveFailureCount = 0;
                _nextRetryDelayMilliseconds = 0;
                _isRunning = true;
                _loopTask = Task.Run(
                    () => AcquisitionLoop(slaveAddress, intervalMilliseconds, cts),
                    CancellationToken.None);
            }
            return true;
        }

        /// <summary>
        /// 请求停止采集。该方法只发出取消信号；在当前串口读操作真正结束前，服务仍视为运行中。
        /// </summary>
        public void Stop()
        {
            RequestStop();
        }

        /// <summary>
        /// 请求停止并异步等待采集循环完全退出。界面“暂停”操作使用本方法，确保再次启动前旧请求已经结束。
        /// </summary>
        public async Task StopAsync()
        {
            Task? loopTask = RequestStop();
            if (loopTask == null) return;
            await loopTask.ConfigureAwait(false);
        }

        /// <summary>
        /// 请求停止并在指定时间内同步等待退出，供应用关闭阶段使用。
        /// 返回 false 表示等待超时，调用方仍可继续执行兜底资源释放。
        /// </summary>
        public bool StopAndWait(TimeSpan timeout)
        {
            if (timeout < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
            Task? loopTask = RequestStop();
            if (loopTask == null) return true;
            return loopTask.Wait(timeout);
        }

        /// <summary>在锁内取得当前循环并发送取消信号，但不提前清除运行状态。</summary>
        private Task? RequestStop()
        {
            lock (_stateLock)
            {
                _cts?.Cancel();
                return _loopTask;
            }
        }

        /// <summary>
        /// 循环读取完整测量数据、分配采集序号并发布事件。
        /// 通信失败采用 1～5 秒线性退避，防止设备无响应时持续高频请求。
        /// </summary>
        private async Task AcquisitionLoop(byte slaveAddress, int intervalMilliseconds, CancellationTokenSource owner)
        {
            CancellationToken token = owner.Token;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    long acquisitionId = Interlocked.Increment(ref _acquisitionId);
                    int delayMilliseconds = intervalMilliseconds;
                    try
                    {
                        List<InspectionChannelData> data = _measurementReader.ReadMeasurements(_calibrationType, slaveAddress, acquisitionId);
                        lock (_stateLock)
                        {
                            _consecutiveFailureCount = 0;
                            _nextRetryDelayMilliseconds = 0;
                        }
                        if (!token.IsCancellationRequested) DataAcquired?.Invoke(acquisitionId, data);
                    }
                    catch (Exception ex) when (!token.IsCancellationRequested)
                    {
                        bool reachedFailureLimit;
                        lock (_stateLock)
                        {
                            _consecutiveFailureCount++;
                            _nextRetryDelayMilliseconds = Math.Max(
                                intervalMilliseconds,
                                Math.Min(RetryMaximumDelayMilliseconds, RetryBaseDelayMilliseconds * _consecutiveFailureCount));
                            delayMilliseconds = _nextRetryDelayMilliseconds;
                            reachedFailureLimit = _consecutiveFailureCount >= MaxConsecutiveFailures;
                        }
                        try
                        {
                            AcquisitionError?.Invoke(ex);
                        }
                        finally
                        {
                            // 连续失败达到上限后终止循环。只有操作人员重新连接/启动才会产生新请求，
                            // 避免断线设备被无限轮询。
                            if (reachedFailureLimit) owner.Cancel();
                        }
                    }
                    await Task.Delay(delayMilliseconds, token).ConfigureAwait(false);
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
                        _isRunning = false;
                        _loopTask = null;
                        _nextRetryDelayMilliseconds = 0;
                    }
                }
                owner.Dispose();
            }
        }
    }
}
